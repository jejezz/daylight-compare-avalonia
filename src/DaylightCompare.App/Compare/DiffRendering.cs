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
        if (state.Row(rowIndex) is not { } row) return;
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
        var textView = TextView;
        if (textView is null || !textView.VisualLinesValid) return;
        var numberBrush = DiffViewState.Brush("AppTextMid") ?? Brushes.Gray;
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
/// 개요 막대: 파일 전체에서 차이가 어디 있는지 세로 막대로 보이고, 누르거나 끌면 그 위치로 간다. 지금 보이는 범위는 테두리 상자.
/// </summary>
sealed class OverviewBar : Control
{
    public DiffViewState? State { get; set; }

    /// <summary>보이는 범위 (행).</summary>
    public (double Top, double Rows) Viewport { get; set; }

    public event Action<int>? RowRequested;

    public OverviewBar()
    {
        Width = 14;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public override void Render(DrawingContext dc)
    {
        var h = Bounds.Height;
        dc.FillRectangle(DiffViewState.Brush("AppSurface") ?? Brushes.Transparent, new Rect(Bounds.Size));
        if (State?.Diff is not { } diff || diff.Rows.Count == 0) return;
        double total = diff.Rows.Count;
        foreach (var range in diff.Ranges)
        {
            var key = range.Kind switch
            {
                DiffBlockKind.Changed => "AppDiffChangedStrong",
                DiffBlockKind.LeftOnly => "AppDiffLeftOnlyStrong",
                DiffBlockKind.RightOnly => "AppDiffRightOnlyStrong",
                DiffBlockKind.Unimportant when State.ShowUnimportant => "AppDiffMarker",
                _ => null,
            };
            if (key is null || DiffViewState.Brush(key) is not { } brush) continue;
            var y = range.StartRow / total * h;
            dc.FillRectangle(brush, new Rect(2, y, Bounds.Width - 4, Math.Max(2, range.RowCount / total * h)));
        }
        var (top, rows) = Viewport;
        var pen = new Pen(DiffViewState.Brush("AppTextMid") ?? Brushes.Gray, 1);
        dc.DrawRectangle(null, pen, new Rect(0.5, top / total * h, Bounds.Width - 1, Math.Max(4, rows / total * h)));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Request(e.GetPosition(this).Y);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) Request(e.GetPosition(this).Y);
    }

    void Request(double y)
    {
        if (State?.Diff is not { } diff || diff.Rows.Count == 0 || Bounds.Height <= 0) return;
        RowRequested?.Invoke((int)Math.Clamp(y / Bounds.Height * diff.Rows.Count, 0, diff.Rows.Count - 1));
    }
}
