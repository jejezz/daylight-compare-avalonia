using System.Globalization;
using Avalonia;
using DaylightCompare.App.Localization;
using DaylightCompare.Domain.Settings;

namespace DaylightCompare.App;

/// <summary>
/// 지금 쓰는 언어의 문구. 코드는 <see cref="Current"/>로 읽고, XAML은 <c>{DynamicResource Str.HomeEmptyTitle}</c>처럼 읽는다.
/// <see cref="Apply"/>가 언어를 바꾸면 리소스를 새로 채우므로 열려 있는 모든 창의 XAML 문구가 바로 바뀌고,
/// 코드가 만든 문구(상태줄, 작업 목록 등)는 <see cref="Changed"/>를 받아 다시 만든다.
/// </summary>
static class Strings
{
    public static IAppStrings Current { get; private set; } = KoreanStrings.Instance;

    /// <summary>언어가 바뀌었다. 코드가 만들어 둔 문구를 다시 만들어야 한다.</summary>
    public static event Action? Changed;

    /// <summary>설정의 언어(없으면 시스템)를 풀어 적용한다. 시스템은 OS가 한국어일 때만 한국어, 그 밖에는 영어.</summary>
    public static void Apply(AppLocale? setting)
    {
        var locale = DaylightCompare.Domain.Settings.Localization.Resolve(setting, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        Current = locale == AppLocale.Korean ? KoreanStrings.Instance : EnglishStrings.Instance;
        PublishResources();
        Changed?.Invoke();
    }

    /// <summary>XAML이 읽는 문구를 앱 리소스에 올린다 (<c>Str.&lt;이름&gt;</c>).</summary>
    static void PublishResources()
    {
        if (Application.Current is not { } app) return;
        var s = Current;
        void Set(string key, string value) => app.Resources["Str." + key] = value;

        Set("CommonCancel", s.CommonCancel);
        Set("CommonConfirm", s.CommonConfirm);
        Set("SettingsTooltip", s.SettingsTooltip);
        Set("HomeEmptyTitle", s.HomeEmptyTitle);
        Set("HomeEmptyHint", s.HomeEmptyHint);
        // Daylight Commander에는 이 줄이 없어 앱 바 [정보] 툴팁이 비어 있다 — Commander에도 필요한 수정.
        Set("AboutTooltip", s.AboutTooltip);

        Set("SettingsSectionAppearance", s.SettingsSectionAppearance);
        Set("SettingsMode", s.SettingsMode);
        Set("SettingsModeSystem", s.SettingsModeSystem);
        Set("ThemeLight", s.ThemeLight);
        Set("ThemeDark", s.ThemeDark);
        Set("SettingsPalette", s.SettingsPalette);
        Set("PaletteSaturn", s.PaletteSaturn);
        Set("PaletteTeal", s.PaletteTeal);
        Set("PaletteAmber", s.PaletteAmber);
        Set("PaletteRose", s.PaletteRose);
        Set("PaletteSlate", s.PaletteSlate);
        Set("PaletteHighContrast", s.PaletteHighContrast);
        Set("SettingsFont", s.SettingsFont);
        Set("FontSearchHint", s.FontSearchHint);
        Set("SettingsFontSize", s.SettingsFontSize);
        Set("SettingsFontSizeHint", s.SettingsFontSizeHint);
        Set("SettingsLanguage", s.SettingsLanguage);
        Set("LanguageSystemShort", s.LanguageSystemShort);



    }
}
