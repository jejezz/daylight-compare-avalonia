using System.Text.Json.Serialization;
using DaylightCompare.Domain.Theme;
using DaylightCompare.Domain.Window;

namespace DaylightCompare.Domain.Settings;

/// <summary>
/// UI 언어 (conventions localization.md): 한국어가 기준이고 영어를 더한다. 저장 값은 <c>ko</c> / <c>en</c>이고,
/// 설정하지 않으면(null) 시스템 언어를 따른다 — <see cref="Localization.Resolve"/>.
/// </summary>
public enum AppLocale { Korean, English }

/// <summary>BC 텍스트 비교의 보기 필터. 첫 항목(All)이 기본값이다.</summary>
public enum CompareViewFilter { All, Differences, Same, Context }

public static class Localization
{
    /// <summary>
    /// 실제로 쓸 언어. 설정이 있으면 그대로, 없으면(시스템) <b>OS 언어가 한국어면 한국어, 그 밖의 모든 언어는 영어</b>다
    /// (localization.md §3). <paramref name="osLanguage"/>는 <c>CultureInfo.CurrentUICulture.TwoLetterISOLanguageName</c> 같은 언어 코드.
    /// </summary>
    public static AppLocale Resolve(AppLocale? setting, string osLanguage) =>
        setting ?? (osLanguage.Equals("ko", StringComparison.OrdinalIgnoreCase) ? AppLocale.Korean : AppLocale.English);
}

/// <summary>화면 글자 크기 배율의 범위·간격 (슬라이더 80%~160%, 5% 단위). 저장은 배율 숫자(<c>text_scale</c>)로 한다.</summary>
public static class TextScales
{
    public const double Min = 0.8, Max = 1.6, Step = 0.05, Default = 1.0;

    /// <summary>범위 안으로, 5% 단위로 맞춘다. 숫자가 아니면 기본(100%).</summary>
    public static double Snap(double value) =>
        !double.IsFinite(value) ? Default : Math.Round(Math.Clamp(Math.Round(value / Step) * Step, Min, Max), 2);
}

/// <summary>
/// 영속화되는 앱 설정. 저장 키는 모든 앱 공통 규약을 따른다 (conventions localization.md §5):
/// <c>theme_mode</c>, <c>accent_palette</c>, <c>text_scale</c>, <c>app_locale</c>, <c>window_placement</c>.
/// 앱 고유 설정은 <c>&lt;기능&gt;_&lt;이름&gt;</c> 키를 쓴다 (예: <c>compare_*</c>). 같은 키 이름의 JSON 파일 하나에 저장한다.
/// <para>
/// 모든 필드에 기본값이 있어서 옛 파일에 없는 키가 있어도, 새 버전이 키를 더해도 읽힌다.
/// 기본값은 규약과 같다 (모드 시스템, 색 구성 새턴, 글자 보통). 불변이라 <c>with</c>로 바꾼다.
/// </para>
/// </summary>
public sealed record AppSettings
{
    [JsonPropertyName("theme_mode")]
    [JsonConverter(typeof(ThemeModeConverter))]
    public ThemeMode Theme { get; init; } = ThemeMode.System;

    [JsonPropertyName("accent_palette")]
    [JsonConverter(typeof(ColorPaletteConverter))]
    public ColorPalette Palette { get; init; } = ColorPalette.Saturn;

    [JsonPropertyName("text_scale")]
    public double TextScale { get; init; } = TextScales.Default;

    /// <summary>null이면 시스템 언어를 따른다.</summary>
    [JsonPropertyName("app_locale")]
    [JsonConverter(typeof(AppLocaleConverter))]
    public AppLocale? Locale { get; init; }

    /// <summary>UI 글꼴 이름. null이면 기본(SeoulNamsan). 시스템에 설치된 글꼴 이름을 고를 수 있다.</summary>
    [JsonPropertyName("ui_font_family")]
    public string? UiFontFamily { get; init; }

    /// <summary>자주 쓰는 네트워크 서버 (연결 대화상자에서 한 번에 고른다). Daylight Commander와 같은 키.</summary>
    [JsonPropertyName("network_profiles")]
    public IReadOnlyList<NetworkProfile> NetworkProfiles { get; init; } = [];

    /// <summary>접속한 적 있는 SSH 서버의 호스트 키 지문 (<c>host:port</c> → 지문). 다음 접속 때 같은 서버인지 확인한다.</summary>
    [JsonPropertyName("known_host_keys")]
    public IReadOnlyDictionary<string, string> KnownHostKeys { get; init; } = new Dictionary<string, string>();

