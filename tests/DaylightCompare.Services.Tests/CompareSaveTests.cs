using System.Text;
using DaylightCompare.Services;

namespace DaylightCompare.Services.Tests;

public sealed class CompareSaveTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-save").FullName;
    string Backups => Path.Combine(_dir, "backups");

    static CompareSaveTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    async Task<(string Path, EditorTextFile File)> Open(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        await File.WriteAllBytesAsync(path, bytes);
        var src = (CompareSource.Text)await CompareSources.LoadAsync(path);
        return (path, src.File);
    }

    [Fact]
    public async Task 읽은_인코딩_줄바꿈_BOM_그대로_저장하고_백업을_남긴다()
    {
        var (path, file) = await Open("a.txt", [0xEF, 0xBB, 0xBF, .. "one\r\ntwo\r\n"u8]);
        var check = CompareSave.Check(path, file, "one\nTWO\n");
        Assert.False(check.NeedsConfirmation);

        var (saved, backup) = await CompareSave.SaveAsync(path, "one\nTWO\n", file, Backups, now: new DateTime(2026, 11, 12, 9, 0, 0));
        Assert.Equal([0xEF, 0xBB, 0xBF, .. "one\r\nTWO\r\n"u8], await File.ReadAllBytesAsync(path));
        Assert.NotNull(backup);
        Assert.EndsWith(Path.Combine("20261112", "a.txt.090000.bak"), backup);
        Assert.Equal([0xEF, 0xBB, 0xBF, .. "one\r\ntwo\r\n"u8], await File.ReadAllBytesAsync(backup!));
        Assert.False(TextFiles.ChangedOnDisk(path, saved)); // 새 기준값
    }

    [Fact]
    public async Task 같은_초에_여러_번_저장하면_백업에_번호를_붙인다()
    {
        var (path, file) = await Open("b.txt", "x\n"u8.ToArray());
        var now = new DateTime(2026, 11, 12, 9, 0, 0);
        var (f1, b1) = await CompareSave.SaveAsync(path, "y\n", file, Backups, now: now);
        var (_, b2) = await CompareSave.SaveAsync(path, "z\n", f1, Backups, now: now);
        Assert.NotEqual(b1, b2);
        Assert.EndsWith(".090000.1.bak", b2);
        Assert.Equal("y\n", await File.ReadAllTextAsync(b2!));
    }

    [Fact]
    public async Task 디스크에서_바뀌었으면_확인이_필요하다()
    {
        var (path, file) = await Open("c.txt", "x\n"u8.ToArray());
        await File.WriteAllTextAsync(path, "changed elsewhere\n");
        File.SetLastWriteTimeUtc(path, file.Modified.AddMinutes(1));
        Assert.True(CompareSave.Check(path, file, "x\n").ChangedOnDisk);
    }

    [Fact]
    public async Task CP949에_없는_글자는_손실로_알리고_UTF8로_바꿔_저장할_수_있다()
    {
        var (path, file) = await Open("k.txt", Encoding.GetEncoding(949).GetBytes("한글 문서입니다\n"));
        Assert.Equal(TextEncodingKind.Cp949, file.Encoding);
        var check = CompareSave.Check(path, file, "한글 문서 😀\n");
        Assert.Contains(check.Lost, l => l.Text == "😀");

        var (saved, _) = await CompareSave.SaveAsync(path, "한글 문서 😀\n", file, Backups, TextEncodingKind.Utf8);
        Assert.Equal(TextEncodingKind.Utf8, saved.Encoding);
        Assert.Equal("한글 문서 😀\n", await File.ReadAllTextAsync(path, Encoding.UTF8));
    }

    [Fact]
    public async Task 깨진_곳이_남아_있으면_알린다()
    {
        var (path, file) = await Open("m.txt", [.. "ok 한\n"u8, 0xC7, 0xD1, (byte)'\n']);
        Assert.True(file.DecodeErrors > 0);
        Assert.True(CompareSave.Check(path, file, file.Text).Damaged > 0);
        Assert.Equal(0, CompareSave.Check(path, file, "fixed\n").Damaged); // 깨진 글자를 지웠으면 묻지 않는다
    }

    [Fact]
    public void 오래된_백업_날짜_폴더만_지운다()
    {
        Directory.CreateDirectory(Path.Combine(Backups, "20260101"));
        Directory.CreateDirectory(Path.Combine(Backups, "20261110"));
        Directory.CreateDirectory(Path.Combine(Backups, "keep-me"));
        CompareSave.Prune(Backups, new DateTime(2026, 11, 12));
        Assert.False(Directory.Exists(Path.Combine(Backups, "20260101")));
        Assert.True(Directory.Exists(Path.Combine(Backups, "20261110")));
        Assert.True(Directory.Exists(Path.Combine(Backups, "keep-me")));
    }
}
