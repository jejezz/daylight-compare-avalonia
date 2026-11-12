using System.Text;
using DaylightCompare.Services;

namespace DaylightCompare.Services.Tests;

/// <summary>인코딩 오류 복구 (ROADMAP §6-1): 깨진 곳 찾기, 다른 인코딩 미리보기, 섞인 인코딩 복구, 저장 손실 경고.</summary>
public class EncodingRecoveryTests
{
    static EncodingRecoveryTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);
    static byte[] Cp949(string s) => Encoding.GetEncoding(949).GetBytes(s);

    /// <summary>대부분 UTF-8인데 3번째 줄만 CP949로 붙여 넣은 파일.</summary>
    static byte[] Mixed() => [.. Utf8("첫 줄 UTF-8\n둘째 줄도 UTF-8\n"), .. Cp949("셋째 줄은 CP949\n"), .. Utf8("넷째 줄 UTF-8\n")];

    [Fact]
    public void 대부분_UTF8이고_한_줄만_깨졌으면_UTF8_손상_텍스트로_읽는다()
    {
        var a = TextEncodings.Analyze(Mixed());
        Assert.Equal(TextEncodingKind.Utf8, a.Kind);
        Assert.True(a.IsDamaged);

        var file = TextFiles.Decode(Mixed(), DateTime.UtcNow).File!;
        Assert.False(file.Binary); // 예전에는 통째로 Latin-1 읽기 전용이었다
        Assert.True(file.DecodeErrors > 0);
        Assert.StartsWith("첫 줄 UTF-8\n둘째 줄도 UTF-8\n", file.Text);
    }

    [Fact]
    public void 깨진_곳의_줄_번호를_알려_준다()
    {
        var errors = TextEncodings.FindErrors(Mixed(), TextEncodingKind.Utf8);
        Assert.NotEmpty(errors);
        Assert.All(errors, e => Assert.Equal(3, e.Line));
    }

    [Fact]
    public void 섞인_인코딩을_줄_단위로_복구한다()
    {
        var repair = TextEncodings.RepairMixed(Mixed(), TextEncodingKind.Utf8, TextEncodingKind.Cp949);
        Assert.Equal("첫 줄 UTF-8\n둘째 줄도 UTF-8\n셋째 줄은 CP949\n넷째 줄 UTF-8\n", repair.Text);
        Assert.Equal([3], repair.RepairedLines);
        Assert.Empty(repair.BrokenLines);

        var (file, _) = TextFiles.DecodeMixed(Mixed(), DateTime.UtcNow, TextEncodingKind.Utf8, TextEncodingKind.Cp949);
        Assert.Equal(TextEncodingKind.Utf8, file.Encoding); // 복구한 결과는 UTF-8로 통일해 저장한다
        Assert.Equal(0, file.DecodeErrors);
    }

    [Fact]
    public void 다른_인코딩_미리보기는_오류가_적은_순이다()
    {
        var previews = TextEncodings.Previews(Cp949("한글 문서입니다\n"));
        Assert.Equal(TextEncodingKind.Cp949, previews.First(p => p.Kind != TextEncodingKind.Latin1).Kind);
        Assert.Equal(0, previews[0].Errors);
        Assert.Contains("한글 문서", previews.First(p => p.Kind == TextEncodingKind.Cp949).Preview);
        Assert.True(previews.First(p => p.Kind == TextEncodingKind.Utf8).Errors > 0);
    }

    [Fact]
    public void 잘못_고른_인코딩은_오류_수로_드러난다()
    {
        var file = TextFiles.Decode(Cp949("한글\n"), DateTime.UtcNow, TextEncodingKind.Utf8).File!;
        Assert.True(file.DecodeErrors > 0);
        Assert.Equal(0, TextFiles.Decode(Cp949("한글\n"), DateTime.UtcNow, TextEncodingKind.Cp949).File!.DecodeErrors);
    }

    [Fact]
    public void 저장하면_사라지는_글자를_찾는다()
    {
        var lost = TextEncodings.Unencodable("가나 😀 다 ✓", TextEncodingKind.Cp949);
        Assert.Contains(lost, l => l.Text == "😀");
        Assert.Empty(TextEncodings.Unencodable("가나 😀", TextEncodingKind.Utf8));
    }

    [Fact]
    public void 정상_파일은_손상으로_보지_않는다()
    {
        Assert.False(TextEncodings.Analyze(Utf8("평범한 UTF-8\n")).IsDamaged);
        Assert.False(TextEncodings.Analyze(Cp949("평범한 CP949 한글 문서\n")).IsDamaged);
    }
}