    [JsonPropertyName("window_placement")]
    public WindowPlacement? Window { get; init; }

    /// <summary>텍스트 비교 창의 보기 필터 (BC View > Show All / Show Differences / Show Same / Show Context). 다음 창도 그대로 연다.</summary>
    [JsonPropertyName("compare_view_filter")]
    [JsonConverter(typeof(CompareViewFilterConverter))]
    public CompareViewFilter CompareViewFilter { get; init; }

    /// <summary>BC View > Ignore Unimportant Differences: 켜면 중요하지 않은 차이를 차이로 보이지 않는다.</summary>
    [JsonPropertyName("compare_ignore_unimportant")]
    public bool CompareIgnoreUnimportant { get; init; }

    /// <summary>Show Context에서 차이 위아래로 남기는 줄 수 (BC Text View Options > Number of context lines) (0~<see cref="MaxContextLines"/>).</summary>
    [JsonPropertyName("compare_context_lines")]
    public int CompareContextLines { get; init; } = DefaultContextLines;

    public const int DefaultContextLines = 3, MaxContextLines = 20;

    /// <summary>폴더 비교의 보기 필터 토글 (<c>FolderShow</c> 비트, BC View > Show …). 기본은 모두 보기(63).</summary>
    [JsonPropertyName("folder_show")]
    public int FolderShowFlags { get; init; } = 63;

    /// <summary>BC View > Always Show Folders: 보일 파일이 없는 폴더도 보인다.</summary>
    [JsonPropertyName("folder_always_show_folders")]
    public bool FolderAlwaysShowFolders { get; init; }

    public AppSettings WithTextScale(double scale) => this with { TextScale = TextScales.Snap(scale) };

    /// <summary>서버를 저장한다. 같은 서버(스킴·호스트·포트·계정)가 이미 있으면 그 자리를 바꾸고, 없으면 맨 끝에 더한다.</summary>
    public AppSettings WithNetworkProfile(NetworkProfile profile)
    {
        static bool Same(NetworkProfile a, NetworkProfile b) =>
            a.Scheme == b.Scheme && a.Port == b.Port && a.User == b.User && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase);
        return NetworkProfiles.Any(p => Same(p, profile))
            ? this with { NetworkProfiles = NetworkProfiles.Select(p => Same(p, profile) ? profile : p).ToList() }
            : this with { NetworkProfiles = [.. NetworkProfiles, profile] };
    }

    public AppSettings WithoutNetworkProfile(NetworkProfile profile) =>
        this with { NetworkProfiles = NetworkProfiles.Where(p => p != profile).ToList() };

    /// <summary>
    /// 파일에서 읽은 값을 쓸 수 있는 값으로 고친다 (배율은 범위 안으로).
    /// JSON 소스 생성기는 파일에 없는 키의 <c>init</c> 속성을 초기값이 아니라 <c>null</c>/0으로 두므로, 목록 속성을 더하면 여기서 초기값으로 채운다.
    /// </summary>
    public AppSettings Normalized()
    {
        var snapped = TextScales.Snap(TextScale);
        var result = snapped == TextScale ? this : this with { TextScale = snapped };
        // 파일에 없던 목록·표는 null로 읽힌다 (컴파일러는 null이 아니라고 믿으므로 object로 본다).
        if ((object?)result.NetworkProfiles is null) result = result with { NetworkProfiles = [] };
        if ((object?)result.KnownHostKeys is null) result = result with { KnownHostKeys = new Dictionary<string, string>() };
        return result;
    }
}

/// <summary>저장해 둔 네트워크 서버. 비밀번호는 저장하지 않는다 (연결할 때마다 묻는다).</summary>
public sealed record NetworkProfile
{
    [JsonPropertyName("scheme")]
    public string Scheme { get; init; } = "sftp";

    [JsonPropertyName("host")]
    public string Host { get; init; } = "";

    [JsonPropertyName("port")]
    public int Port { get; init; }

    [JsonPropertyName("user")]
    public string User { get; init; } = "";

    /// <summary>시작 폴더 (WebDAV처럼 서버 주소 뒤에 <c>/dav/</c> 같은 경로가 붙는 곳). 옛 파일에는 없으므로 null일 수 있다.</summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }
}

/// <summary>설정을 읽고 쓴다. 구현은 data 계층.</summary>
public interface ISettingsRepository
{
    /// <summary>파일이 없거나 읽을 수 없으면 기본값. 예외를 던지지 않는다.</summary>
    AppSettings Load();

    void Save(AppSettings settings);
}
