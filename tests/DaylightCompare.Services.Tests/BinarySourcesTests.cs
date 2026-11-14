using DaylightCompare.Domain.Binary;
using DaylightCompare.Services;

namespace DaylightCompare.Services.Tests;

public sealed class BinarySourcesTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dc-binsrc").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void 로컬_파일은_위치로_읽고_끝을_넘으면_적게_읽는다()
    {
        var path = Path.Combine(_dir, "a.bin");
        File.WriteAllBytes(path, [10, 20, 30, 40, 50]);
        using var src = new FileByteSource(path);
        Assert.Equal(5, src.Length);
        var buf = new byte[3];
        Assert.Equal(3, src.Read(1, buf));
        Assert.Equal(new byte[] { 20, 30, 40 }, buf);
        Assert.Equal(2, src.Read(3, buf));
        Assert.Equal(0, src.Read(5, buf));
        Assert.Equal(0, src.Read(-1, buf));
    }

    [Fact]
    public async Task 열기는_파일_정보를_주고_없는_파일은_오류다()
    {
        var path = Path.Combine(_dir, "b.bin");
        File.WriteAllBytes(path, new byte[1000]);
        using var ok = await BinarySources.OpenAsync(path);
        Assert.True(ok.Ok);
        Assert.Equal(1000, ok.Length);
        var missing = await BinarySources.OpenAsync(Path.Combine(_dir, "없음.bin"));
        Assert.False(missing.Ok);
        Assert.NotNull(missing.Error);
    }

    [Fact]
    public async Task 쓰는_중인_파일도_읽고_큰_파일을_통째로_올리지_않는다()
    {
        var path = Path.Combine(_dir, "big.bin");
        await using (var fs = File.Create(path))
        {
            fs.SetLength(300L * 1024 * 1024); // 희소 파일 300MB
            fs.Position = 200L * 1024 * 1024;
            fs.WriteByte(0x7F);
        }
        using var src = new FileByteSource(path);
        var one = new byte[1];
        Assert.Equal(1, src.Read(200L * 1024 * 1024, one));
        Assert.Equal(0x7F, one[0]);
        // 두 큰 파일의 비교: 같은 위치의 한 바이트 차이만 나온다 (블록 단위로 건너뛰므로 끊기지 않는다).
        var path2 = Path.Combine(_dir, "big2.bin");
        File.Copy(path, path2);
        await using (var fs = new FileStream(path2, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            fs.Position = 123_456_789;
            fs.WriteByte(0x01);
        }
        using var a = new FileByteSource(path);
        using var b = new FileByteSource(path2);
        var r = BinaryDiff.Compare(a, b);
        Assert.Equal(1, r.DifferenceCount);
        Assert.Equal(1, r.DifferentBytes);
    }
}
