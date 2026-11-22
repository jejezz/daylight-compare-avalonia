using System.Security.Cryptography;
using DaylightCompare.Domain;
using DaylightCompare.Domain.Diff;
using DaylightCompare.Domain.Folder;
using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Services.Folder;

/// <summary>
/// 폴더 비교 (ROADMAP 4단계). 두 쪽은 어느 <see cref="IFileSystem"/>이든 된다 — 로컬·SFTP·FTP·WebDAV·압축 파일.
/// <para>
/// 1) <b>구조</b>: 폴더를 넓이 우선으로 읽어 짝짓고(<see cref="FolderPairing"/>), 필터(정책 + BC 필터 막대)를 적용하고, 빠른 검사(<see cref="FolderJudge.Quick"/>)로
/// 정해지는 것은 바로 정한다. 읽는 대로 <see cref="Changed"/>를 알리므로 첫 결과가 금방 보인다.
/// 2) <b>내용</b>: 빠른 검사가 내용을 봐야 한다고 한 짝을 동시에 몇 개씩 비교한다 — 규칙 기준(텍스트는 Compare Policy, 아니면 바이트) 또는 해시.
/// 해시는 서버가 할 수 있으면(<see cref="IServerHasher"/>, SFTP의 <c>sha256sum</c>) 내려받지 않고 서버에서 구한다.
/// </para>
/// 알림은 백그라운드 스레드에서 오므로 화면은 UI 스레드로 넘겨 읽는다. 마디를 바꾸는 일은 <see cref="Gate"/>를 잡고 한다.
/// </summary>
public sealed class FolderComparer
{
    /// <summary>규칙 기준 내용 비교를 텍스트로 해 보는 크기 상한. 넘으면 바이트로 비교한다.</summary>
    public const long RulesMaxBytes = CompareSources.TextMaxBytes;

    readonly IFileSystem _leftFs, _rightFs;
    readonly string _leftRoot, _rightRoot;
    readonly ComparePolicy _policy;
    readonly bool _namesIgnoreCase;
    readonly FolderRules _rules;
    readonly FileMask _filter;
    readonly int _parallel;

    public FolderComparer(IFileSystem leftFs, string leftRoot, IFileSystem rightFs, string rightRoot, ComparePolicy policy,
        bool namesIgnoreCase, FileMask? filter = null, int parallel = 4)
    {
        (_leftFs, _leftRoot, _rightFs, _rightRoot) = (leftFs, leftRoot, rightFs, rightRoot);
        _policy = policy;
        _namesIgnoreCase = namesIgnoreCase;
        _rules = policy.Resolve(namesIgnoreCase: namesIgnoreCase).Folder;
        _filter = filter ?? FileMask.All;
        _parallel = Math.Max(1, parallel);
        Root = new FolderNode("", "", true, new FileEntry(leftRoot, Locations.NameOf(leftRoot), true), new FileEntry(rightRoot, Locations.NameOf(rightRoot), true), null);
    }

    public FolderNode Root { get; }

    /// <summary>마디를 바꾸거나 나무를 훑을 때 잡는 잠금 (화면이 읽는 동안 비교기가 바꾸지 않게).</summary>
    public object Gate { get; } = new();

    /// <summary>마디가 생기거나 상태가 바뀌었다 (백그라운드 스레드). 폴더면 그 아래 목록이 새로 생긴 것.</summary>
    public event Action<FolderNode>? Changed;

    /// <summary>모든 비교가 끝났다.</summary>
    public bool Completed { get; private set; }

    /// <summary>
    /// 비교를 실행한다. <b>항상 백그라운드(스레드 풀)에서</b> 돈다 — 부르는 쪽이 UI 스레드여도 파일을 읽고 해석하고 diff하는 일의 이어지기가
    /// UI 스레드로 돌아오지 않는다. (UI 스레드에서 이어지면 일반 우선순위의 이어지기가 입력·그리기보다 먼저 처리되어, 파일이 많거나 읽기가 느린
    /// 네트워크 드라이브에서 앱이 통째로 멈췄다.) 그래서 <see cref="Changed"/>도 백그라운드 스레드에서 온다.
    /// </summary>
    public Task RunAsync(CancellationToken ct = default) => Task.Run(() => RunCoreAsync(ct), ct);

    async Task RunCoreAsync(CancellationToken ct)
    {
        var contentJobs = new List<FolderNode>();
        var queue = new Queue<FolderNode>();
        queue.Enqueue(Root);
        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var folder = queue.Dequeue();
            await ScanAsync(folder, queue, contentJobs, ct);
        }

