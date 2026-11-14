using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using DaylightCompare.Services;

namespace DaylightCompare.App.Compare;

/// <summary>한쪽의 줄 하나: 줄이 없으면(채움 행) <see cref="Bytes"/>가 비어 있고 <see cref="Present"/>가 거짓이다.</summary>
sealed record ByteLine(string Label, ReadOnlyMemory<byte> Bytes, bool Present, int BomLength);

/// <summary>
/// 텍스트 비교 아래쪽의 줄 단위 바이트 패널 (BC 텍스트 비교의 16진수 줄): 커서가 있는 행의 양쪽 줄 바이트를 두 줄로 보인다.
/// 같은 칸에 나란히 서도록 고정폭 글꼴로 그리고, 다른 바이트는 칠한다(끼어든 바이트만 — LCS). 중요하지 않은 차이(공백·줄 끝 등 정책이 무시하는 것)는 흐리게,
/// 줄 끝·BOM 바이트는 글자색을 낮춰 CRLF와 LF의 차이가 한눈에 보이게 한다.
/// </summary>
sealed class LineBytesPanel : Border
{
    /// <summary>한 줄에 그리는 바이트 수의 상한 (한 줄짜리 거대 파일에서 느려지지 않게). 넘으면 "…"로 알린다.</summary>
    const int MaxShown = 2048;

    readonly TextBlock _leftLabel = Label();
    readonly TextBlock _rightLabel = Label();
    readonly TextBlock _leftBytes = Bytes();
    readonly TextBlock _rightBytes = Bytes();
    ByteLine? _left, _right;
    bool _unimportant;
    string _noLine = "";

    public LineBytesPanel()
    {
        BorderThickness = new Thickness(0, 1, 0, 0);
        Padding = new Thickness(12, 4);
        var labels = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 12, 0), Children = { _leftLabel, _rightLabel } };
        var rows = new StackPanel { Spacing = 2, Children = { _leftBytes, _rightBytes } };
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = rows,
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(labels);
        grid.Children.Add(scroll);
        Child = grid;
        this.Bind(BackgroundProperty, this.GetResourceObservable("AppSurfaceHi"));
        this.Bind(BorderBrushProperty, this.GetResourceObservable("AppStrokeStrong"));
        foreach (var t in new[] { _leftBytes, _rightBytes })
        {
            t.Bind(TextBlock.FontFamilyProperty, this.GetResourceObservable("AppEditorFont"));
            t.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsBody"));
        }
        foreach (var t in new[] { _leftLabel, _rightLabel })
            t.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        ActualThemeVariantChanged += (_, _) => Refresh();
    }

    static TextBlock Label() => new() { Height = 22, VerticalAlignment = VerticalAlignment.Center, Classes = { "small" }, Padding = new Thickness(0, 3, 0, 0) };
    static TextBlock Bytes() => new() { Height = 22, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.NoWrap };

    /// <summary>보여 줄 양쪽 줄을 바꾼다. <paramref name="unimportant"/>: 이 행의 차이가 중요하지 않은 것(정책이 무시하는 것)이다.</summary>
    public void Show(ByteLine left, ByteLine right, bool unimportant, string noLine)
    {
        (_left, _right, _unimportant, _noLine) = (left, right, unimportant, noLine);
        Refresh();
    }

    void Refresh()
    {
        if (_left is null || _right is null) return;
        _leftLabel.Text = _left.Label;
        _rightLabel.Text = _right.Label;
        var (dl, dr) = _left.Present && _right.Present
            ? ByteDiff.Differences(_left.Bytes.Span, _right.Bytes.Span)
            : (new bool[_left.Bytes.Length], new bool[_right.Bytes.Length]);
        Fill(_leftBytes, _left, dl);
        Fill(_rightBytes, _right, dr);
    }

    void Fill(TextBlock target, ByteLine line, bool[] different)
    {
        target.Inlines ??= new InlineCollection();
        target.Inlines.Clear();
        var normal = Brush("AppTextHi");
        var dim = Brush("AppTextMid");
        var diffBack = Brush(_unimportant ? "AppDiffUnimportant" : "AppDiffChangedStrong");
        var diffFore = _unimportant ? Brush("AppDiffUnimportantText") ?? dim : normal;
        if (!line.Present)
        {
            target.Inlines.Add(new Run(_noLine) { Foreground = dim, FontStyle = FontStyle.Italic });
            return;
        }
        var span = line.Bytes.Span;
        var shown = Math.Min(span.Length, MaxShown);
        // 줄 끝(CR·LF)·BOM 바이트의 위치: 줄 끝에서부터 \r과 \n이 이어진 만큼 (UTF-16이면 2바이트 단위로 0이 섞여 있다 — 바이트 값으로만 본다).
        var eolStart = EolStart(span);
        for (var i = 0; i < shown; i++)
        {
            var isDiff = i < different.Length && different[i];
            var isMeta = i >= eolStart || i < line.BomLength;
            var run = new Run(span[i].ToString("X2")) { Foreground = isDiff ? diffFore : isMeta ? dim : normal };
            if (isDiff && diffBack is not null) run.Background = diffBack;
            target.Inlines.Add(run);
            if (i < shown - 1) target.Inlines.Add(new Run(" ")); // 칸 사이 공백은 칠하지 않는다 (붙은 차이 바이트가 한 덩어리로 보이지 않게)
        }
        if (span.Length > shown) target.Inlines.Add(new Run($"  … +{span.Length - shown:N0}") { Foreground = dim });
    }

    /// <summary>줄 끝 바이트가 시작하는 위치 (없으면 줄 길이). 끝에서 0x0A, 0x0D(그리고 UTF-16의 0x00)를 걷어 낸다.</summary>
    static int EolStart(ReadOnlySpan<byte> span)
    {
        var i = span.Length;
        while (i > 0 && span[i - 1] is 0x0A or 0x0D or 0x00) i--;
        // 글자 끝의 0x00만 걷어 낸 경우(줄 끝이 없다)는 줄 끝이 아니다.
        return i < span.Length && span[i..].IndexOfAny((byte)0x0A, (byte)0x0D) >= 0 ? i : span.Length;
    }

    static IBrush? Brush(string key) => DiffViewState.Brush(key);
}
