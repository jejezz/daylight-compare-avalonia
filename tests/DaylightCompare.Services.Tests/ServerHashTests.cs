using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using DaylightCompare.Domain;
using DaylightCompare.Domain.Folder;
using DaylightCompare.Domain.Policy;
using DaylightCompare.Services.Folder;

namespace DaylightCompare.Services.Tests;

/// <summary>원격 해시 (ROADMAP 4c-1): 서버 해시 명령 만들기·읽기, 폴더 비교가 서버 해시를 쓰는지.</summary>
public sealed class ServerHashTests
{
    static string Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void 찾은_도구로_명령을_만든다()
    {
        Assert.Equal("'/usr/bin/sha256sum'", ServerHashCommands.ToolCommand("/usr/bin/sha256sum\n"));
        Assert.Equal("'/usr/bin/shasum' -a 256", ServerHashCommands.ToolCommand("/usr/bin/shasum\n"));
        Assert.Null(ServerHashCommands.ToolCommand(""));
        Assert.Null(ServerHashCommands.ToolCommand("/bin/md5\n"));
    }

    [Fact]
    public void 작은따옴표를_인용한다()
    {
        Assert.Equal("'/a b/it'\\''s $x'", ServerHashCommands.Quote("/a b/it's $x"));
    }

    [Fact]
    public void 출력을_이름별_해시로_읽는다()
    {
        var output = $"{Hex("a")}  /d/a.txt\n{Hex("b")} */d/b c.bin\r\n\\{Hex("c")}  /d/new\\nline\nsha256sum: /d/x: Permission denied\n";
        var parsed = ServerHashCommands.Parse(output);
        Assert.Equal(2, parsed.Count);
        Assert.Equal(Hex("a"), Convert.ToHexStringLower(parsed["/d/a.txt"]));
        Assert.Equal(Hex("b"), Convert.ToHexStringLower(parsed["/d/b c.bin"]));
    }

    [Fact]
    public void 긴_목록은_명령_여러_개로_나눈다()
    {
        var paths = Enumerable.Range(0, ServerHashCommands.MaxFilesPerCommand * 2 + 5).Select(i => $"/d/f{i}").ToList();
        var batches = ServerHashCommands.Batches("sha256sum", paths).ToList();
        Assert.Equal(3, batches.Count);
        Assert.Equal(paths, batches.SelectMany(b => b.Paths));
        Assert.All(batches, b => Assert.True(b.Command.Length <= ServerHashCommands.MaxCharsPerCommand));
        Assert.StartsWith("sha256sum '/d/f0' '/d/f1'", batches[0].Command);
    }

    /// <summary>서버 해시를 흉내 내는 원격: 해시를 서버에서 주고, 파일을 내려받으면 센다.</summary>
    sealed class HashingRemote(MemoryFileSystem inner, bool canHash) : IFileSystem, IServerHasher
    {
        public int Downloads, HashCalls;
        public Task<IReadOnlyList<FileEntry>> ListAsync(string path, CancellationToken ct = default) => inner.ListAsync(path, ct);
        public Task<FileEntry?> StatAsync(string path, CancellationToken ct = default) => inner.StatAsync(path, ct);
        public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default) { Interlocked.Increment(ref Downloads); return inner.OpenReadAsync(path, ct); }
        public Task<Stream> OpenWriteAsync(string path, bool overwrite, CancellationToken ct = default) => inner.OpenWriteAsync(path, overwrite, ct);
        public Task CreateDirectoryAsync(string path, CancellationToken ct = default) => inner.CreateDirectoryAsync(path, ct);
        public Task DeleteAsync(string path, CancellationToken ct = default) => inner.DeleteAsync(path, ct);
        public Task RenameAsync(string from, string to, CancellationToken ct = default) => inner.RenameAsync(from, to, ct);
        public Task SetModifiedAsync(string path, DateTimeOffset modified, CancellationToken ct = default) => inner.SetModifiedAsync(path, modified, ct);

