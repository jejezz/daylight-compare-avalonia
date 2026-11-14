using Avalonia;
using PixelRect = DaylightCompare.Domain.Imaging.PixelRect;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DaylightCompare.Domain.Imaging;

namespace DaylightCompare.App.Compare;

/// <summary>
/// 그림 비교의 그림판 하나: 도화지 좌표(픽셀)에 놓인 그림들(<see cref="Layer"/>)을 확대·이동해 그린다.
/// 휠은 포인터를 중심으로 확대·축소, 끌면 이동, 두 번 누르면 맞춤 ↔ 100%. 사용자가 확대·이동하면 <see cref="ViewChanged"/>로 알려서
/// 나란히 보기의 다른 쪽이 같은 자리를 보게 한다.
/// </summary>
sealed class ImageCanvas : Control
{
    public sealed record Layer(IImage Image, PixelRect Dest, double Opacity = 1);

    public const double MinZoom = 0.02;
    public const double MaxZoom = 64;

    static readonly IBrush Checker = MakeChecker();

    IReadOnlyList<Layer> _layers = [];
    IReadOnlyList<PixelRect> _marks = [];
    int _current = -1;
    Size _canvas;
    double _zoom = 1;
    Point _offset;
    bool _fit = true;
    Point? _dragFrom;
    Point _dragOffset;
    int _hoverX = -1, _hoverY = -1;

    public ImageCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    /// <summary>사용자가 확대·이동했다 (프로그램이 <see cref="SetView"/>로 바꾼 것은 알리지 않는다).</summary>
    public event Action? ViewChanged;

    /// <summary>포인터가 가리키는 도화지 픽셀 (그림판 밖이면 -1, -1).</summary>
    public event Action<int, int>? Hover;

    public double Zoom => _zoom;
    public Point Offset => _offset;
    public bool IsFit => _fit;
    public Size CanvasSize => _canvas;

    public IReadOnlyList<Layer> Layers
    {
        get => _layers;
        set { _layers = value; InvalidateVisual(); }
    }

    /// <summary>차이 묶음 사각형들과 그중 지금 가리키는 것 (없으면 -1) — 테두리로 표시한다.</summary>
    public void SetMarks(IReadOnlyList<PixelRect> marks, int current)
    {
        _marks = marks;
        _current = current;
        InvalidateVisual();
    }

    public void SetCanvas(int width, int height)
    {
        _canvas = new Size(width, height);
        if (_fit) FitToWindow(raise: false);
        else InvalidateVisual();
    }

