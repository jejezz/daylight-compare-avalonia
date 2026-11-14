using Avalonia;
using PixelRect = DaylightCompare.Domain.Imaging.PixelRect;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DaylightCompare.App.Dialogs;
using DaylightCompare.App.Icons;
using DaylightCompare.App.Views;
using DaylightCompare.Domain;
using DaylightCompare.Domain.Imaging;
using DaylightCompare.Domain.Policy;
using DaylightCompare.Domain.Settings;
using DaylightCompare.Services;

namespace DaylightCompare.App.Compare;

public enum ImageViewMode { SideBySide, Difference, Overlay, Blink }

/// <summary>
/// 그림 비교 (ROADMAP 6단계, BC의 Picture Compare): 두 그림을 나란히(함께 확대·이동) · 차이(다른 픽셀을 붉게) · 겹치기(투명도) · 깜빡이기(번갈아)로 본다.
/// 허용 오차와 투명도 무시, 크기가 다를 때의 맞춤(왼쪽 위 · 가운데 · 크기 맞춤)은 정책의 <c>image</c> 구역이 기본을 정하고 이 창에서 바꿀 수 있다.
/// 다른 픽셀이 모인 곳(묶음)으로 Ctrl+N/P로 건너뛴다. 비교 창의 탭 하나이며 읽기 전용이다 (그림 파일을 복사·합치는 일은 없다).
/// </summary>
public sealed class ImageCompareView : UserControl, ICompareTab
{
    sealed class Side(bool left)
    {
        public readonly bool Left = left;
        public string? Path;
        public LoadedImage? Image;
        public readonly TextBox PathText = new() { VerticalAlignment = VerticalAlignment.Center };
        public readonly TextBlock InfoText = new() { VerticalAlignment = VerticalAlignment.Center, Classes = { "small" } };
        public readonly ImageCanvas Canvas = new();
        public readonly TextBlock OverlayText = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 420 };
        public readonly Border Overlay = new() { IsHitTestVisible = false, IsVisible = false };
        public Grid? Body;
        public string Name => Path is null ? "" : Locations.NameOf(Path.Replace(ArchivePaths.Separator, "/"));
    }

    readonly Side _left = new(true);
    readonly Side _right = new(false);
    readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _sectionText = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _leftPixel = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _rightPixel = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly DropDownButton _modeButton = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly DropDownButton _alignButton = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly NumericUpDown _tolerance = new() { Minimum = 0, Maximum = 255, Increment = 1, FormatString = "0", Width = 96, VerticalAlignment = VerticalAlignment.Center, ShowButtonSpinner = true };
    readonly Slider _opacity = new() { Minimum = 0, Maximum = 100, Value = 50, Width = 110, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel _opacityPanel = new() { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _gutter = new() { BorderThickness = new Thickness(1, 0, 1, 0) };
    readonly DispatcherTimer _blinkTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    ImageViewMode _mode = ImageViewMode.SideBySide;
    ImageAlignKind _align;
    ImageAlignKind? _alignOverride;
    bool _ignoreAlpha;
    bool? _ignoreAlphaOverride;
    int _toleranceValue;
    int? _toleranceOverride;
    double _maxPercent;
    ImageDiffResult? _result;
    WriteableBitmap? _maskBitmap;
    int _region = -1;
    bool _blinkRight;
    bool _syncing;
    bool _settingUi;
    int _version;
    string? _notice;
    bool _recordHistory = true;
    bool _disposed;
    CancellationTokenSource? _cts;

    public string? LeftPath => _left.Path;
    public string? RightPath => _right.Path;
    public ImageDiffResult? Result => _result;
    public ImageViewMode Mode => _mode;
    public ImageAlignKind Align => _alignOverride ?? _align;
    public int Tolerance => _toleranceOverride ?? _toleranceValue;
    public bool IgnoreAlpha => _ignoreAlphaOverride ?? _ignoreAlpha;
    public int RegionIndex => _region;

    /// <summary>탭에 넣을 그림 비교 보기를 만든다 (아직 읽지 않는다 — 탭이 보일 때 <see cref="Start"/>).</summary>
    public static ImageCompareView Create(string? left, string? right, SessionTab? restore = null, bool recordHistory = true) =>
        new(left, right, restore) { _recordHistory = recordHistory };

    /// <summary>새 비교 창을 열고 첫 탭으로 둔다.</summary>
    public static CompareHost Open(Window? near, string? left, string? right) =>
        CompareHost.Open(near, Create(left, right));

    Window Owner => TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("not attached");

    ImageCompareView(string? left, string? right, SessionTab? restore)
    {
        _left.Path = left;
        _right.Path = right;
        if (restore is not null)
        {
            _mode = restore.ImageMode switch { "difference" => ImageViewMode.Difference, "overlay" => ImageViewMode.Overlay, "blink" => ImageViewMode.Blink, _ => ImageViewMode.SideBySide };
            _alignOverride = restore.Align switch { "topLeft" => ImageAlignKind.TopLeft, "center" => ImageAlignKind.Center, "fit" => ImageAlignKind.Fit, _ => null };
            _toleranceOverride = restore.Tolerance is >= 0 and <= 255 ? restore.Tolerance : null;
        }
        Content = BuildLayout();
        UpdateTitle(raise: false);
        UpdateModeUi();
        foreach (var side in new[] { _left, _right })
        {
            var other = side.Left ? _right : _left;
            side.Canvas.ViewChanged += () =>
            {
                if (_syncing) return;
                _syncing = true;
                other.Canvas.SetView(side.Canvas.Zoom, side.Canvas.Offset, side.Canvas.IsFit);
                _syncing = false;
            };
            side.Canvas.Hover += (x, y) => UpdateHover(x, y);
        }
        _tolerance.ValueChanged += async (_, e) =>
        {
            if (_settingUi || e.NewValue is not { } v) return;
            _toleranceOverride = (int)Math.Clamp(v, 0, 255);
            await CompareAsync(++_version);
        };
        _opacity.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty) UpdateLayers(); };
        _blinkTimer.Tick += (_, _) => { _blinkRight = !_blinkRight; UpdateLayers(); };
        Strings.Changed += OnLanguageChanged;
    }

    // --- 탭 호스트가 부르는 부분 (ICompareTab) ---

    public Control View => this;
    public bool IsFolder => false;
    public string TabIcon => "file-image";
    public event Action? TitleChanged;
    public string WindowTitle { get; private set; } = "";
    public string TabTitle { get; private set; } = "";
    public string? TabToolTip => string.Join('\n', new[] { _left.Path, _right.Path }.Where(p => !string.IsNullOrEmpty(p)));
    public bool IsDirty => false;

    public SessionTab Capture() => new()
    {
        Kind = SessionTab.Image,
        Left = _left.Path ?? "",
        Right = _right.Path ?? "",
        Align = _alignOverride switch { ImageAlignKind.TopLeft => "topLeft", ImageAlignKind.Center => "center", ImageAlignKind.Fit => "fit", _ => "" },
        ImageMode = _mode switch { ImageViewMode.Difference => "difference", ImageViewMode.Overlay => "overlay", ImageViewMode.Blink => "blink", _ => "" },
        Tolerance = _toleranceOverride ?? -1,
    };

    public void Announce(string text)
    {
        _notice = text;
        UpdateStatus();
    }

    public async void Start()
    {
        await ReloadAsync();
        // 읽는 동안 탭이 닫혔으면 기록하지 않는다 (닫힌 뒤에 늦게 쓰면 다음 일의 기록에 섞인다).
        if (!_disposed && _recordHistory && _left.Path is { } l && _right.Path is { } r) App.RecordHistory(l, r, folders: false);
    }

    public Task<bool> ConfirmCloseAsync() => Task.FromResult(true);

    public void Dispose()
    {
        _disposed = true;
        Strings.Changed -= OnLanguageChanged;
        _blinkTimer.Stop();
        _cts?.Cancel();
        _left.Image?.Dispose();
        _right.Image?.Dispose();
        _maskBitmap?.Dispose();
    }

    void OnLanguageChanged() => Dispatcher.UIThread.Post(() =>
    {
        UpdateTitle();
        UpdateModeUi();
        UpdateStatus();
        foreach (var side in new[] { _left, _right }) UpdateSideChrome(side);
    });

    // --- 배치 ---

    Control BuildLayout()
    {
        var s = Strings.Current;
        string Tip(string name, string? key) => key is null ? name : $"{name} ({(OperatingSystem.IsMacOS() ? key.Replace("Ctrl+", "⌘") : key)})";
        Button Tool(string icon, string tip, Action click)
        {
            var b = new Button { Classes = { "tool" }, Content = new DuoIcon { Icon = icon } };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => click();
            return b;
        }
        Button Text(string content, string tip, Action click)
        {
            var b = UiScale.Enable(new Button { Content = content, Classes = { "compact" }, VerticalAlignment = VerticalAlignment.Center });
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => click();
            return b;
        }
        Border Separator() => new() { Width = 1, Margin = new Thickness(4, 8), [!Border.BackgroundProperty] = this.GetResourceObservable("AppStrokeStrong").ToBinding() };
        ToolTip.SetTip(_modeButton, s.ImageModeTooltip);
        ToolTip.SetTip(_alignButton, s.ImageAlignTooltip);
        ToolTip.SetTip(_tolerance, s.ImageToleranceTooltip);
        ToolTip.SetTip(_opacity, s.ImageOverlayTooltip);
        _opacityPanel.Children.Add(_opacity);
        var fit = Text(s.ImageZoomFit, Tip(s.ImageZoomFitTooltip, "Ctrl+0"), () => FitBoth());
        var actual = Text(s.ImageZoomActual, Tip(s.ImageZoomActualTooltip, "Ctrl+1"), () => ActualBoth());
        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _modeButton,
                _opacityPanel,
                Separator(),
                _alignButton,
                ToleranceLabel(),
                _tolerance,
                Separator(),
                fit,
                actual,
                Separator(),
                Tool("up", Tip(s.ComparePrevSection, "Ctrl+P"), () => GoDifference(forward: false)),
                Tool("down", Tip(s.CompareNextSection, "Ctrl+N"), () => GoDifference(forward: true)),
                Separator(),
                Tool("swap", Tip(s.CompareSwap, "Ctrl+U"), () => _ = SwapAsync()),
                Tool("refresh", Tip(s.CompareReload, "F5"), () => _ = ReloadAsync()),
                Tool("bookmark", s.SessionSaveMenu, () => _ = (TopLevel.GetTopLevel(this) as CompareHost)?.SaveSessionAsync()),
                Separator(),
                Tool("settings", s.SettingsTooltip, () => SettingsWindow.Open(Owner)),
                Tool("info", s.AboutTooltip, () => AboutDialog.Open(Owner)),
            },
        };
        var appBar = new Border
        {
            Height = 44,
            Padding = new Thickness(14, 0),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { _status, tools } },
        };
        Grid.SetColumn(tools, 1);
        appBar.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurfaceHi"));
        appBar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStrokeStrong"));

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("44,Auto,*,Auto"),
            ColumnDefinitions = new ColumnDefinitions("*,12,*"),
        };
        Place(grid, appBar, 0, 0, 3);
        foreach (var side in new[] { _left, _right })
        {
            var col = side.Left ? 0 : 2;
            Place(grid, PathRow(side), 1, col, 1);
            side.Overlay.Child = side.OverlayText;
            side.OverlayText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppEdMid"));
            var body = new Grid { Children = { side.Canvas, side.Overlay } };
            Place(grid, body, 2, col, 1);
            side.Body = body;
        }
        _gutter.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurfaceHi"));
        _gutter.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStrokeStrong"));
        Place(grid, _gutter, 1, 1, 1, rowSpan: 2);
        Place(grid, BuildStatusBar(), 3, 0, 3);
        return grid;
    }

    TextBlock ToleranceLabel()
    {
        var label = new TextBlock { Text = Strings.Current.ImageToleranceLabel, VerticalAlignment = VerticalAlignment.Center, Classes = { "small" } };
        label.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        _toleranceLabel = label;
        return label;
    }

    TextBlock? _toleranceLabel;

    static void Place(Grid grid, Control c, int row, int col, int colSpan, int rowSpan = 1)
    {
        Grid.SetRow(c, row);
        Grid.SetColumn(c, col);
        Grid.SetColumnSpan(c, colSpan);
        Grid.SetRowSpan(c, rowSpan);
        grid.Children.Add(c);
    }

    Control PathRow(Side side)
    {
        var s = Strings.Current;
        var icon = new DuoIcon { Icon = "file-image", Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        var open = UiScale.Enable(new Button { Content = s.CompareOpen, Classes = { "compact" } });
        open.Click += async (_, _) => await PickAsync(side);
        side.PathText.PlaceholderText = side.Left ? s.CompareOpenLeftTitle : s.CompareOpenRightTitle;
        side.PathText.AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await ApplyTypedPathAsync(side); }
            else if (e.Key == Key.Escape) { e.Handled = true; side.Canvas.Focus(); }
        }, RoutingStrategies.Tunnel);
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(12, 6),
            Children = { icon, side.PathText, open },
        };
        Grid.SetColumn(side.PathText, 1);
        Grid.SetColumn(open, 2);
        side.InfoText.Margin = new Thickness(12, 0, 12, 6);
        side.InfoText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        var border = new Border { Child = new StackPanel { Children = { row, side.InfoText } }, BorderThickness = new Thickness(0, 0, 0, 1) };
        border.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStrokeStrong"));
        border.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurfaceHi"));
        return border;
    }

    Control BuildStatusBar()
    {
        foreach (var t in new[] { _sectionText, _leftPixel, _rightPixel })
            t.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        var bar = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 3), MinHeight = 26 };
        bar.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurfaceHi"));
        bar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStrokeStrong"));
        var panes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 32, Margin = new Thickness(24, 0), Children = { _leftPixel, _rightPixel } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { _sectionText, panes } };
        Grid.SetColumn(panes, 1);
        bar.Child = grid;
        return bar;
    }

    // --- 보기 방식·맞춤 단추 ---

    string ModeName(ImageViewMode m)
    {
        var s = Strings.Current;
        return m switch { ImageViewMode.Difference => s.ImageModeDifference, ImageViewMode.Overlay => s.ImageModeOverlay, ImageViewMode.Blink => s.ImageModeBlink, _ => s.ImageModeSideBySide };
    }

    string AlignName(ImageAlignKind a)
    {
        var s = Strings.Current;
        return a switch { ImageAlignKind.Center => s.ImageAlignCenter, ImageAlignKind.Fit => s.ImageAlignFit, _ => s.ImageAlignTopLeft };
    }

    /// <summary>보기 방식·맞춤·허용 오차 단추들과 화면 배치(한 그림판인지 두 그림판인지)를 지금 상태에 맞춘다.</summary>
    void UpdateModeUi()
    {
        var s = Strings.Current;
        _modeButton.Content = ModeName(_mode);
        if (_toleranceLabel is not null) _toleranceLabel.Text = s.ImageToleranceLabel;
        var modes = new MenuFlyout();
        foreach (var m in new[] { ImageViewMode.SideBySide, ImageViewMode.Difference, ImageViewMode.Overlay, ImageViewMode.Blink })
        {
            var item = new MenuItem { Header = ModeName(m), ToggleType = MenuItemToggleType.Radio, IsChecked = _mode == m, GroupName = "image-mode" };
            var target = m;
            item.Click += (_, _) => SetMode(target);
            modes.Items.Add(item);
        }
        _modeButton.Flyout = modes;

        _alignButton.Content = AlignName(Align);
        var aligns = new MenuFlyout();
        foreach (var a in new[] { ImageAlignKind.TopLeft, ImageAlignKind.Center, ImageAlignKind.Fit })
        {
            var item = new MenuItem { Header = AlignName(a), ToggleType = MenuItemToggleType.Radio, IsChecked = Align == a, GroupName = "image-align" };
            var target = a;
            item.Click += async (_, _) => await SetAlignAsync(target);
            aligns.Items.Add(item);
        }
        aligns.Items.Add(new Separator());
        var alpha = new MenuItem { Header = s.ImageIgnoreAlpha, ToggleType = MenuItemToggleType.CheckBox, IsChecked = IgnoreAlpha };
        alpha.Click += async (_, _) => await SetIgnoreAlphaAsync(!IgnoreAlpha);
        aligns.Items.Add(alpha);
        _alignButton.Flyout = aligns;

        _settingUi = true;
        _tolerance.Value = Tolerance;
        _settingUi = false;

        var single = _mode != ImageViewMode.SideBySide;
        _opacityPanel.IsVisible = _mode == ImageViewMode.Overlay;
        _gutter.IsVisible = !single;
        _right.Body!.IsVisible = !single;
        Grid.SetColumnSpan(_left.Body!, single ? 3 : 1);
        if (_mode == ImageViewMode.Blink) _blinkTimer.Start();
        else { _blinkTimer.Stop(); _blinkRight = false; }
    }

    public void SetMode(ImageViewMode mode)
    {
        if (_mode == mode) return;
        _mode = mode;
        UpdateModeUi();
        UpdateLayers();
        UpdateStatus();
        // 한 그림판으로 바뀌면 크기가 달라지므로 맞춤 상태면 다시 맞춘다.
        Dispatcher.UIThread.Post(() =>
        {
            if (_left.Canvas.IsFit) _left.Canvas.FitToWindow(raise: true);
            else _right.Canvas.SetView(_left.Canvas.Zoom, _left.Canvas.Offset, false);
        }, DispatcherPriority.Loaded);
    }

    public async Task SetAlignAsync(ImageAlignKind kind)
    {
        if (Align == kind) return;
        _alignOverride = kind;
        UpdateModeUi();
        await CompareAsync(++_version);
    }

    public async Task SetIgnoreAlphaAsync(bool on)
    {
        if (IgnoreAlpha == on) return;
        _ignoreAlphaOverride = on;
        UpdateModeUi();
        await CompareAsync(++_version);
    }

    public async Task SetToleranceAsync(int value)
    {
        _toleranceOverride = Math.Clamp(value, 0, 255);
        UpdateModeUi();
        await CompareAsync(++_version);
    }

    // --- 읽기·비교 ---

    public async Task ReloadAsync()
    {
        var version = ++_version;
        _cts?.Cancel();
        _notice = null;
        _status.Text = Strings.Current.ImageComparing;
        var oldL = _left.Image;
        var oldR = _right.Image;
        _left.Image = _right.Image = null;
        _result = null;
        _region = -1;
        UpdateLayers();
        oldL?.Dispose();
        oldR?.Dispose();
        var loaded = await Task.WhenAll(LoadSide(_left.Path), LoadSide(_right.Path));
        if (version != _version)
        {
            foreach (var x in loaded) x?.Dispose();
            return;
        }
        _left.Image = loaded[0];
        _right.Image = loaded[1];
        foreach (var side in new[] { _left, _right }) UpdateSideChrome(side);
        UpdateTitle();
        UpdateLayers();
        await CompareAsync(version);
    }

    static async Task<LoadedImage?> LoadSide(string? path) =>
        path is { Length: > 0 } ? await ImageFiles.LoadAsync(path) : null;

    /// <summary>두 그림이 모두 있으면 정책의 기본(<c>image</c> 구역)에 이 창에서 고른 것을 얹어 비교한다.</summary>
    async Task CompareAsync(int version)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        if (_left.Image is not { Ok: true } l || _right.Image is not { Ok: true } r)
        {
            _result = null;
            UpdateLayers();
            UpdateStatus();
            return;
        }
        _status.Text = Strings.Current.ImageComparing;
        var policy = await App.Policy.LoadAsync();
        var effective = policy.Resolve(System.IO.Path.GetFileName((_left.Path ?? _right.Path ?? "").Replace(ArchivePaths.Separator, "/")), namesIgnoreCase: PolicyStore.NamesIgnoreCase);
        (_align, _toleranceValue, _ignoreAlpha, _maxPercent) = (effective.Image.Align, effective.Image.Tolerance, effective.Image.IgnoreAlpha, effective.Image.MaxDiffPercent);
        UpdateModeUi();
        var options = new ImageDiffOptions(Tolerance, IgnoreAlpha, Align);
        var layout = ImageLayout.Compute(l.Pixels!.Width, l.Pixels.Height, r.Pixels!.Width, r.Pixels.Height, options.Align);
        if (layout.Pixels > ImageDiff.MaxCanvasPixels)
        {
            _result = null;
            UpdateLayers();
            _status.Text = Strings.Current.ImageTooLarge;
            return;
        }
        ImageDiffResult result;
        try
        {
            var (lp, rp) = (l.Pixels, r.Pixels);
            result = await Task.Run(() => ImageDiff.Compare(lp, rp, options, cts.Token), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (version != _version) return;
        _result = result;
        _maskBitmap?.Dispose();
        _maskBitmap = null;
        _region = -1;
        UpdateLayers();
        UpdateStatus();
        // 처음 차이로
        if (result.Regions.Count > 0) Dispatcher.UIThread.Post(() => GoDifference(forward: true), DispatcherPriority.Background);
    }

    /// <summary>지금 보기 방식에 맞게 그림판에 그림들을 놓는다.</summary>
    void UpdateLayers()
    {
        var l = _left.Image is { Ok: true } ? _left.Image : null;
        var r = _right.Image is { Ok: true } ? _right.Image : null;
        var layout = CurrentLayout(l, r);
        foreach (var side in new[] { _left, _right }) UpdateOverlay(side);
        if (layout is null)
        {
            _left.Canvas.Layers = _right.Canvas.Layers = [];
            _left.Canvas.SetCanvas(0, 0);
            _right.Canvas.SetCanvas(0, 0);
            return;
        }
        _left.Canvas.SetCanvas(layout.Width, layout.Height);
        _right.Canvas.SetCanvas(layout.Width, layout.Height);
        var marks = _result?.Regions ?? [];
        switch (_mode)
        {
            case ImageViewMode.SideBySide:
                _left.Canvas.Layers = l is null ? [] : [new(l.Bitmap!, layout.Left)];
                _right.Canvas.Layers = r is null ? [] : [new(r.Bitmap!, layout.Right)];
                _left.Canvas.SetMarks(marks, _region);
                _right.Canvas.SetMarks(marks, _region);
                break;
            case ImageViewMode.Difference:
                var layers = new List<ImageCanvas.Layer>();
                if (l is not null) layers.Add(new(l.Bitmap!, layout.Left, 0.35));
                else if (r is not null) layers.Add(new(r.Bitmap!, layout.Right, 0.35));
                if (_result is { } res && l is not null && r is not null) layers.Add(new(_maskBitmap ??= ImageFiles.MaskBitmap(res), new PixelRect(0, 0, layout.Width, layout.Height)));
                _left.Canvas.Layers = layers;
                _left.Canvas.SetMarks([], -1);
                break;
            case ImageViewMode.Overlay:
                var over = new List<ImageCanvas.Layer>();
                if (l is not null) over.Add(new(l.Bitmap!, layout.Left));
                if (r is not null) over.Add(new(r.Bitmap!, layout.Right, l is null ? 1 : _opacity.Value / 100));
                _left.Canvas.Layers = over;
                _left.Canvas.SetMarks([], -1);
                break;
            default: // 깜빡이기
                var shown = (_blinkRight && r is not null) || l is null ? r : l;
                _left.Canvas.Layers = shown is null ? [] : [new(shown.Bitmap!, shown == r ? layout.Right : layout.Left)];
                _left.Canvas.SetMarks([], -1);
                break;
        }
    }

    /// <summary>두 그림이 놓이는 도화지 (한쪽만 있으면 그 그림 크기로).</summary>
    ImageLayout? CurrentLayout(LoadedImage? l, LoadedImage? r)
    {
        if (l is null && r is null) return null;
        var (lw, lh) = l is null ? (r!.Pixels!.Width, r.Pixels.Height) : (l.Pixels!.Width, l.Pixels.Height);
        var (rw, rh) = r is null ? (lw, lh) : (r.Pixels!.Width, r.Pixels.Height);
        return ImageLayout.Compute(lw, lh, rw, rh, Align);
    }

    void UpdateOverlay(Side side)
    {
        var s = Strings.Current;
        (side.Overlay.IsVisible, side.OverlayText.Text) = side.Image switch
        {
            null => (true, s.CompareEmptySide),
            { Error: { } e } => (true, s.CompareLoadFailed(e.ToString())),
            { DecodeError: { } d } => (true, s.ImageDecodeFailed(d)),
            _ => (false, ""),
        };
    }

    void UpdateSideChrome(Side side)
    {
        var s = Strings.Current;
        if (!side.PathText.IsFocused) side.PathText.Text = side.Path ?? "";
        ToolTip.SetTip(side.PathText, side.Path);
        side.InfoText.Text = side.Image is { Ok: true, Pixels: { } p } img
            ? s.CompareHeaderInfo(img.Modified.ToLocalTime(), img.Length) + "   ·   " + s.ImageSizeInfo(p.Width, p.Height)
            : "";
        UpdateOverlay(side);
    }

    void UpdateTitle(bool raise = true)
    {
        WindowTitle = Strings.Current.CompareWindowTitle(_left.Name, _right.Name);
        TabTitle = _left.Name == _right.Name ? _left.Name : WindowTitle;
        if (raise) TitleChanged?.Invoke();
    }

    // --- 열기·바꾸기 ---

    async Task PickAsync(Side side)
    {
        var s = Strings.Current;
        var files = await Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = side.Left ? s.CompareOpenLeftTitle : s.CompareOpenRightTitle,
            AllowMultiple = false,
            SuggestedStartLocation = await PickLocations.StartAsync(Owner.StorageProvider),
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        PickLocations.Remember(path, isFolder: false);
        await SetPathAsync(side, path);
    }

    async Task ApplyTypedPathAsync(Side side)
    {
        var typed = side.PathText.Text?.Trim() ?? "";
        side.Canvas.Focus();
        if (typed.Length == 0 || typed == side.Path) { UpdateSideChrome(side); return; }
        await SetPathAsync(side, typed);
    }

    async Task SetPathAsync(Side side, string path)
    {
        side.Path = path;
        UpdateTitle();
        await ReloadAsync();
        if (_left.Path is { } l && _right.Path is { } r) App.RecordHistory(l, r, folders: false);
    }

    public async Task SwapAsync()
    {
        (_left.Path, _right.Path) = (_right.Path, _left.Path);
        UpdateTitle();
        await ReloadAsync();
    }

    // --- 이동 ---

    void FitBoth()
    {
        _left.Canvas.FitToWindow();
    }

    void ActualBoth() => _left.Canvas.ZoomActual();

    void Zoom(double factor) => _left.Canvas.ZoomBy(factor);

    void Pan(double dx, double dy) => _left.Canvas.PanBy(dx, dy);

    /// <summary>다음·이전 차이 묶음으로 (끝에서 멈춘다).</summary>
    public void GoDifference(bool forward)
    {
        if (_result is null || ImageDiff.NextRegion(_result.Regions, _region, forward) is not { } index) return;
        _region = index;
        var rect = _result.Regions[index];
        foreach (var side in new[] { _left, _right }) side.Canvas.SetMarks(_mode == ImageViewMode.SideBySide ? _result.Regions : [], _region);
        _left.Canvas.CenterOn(rect);
        UpdateStatus();
    }

    // --- 상태 ---

    void UpdateStatus()
    {
        var s = Strings.Current;
        if (_notice is { } n) _status.Text = n;
        else if (_left.Image is not { Ok: true } || _right.Image is not { Ok: true }) _status.Text = "";
        else if (_result is not { } result) _status.Text = s.ImageComparing;
        else if (result.IsIdentical) _status.Text = s.CompareIdentical;
        else if (_maxPercent > 0 && result.IsWithin(_maxPercent)) _status.Text = s.ImageWithinLimit(result.Percent, _maxPercent);
        else
        {
            var (lw, lh, rw, rh) = (_left.Image.Pixels!.Width, _left.Image.Pixels.Height, _right.Image.Pixels!.Width, _right.Image.Pixels.Height);
            _status.Text = s.ImageDifferences(result.DifferentPixels, result.Percent) + (lw != rw || lh != rh ? "  ·  " + s.ImageSizesDiffer(lw, lh, rw, rh) : "");
        }
        if (_result is null || _result.Regions.Count == 0) _sectionText.Text = "";
        else _sectionText.Text = _region >= 0 ? s.CompareSectionPosition(_region + 1, _result.Regions.Count) : s.CompareSectionCount(_result.Regions.Count);
    }

    /// <summary>포인터가 가리키는 픽셀의 좌표와 색 (양쪽 그림 각각, 그 자리에 그림이 없으면 —).</summary>
    void UpdateHover(int x, int y)
    {
        var s = Strings.Current;
        if (x < 0) { _leftPixel.Text = _rightPixel.Text = ""; return; }
        var layout = CurrentLayout(_left.Image is { Ok: true } ? _left.Image : null, _right.Image is { Ok: true } ? _right.Image : null);
        _leftPixel.Text = PixelText(s.HomeLeft, _left.Image, layout?.Left, x, y);
        _rightPixel.Text = PixelText(s.HomeRight, _right.Image, layout?.Right, x, y);
    }

    static string PixelText(string name, LoadedImage? image, PixelRect? rect, int x, int y)
    {
        if (image is not { Ok: true } || rect is not { } r) return $"{name}  —";
        var px = ImageLayout.Sample(image.Pixels!, r, x, y);
        return Strings.Current.ImagePixel(name, x, y, px is { } c ? $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}" : "—");
    }

    // --- 키 (Beyond Compare 기본 키: BC_COMPAT.md) ---

    public void HandleKey(KeyEventArgs e)
    {
        if (_left.PathText.IsFocused || _right.PathText.IsFocused || _tolerance.IsKeyboardFocusWithin) return;
        var primary = Mods.Primary(e.KeyModifiers);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var handled = true;
        switch (e.Key)
        {
            case Key.N when primary: GoDifference(forward: true); break;
            case Key.P when primary: GoDifference(forward: false); break;
            case Key.F8: GoDifference(forward: !shift); break;
            case Key.Down when alt: GoDifference(forward: true); break;
            case Key.Up when alt: GoDifference(forward: false); break;
            case Key.U when primary && !shift: _ = SwapAsync(); break;
            case Key.F5: _ = ReloadAsync(); break;
            case Key.OemComma when primary: SettingsWindow.Open(Owner); break;
            case Key.D0 or Key.NumPad0 when primary: FitBoth(); break;
            case Key.D1 or Key.NumPad1 when primary: ActualBoth(); break;
            case Key.OemPlus or Key.Add: Zoom(1.25); break;
            case Key.OemMinus or Key.Subtract: Zoom(1 / 1.25); break;
            case Key.Left: Pan(48, 0); break;
            case Key.Right: Pan(-48, 0); break;
            case Key.Up: Pan(0, 48); break;
            case Key.Down: Pan(0, -48); break;
            default: handled = false; break;
        }
        e.Handled = handled;
    }
}
