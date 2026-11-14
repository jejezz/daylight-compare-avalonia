using Avalonia.Controls;
using DaylightCompare.Services;

namespace DaylightCompare.App.Compare;

/// <summary>
/// 패널 머리글의 "저장 형식" (BC의 머리글 인코딩·줄 끝 드롭다운): 인코딩·BOM·줄 끝을 바꾸면 <b>저장할 때 그 형식으로 쓴다</b>.
/// 화면의 글은 그대로이고, 바꾸면 그 쪽이 "고친 상태"가 된다 (저장해야 파일이 바뀐다). 읽은 글을 해석하는 인코딩을 바꾸는 것은
/// 따로 "인코딩 다시 열기"다 (<see cref="EncodingFlyout"/>).
/// </summary>
public sealed partial class CompareView
{
    const string Crlf = "\r\n", Lf = "\n", Cr = "\r";

    static string LineBreakName(string lineBreak) => lineBreak switch
    {
        Crlf => "CRLF (Windows)",
        Cr => "CR (Mac)",
        _ => "LF (Unix)",
    };

    static string LineBreakShort(string lineBreak) => lineBreak switch { Crlf => "CRLF", Cr => "CR", _ => "LF" };

    /// <summary>머리글 글자: 인코딩 · 줄 끝 (저장할 형식).</summary>
    static string FormatLabel(EditorTextFile f) => $"{TextEncodings.DisplayName(f.Encoding, f.HasBom)} · {LineBreakShort(f.LineBreak)}";

    /// <summary>저장 형식을 바꾼다 (null은 그대로). 읽을 때와 같아지면 "고친 상태"가 풀린다.</summary>
    void SetSaveFormat(Side side, TextEncodingKind? encoding = null, bool? bom = null, string? lineBreak = null)
    {
        if (side.Text is not { } t || !side.CanEdit) return;
        var f = t.File;
        var enc = encoding ?? f.Encoding;
        var hasBom = bom ?? (encoding is { } e && e != f.Encoding ? e is TextEncodingKind.Utf16Le or TextEncodingKind.Utf16Be : f.HasBom);
        var next = f with { Encoding = enc, HasBom = hasBom, LineBreak = lineBreak ?? f.LineBreak };
        if (next == f) return;
        side.Source = t with { File = next };
        UpdateSideChrome(side);
        UpdateTitle();
        UpdateCaretUi();
    }

    /// <summary>머리글을 누르면: 인코딩 다시 열기 · 줄 끝 · 저장 인코딩.</summary>
    MenuFlyout FormatFlyout(Side side)
    {
        var s = Strings.Current;
        var flyout = new MenuFlyout();
        if (side.Text is not { } t) return flyout;

        var reopen = new MenuItem { Header = s.CompareReopenEncoding };
        foreach (var item in EncodingFlyout(side).Items) reopen.Items.Add(item);
        flyout.Items.Add(reopen);
        if (!side.CanEdit) return flyout; // 읽기 전용(압축 안 파일 등)은 저장 형식을 바꿀 수 없다

        flyout.Items.Add(new Separator());
        flyout.Items.Add(new MenuItem { Header = s.CompareFormatLineBreak, IsEnabled = false });
        foreach (var lb in new[] { Crlf, Lf, Cr })
        {
            var target = lb;
            var item = new MenuItem { Header = LineBreakName(lb), ToggleType = MenuItemToggleType.Radio, IsChecked = t.File.LineBreak == lb, GroupName = "format-linebreak" };
            item.Click += (_, _) => SetSaveFormat(side, lineBreak: target);
            flyout.Items.Add(item);
        }

        flyout.Items.Add(new Separator());
        flyout.Items.Add(new MenuItem { Header = s.CompareFormatSaveEncoding, IsEnabled = false });
        foreach (var (kind, bom) in SaveEncodings())
        {
            var (k, b) = (kind, bom);
            var item = new MenuItem
            {
                Header = TextEncodings.DisplayName(kind, bom),
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = t.File.Encoding == kind && t.File.HasBom == bom,
                GroupName = "format-encoding",
            };
            item.Click += (_, _) => SetSaveFormat(side, encoding: k, bom: b);
            flyout.Items.Add(item);
        }
        return flyout;
    }

    /// <summary>저장 인코딩 후보: 인코딩마다, UTF-8은 BOM 없음·있음 둘 다 (UTF-16은 BOM이 붙는 것이 보통이다).</summary>
    static IEnumerable<(TextEncodingKind Kind, bool Bom)> SaveEncodings()
    {
        foreach (var kind in TextEncodings.All)
        {
            if (kind == TextEncodingKind.Utf8)
            {
                yield return (kind, false);
                yield return (kind, true);
            }
            else yield return (kind, kind is TextEncodingKind.Utf16Le or TextEncodingKind.Utf16Be);
        }
    }
}