    public void SetView(double zoom, Point offset, bool fit)
    {
        _zoom = zoom;
        _offset = offset;
        _fit = fit;
        UpdateInterpolation();
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty && _fit && Bounds.Width > 0) FitToWindow(raise: false);
    }

    // --- 확대·이동 ---

    public void FitToWindow(bool raise = true)
    {
        if (_canvas.Width <= 0 || _canvas.Height <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0) { _fit = true; return; }
        const double margin = 16;
        var zoom = Math.Min((Bounds.Width - margin) / _canvas.Width, (Bounds.Height - margin) / _canvas.Height);
        _zoom = Math.Clamp(Math.Min(zoom, 4), MinZoom, MaxZoom); // 작은 그림은 4배까지만 키운다
        _offset = new Point((Bounds.Width - _canvas.Width * _zoom) / 2, (Bounds.Height - _canvas.Height * _zoom) / 2);
        _fit = true;
        UpdateInterpolation();
        InvalidateVisual();
        if (raise) ViewChanged?.Invoke();
    }

    /// <summary>화면 점 <paramref name="at"/>이 가리키던 도화지 자리를 그대로 두고 <paramref name="factor"/>배로.</summary>
    public void ZoomAt(Point at, double factor)
    {
        var zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        if (zoom == _zoom) return;
        var anchor = (at - _offset) / _zoom;
        _zoom = zoom;
        _offset = at - anchor * zoom;
        _fit = false;
        UpdateInterpolation();
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    public void ZoomActual()
    {
        ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), 1 / _zoom);
    }

    public void ZoomBy(double factor) => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), factor);

    public void PanBy(double dx, double dy)
    {
        _offset += new Point(dx, dy);
        _fit = false;
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    /// <summary>
    /// 도화지의 한 곳이 보이게 한다: 이미 그림 전체가 보이면 그대로 두고, 아니면 그곳이 가운데 오게 옮긴다
    /// (현재 배율로 그 구간이 안 들어오면 들어올 만큼 줄인다).
    /// </summary>
    public void CenterOn(PixelRect rect)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        if (_canvas.Width * _zoom <= Bounds.Width && _canvas.Height * _zoom <= Bounds.Height) return;
        var zoom = _zoom;
        if (rect.Width * zoom > Bounds.Width * 0.9 || rect.Height * zoom > Bounds.Height * 0.9)
            zoom = Math.Clamp(Math.Min(Bounds.Width * 0.8 / rect.Width, Bounds.Height * 0.8 / rect.Height), MinZoom, _zoom);
        _zoom = zoom;
        _offset = new Point(Bounds.Width / 2 - (rect.X + rect.Width / 2.0) * zoom, Bounds.Height / 2 - (rect.Y + rect.Height / 2.0) * zoom);
        _fit = false;
        UpdateInterpolation();
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    void UpdateInterpolation() =>
        RenderOptions.SetBitmapInterpolationMode(this, _zoom >= 2 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);

    // --- 그리기 ---

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        IBrush back = Brushes.Gray;
        if (this.TryFindResource("AppEdBg", ActualThemeVariant, out var v) && v is IBrush b) back = b;
        context.FillRectangle(back, bounds);
        if (_canvas.Width <= 0) return;
        var area = new Rect(_offset.X, _offset.Y, _canvas.Width * _zoom, _canvas.Height * _zoom).Intersect(bounds);
        if (area.Width > 0 && area.Height > 0) context.FillRectangle(Checker, area);
        foreach (var layer in _layers)
        {
            var dest = new Rect(_offset.X + layer.Dest.X * _zoom, _offset.Y + layer.Dest.Y * _zoom, layer.Dest.Width * _zoom, layer.Dest.Height * _zoom);
            if (!dest.Intersects(bounds)) continue;
            using (context.PushOpacity(layer.Opacity))
                context.DrawImage(layer.Image, new Rect(0, 0, layer.Image.Size.Width, layer.Image.Size.Height), dest);
        }
        if (_marks.Count == 0) return;
        var normal = new Pen(new SolidColorBrush(Color.FromRgb(240, 70, 70)), 1.5);
        var current = new Pen(new SolidColorBrush(Color.FromRgb(255, 190, 40)), 3);
        for (var i = 0; i < _marks.Count; i++)
        {
            var m = _marks[i];
            var r = new Rect(_offset.X + m.X * _zoom, _offset.Y + m.Y * _zoom, m.Width * _zoom, m.Height * _zoom).Inflate(3);
            if (!r.Intersects(bounds)) continue;
            context.DrawRectangle(null, i == _current ? current : normal, r);
        }
    }

    static IBrush MakeChecker()
    {
        var light = new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xB8));
        var dark = new SolidColorBrush(Color.FromRgb(0x98, 0x98, 0x98));
        var drawing = new DrawingGroup
        {
            Children =
            {
                new GeometryDrawing { Brush = light, Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)) },
                new GeometryDrawing { Brush = dark, Geometry = new RectangleGeometry(new Rect(0, 0, 8, 8)) },
                new GeometryDrawing { Brush = dark, Geometry = new RectangleGeometry(new Rect(8, 8, 8, 8)) },
            },
        };
        return new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            DestinationRect = new RelativeRect(0, 0, 16, 16, RelativeUnit.Absolute),
            SourceRect = new RelativeRect(0, 0, 16, 16, RelativeUnit.Absolute),
        };
    }

    // --- 입력 ---

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y == 0) return;
        ZoomAt(e.GetPosition(this), Math.Pow(1.15, e.Delta.Y));
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragFrom = e.GetPosition(this);
        _dragOffset = _offset;
        e.Pointer.Capture(this);
        Cursor = new Cursor(StandardCursorType.SizeAll);
        if (e.ClickCount == 2)
        {
            _dragFrom = null;
            if (_fit) ZoomAt(e.GetPosition(this), 1 / _zoom);
            else FitToWindow();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_dragFrom is { } from)
        {
            _offset = _dragOffset + (p - from);
            _fit = false;
            InvalidateVisual();
            ViewChanged?.Invoke();
        }
        var x = (int)Math.Floor((p.X - _offset.X) / _zoom);
        var y = (int)Math.Floor((p.Y - _offset.Y) / _zoom);
        if (x < 0 || y < 0 || x >= _canvas.Width || y >= _canvas.Height) x = y = -1;
        if (x == _hoverX && y == _hoverY) return;
        (_hoverX, _hoverY) = (x, y);
        Hover?.Invoke(x, y);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragFrom is null) return;
        _dragFrom = null;
        e.Pointer.Capture(null);
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverX == -1) return;
        (_hoverX, _hoverY) = (-1, -1);
        Hover?.Invoke(-1, -1);
    }
}
