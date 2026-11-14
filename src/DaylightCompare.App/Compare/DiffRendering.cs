using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using DaylightCompare.Domain.Diff;

namespace DaylightCompare.App.Compare;

/// <summary>비교 화면이 그릴 때 함께 보는 상태: 맞춘 diff, 이 편집기가 왼쪽인가, 중요하지 않은 차이를 보이는가.</summary>
sealed class DiffViewState
{
    public AlignedDiff? Diff { get; set; }
    public bool ShowUnimportant { get; set; } = true;

    /// <summary>고치는 중(다시 비교 전): 행 정보가 잠시 맞지 않으므로 줄 안 강조는 쉰다.</summary>
    public bool Editing { get; set; }

    /// <summary>
    /// 사용자가 병합하려고 고른 행 범위(화면 행, 0부터)와, 이 편집기가 그 고른 쪽(원본)인가. 양쪽 편집기가 같은 범위를 틀로 감싸 보인다 —
    /// 원본 쪽은 실선, 합쳐질 대상 쪽은 점선.
    /// </summary>
    public (int First, int Last)? MergeRows { get; set; }

    public bool MergeIsSource { get; set; }

    /// <summary>바뀐 행의 줄 안 차이 (보이는 줄만 계산해서 기억).</summary>
    public Dictionary<int, IntralineDiff> Intraline { get; } = [];

    public AlignedRow? Row(int row) => Diff is { } d && row >= 0 && row < d.Rows.Count ? d.Rows[row] : null;

    public static IBrush? Brush(string key) =>
        Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var v) == true ? v as IBrush : null;

    /// <summary>이 행을 칠할 바탕 자원 이름 (없으면 null).</summary>
    public string? LineBrushKey(AlignedRow r, bool left)
    {
        var mine = left ? r.Left : r.Right;
        if (mine is null) return "AppDiffFiller";
        return r.Kind switch
        {
            DiffBlockKind.Changed => "AppDiffChanged",
            DiffBlockKind.LeftOnly => "AppDiffLeftOnly",
            DiffBlockKind.RightOnly => "AppDiffRightOnly",
            DiffBlockKind.Unimportant when ShowUnimportant => "AppDiffUnimportant",
            _ => null,
        };
    }

    public IntralineDiff? IntralineFor(int row)
    {
        if (Row(row) is not { Left: { } l, Right: { } r } ar || ar.Kind is not (DiffBlockKind.Changed or DiffBlockKind.Unimportant)) return null;
        if (!Intraline.TryGetValue(row, out var intra)) Intraline[row] = intra = Diff!.Diff.Intraline(l, r);
        return intra;
    }
}

/// <summary>
/// 병합하려고 고른 행들을 감싸는 틀 (<see cref="DiffViewState.MergeRows"/>). 고른 쪽은 실선, 합쳐질 쪽은 점선 + 옅은 바탕 —
/// 가운데 → / ← 버튼을 누르면 이 틀 안의 줄만 옮겨진다는 것을 양쪽에서 같은 자리로 보인다.
/// </summary>
sealed class MergeFrameRenderer(DiffViewState state) : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (state.MergeRows is not { } m || textView.Document is not { LineCount: > 0 } doc || !textView.VisualLinesValid) return;
        var first = Math.Clamp(m.First, 0, doc.LineCount - 1) + 1;
        var last = Math.Clamp(m.Last, 0, doc.LineCount - 1) + 1;
        var top = textView.GetVisualTopByDocumentLine(first) - textView.VerticalOffset;
        var bottom = textView.GetVisualTopByDocumentLine(last) + textView.DefaultLineHeight - textView.VerticalOffset;
        if (DiffViewState.Brush("AppPrimary") is not ISolidColorBrush primary) return;
        var rect = new Rect(1, top + 0.5, Math.Max(0, textView.Bounds.Width - 2), Math.Max(2, bottom - top - 1));
        var fill = new SolidColorBrush(primary.Color, state.MergeIsSource ? 0.06 : 0.10);
        var pen = new Pen(primary, 2, state.MergeIsSource ? null : new DashStyle([4, 3], 0));
        dc.DrawRectangle(fill, pen, rect, 3, 3);
    }
}

