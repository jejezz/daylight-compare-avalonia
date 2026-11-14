using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace DaylightCompare.App;

/// <summary>편집기 색상 구성표 하나: 설정에 저장하는 <see cref="Id"/>, 목록에 보일 이름, 문법 색을 정하는 TextMate 테마.</summary>
sealed record EditorScheme(string Id, string Name, ThemeName Theme);

/// <summary>구성표가 편집기에 주는 색: 바탕·글자·흐린 글자(줄 번호 등), 어두운 구성표인가, 목록의 견본 칩에 쓸 문법 색 몇 개.</summary>
sealed record SchemeColors(uint Background, uint Foreground, uint Dim, bool Dark, uint[] Swatch);

/// <summary>
/// 파일 비교 편집기의 색상 구성표 (설정 &gt; 편집기 색상 구성표). 구성표는 TextMate 테마(문법 색)와 그 테마가 정한 편집기 바탕·글자색으로 이루어진다.
/// 고르지 않으면(<c>null</c>) 앱 테마를 따른다 — 어두우면 Dark+, 밝으면 Light+ 문법 색에 앱 팔레트의 바탕·글자색.
/// 어두운 구성표인지(바탕 밝기)가 차이 색(<see cref="Domain.Theme.DiffColors"/>)의 밝은·어두운 벌을 정한다 — 앱 테마와 달라도 차이 줄이 읽힌다.
/// 1차는 TextMateSharp에 번들된 테마만 쓴다.
/// </summary>
static class EditorSchemes
{
    public static readonly IReadOnlyList<EditorScheme> All =
    [
        new("vscode-dark", "VS Code Dark", ThemeName.DarkPlus),
        new("vscode-light", "VS Code Light", ThemeName.LightPlus),
        new("vs-dark", "Visual Studio Dark", ThemeName.VisualStudioDark),
        new("vs-light", "Visual Studio Light", ThemeName.VisualStudioLight),
        new("solarized-dark", "Solarized Dark", ThemeName.SolarizedDark),
        new("solarized-light", "Solarized Light", ThemeName.SolarizedLight),
        new("dracula", "Dracula", ThemeName.Dracula),
        new("one-dark", "One Dark", ThemeName.OneDark),
        new("atom-one-light", "Atom One Light", ThemeName.AtomOneLight),
        new("monokai", "Monokai", ThemeName.Monokai),
        new("dimmed-monokai", "Dimmed Monokai", ThemeName.DimmedMonokai),
        new("tomorrow-night-blue", "Tomorrow Night Blue", ThemeName.TomorrowNightBlue),
        new("quiet-light", "Quiet Light", ThemeName.QuietLight),
        new("kimbie-dark", "Kimbie Dark", ThemeName.KimbieDark),
        new("abyss", "Abyss", ThemeName.Abbys),
        new("hc-dark", "High Contrast Dark", ThemeName.HighContrastDark),
        new("hc-light", "High Contrast Light", ThemeName.HighContrastLight),
    ];

    public static EditorScheme? Find(string? id) => id is { Length: > 0 } ? All.FirstOrDefault(s => s.Id == id) : null;

    static readonly Dictionary<string, SchemeColors?> Cache = [];

    /// <summary>구성표의 편집기 색 (테마 파일을 한 번 읽어 둔다). 읽지 못하면 null — 그 구성표는 앱 테마를 따른다.</summary>
    public static SchemeColors? ColorsOf(EditorScheme scheme)
    {
        if (Cache.TryGetValue(scheme.Id, out var cached)) return cached;
        SchemeColors? colors = null;
        try
        {
            var options = new RegistryOptions(scheme.Theme);
            var theme = Theme.CreateFromRawTheme(options.LoadTheme(scheme.Theme), options);
            var gui = theme.GetGuiColorDictionary();
            var bg = Parse(gui.GetValueOrDefault("editor.background")) ?? Parse(theme.GetColor(2)); // 기본 배경 (색 지도의 둘째)
            var fg = Parse(gui.GetValueOrDefault("editor.foreground")) ?? Parse(theme.GetColor(1)); // 기본 글자
            if (bg is { } b && fg is { } f)
            {
                var swatch = new List<uint>();
                foreach (var scope in new[] { "keyword", "string", "comment", "constant.numeric", "entity.name.function", "storage.type" })
                    if (ScopeColor(theme, scope) is { } c) swatch.Add(c);
                colors = new SchemeColors(b, f, Blend(f, b, 0.55), Luminance(b) < 0.5, [.. swatch]);
            }
        }
        catch (Exception)
        {
            colors = null; // 테마를 읽지 못하는 구성표는 앱 테마를 따른다
        }
        Cache[scheme.Id] = colors;
        return colors;
    }

    static uint? ScopeColor(Theme theme, string scope)
    {
        foreach (var rule in theme.Match([scope]))
            if (rule.foreground > 0 && Parse(theme.GetColor(rule.foreground)) is { } c) return c;
        return null;
    }

    /// <summary><c>#RRGGBB</c>·<c>#RRGGBBAA</c>·<c>#RGB</c>을 불투명한 0xAARRGGBB로 (알파는 무시 — 편집기 바탕·글자는 불투명하게 쓴다).</summary>
    public static uint? Parse(string? hex)
    {
        if (hex is null) return null;
        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 3) hex = string.Concat(hex.Select(ch => $"{ch}{ch}"));
        if (hex.Length is not (6 or 8)) return null;
        return uint.TryParse(hex[..6], System.Globalization.NumberStyles.HexNumber, null, out var rgb) ? 0xFF000000u | rgb : null;
    }

    static double Luminance(uint argb)
    {
        double r = (argb >> 16 & 0xFF) / 255.0, g = (argb >> 8 & 0xFF) / 255.0, b = (argb & 0xFF) / 255.0;
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    /// <summary><paramref name="a"/>에 <paramref name="b"/>를 <paramref name="t"/>만큼 섞는다.</summary>
    static uint Blend(uint a, uint b, double t)
    {
        uint Ch(int shift) => (uint)Math.Round(((a >> shift & 0xFF) * (1 - t)) + ((b >> shift & 0xFF) * t));
        return 0xFF000000u | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }
}
