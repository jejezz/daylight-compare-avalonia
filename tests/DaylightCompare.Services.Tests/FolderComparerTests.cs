using System.IO.Compression;
using System.Text.Json.Nodes;
using DaylightCompare.Domain;
using DaylightCompare.Domain.Folder;
using DaylightCompare.Domain.Policy;
using DaylightCompare.Services.Folder;

namespace DaylightCompare.Services.Tests;

public sealed class FolderComparerTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-folder").FullName;
    string L => Path.Combine(_dir, "left");
    string R => Path.Combine(_dir, "right");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    void Put(string root, string rel, string text, int minutesAgo = 0)
    {
        var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc).AddMinutes(-minutesAgo));
    }

    /// <summary>
    /// 왼쪽·오른쪽: same(같음) · crlf(줄바꿈만, 시각 다름 → 규칙으로 같음) · changed(다름) · onlyLeft · onlyRight ·
    /// sub/deep.txt(다름) · .git/HEAD(기본 정책이 제외) · data.bin(필터로 뺄 것).
    /// </summary>
    void MakeTrees()
    {
        Put(L, "same.txt", "a\nb\n"); Put(R, "same.txt", "a\nb\n");
        Put(L, "crlf.txt", "a\nb\n", 30); Put(R, "crlf.txt", "a\r\nb\r\n");
        Put(L, "changed.txt", "one\n", 5); Put(R, "changed.txt", "two\n");
        Put(L, "onlyLeft.txt", "x"); Put(R, "onlyRight.txt", "y");
        Put(L, "sub/deep.txt", "1\n"); Put(R, "sub/deep.txt", "2\n");
        Put(L, ".git/HEAD", "ref"); Put(R, ".git/HEAD", "other");
        Put(L, "data.bin", "\0\u0001"); Put(R, "data.bin", "\0\u0002");
    }

    static FolderNode Find(FolderNode root, string rel) => root.Descendants().Single(n => n.RelativePath == rel);

    async Task<FolderComparer> Run(IFileSystem lfs, string lroot, IFileSystem rfs, string rroot, ComparePolicy? policy = null, FileMask? filter = null)
    {
        var c = new FolderComparer(lfs, lroot, rfs, rroot, policy ?? ComparePolicy.Builtin, namesIgnoreCase: true, filter);
        await c.RunAsync();
        return c;
    }

    [Fact]
    public async Task 로컬_두_폴더를_BC처럼_판정한다()
    {
        MakeTrees();
        var events = 0;
        var c = new FolderComparer(LocalFileSystem.Instance, L, LocalFileSystem.Instance, R, ComparePolicy.Builtin, namesIgnoreCase: true);
        c.Changed += _ => Interlocked.Increment(ref events);
        await c.RunAsync();

        Assert.True(c.Completed);
        Assert.True(events > 2); // 읽는 대로 알린다
        Assert.Equal(FolderState.Same, Find(c.Root, "same.txt").State);
        Assert.Equal(FolderState.Same, Find(c.Root, "crlf.txt").State); // 시각·줄바꿈만 다름 → 규칙으로 같음
        var changed = Find(c.Root, "changed.txt");
        Assert.Equal(FolderState.Different, changed.State);
        Assert.Equal(NewerSide.Right, changed.Newer);
        Assert.Equal(FolderState.LeftOrphan, Find(c.Root, "onlyLeft.txt").State);
        Assert.Equal(FolderState.RightOrphan, Find(c.Root, "onlyRight.txt").State);
        Assert.Equal(FolderState.Different, Find(c.Root, "sub").State);
        Assert.Equal(FolderState.Different, Find(c.Root, "sub/deep.txt").State);
        Assert.DoesNotContain(c.Root.Descendants(), n => n.Name == ".git"); // 정책 excludeFolders
        Assert.Equal(FolderState.Different, c.Root.State);
        Assert.Equal(["sub", "changed.txt", "crlf.txt", "data.bin", "onlyLeft.txt", "onlyRight.txt", "same.txt"], c.Root.Children.Select(n => n.Name));
    }

    [Fact]
    public async Task BC_필터_막대_문법으로_거른다()
    {
        MakeTrees();
        var c = await Run(LocalFileSystem.Instance, L, LocalFileSystem.Instance, R, filter: new FileMask(@"*.txt;-sub\"));
        Assert.DoesNotContain(c.Root.Descendants(), n => n.Name is "data.bin" or "sub");
        Assert.Contains(c.Root.Descendants(), n => n.Name == "same.txt");
    }

    [Fact]
    public async Task 같은_폴더면_모두_같다()
    {
        Put(L, "a.txt", "x"); Put(L, "s/b.txt", "y");
        Put(R, "a.txt", "x"); Put(R, "s/b.txt", "y");
        var c = await Run(LocalFileSystem.Instance, L, LocalFileSystem.Instance, R);
        Assert.Equal(FolderState.Same, c.Root.State);
        Assert.All(c.Root.Descendants(), n => Assert.Equal(FolderState.Same, n.State));
    }

    [Fact]
    public async Task 한쪽에만_있는_폴더의_안쪽도_고아로_보인다()
    {
        Put(L, "only/x.txt", "1");
        Directory.CreateDirectory(R);
        var c = await Run(LocalFileSystem.Instance, L, LocalFileSystem.Instance, R);
        Assert.Equal(FolderState.LeftOrphan, Find(c.Root, "only").State);
        Assert.Equal(FolderState.LeftOrphan, Find(c.Root, "only/x.txt").State);
    }

    [Fact]
    public async Task 해시_단계는_CRC로_비교한다()
    {
        Put(L, "a.txt", "a\nb\n", 10); Put(R, "a.txt", "a\r\nb\r\n"); // 바이트가 다르다 — 해시는 규칙을 보지 않는다
        Put(L, "b.txt", "same", 10); Put(R, "b.txt", "same");
        var policy = ComparePolicy.FromJson((JsonObject)JsonNode.Parse("""
            { "schema": 1, "defaults": { "folder": { "criteria": [ { "by": "hash", "algorithm": "crc32" } ] } } }
            """)!);
        var c = await Run(LocalFileSystem.Instance, L, LocalFileSystem.Instance, R, policy);
        Assert.Equal(FolderState.Different, Find(c.Root, "a.txt").State);
        Assert.Equal(FolderState.Same, Find(c.Root, "b.txt").State);
    }

    [Fact]
    public async Task 압축_파일과_폴더를_비교한다()
    {
        MakeTrees();
        var zip = Path.Combine(_dir, "left.zip");
        ZipFile.CreateFromDirectory(L, zip);
        var c = await Run(new ArchiveFileSystem(zip), ArchivePaths.Format(zip), LocalFileSystem.Instance, R);
        Assert.Equal(FolderState.Same, Find(c.Root, "same.txt").State);
        Assert.Equal(FolderState.Different, Find(c.Root, "changed.txt").State);
        Assert.Equal(FolderState.Different, Find(c.Root, "sub/deep.txt").State);
        Assert.Equal(FolderState.RightOrphan, Find(c.Root, "onlyRight.txt").State);
    }

    [Fact]
    public void CRC32는_표준_값과_같다() =>
        Assert.Equal(0xCBF43926u, Crc32.Append(0, "123456789"u8)); // IEEE 802.3 검사 값

    [Fact]
    public async Task 취소하면_멈춘다()
    {
        MakeTrees();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var c = new FolderComparer(LocalFileSystem.Instance, L, LocalFileSystem.Instance, R, ComparePolicy.Builtin, namesIgnoreCase: true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => c.RunAsync(cts.Token));
        Assert.False(c.Completed);
    }
}
