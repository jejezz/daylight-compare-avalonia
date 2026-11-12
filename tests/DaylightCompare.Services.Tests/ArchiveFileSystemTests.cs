using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using DaylightCompare.Domain;
using DaylightCompare.Services;

namespace DaylightCompare.Services.Tests;

public sealed class ArchiveFileSystemTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-archive").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    static readonly (string Name, string? Text)[] Sample =
    [
        ("readme.txt", "hello"),
        ("src/", null),                 // 명시한 폴더
        ("src/main.c", "int main;"),
        ("docs/guide/intro.md", "# hi"), // 폴더 항목 없이 경로로만 생기는 폴더
        ("../evil.txt", "x"),            // 밖을 가리키는 이름은 목록에 넣지 않는다
    ];

    string MakeZip()
    {
        var path = Path.Combine(_dir, "sample.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, text) in Sample)
        {
            var entry = zip.CreateEntry(name);
            if (text is null) continue;
            using var w = new StreamWriter(entry.Open());
            w.Write(text);
        }
        return path;
    }

    string MakeTar(bool gzip)
    {
        var path = Path.Combine(_dir, gzip ? "sample.tar.gz" : "sample.tar");
        using var file = File.Create(path);
        using Stream data = gzip ? new GZipStream(file, CompressionLevel.Fastest) : file;
        using var writer = new TarWriter(data, TarEntryFormat.Pax, leaveOpen: true);
        foreach (var (name, text) in Sample)
        {
            var entry = new PaxTarEntry(text is null ? TarEntryType.Directory : TarEntryType.RegularFile, "./" + name);
            if (text is not null) entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            writer.WriteEntry(entry);
        }
        return path;
    }

    public static TheoryData<string> Kinds => ["zip", "tar", "tar.gz"];

    string Make(string kind) => kind switch { "zip" => MakeZip(), "tar" => MakeTar(false), _ => MakeTar(true) };

    static async Task<string> ReadAllAsync(IFileSystem fs, string path)
    {
        await using var s = await fs.OpenReadAsync(path);
        using var r = new StreamReader(s);
        return await r.ReadToEndAsync();
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task 루트와_안쪽_폴더를_목록으로_읽는다_폴더가_먼저(string kind)
    {
        var fs = new ArchiveFileSystem(Make(kind));
        var root = await fs.ListAsync(fs.Root);
        Assert.Equal(["docs", "src", "readme.txt"], root.Select(e => e.Name));
        Assert.True(root[0].IsDirectory);
        Assert.Equal(5, root[2].SizeBytes);
        Assert.Equal(ArchivePaths.Format(fs.ArchivePath, "readme.txt"), root[2].Path);

        var guide = await fs.ListAsync(ArchivePaths.Format(fs.ArchivePath, "docs/guide"));
        Assert.Equal(["intro.md"], guide.Select(e => e.Name));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task 파일_내용을_읽는다(string kind)
    {
        var fs = new ArchiveFileSystem(Make(kind));
        Assert.Equal("int main;", await ReadAllAsync(fs, ArchivePaths.Format(fs.ArchivePath, "src/main.c")));
        Assert.Equal("# hi", await ReadAllAsync(fs, ArchivePaths.Format(fs.ArchivePath, "docs/guide/intro.md")));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task 항목_정보와_없는_항목(string kind)
    {
        var fs = new ArchiveFileSystem(Make(kind));
        var main = await fs.StatAsync(ArchivePaths.Format(fs.ArchivePath, "src/main.c"));
        Assert.NotNull(main);
        Assert.False(main.IsDirectory);
        Assert.Equal(9, main.SizeBytes);
        Assert.Null(await fs.StatAsync(ArchivePaths.Format(fs.ArchivePath, "nope.txt")));
        Assert.Null(await fs.StatAsync(ArchivePaths.Format(fs.ArchivePath, "evil.txt")));
        var missing = await Assert.ThrowsAsync<FileOperationException>(() => fs.ListAsync(ArchivePaths.Format(fs.ArchivePath, "nope")));
        Assert.IsType<FileError.NotFound>(missing.Error);
    }

    [Fact]
    public async Task 쓰기는_모두_거절한다()
    {
        var fs = new ArchiveFileSystem(MakeZip());
        var file = ArchivePaths.Format(fs.ArchivePath, "readme.txt");
        foreach (var write in new Func<Task>[]
                 {
                     () => fs.OpenWriteAsync(file, overwrite: true),
                     () => fs.DeleteAsync(file),
                     () => fs.CreateDirectoryAsync(ArchivePaths.Format(fs.ArchivePath, "new")),
                     () => fs.RenameAsync(file, ArchivePaths.Format(fs.ArchivePath, "x.txt")),
                 })
        {
            var e = await Assert.ThrowsAsync<FileOperationException>(write);
            Assert.IsType<FileError.AccessDenied>(e.Error);
        }
    }

    [Fact]
    public async Task 압축_파일이_깨졌으면_파일_오류다()
    {
        var path = Path.Combine(_dir, "broken.zip");
        File.WriteAllText(path, "this is not a zip");
        var fs = new ArchiveFileSystem(path);
        await Assert.ThrowsAsync<FileOperationException>(() => fs.ListAsync(fs.Root));
    }

    [Theory]
    [InlineData("a.zip", ArchiveKind.Zip)]
    [InlineData("A.JAR", ArchiveKind.Zip)]
    [InlineData("x.tar", ArchiveKind.Tar)]
    [InlineData("x.tar.gz", ArchiveKind.TarGz)]
    [InlineData("x.tgz", ArchiveKind.TarGz)]
    [InlineData("x.txt", null)]
    public void 확장자로_형식을_고른다(string name, ArchiveKind? kind) => Assert.Equal(kind, ArchivePaths.KindOf(name));

    [Fact]
    public void 경로를_나누고_합친다()
    {
        var loc = ArchivePaths.TryParse(@"C:\data\a.zip!/src/main.c")!;
        Assert.Equal(@"C:\data\a.zip", loc.Archive);
        Assert.Equal("src/main.c", loc.Inner);
        Assert.Equal(@"C:\data\a.zip!/", ArchivePaths.Format(@"C:\data\a.zip"));
        Assert.Null(ArchivePaths.TryParse(@"C:\data\a.zip"));
        Assert.Equal("src/x", ArchivePaths.EntryName(@".\src\x"));
        Assert.Null(ArchivePaths.EntryName("a/../../b"));
    }
}
