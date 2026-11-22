using System.Security.Cryptography;
using System.Text.Json.Nodes;
using DaylightCompare.Domain.Folder;
using DaylightCompare.Domain.Policy;
using DaylightCompare.Services.Folder;

namespace DaylightCompare.Services.Tests;

/// <summary>로컬 대 SFTP 폴더 비교 (ROADMAP 4단계 끝나는 조건의 엔진 쪽). 파이썬 asyncssh가 없으면 건너뜀.</summary>
public sealed class FolderCompareSftpTests : IClassFixture<SftpServerFixture>, IDisposable
{
    sealed class MemoryHostKeys : IHostKeyStore
    {
        readonly Dictionary<string, string> _keys = [];
        public string? Get(string hostPort) => _keys.GetValueOrDefault(hostPort);
        public void Set(string hostPort, string fingerprint) => _keys[hostPort] = fingerprint;
    }

    readonly SftpServerFixture _server;
    readonly RemoteSessions _sessions = new(new MemoryHostKeys());
    readonly string _local = Directory.CreateTempSubdirectory("dc-folder-local").FullName;

    public FolderCompareSftpTests(SftpServerFixture server)
    {
        _server = server;
        foreach (var entry in new DirectoryInfo(server.Root).EnumerateFileSystemInfos())
            if (entry is DirectoryInfo dir) dir.Delete(recursive: true); else entry.Delete();
    }

    public void Dispose()
    {
        _sessions.Dispose();
        Directory.Delete(_local, recursive: true);
    }

    static void Put(string root, string rel, string text)
    {
        var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [FactIfSftpServer]
    public async Task 로컬_대_SFTP_폴더를_비교한다()
    {
        // 서버 루트는 테스트 서버가 그대로 내보내는 로컬 폴더다.
        for (var i = 0; i < 40; i++)
        {
            Put(_local, $"d{i % 4}/f{i}.txt", $"line {i}\n");
            Put(_server.Root, $"d{i % 4}/f{i}.txt", i == 7 ? "changed\n" : $"line {i}\r\n"); // 줄바꿈만 다름 → 규칙으로 같음
        }
        Put(_local, "onlyLocal.txt", "x");
        await _sessions.ConnectAsync(new RemoteConnectRequest("sftp", "127.0.0.1", _server.Port, "user", "pw"));

        var c = new FolderComparer(LocalFileSystem.Instance, _local, _sessions.FileSystemFor(_server.Url()), _server.Url(),
            ComparePolicy.Builtin, namesIgnoreCase: true);
        await c.RunAsync();

        var files = c.Root.Descendants().Where(n => !n.IsDirectory).ToList();
        Assert.Equal(41, files.Count);
        Assert.Equal(FolderState.Different, files.Single(n => n.Name == "f7.txt").State);
        Assert.Equal(FolderState.LeftOrphan, files.Single(n => n.Name == "onlyLocal.txt").State);
        Assert.All(files.Where(n => n.Name is not ("f7.txt" or "onlyLocal.txt")), n => Assert.Equal(FolderState.Same, n.State));
        Assert.Equal(FolderState.Different, c.Root.State);
    }

    [FactIfSftpServer]
    public async Task 서버에서_sha256sum으로_해시를_구한다()
    {
        Put(_server.Root, "a.txt", "alpha\n");
        Put(_server.Root, "it's b.txt", "beta\n");
        await _sessions.ConnectAsync(new RemoteConnectRequest("sftp", "127.0.0.1", _server.Port, "user", "pw"));
        var hasher = Assert.IsAssignableFrom<IServerHasher>(_sessions.FileSystemFor(_server.Url()));

        var a = _server.Url("a.txt");
        var b = _server.Url("it's b.txt");
        var hashes = await hasher.HashSha256Async([a, b, _server.Url("missing.txt")]);

        Assert.NotNull(hashes);
        Assert.Equal(2, hashes.Count); // 없는 파일은 빠진다
        Assert.Equal(SHA256.HashData("alpha\n"u8), hashes[a]);
        Assert.Equal(SHA256.HashData("beta\n"u8), hashes[b]);
    }

    [FactIfSftpServer]
    public async Task 로컬_대_SFTP를_서버_해시로_비교한다()
    {
        for (var i = 0; i < 40; i++)
        {
            Put(_local, $"d{i % 4}/f{i}.txt", $"line {i}\n");
            Put(_server.Root, $"d{i % 4}/f{i}.txt", i == 7 ? "changed\n" : $"line {i}\n");
        }
        await _sessions.ConnectAsync(new RemoteConnectRequest("sftp", "127.0.0.1", _server.Port, "user", "pw"));
        var policy = ComparePolicy.FromJson((JsonObject)JsonNode.Parse("""
            { "schema": 1, "defaults": { "folder": { "criteria": [ { "by": "hash", "algorithm": "crc32" } ] } } }
            """)!);

        var c = new FolderComparer(LocalFileSystem.Instance, _local, _sessions.FileSystemFor(_server.Url()), _server.Url(), policy, namesIgnoreCase: true);
        await c.RunAsync();

        var files = c.Root.Descendants().Where(n => !n.IsDirectory).ToList();
        Assert.Equal(40, files.Count);
        Assert.Equal(FolderState.Different, files.Single(n => n.Name == "f7.txt").State);
        Assert.All(files.Where(n => n.Name != "f7.txt"), n => Assert.Equal(FolderState.Same, n.State));
    }
}
