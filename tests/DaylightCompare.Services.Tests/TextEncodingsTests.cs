using System.Text;
using DaylightCompare.Services;

namespace DaylightCompare.Services.Tests;

public class TextEncodingsTests
{
    static TextEncodingsTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    const string Korean = "안녕하세요. 비교할 두 파일입니다.\n// 주석: 한글 소스\nint x = 1;\n";
    const string Japanese = "こんにちは。比較するファイルです。\nカタカナとひらがな\n";

    static byte[] Cp(int codePage, string text) => Encoding.GetEncoding(codePage).GetBytes(text);

    [Fact]
    public void BOM이_있으면_그_인코딩이고_BOM을_뗀다()
    {
        Assert.Equal((TextEncodingKind.Utf8, true), TextEncodings.Detect([0xEF, 0xBB, 0xBF, (byte)'a']));
        Assert.Equal((TextEncodingKind.Utf16Le, true), TextEncodings.Detect([0xFF, 0xFE, (byte)'a', 0]));
        Assert.Equal((TextEncodingKind.Utf16Be, true), TextEncodings.Detect([0xFE, 0xFF, 0, (byte)'a']));
        Assert.Equal("a", TextEncodings.Decode([0xFF, 0xFE, (byte)'a', 0], TextEncodingKind.Utf16Le));
    }

    [Fact]
    public void BOM_없는_UTF16도_알아보고_바이너리로_보지_않는다()
    {
        var le = Encoding.Unicode.GetBytes("hello world\r\nline two\r\n");
        var be = Encoding.BigEndianUnicode.GetBytes("hello world\r\nline two\r\n");
        Assert.Equal((TextEncodingKind.Utf16Le, false), TextEncodings.Detect(le));
        Assert.Equal((TextEncodingKind.Utf16Be, false), TextEncodings.Detect(be));
    }

    [Fact]
    public void UTF8은_UTF8이다()
    {
        Assert.Equal((TextEncodingKind.Utf8, false), TextEncodings.Detect(Encoding.UTF8.GetBytes(Korean)));
        Assert.Equal((TextEncodingKind.Utf8, false), TextEncodings.Detect("plain ascii"u8));
    }

    [Fact]
    public void CP949_한글은_CP949이다()
    {
        var bytes = Cp(949, Korean);
        Assert.Equal((TextEncodingKind.Cp949, false), TextEncodings.Detect(bytes));
        Assert.Equal(Korean, TextEncodings.Decode(bytes, TextEncodingKind.Cp949));
    }

    [Fact]
    public void ShiftJIS_일본어는_ShiftJIS이다()
    {
        var bytes = Cp(932, Japanese);
        Assert.Equal((TextEncodingKind.ShiftJis, false), TextEncodings.Detect(bytes));
        Assert.Equal(Japanese, TextEncodings.Decode(bytes, TextEncodingKind.ShiftJis));
    }

    [Fact]
    public void NUL이_섞인_바이너리는_Latin1이다()
    {
        Assert.Equal((TextEncodingKind.Latin1, false), TextEncodings.Detect([0x89, 0x50, 0x4E, 0x47, 0x00, 0x13, 0x00, 0x00, 0x7F, 0x00, 0x01]));
    }

    [Theory]
    [InlineData(TextEncodingKind.Utf8, true)]
    [InlineData(TextEncodingKind.Utf16Le, true)]
    [InlineData(TextEncodingKind.Utf16Be, false)]
    [InlineData(TextEncodingKind.Cp949, false)]
    public void 쓰고_다시_읽으면_같다(TextEncodingKind kind, bool bom)
    {
        var bytes = TextEncodings.Encode(Korean, kind, bom);
        Assert.Equal((kind, bom), TextEncodings.Detect(bytes));
        Assert.Equal(Korean, TextEncodings.Decode(bytes, kind));
    }

    [Fact]
    public void CP949에_없는_글자는_손실_없이_쓸_수_없다()
    {
        Assert.True(TextEncodings.CanEncode(Korean, TextEncodingKind.Cp949));
        Assert.False(TextEncodings.CanEncode("이모지 😀", TextEncodingKind.Cp949));
        Assert.True(TextEncodings.CanEncode("이모지 😀", TextEncodingKind.Utf8));
    }

    [Fact]
    public void 텍스트_파일은_감지한_인코딩으로_읽고_그대로_저장한다()
    {
        var file = TextFiles.Decode(Cp(949, Korean), DateTime.UtcNow).File!;
        Assert.Equal(TextEncodingKind.Cp949, file.Encoding);
        Assert.False(file.Binary);
        Assert.Equal(Korean, file.Text);
        Assert.Equal(Cp(949, Korean), TextFiles.Encode(file.Text, file.HasBom, file.Encoding));
    }

    [Fact]
    public void 사용자가_고른_인코딩으로_다시_읽는다()
    {
        var bytes = Cp(949, Korean);
        var wrong = TextFiles.Decode(bytes, DateTime.UtcNow, TextEncodingKind.Utf8).File!;
        Assert.Equal(TextEncodingKind.Utf8, wrong.Encoding);
        Assert.Contains('�', wrong.Text); // 틀린 인코딩은 대체 문자로 보인다
        var right = TextFiles.Decode(bytes, DateTime.UtcNow, TextEncodingKind.Cp949).File!;
        Assert.Equal(Korean, right.Text);
    }

    [Fact]
    public void 앞부분만_보고_감지해도_잘린_글자_때문에_틀리지_않는다()
    {
        // 64KB 표본 경계가 한글(UTF-8 3바이트) 가운데에 걸리게 만든다.
        var text = new string('a', 64 * 1024 - 1) + string.Concat(Enumerable.Repeat("한글", 100));
        Assert.Equal((TextEncodingKind.Utf8, false), TextEncodings.Detect(Encoding.UTF8.GetBytes(text)));
    }
}
