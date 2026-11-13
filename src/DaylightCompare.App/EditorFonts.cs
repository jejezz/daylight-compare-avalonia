using Avalonia.Media;

namespace DaylightCompare.App;

/// <summary>
/// 편집기(비교 창의 좌우 편집기) 글꼴. <b>고정폭 글꼴만</b> 쓴다 — 좌우 행과 글자 열이 맞아야 차이가 바로 보인다.
/// 기본은 OS마다 흔한 고정폭 글꼴 중 설치된 첫 글꼴(<see cref="Candidates"/>)이고, 설정에서 시스템에 설치된 다른 고정폭 글꼴을 고를 수 있다.
/// 한글처럼 고정폭 글꼴에 없는 글자는 Avalonia가 글자마다 다른 글꼴로 채운다.
/// </summary>
public static class EditorFonts
{
    /// <summary>기본 글꼴 후보 (앞에서부터, 설치된 첫 글꼴): Windows → macOS → Linux 순.</summary>
    static readonly string[] Candidates =
        ["Cascadia Mono", "Consolas", "SF Mono", "Menlo", "Monaco", "JetBrains Mono", "DejaVu Sans Mono", "Liberation Mono",
         "Ubuntu Mono", "Noto Sans Mono", "Courier New"];

    /// <summary>후보가 하나도 없을 때: 이름 목록 그대로 (Avalonia가 앞에서부터 찾고, 끝의 <c>monospace</c>는 OS 별칭).</summary>
    const string FallbackList = "Cascadia Mono, Consolas, Menlo, Monaco, DejaVu Sans Mono, monospace";

    static string? _defaultName;
    static Task<IReadOnlyList<string>>? _monospaced;

    /// <summary>기본 고정폭 글꼴 이름 (설정 창에 "… (기본)"으로 보인다). 설치된 후보가 없으면 빈 글자.</summary>
    public static string? DefaultName => _defaultName ??= Candidates.FirstOrDefault(IsInstalled) ?? "";

    /// <summary>설정 값(null이면 기본)을 실제 글꼴로. 설치되지 않았거나 고정폭이 아닌 이름은 기본으로 돌아간다.</summary>
    public static FontFamily For(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name) && IsInstalled(name) && IsMonospaced(name)) return new FontFamily(name);
        return Default();
    }

    public static FontFamily Default() => DefaultName is { Length: > 0 } d ? new FontFamily(d) : new FontFamily(FallbackList);

    /// <summary>
    /// 시스템에 설치된 고정폭 글꼴 이름 (이름 순). 글꼴 파일을 하나씩 열어 보므로 백그라운드에서 한 번만 만들고 그 뒤로는 그대로 쓴다.
    /// </summary>
    public static Task<IReadOnlyList<string>> MonospacedAsync() => _monospaced ??= Task.Run(() =>
        (IReadOnlyList<string>)FontManager.Current.SystemFonts.Select(f => f.Name).Distinct()
            .Where(IsMonospaced)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList());

    static bool IsInstalled(string name) => FontManager.Current.SystemFonts.Any(f => f.Name == name);

    /// <summary>
    /// 고정폭인지: 글꼴이 스스로 고정폭이라고 하거나(<c>post.isFixedPitch</c>), 그렇게 적혀 있지 않아도 폭이 다른 ASCII 글자들
    /// (<c>i l . W M 0</c> 공백)의 너비가 모두 같으면 고정폭으로 본다 — 플래그를 빠뜨린 코딩 글꼴이 꽤 있다.
    /// </summary>
    public static bool IsMonospaced(string name)
    {
        try
        {
            if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(name), out var face)) return false;
            // 없는 글꼴이면 다른 글꼴로 대신 찾아 준다 — 그 경우는 이 글꼴이 아니다.
            if (!string.Equals(face.FamilyName, name, StringComparison.OrdinalIgnoreCase)
                && !face.FamilyNames.Values.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))) return false;
            if (face.Metrics.IsFixedPitch) return true;
            ushort width = 0;
            foreach (var ch in "il.WM0 ")
            {
                if (!face.CharacterToGlyphMap.TryGetGlyph(ch, out var glyph) || glyph == 0) return false;
                if (!face.TryGetHorizontalGlyphAdvance(glyph, out var advance) || advance == 0) return false;
                if (width == 0) width = advance;
                else if (advance != width) return false;
            }
            return true;
        }
        catch
        {
            return false; // 깨진 글꼴 파일 하나 때문에 목록 전체가 실패하지 않게
        }
    }
}
