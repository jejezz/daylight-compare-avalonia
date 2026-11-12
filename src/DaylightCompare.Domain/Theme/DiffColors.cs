namespace DaylightCompare.Domain.Theme;

/// <summary>
/// 차이 색 (UI_UX.md "색"). 모두 0xAARRGGBB. 줄 바탕은 옅게(라이트 14~18%, 다크 20~26%), 줄 안 강조는 진하게.
/// 바뀜 = 주황, 왼쪽에만 = 빨강, 오른쪽에만 = 초록 (git·대부분의 비교 도구와 같은 뜻), 중요하지 않음 = 회청색 글자.
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
    const uint Orange = 0xFFB020, Red = 0xFF5A5F, Green = 0x2EB872, Slate = 0x7D8CA3;

    static uint A(uint rgb, double alpha) => ((uint)Math.Round(alpha * 255) << 24) | (rgb & 0xFFFFFF);

    public static DiffColorSet For(bool dark, bool highContrast = false)
    {
        var line = highContrast ? (dark ? 0.38 : 0.30) : (dark ? 0.22 : 0.16);
        var strong = highContrast ? (dark ? 0.70 : 0.60) : (dark ? 0.50 : 0.42);
        return new DiffColorSet(
            A(Orange, line), A(Orange, strong),
            A(Red, line), A(Red, strong),
            A(Green, line), A(Green, strong),
            dark ? 0xFF9AA8BCu : 0xFF6A7A90u, A(Slate, dark ? 0.10 : 0.07),
            dark ? 0x14FFFFFFu : 0x0F000000u,
            A(Slate, dark ? 0.55 : 0.45));
    }
}