/// <summary>행 바탕: 바뀜·한쪽에만·중요하지 않음·채움 줄. 편집기 글자 아래 층에 그린다.</summary>
sealed class DiffBackgroundRenderer(DiffViewState state, bool left) : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (state.Diff is null || !textView.VisualLinesValid) return;
        var width = textView.Bounds.Width + textView.HorizontalOffset; // 가로로 스크롤해도 줄 끝까지 칠한다
        foreach (var vl in textView.VisualLines)
        {
            if (state.Row(vl.FirstDocumentLine.LineNumber - 1) is not { } row) continue;
            if (state.LineBrushKey(row, left) is not { } key || DiffViewState.Brush(key) is not { } brush) continue;
            var y = vl.VisualTop - textView.VerticalOffset;
            dc.FillRectangle(brush, new Rect(0, y, width, vl.Height));
        }
    }
}

/// <summary>줄 안 강조: 바뀐 단어·글자는 진한 바탕, 중요하지 않은 부분은 흐린 글자.</summary>
sealed class IntralineColorizer(DiffViewState state, bool left) : DocumentColorizingTransformer
{
    protected override void ColorizeLine(DocumentLine line)
    {
        var rowIndex = line.LineNumber - 1;
        if (state.Editing || state.Row(rowIndex) is not { } row) return;
        var dim = DiffViewState.Brush("AppDiffUnimportantText");
        if (row.Kind == DiffBlockKind.Unimportant && (left ? row.Left : row.Right) is not null && state.ShowUnimportant
            && (row.Left is null || row.Right is null))
        {
            // 주석만 있는 줄이 한쪽에 더 있는 경우 등: 줄 전체를 흐리게.
            if (line.Length > 0 && dim is not null) ChangeLinePart(line.Offset, line.EndOffset, e => e.TextRunProperties.SetForegroundBrush(dim));
            return;
        }
        if (state.IntralineFor(rowIndex) is not { } intra) return;
        var strongKey = row.Kind == DiffBlockKind.Changed ? "AppDiffChangedStrong" : null;
        var strong = strongKey is null ? null : DiffViewState.Brush(strongKey);
        foreach (var seg in left ? intra.Left : intra.Right)
        {
            var start = line.Offset + Math.Min(seg.Start, line.Length);
            var end = line.Offset + Math.Min(seg.End, line.Length);
            if (end <= start) continue;
            if (seg.Unimportant || row.Kind == DiffBlockKind.Unimportant)
            {
                if (dim is not null && state.ShowUnimportant) ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(dim));
            }
            else if (strong is not null) ChangeLinePart(start, end, e => e.BackgroundBrush = strong);
        }
    }
}

/// <summary>
/// 줄 번호 여백: 화면 행이 아니라 <b>파일의 실제 줄 번호</b>를 보인다(채움 줄은 비움). 오른쪽 끝에는 색만으로 구분하지 않도록
/// 차이 기호(바뀜 <c>~</c>, 빠짐 <c>−</c>, 더해짐 <c>+</c>)를 그린다 — 고대비·색약 사용자용.
/// </summary>
sealed class RowNumberMargin(DiffViewState state, bool left) : AbstractMargin
{
    Typeface _typeface = new(FontFamily.Default);
    double _fontSize = 12;

