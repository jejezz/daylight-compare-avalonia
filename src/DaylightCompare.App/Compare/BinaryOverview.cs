using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DaylightCompare.Domain.Binary;
using DaylightCompare.Domain.Diff;

namespace DaylightCompare.App.Compare;

/// <summary>
/// 바이너리 비교의 개요 막대 = 세로 스크롤 막대 (텍스트 비교의 <see cref="OverviewBar"/>와 같은 약속): 파일 전체에서 다른 구간이 어디 있는지 보이고,
/// 보이는 범위는 손잡이 — 끌거나 눌러서 이동하고 휠도 된다. 행 수가 아주 많아도(GB) 구간 목록만 그린다.
/// </summary>
sealed class BinaryOverview : Control
{
    BinaryRows? _rows;
    (double Top, double Rows) _viewport;
    double _grab;
    bool _dragging;
    long? _caretRow;

    public BinaryOverview()
    {
        Width = 22;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    public event Action<double>? ScrollRequested;
    public event Action<double>? WheelRequested;

    public BinaryRows? Rows { get => _rows; set { _rows = value; InvalidateVisual(); } }
    public (double Top, double Rows) Viewport { get => _viewport; set { _viewport = value; InvalidateVisual(); } }
    public long? CaretRow { get => _caretRow; set { if (_caretRow == value) return; _caretRow = value; InvalidateVisual(); } }

    static IBrush? Solid(string key) => DiffViewState.Brush(key) is ISolidColorBrush b ? new SolidColorBrush(Color.FromRgb(b.Color.R, b.Color.G, b.Color.B)) : null;

    Rect Thumb()
    {
        if (_rows is null || _rows.RowCount == 0) return default;
        double total = _rows.RowCount;
        return new Rect(0.5, _viewport.Top / total * Bounds.Height, Math.Max(0, Bounds.Width - 1), Math.Max(10, _viewport.Rows / total * Bounds.Height));
    }

    public override void Render(DrawingContext dc)
    {
        var h = Bounds.Height;
        var w = Bounds.Width;
        dc.FillRectangle(DiffViewState.Brush("AppSurfaceHi") ?? Brushes.Transparent, new Rect(Bounds.Size));
        if (DiffViewState.Brush("AppStrokeStrong") is { } edge) dc.FillRectangle(edge, new Rect(0, 0, 1, h));
        if (_rows is null || _rows.RowCount == 0) return;
        double total = _rows.RowCount;
        var mid = DiffViewState.Brush("AppTextMid") ?? Brushes.Gray;
        var thumb = Thumb();
        dc.FillRectangle(new SolidColorBrush(((ISolidColorBrush)mid).Color, 0.2), thumb);

        var important = Solid("AppDiffChangedStrong");
        const double inset = 3;
        var inner = w - inset * 2;
        for (var i = 0; i < _rows.Blocks.Count; i++)
        {
            var b = _rows.Blocks[i];
            if (!b.IsDifference || important is null) continue;
            var (x, width) = b.Kind switch
            {
                DiffBlockKind.LeftOnly => (inset, inner / 2 - 0.5),
                DiffBlockKind.RightOnly => (inset + inner / 2 + 0.5, inner / 2 - 0.5),
                _ => (inset, inner),
            };
            var first = _rows.FirstRowOf(i);
            var rowCount = (Math.Max(b.LeftLength, b.RightLength) + BinaryRows.Width - 1) / BinaryRows.Width;
            dc.FillRectangle(important, new Rect(x, first / total * h, width, Math.Max(5, rowCount / total * h)));
        }
        if (_caretRow is { } caret && caret >= 0 && caret < total && Solid("AppTextHi") is { } caretBrush)
            dc.FillRectangle(caretBrush, new Rect(0, Math.Clamp(caret / total * h - 1, 0, h - 2), w, 2));
        dc.DrawRectangle(null, new Pen(mid, 1.5), thumb.Deflate(0.75), 3, 3);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var y = e.GetPosition(this).Y;
        var thumb = Thumb();
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
        if (_rows is null || _rows.RowCount == 0 || Bounds.Height <= 0) return;
        double total = _rows.RowCount;
        var top = (y - _grab) / Bounds.Height * total;
        ScrollRequested?.Invoke(Math.Clamp(top, 0, Math.Max(0, total - _viewport.Rows)));
    }
}