        public Task<IReadOnlyDictionary<string, byte[]>?> HashSha256Async(IReadOnlyList<string> paths, CancellationToken ct = default)
        {
            Interlocked.Increment(ref HashCalls);
            if (!canHash) return Task.FromResult<IReadOnlyDictionary<string, byte[]>?>(null);
            var result = new Dictionary<string, byte[]>();
            foreach (var p in paths)
                if (inner.Read(Locations.TryParse(p)!.Path) is { } text) result[p] = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            return Task.FromResult<IReadOnlyDictionary<string, byte[]>?>(result);
        }
    }

    static ComparePolicy HashPolicy(string algorithm) => ComparePolicy.FromJson((JsonObject)JsonNode.Parse(
        $$"""{ "schema": 1, "defaults": { "folder": { "criteria": [ { "by": "hash", "algorithm": "{{algorithm}}" } ] } } }""")!);

    static MemoryFileSystem Server(string authority, string changed)
    {
        var fs = new MemoryFileSystem(authority);
        for (var i = 0; i < 30; i++) fs.File($"/srv/d{i % 3}/f{i}.txt", i == 4 ? changed : $"line {i}\n");
        return fs;
    }

    [Theory]
    [InlineData("sha256")]
    [InlineData("crc32")] // 서버가 끼면 CRC32 설정이어도 SHA-256으로 — 뜻은 같다
    public async Task 서버_두_대는_내려받지_않고_해시로_비교한다(string algorithm)
    {
        var left = new HashingRemote(Server("sftp://u@a", "line 4\n"), canHash: true);
        var right = new HashingRemote(Server("sftp://u@b", "other\n"), canHash: true);
        var c = new FolderComparer(left, "sftp://u@a/srv", right, "sftp://u@b/srv", HashPolicy(algorithm), namesIgnoreCase: true);
        await c.RunAsync();

        var files = c.Root.Descendants().Where(n => !n.IsDirectory).ToList();
        Assert.Equal(30, files.Count);
        Assert.Equal(FolderState.Different, files.Single(n => n.Name == "f4.txt").State);
        Assert.All(files.Where(n => n.Name != "f4.txt"), n => Assert.Equal(FolderState.Same, n.State));
        Assert.Equal(0, left.Downloads + right.Downloads);
        Assert.Equal(3, left.HashCalls); // 폴더마다 한 번
    }

    [Fact]
    public async Task 로컬_대_서버는_로컬만_읽는다()
    {
        var local = Directory.CreateTempSubdirectory("dcmp-serverhash").FullName;
        try
        {
            for (var i = 0; i < 30; i++)
            {
                var path = Path.Combine(local, $"d{i % 3}", $"f{i}.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, $"line {i}\n");
            }
            var right = new HashingRemote(Server("sftp://u@b", "other\n"), canHash: true);
            var c = new FolderComparer(LocalFileSystem.Instance, local, right, "sftp://u@b/srv", HashPolicy("crc32"), namesIgnoreCase: true);
            await c.RunAsync();
            Assert.Equal(FolderState.Different, c.Root.Descendants().Single(n => n.Name == "f4.txt").State);
            Assert.Equal(29, c.Root.Descendants().Count(n => !n.IsDirectory && n.State == FolderState.Same));
            Assert.Equal(0, right.Downloads);
        }
        finally
        {
            Directory.Delete(local, recursive: true);
        }
    }

    [Fact]
    public async Task 서버가_못_하면_내려받아_해시한다()
    {
        var left = new HashingRemote(Server("sftp://u@a", "line 4\n"), canHash: false);
        var right = new HashingRemote(Server("sftp://u@b", "other\n"), canHash: false);
        var c = new FolderComparer(left, "sftp://u@a/srv", right, "sftp://u@b/srv", HashPolicy("sha256"), namesIgnoreCase: true);
        await c.RunAsync();
        Assert.Equal(FolderState.Different, c.Root.Descendants().Single(n => n.Name == "f4.txt").State);
        Assert.Equal(29, c.Root.Descendants().Count(n => !n.IsDirectory && n.State == FolderState.Same));
        Assert.Equal(30, left.Downloads);
    }
}
