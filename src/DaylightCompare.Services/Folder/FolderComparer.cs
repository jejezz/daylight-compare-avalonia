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

    public async Task RunAsync(CancellationToken ct = default)
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
        await Task.WhenAll(contentJobs.Select(async node =>
        {
            await gate.WaitAsync(ct);
            try { await CompareContentAsync(node, ct); }
            finally { gate.Release(); }
        }));
        Completed = true;
        Changed?.Invoke(Root);
    }

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
                if (node.IsDirectory) queue.Enqueue(node);
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

    async Task CompareContentAsync(FolderNode node, CancellationToken ct)
    {
        FolderState state;
        string? error = null;
        try
        {
            var step = FolderJudge.Quick(node.Left!, node.Right!, _rules.Criteria).ContentStep!;
            var same = step.By == CriterionKind.Hash
                ? await SameHashAsync(node, step.Algorithm, ct)
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

    /// <summary>해시 비교 (BC "CRC"). 원격 쪽을 서버에서 해시하는 것(내려받지 않기)은 4c.</summary>
    async Task<bool> SameHashAsync(FolderNode node, HashAlgorithmKind algorithm, CancellationToken ct)
    {
        var a = await HashAsync(_leftFs, node.Left!.Path, algorithm, ct);
        var b = await HashAsync(_rightFs, node.Right!.Path, algorithm, ct);
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
