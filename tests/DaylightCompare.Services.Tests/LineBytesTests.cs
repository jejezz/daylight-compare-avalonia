using System.Text;
using DaylightCompare.Services;

namespace DaylightCompare.Services.Tests;

public class LineBytesTests
{
    static string Hex(ReadOnlyMemory<byte> b) => Convert.ToHexString(b.Span);

    [Fact]
    public void 줄_끝_바이트까지가_한_줄이고_CRLF_LF_CR이_섞여도_따로_센다()
    {
        var bytes = Encoding.UTF8.GetBytes("a\r\nb\nc\rd");
        var map = LineByteMap.Build(bytes, TextEncodingKind.Utf8, false);
        Assert.Equal(4, map.LineCount);
        Assert.Equal("610D0A", Hex(map.Line(0)));
        Assert.Equal("620A", Hex(map.Line(1)));
        Assert.Equal("630D", Hex(map.Line(2)));
        Assert.Equal("64", Hex(map.Line(3)));
    }

    [Fact]
    public void 끝의_줄바꿈_뒤에_빈_줄을_더하지_않고_빈_파일은_줄이_없다()
    {
        Assert.Equal(2, LineByteMap.Build("a\nb\n"u8.ToArray(), TextEncodingKind.Utf8, false).LineCount);
        Assert.Equal(0, LineByteMap.Build([], TextEncodingKind.Utf8, false).LineCount);
        Assert.Empty(LineByteMap.Build("a\n"u8.ToArray(), TextEncodingKind.Utf8, false).Line(5).ToArray());
    }

    [Fact]
    public void BOM은_첫_줄에_들어간다()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat("x\ny"u8.ToArray()).ToArray();
        var map = LineByteMap.Build(bytes, TextEncodingKind.Utf8, true);
        Assert.Equal(3, map.BomLength);
        Assert.Equal("EFBBBF780A", Hex(map.Line(0)));
        Assert.Equal("79", Hex(map.Line(1)));
    }

    [Fact]
    public void UTF16은_두_바이트_단위로_줄을_나눈다()
    {
        var le = new UnicodeEncoding(false, true).GetPreamble().Concat(new UnicodeEncoding(false, false).GetBytes("a\r\nb")).ToArray();
        var map = LineByteMap.Build(le, TextEncodingKind.Utf16Le, true);
        Assert.Equal(2, map.LineCount);
        Assert.Equal("FFFE61000D000A00", Hex(map.Line(0)));
        Assert.Equal("6200", Hex(map.Line(1)));
        var be = new UnicodeEncoding(true, false).GetBytes("a\nb");
        var mapBe = LineByteMap.Build(be, TextEncodingKind.Utf16Be, false);
        Assert.Equal("0061000A", Hex(mapBe.Line(0)));
    }

    [Fact]
    public void CP949_한글은_줄_끝으로_잘못_세지_않는다()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(949).GetBytes("가나다\n라마\n");
        var map = LineByteMap.Build(bytes, TextEncodingKind.Cp949, false);
        Assert.Equal(2, map.LineCount);
        Assert.Equal(7, map.Line(0).Length);
    }

    [Fact]
    public void 바이트_차이는_끼어든_바이트만_다르게_표시한다()
    {
        var (l, r) = ByteDiff.Differences("ir0 = &irrx;"u8, "ir0\t= &irrx;"u8);
        Assert.Equal(1, l.Count(x => x)); // 공백 하나
        Assert.Equal(1, r.Count(x => x)); // 탭 하나
        var (l2, r2) = ByteDiff.Differences("abc"u8, "abXc"u8);
        Assert.Empty(l2.Where(x => x));
        Assert.Single(r2.Where(x => x));
    }
}