    public void SetFont(FontFamily family, double size)
    {
        _typeface = new Typeface(family);
        _fontSize = size;
        InvalidateMeasure();
    }

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView is not null) oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null) newTextView.VisualLinesChanged += OnVisualLinesChanged;
        InvalidateVisual();
    }

    void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    int MaxNumber => state.Diff is { } d ? (left ? d.Diff.Left.LineCount : d.Diff.Right.LineCount) : (Document?.LineCount ?? 1);

    FormattedText Format(string text, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, _fontSize, brush);

    protected override Size MeasureOverride(Size availableSize)
    {
        var digits = Math.Max(3, MaxNumber.ToString(CultureInfo.InvariantCulture).Length);
        var w = Format(new string('9', digits), Brushes.Black).Width;
        return new Size(w + 26, 0);
    }

    public override void Render(DrawingContext dc)
    {
        // 여백 전체를 (보이지 않게) 칠한다 — Avalonia는 그리기 연산이 있는 곳만 클릭을 받으므로, 안 그리면 번호도 기호도 없는 줄(채움 줄)은 눌러도 닿지 않는다.
        dc.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var textView = TextView;
        if (textView is null || !textView.VisualLinesValid) return;
        var numberBrush = DiffViewState.Brush("AppEdMid") ?? DiffViewState.Brush("AppTextMid") ?? Brushes.Gray;
        var markerBrush = DiffViewState.Brush("AppDiffMarker") ?? Brushes.Gray;
        var markerX = Bounds.Width - 12;
        foreach (var vl in textView.VisualLines)
        {
            var rowIndex = vl.FirstDocumentLine.LineNumber - 1;
            var y = vl.VisualTop - textView.VerticalOffset;
            int? number = state.Diff is null ? rowIndex : state.Row(rowIndex) is { } r ? (left ? r.Left : r.Right) : null;
            if (number is { } n)
            {
                var text = Format((n + 1).ToString(CultureInfo.InvariantCulture), numberBrush);
                dc.DrawText(text, new Point(markerX - 6 - text.Width, y + (vl.Height - text.Height) / 2));
            }
            if (state.Row(rowIndex) is { IsDifference: true } row)
            {
                var symbol = row.Kind switch
                {
                    DiffBlockKind.Changed when (left ? row.Left : row.Right) is not null => "~",
                    DiffBlockKind.LeftOnly when left => "−",
                    DiffBlockKind.RightOnly when !left => "+",
                    _ => null,
                };
                if (symbol is not null)
                {
                    var t = Format(symbol, markerBrush);
                    dc.DrawText(t, new Point(markerX, y + (vl.Height - t.Height) / 2));
                }
            }
        }
    }
}

/// <summary>
/// 개요 막대 = 이 창의 <b>하나뿐인 세로 스크롤 막대</b> (편집기 자신의 세로 막대는 숨긴다). 파일 전체에서 차이가 어디 있는지 보이고,
/// 보이는 범위는 반투명 손잡이 — 손잡이를 끌면 따라가고, 빈 자리를 누르면 그 자리로 가고, 휠도 된다 (캐럿은 옮기지 않는다).
/// </summary>
sealed class OverviewBar : Control
{
    public DiffViewState? State { get; set; }

    /// <summary>보이는 범위 (행).</summary>
    public (double Top, double Rows) Viewport { get; set; }

    /// <summary>눌러서 끌면 보이는 범위의 맨 위가 이 행(소수 가능)이 되도록 스크롤해 달라는 요청.</summary>
    public event Action<double>? ScrollRequested;

    /// <summary>마우스 휠: 줄 단위로 얼마나 (위로 굴리면 음수).</summary>
    public event Action<double>? WheelRequested;

    double _grab;
    bool _dragging;

    int? _caretRow;
    /// <summary>캐럿이 있는 행 — 지도에 가는 선으로 표시한다 (지금 어디 있는지).</summary>
    public int? CaretRow
    {
        get => _caretRow;
        set { if (_caretRow == value) return; _caretRow = value; InvalidateVisual(); }
    }