        using var gate = new SemaphoreSlim(_parallel);
        async Task CompareOne(FolderNode node, FolderCriterion step, ServerHashes? known)
        {
            await gate.WaitAsync(ct);
            try { await CompareContentAsync(node, step, known, ct); }
            finally { gate.Release(); }
        }

        var jobs = contentJobs.Select(n => (Node: n, Step: FolderJudge.Quick(n.Left!, n.Right!, _rules.Criteria).ContentStep!)).ToList();
        var serverHash = _leftFs is IServerHasher || _rightFs is IServerHasher;
        var hashJobs = serverHash ? jobs.Where(j => j.Step.By == CriterionKind.Hash).ToList() : [];
        var others = serverHash ? jobs.Where(j => j.Step.By != CriterionKind.Hash) : jobs;
        await Task.WhenAll(
            Task.WhenAll(others.Select(j => CompareOne(j.Node, j.Step, null))),
            ServerHashJobsAsync(hashJobs, CompareOne, ct));
        Completed = true;
        Changed?.Invoke(Root);
    }

    readonly HashSet<FolderNode> _loading = [];

    /// <summary>
    /// 한쪽에만 있는 폴더를 한 칸 읽는다 (펼칠 때). 이미 읽었거나 읽는 중이면 아무것도 하지 않는다.
    /// 아래 폴더도 한쪽에만 있으니 마찬가지로 펼칠 때 읽는다. 항상 백그라운드에서 돈다.
    /// </summary>
    public Task LoadAsync(FolderNode folder, CancellationToken ct = default) => Task.Run(async () =>
    {
        lock (Gate)
        {
            if (!folder.Scanning || !folder.IsOrphan || !_loading.Add(folder)) return;
        }
        try { await ScanAsync(folder, new Queue<FolderNode>(), [], ct); }
        finally { lock (Gate) _loading.Remove(folder); }
    }, ct);

    async Task ScanAsync(FolderNode folder, Queue<FolderNode> queue, List<FolderNode> contentJobs, CancellationToken ct)
    {
        var lists = await Task.WhenAll(ListAsync(_leftFs, folder.Left, ct), ListAsync(_rightFs, folder.Right, ct));
        lock (Gate)
        {
            if ((lists[0].Error ?? lists[1].Error) is { } error)
            {
                folder.State = FolderState.Error;
                folder.Error = error;
            }
            foreach (var (l, r) in FolderPairing.Pair(Filter(lists[0].Entries, folder), Filter(lists[1].Entries, folder), _rules))
            {
                var any = (l ?? r)!;
                var rel = folder.RelativePath.Length == 0 ? any.Name : folder.RelativePath + "/" + any.Name;
                var node = new FolderNode(any.Name, rel, any.IsDirectory, l, r, folder);
                folder.Children.Add(node);
                // 한쪽에만 있는 폴더는 훑지 않는다 — 안은 모두 한쪽에만 있는 것이고, 크면 (엉뚱한 폴더로 올라갔을 때처럼) 훑는 데만 몇 분이 걸린다.
                // 사용자가 펼칠 때 LoadAsync로 한 칸만 읽는다 (BC도 그렇게 한다). Scanning이 true인 채로 남는다.
                if (node.IsDirectory) { if (l is not null && r is not null) queue.Enqueue(node); }
                else if (l is not null && r is not null)
                {
                    var (verdict, newer, _) = FolderJudge.Quick(l, r, _rules.Criteria);
                    node.Newer = newer;
                    node.State = verdict switch
                    {
                        QuickVerdict.Same => FolderState.Same,
                        QuickVerdict.Different => FolderState.Different,
                        _ => FolderState.Pending,
                    };
                    if (verdict == QuickVerdict.NeedsContent) contentJobs.Add(node);
                }
            }
            folder.Scanning = false;
            UpdateFolders(folder);
        }
        Changed?.Invoke(folder);
    }

    /// <summary>필터: 숨김 · 크기 · 정책의 파일·폴더 마스크 · 필터 막대(BC 문법, 기준 폴더에서의 상대 경로).</summary>
    IReadOnlyList<FileEntry> Filter(IReadOnlyList<FileEntry> entries, FolderNode folder) =>
        [.. entries.Where(e =>
        {
            if (e.IsHidden && !_rules.ShowHidden) return false;
            var rel = folder.RelativePath.Length == 0 ? e.Name : folder.RelativePath + "/" + e.Name;
            if (e.IsDirectory) return _rules.Folders.MatchesFolder(e.Name, rel) && _rules.Files.MatchesFolder(e.Name, rel) && _filter.MatchesFolder(e.Name, rel);
            if (e.SizeBytes is { } size && (size < (_rules.MinSize ?? 0) || size > (_rules.MaxSize ?? long.MaxValue))) return false;
            return _rules.Files.Matches(e.Name, rel) && _rules.Folders.Matches(e.Name, rel) && _filter.Matches(e.Name, rel);
        })];

    static async Task<(IReadOnlyList<FileEntry> Entries, string? Error)> ListAsync(IFileSystem fs, FileEntry? dir, CancellationToken ct)
    {
        if (dir is null) return ([], null);
        try { return (await fs.ListAsync(dir.Path, ct), null); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ([], e.Message);
        }
    }

    /// <summary>바뀐 마디부터 위로 폴더 상태를 다시 정한다 (<see cref="Gate"/> 안에서).</summary>
    void UpdateFolders(FolderNode? folder)
    {
        for (var f = folder; f is not null; f = f.Parent)
            if (f.State != FolderState.Error) f.State = FolderJudge.FolderStateOf(f);
    }

    /// <summary>서버에서 미리 구한 SHA-256 (쪽마다 경로 → 해시, 서버 해시를 못 하는 쪽은 null).</summary>
    sealed record ServerHashes(IReadOnlyDictionary<string, byte[]>? Left, IReadOnlyDictionary<string, byte[]>? Right);

    /// <summary>
    /// 원격 해시 (ROADMAP §8-1): 해시 단계의 짝을 폴더마다 묶어 서버에 한 번에 묻고(<see cref="IServerHasher"/>, 명령 하나에 최대
    /// <see cref="ServerHashCommands.MaxFilesPerCommand"/>개), 받은 묶음부터 비교한다. 다음 묶음을 묻는 동안 앞 묶음의 비교(로컬 쪽 해시)가 돈다.
    /// </summary>
    async Task ServerHashJobsAsync(List<(FolderNode Node, FolderCriterion Step)> jobs, Func<FolderNode, FolderCriterion, ServerHashes?, Task> compare, CancellationToken ct)
    {
        var running = new List<Task>();
        foreach (var chunk in jobs.GroupBy(j => j.Node.Parent).SelectMany(g => g.Chunk(ServerHashCommands.MaxFilesPerCommand)))
        {
            ct.ThrowIfCancellationRequested();
            var left = AskServerAsync(_leftFs, [.. chunk.Select(j => j.Node.Left!.Path)], ct);
            var right = AskServerAsync(_rightFs, [.. chunk.Select(j => j.Node.Right!.Path)], ct);
            var known = new ServerHashes(await left, await right);
            running.AddRange(chunk.Select(j => compare(j.Node, j.Step, known)));
        }
        await Task.WhenAll(running);
    }

    static async Task<IReadOnlyDictionary<string, byte[]>?> AskServerAsync(IFileSystem fs, IReadOnlyList<string> paths, CancellationToken ct)
    {
        if (fs is not IServerHasher hasher) return null;
        try { return await hasher.HashSha256Async(paths, ct); }
        catch (Exception e) when (e is not OperationCanceledException) { return null; } // 서버가 못 하면 내려받아 해시한다
    }

    async Task CompareContentAsync(FolderNode node, FolderCriterion step, ServerHashes? known, CancellationToken ct)
    {
        FolderState state;
        string? error = null;
        try
        {
            var same = step.By == CriterionKind.Hash
                ? await SameHashAsync(node, step.Algorithm, known, ct)
                : await SameByRulesAsync(node, ct);
            state = same ? FolderState.Same : FolderState.Different;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            state = FolderState.Error;
            error = e.Message;
        }
        lock (Gate)
        {
            node.State = state;
            node.Error = error;
            UpdateFolders(node.Parent);
        }
        Changed?.Invoke(node);
    }

    /// <summary>
    /// 규칙 기준 (BC "Rules-based"): 바이트가 같으면 같음. 둘 다 텍스트면 Compare Policy로 — 중요하지 않은 차이만 있으면 같음.
    /// 텍스트가 아니거나 너무 크면 바이트로.
    /// </summary>
    async Task<bool> SameByRulesAsync(FolderNode node, CancellationToken ct)
    {
        var (l, r) = (node.Left!, node.Right!);
        if ((l.SizeBytes ?? 0) > RulesMaxBytes || (r.SizeBytes ?? 0) > RulesMaxBytes) return await SameBytesAsync(l, r, ct);
        var a = await ReadAllAsync(_leftFs, l.Path, ct);
        var b = await ReadAllAsync(_rightFs, r.Path, ct);
        if (a.AsSpan().SequenceEqual(b)) return true;
        var la = TextFiles.Decode(a, DateTime.MinValue, maxBytes: (int)RulesMaxBytes).File;
        var lb = TextFiles.Decode(b, DateTime.MinValue, maxBytes: (int)RulesMaxBytes).File;
        if (la is null || lb is null || la.Binary || lb.Binary) return false;
        var effective = _policy.Resolve(node.Name, node.RelativePath, namesIgnoreCase: _namesIgnoreCase);
        return TextDiff.Compare(TextFiles.NormalizeLineBreaks(la.Text), TextFiles.NormalizeLineBreaks(lb.Text), effective).IsEquivalent;
    }

    async Task<bool> SameBytesAsync(FileEntry l, FileEntry r, CancellationToken ct)
    {
        await using var a = await _leftFs.OpenReadAsync(l.Path, ct);
        await using var b = await _rightFs.OpenReadAsync(r.Path, ct);
        var bufA = new byte[1 << 16];
        var bufB = new byte[1 << 16];
        while (true)
        {
            var n = await a.ReadAtLeastAsync(bufA, bufA.Length, throwOnEndOfStream: false, ct);
            var m = await b.ReadAtLeastAsync(bufB, bufB.Length, throwOnEndOfStream: false, ct);
            if (n != m || !bufA.AsSpan(0, n).SequenceEqual(bufB.AsSpan(0, m))) return false;
            if (n < bufA.Length) return true;
        }
    }

    /// <summary>
    /// 해시 비교 (BC "CRC"). 서버가 구해 둔 해시가 한쪽이라도 있으면 두 쪽 다 SHA-256으로 비교한다 — 서버 도구는 SHA-256만 있고,
    /// 정책이 CRC32여도 같음/다름의 뜻은 같다. 서버 해시가 없는 쪽은 내려받아(로컬은 읽어) 구한다.
    /// </summary>
    async Task<bool> SameHashAsync(FolderNode node, HashAlgorithmKind algorithm, ServerHashes? known, CancellationToken ct)
    {
        var (l, r) = (node.Left!.Path, node.Right!.Path);
        var ka = known?.Left?.GetValueOrDefault(l);
        var kb = known?.Right?.GetValueOrDefault(r);
        if (ka is not null || kb is not null) algorithm = HashAlgorithmKind.Sha256;
        var a = ka ?? await HashAsync(_leftFs, l, algorithm, ct);
        var b = kb ?? await HashAsync(_rightFs, r, algorithm, ct);
        return a.AsSpan().SequenceEqual(b);
    }

    static async Task<byte[]> HashAsync(IFileSystem fs, string path, HashAlgorithmKind algorithm, CancellationToken ct)
    {
        await using var s = await fs.OpenReadAsync(path, ct);
        if (algorithm == HashAlgorithmKind.Sha256) return await SHA256.HashDataAsync(s, ct);
        return BitConverter.GetBytes(await Crc32.ComputeAsync(s, ct));
    }

    static async Task<byte[]> ReadAllAsync(IFileSystem fs, string path, CancellationToken ct)
    {
        await using var s = await fs.OpenReadAsync(path, ct);
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms, ct);
        return ms.ToArray();
    }
}

/// <summary>CRC-32 (IEEE 802.3, zip과 같은 다항식). 의존성을 늘리지 않으려고 직접 둔다 — 표 하나.</summary>
public static class Crc32
{
    static readonly uint[] Table = Enumerable.Range(0, 256).Select(i =>
    {
        var c = (uint)i;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    public static uint Append(uint crc, ReadOnlySpan<byte> data)
    {
        crc = ~crc;
        foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }

    public static async Task<uint> ComputeAsync(Stream s, CancellationToken ct = default)
    {
        var buffer = new byte[1 << 16];
        uint crc = 0;
        int n;
        while ((n = await s.ReadAsync(buffer, ct)) > 0) crc = Append(crc, buffer.AsSpan(0, n));
        return crc;
    }
}
