using DaylightCompare.Domain.Binary;

namespace DaylightCompare.Domain.Tests;

public class FirmwareFormatsTests
{
    // Intel HEX 설명서의 예: 0x0100부터 16바이트 + 끝 레코드
    const string Classic = ":10010000214601360121470136007EFE09D2190140\n:00000001FF\n";

    [Fact]
    public void Intel_HEX_데이터_레코드를_이미지로_푼다()
    {
        var r = FirmwareFormats.FromIntelHex(Classic, fill: 0xFF);
        Assert.Null(r.Error);
        var img = r.Image!;
        Assert.Equal(0x100, img.BaseAddress);
        Assert.Equal(16, img.Data.Length);
        Assert.Equal(Convert.FromHexString("214601360121470136007EFE09D21901"), img.Data);
        Assert.Empty(img.Warnings);
        Assert.Equal(2, img.Records);
    }

    [Fact]
    public void 주소_사이의_빈틈은_채움_값으로_채운다()
    {
        // 0x0000에 2바이트, 0x0008에 2바이트
        var text = ":020000000102FB\n:020008000304EF\n:00000001FF\n";
        var img = FirmwareFormats.FromIntelHex(text, fill: 0xAA).Image!;
        Assert.Equal(10, img.Data.Length);
        Assert.Equal(Convert.FromHexString("0102AAAAAAAAAAAA0304"), img.Data);
        Assert.Equal(Convert.FromHexString("01020000000000000304"), FirmwareFormats.FromIntelHex(text, fill: 0).Image!.Data);
    }

    [Fact]
    public void 확장_선형_주소와_확장_세그먼트_주소를_더한다()
    {
        // 상위 주소 0x0800 → 0x08000000, 그 뒤 4바이트
        var linear = ":020000040800F2\n:0400000011223344" + "52" + "\n:00000001FF\n";
        var img = FirmwareFormats.FromIntelHex(linear, 0xFF).Image!;
        Assert.Equal(0x08000000, img.BaseAddress);
        Assert.Equal(Convert.FromHexString("11223344"), img.Data);
        // 세그먼트 0x1000 → ×16 = 0x10000
        var segment = ":020000021000EC\n:0200000055AA" + "FF" + "\n:00000001FF\n";
        var seg = FirmwareFormats.FromIntelHex(segment, 0xFF).Image!;
        Assert.Equal(0x10000, seg.BaseAddress);
    }

    [Fact]
    public void 체크섬이_틀린_줄과_이상한_줄은_건너뛰고_경고한다()
    {
        var text = ":10010000214601360121470136007EFE09D2190141\n garbage\n:020000000102FB\n:00000001FF\n";
        var r = FirmwareFormats.FromIntelHex(text, 0xFF);
        var img = r.Image!;
        Assert.Equal(2, img.Warnings.Count);
        Assert.Contains("체크섬", img.Warnings[0]);
        Assert.Equal(Convert.FromHexString("0102"), img.Data);
    }

    [Fact]
    public void 데이터가_없으면_오류이고_끝_레코드_뒤는_읽지_않는다()
    {
        Assert.NotNull(FirmwareFormats.FromIntelHex(":00000001FF\n", 0xFF).Error);
        var img = FirmwareFormats.FromIntelHex(":020000000102FB\n:00000001FF\n:020000000304F7\n", 0xFF).Image!;
        Assert.Equal(Convert.FromHexString("0102"), img.Data);
    }

    [Fact]
    public void S_Record를_종류별_주소_너비로_푼다()
    {
        // S1(16비트 주소): 0x0000에 16바이트 (S-Record 설명서의 예)
        var s1 = "S0030000FC\nS1130000285F245F2212226A000424290008237C2A\nS9030000FC\n";
        var img = FirmwareFormats.FromSRecord(s1, 0xFF).Image!;
        Assert.Equal(0, img.BaseAddress);
        Assert.Equal(Convert.FromHexString("285F245F2212226A000424290008237C"), img.Data);
        Assert.Empty(img.Warnings);
        // S3(32비트 주소) 0x08001000
        var s3 = "S3090800100001020304" + Checksum("090800100001020304") + "\nS70500000000FA\n";
        var s3img = FirmwareFormats.FromSRecord(s3, 0xFF).Image!;
        Assert.Equal(0x08001000, s3img.BaseAddress);
        Assert.Equal(Convert.FromHexString("01020304"), s3img.Data);
    }

    static string Checksum(string hex)
    {
        var sum = Convert.FromHexString(hex).Sum(b => b);
        return ((~sum) & 0xFF).ToString("X2");
    }

    [Fact]
    public void S_Record_체크섬이_틀린_줄은_건너뛴다()
    {
        var r = FirmwareFormats.FromSRecord("S1130000285F245F2212226A000424290008237C2B\nS1050000AABB95\n", 0xFF);
        Assert.Single(r.Image!.Warnings);                       // 첫 줄만 건너뛴다
        Assert.Equal(new byte[] { 0xAA, 0xBB }, r.Image.Data);
    }

    [Fact]
    public void 변환기_이름으로_고르고_모르는_이름은_오류다()
    {
        Assert.NotNull(FirmwareFormats.Convert("ihex-to-bin", System.Text.Encoding.ASCII.GetBytes(Classic), 0xFF).Image);
        Assert.NotNull(FirmwareFormats.Convert("없음", [], 0xFF).Error);
    }

    [Fact]
    public void 시작_주소가_다른_두_이미지를_주소끼리_맞춘다()
    {
        var a = new FirmwareImage([1, 2, 3, 4], 0x100, 1, []);
        var b = new FirmwareImage([9, 9], 0x102, 1, []);
        var (la, lb) = FirmwareFormats.Align(a, b, fill: 0xFF);
        Assert.Equal(0x100, la.BaseAddress);
        Assert.Equal(0x100, lb.BaseAddress);
        Assert.Equal(4, la.Data.Length);
        Assert.Equal(new byte[] { 0xFF, 0xFF, 9, 9 }, lb.Data);
        var (same1, same2) = FirmwareFormats.Align(la, lb, 0xFF);
        Assert.Same(la, same1);
        Assert.Same(lb, same2);
    }
}
