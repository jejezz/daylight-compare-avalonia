using DaylightCompare.Domain.Theme;

namespace DaylightCompare.Domain.Tests;

public class AppColorsTests
{
    [Fact]
    public void 색_구성은_6종이고_주색이_모두_다르다()
    {
        var all = Enum.GetValues<ColorPalette>();
        Assert.Equal(6, all.Length);
        Assert.Equal(6, all.Select(p => p.DarkPrimary()).Distinct().Count());
        Assert.Equal(6, all.Select(p => p.LightPrimary()).Distinct().Count());
    }

    [Fact]
    public void 새턴_다크는_원본_색이다()
    {
        var c = AppColors.For(ColorPalette.Saturn, dark: true);
        Assert.Equal(0xFF0A0E14u, c.Bg);
        Assert.Equal(0xFF151D27u, c.Surface);
        Assert.Equal(0xFF4C9DFFu, c.Primary);
        Assert.Equal(0xFFFFFFFFu, c.OnPrimary); // 새턴은 다크에서도 흰 글자
    }

    [Fact]
    public void 새턴_외_다크_색_구성은_밝은_주색이라_어두운_글자다()
    {
        foreach (var p in Enum.GetValues<ColorPalette>().Where(p => p is not (ColorPalette.Saturn or ColorPalette.HighContrast)))
            Assert.Equal(0xFF0A0E14u, AppColors.For(p, dark: true).OnPrimary);
    }

    [Fact]
    public void 라이트는_흰_글자_주색이다()
    {
        foreach (var p in Enum.GetValues<ColorPalette>().Where(p => p != ColorPalette.HighContrast))
            Assert.Equal(0xFFFFFFFFu, AppColors.For(p, dark: false).OnPrimary);
    }

    [Fact]
    public void 고대비는_순흑_순백이다()
    {
        var dark = AppColors.For(ColorPalette.HighContrast, dark: true);
        Assert.Equal(0xFF000000u, dark.Bg);
        Assert.Equal(0xFFFFFFFFu, dark.TextHi);
        var light = AppColors.For(ColorPalette.HighContrast, dark: false);
        Assert.Equal(0xFFFFFFFFu, light.Bg);
        Assert.Equal(0xFF000000u, light.TextHi);
    }
}

/// <summary>비교 창은 도구 모음·경로 줄(<c>SurfaceHi</c>)과 편집기·목록 바탕(<c>AppEditorBg</c>)을 다른 색으로 둔다 — 어디서부터 편집기인지 분명하게.</summary>
public class ChromeContrastTests
{
    static double Luminance(uint argb)
    {
        static double Channel(uint v) { var c = v / 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel((argb >> 16) & 0xFF) + 0.7152 * Channel((argb >> 8) & 0xFF) + 0.0722 * Channel(argb & 0xFF);
    }

    static double Ratio(uint a, uint b)
    {
        var (hi, lo) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (hi + 0.05) / (lo + 0.05);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 틀_띠와_편집기_바탕은_모든_색_구성에서_구분된다(bool dark)
    {
        foreach (var palette in Enum.GetValues<ColorPalette>())
        {
            var c = AppColors.For(palette, dark);
            var editor = AppColors.EditorBackground(c, dark);
            Assert.True(Ratio(c.SurfaceHi, editor) >= 1.2, $"{palette} dark={dark}: 틀 띠와 편집기 바탕의 밝기 차가 작다 ({Ratio(c.SurfaceHi, editor):F2})");
        }
    }
}
