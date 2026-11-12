namespace DaylightCompare.Domain.Theme;

/// <summary>
/// 차이 색 (UI_UX.md "색"). 모두 0xAARRGGBB. 줄 바탕은 옅게(라이트 14~18%, 다크 20~26%), 줄 안 강조는 진하게.
/// Beyond Compare와 같은 뜻 (BC_COMPAT.md): <b>중요한 차이 = 빨강</b>(바뀜·한쪽에만 모두 — 어느 쪽인지는 여백 기호 `~ − +`로 가른다),
/// <b>중요하지 않은 차이 = 파랑</b>(옅은 파랑 바탕 + 파란 글자).
/// 고대비 색 구성은 바탕을 더 진하게 하고, 색만으로 구분하지 않도록 화면이 줄 옆에 표시 막대·기호를 함께 그린다.
/// </summary>
public sealed record DiffColorSet(
    uint ChangedLine, uint ChangedStrong,
    uint LeftOnlyLine, uint LeftOnlyStrong,
    uint RightOnlyLine, uint RightOnlyStrong,
    uint UnimportantText, uint UnimportantLine,
    uint Filler, uint Marker);

public static class DiffColors
{
    const uint Red = 0xE5484D, Blue = 0x3E7BFA, Slate = 0x7D8CA3;

    static uint A(uint rgb, double alpha) => ((uint)Math.Round(alpha * 255) << 24) | (rgb & 0xFFFFFF);

    public static DiffColorSet For(bool dark, bool highContrast = false)
    {
        var line = highContrast ? (dark ? 0.38 : 0.30) : (dark ? 0.22 : 0.16);
        var strong = highContrast ? (dark ? 0.70 : 0.60) : (dark ? 0.50 : 0.42);
        return new DiffColorSet(
            A(Red, line), A(Red, strong),
            A(Red, line), A(Red, strong),
            A(Red, line), A(Red, strong),
            dark ? 0xFF8DB4FFu : 0xFF2F64D6u, A(Blue, dark ? 0.16 : 0.10),
            dark ? 0x14FFFFFFu : 0x0F000000u,
            A(Slate, dark ? 0.55 : 0.45));
    }
}
