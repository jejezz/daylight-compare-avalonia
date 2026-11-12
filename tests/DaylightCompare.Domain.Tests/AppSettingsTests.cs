using DaylightCompare.Domain.Settings;
using DaylightCompare.Domain.Theme;

namespace DaylightCompare.Domain.Tests;

public class AppSettingsTests
{
    [Fact]
    public void 기본값은_원본과_같다_시스템_테마_새턴_보통_글자()
    {
        var s = new AppSettings();
        Assert.Equal(ThemeMode.System, s.Theme);
        Assert.Equal(ColorPalette.Saturn, s.Palette);
        Assert.Equal(1.0, s.TextScale);
        Assert.Null(s.Locale);
        Assert.Null(s.UiFontFamily);
        Assert.Null(s.Window);
    }

    [Fact]
    public void 글자_크기는_범위_안_5퍼센트_단위로_맞춘다()
    {
        Assert.Equal(1.15, new AppSettings().WithTextScale(1.14).TextScale);
        Assert.Equal(TextScales.Max, new AppSettings().WithTextScale(3).TextScale);
        Assert.Equal(TextScales.Min, new AppSettings().WithTextScale(0.1).TextScale);
        Assert.Equal(1.0, TextScales.Snap(double.NaN));
    }

    [Fact]
    public void 범위_밖_배율은_범위_안으로_고쳐_읽는다()
    {
        var s = (new AppSettings() with { TextScale = 1.7 }).Normalized();
        Assert.Equal(TextScales.Max, s.TextScale);
        Assert.Same(s, s.Normalized());
        Assert.Equal(1.25, (new AppSettings() with { TextScale = 1.25 }).Normalized().TextScale);
    }

    [Fact]
    public void 색_구성은_원본의_여섯_가지와_주색이다()
    {
        Assert.Equal(6, Enum.GetValues<ColorPalette>().Length);
        Assert.Equal(0xFF4C9DFFu, ColorPalette.Saturn.DarkPrimary());
        Assert.Equal(0xFF2C6BE0u, ColorPalette.Saturn.LightPrimary());
        Assert.Equal(0xFFFFD60Au, ColorPalette.HighContrast.DarkPrimary());
    }

    [Fact]
    public void 언어는_설정이_있으면_그대로_없으면_OS가_한국어일_때만_한국어다()
    {
        Assert.Equal(AppLocale.English, Localization.Resolve(AppLocale.English, "ko"));
        Assert.Equal(AppLocale.Korean, Localization.Resolve(AppLocale.Korean, "en"));
        Assert.Equal(AppLocale.Korean, Localization.Resolve(null, "ko"));
        Assert.Equal(AppLocale.Korean, Localization.Resolve(null, "KO"));
        Assert.Equal(AppLocale.English, Localization.Resolve(null, "en"));
        Assert.Equal(AppLocale.English, Localization.Resolve(null, "ja"));
        Assert.Equal(AppLocale.English, Localization.Resolve(null, ""));
    }
}