    public OverviewBar()
    {
        Width = 22; // 차이 위치가 눈에 띄게 넉넉한 너비
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    /// <summary>보이는 범위를 나타내는 상자 (스크롤 막대의 손잡이).</summary>
    Rect Thumb()
    {
        if (State?.Diff is not { } diff || diff.Rows.Count == 0) return default;
        double total = diff.Rows.Count;
        var (top, rows) = Viewport;
        return new Rect(0.5, top / total * Bounds.Height, Math.Max(0, Bounds.Width - 1), Math.Max(10, rows / total * Bounds.Height));
    }

    /// <summary>자원 이름의 색을 불투명하게 (지도의 차이 표시는 반투명 줄 바탕과 달리 선명해야 한다).</summary>
    static IBrush? Solid(string key) => DiffViewState.Brush(key) is ISolidColorBrush b ? new SolidColorBrush(Color.FromRgb(b.Color.R, b.Color.G, b.Color.B)) : null;

    public override void Render(DrawingContext dc)
    {
        var h = Bounds.Height;
        var w = Bounds.Width;
        dc.FillRectangle(DiffViewState.Brush("AppSurfaceHi") ?? Brushes.Transparent, new Rect(Bounds.Size));
        if (DiffViewState.Brush("AppStrokeStrong") is { } edge) dc.FillRectangle(edge, new Rect(0, 0, 1, h)); // 편집기와의 경계
        if (State?.Diff is not { } diff || diff.Rows.Count == 0) return;
        double total = diff.Rows.Count;

        // 1) 손잡이(보이는 범위)를 먼저 깔고, 2) 그 위에 차이 표시를 올린다 — 손잡이 안에서도 차이가 가려지지 않고 선명하다.
        var thumb = Thumb();
        var mid = DiffViewState.Brush("AppTextMid") ?? Brushes.Gray;
        dc.DrawRectangle(mid is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, 0.28) : null, null, thumb, 3, 3);

        // 차이 표시: 중요한 차이는 진한 빨강, 중요하지 않은 차이는 파랑 — 모두 불투명하고 최소 5px 높이.
        // 바뀜은 막대 전체 너비, 왼쪽에만 있는 것은 왼쪽 절반, 오른쪽에만 있는 것은 오른쪽 절반 (어느 쪽에 있는지 지도에서도 보인다).
        var important = Solid("AppDiffChangedStrong");
        var minor = Solid("AppDiffUnimportantText");
        const double inset = 3;
        var inner = w - inset * 2;
        foreach (var range in diff.Ranges)
        {
            var (brush, x, width) = range.Kind switch
            {
                DiffBlockKind.Changed => (important, inset, inner),
                DiffBlockKind.LeftOnly => (important, inset, inner / 2 - 0.5),
                DiffBlockKind.RightOnly => (important, inset + inner / 2 + 0.5, inner / 2 - 0.5),
                DiffBlockKind.Unimportant when State.ShowUnimportant => (minor, inset, inner),
                _ => ((IBrush?)null, 0d, 0d),
            };
            if (brush is null) continue;
            var y = range.StartRow / total * h;
            dc.FillRectangle(brush, new Rect(x, y, width, Math.Max(5, range.RowCount / total * h)));
        }

        if (CaretRow is { } caret && caret >= 0 && caret < total && Solid("AppTextHi") is { } caretBrush)
            dc.FillRectangle(caretBrush, new Rect(0, Math.Clamp(caret / total * h - 1, 0, h - 2), w, 2));

        // 손잡이 테두리는 맨 위에 — 보이는 범위가 어디인지 분명하게.
        dc.DrawRectangle(null, new Pen(mid, 1.5), thumb.Deflate(0.75), 3, 3);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var y = e.GetPosition(this).Y;
        var thumb = Thumb();
        // 손잡이를 누르면 잡은 자리를 그대로 끌고, 빈 자리를 누르면 그 자리가 보이는 범위의 가운데가 되도록 바로 이동한다.
        _grab = thumb.Height > 0 && y >= thumb.Top && y <= thumb.Bottom ? y - thumb.Top : thumb.Height / 2;
        _dragging = true;
        e.Pointer.Capture(this);
        Request(y);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) Request(e.GetPosition(this).Y);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        WheelRequested?.Invoke(-e.Delta.Y * 3);
        e.Handled = true;
    }

    void Request(double y)
    {
        if (State?.Diff is not { } diff || diff.Rows.Count == 0 || Bounds.Height <= 0) return;
        double total = diff.Rows.Count;
        var top = (y - _grab) / Bounds.Height * total;
        ScrollRequested?.Invoke(Math.Clamp(top, 0, Math.Max(0, total - Viewport.Rows)));
    }
}
