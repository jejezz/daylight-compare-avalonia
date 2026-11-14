using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DaylightCompare.App.Dialogs;
using DaylightCompare.App.Icons;
using DaylightCompare.App.Views;
using DaylightCompare.Domain;
using DaylightCompare.Domain.Binary;
using DaylightCompare.Domain.Policy;
using DaylightCompare.Domain.Settings;
using DaylightCompare.Services;

namespace DaylightCompare.App.Compare;

/// <summary>
/// 바이너리(16진수) 비교 (ROADMAP 5단계): 좌우 두 패널에 오프셋·16진수·문자를 보이고 다른 바이트를 칠한다. 비교 창의 탭 하나다.
/// 정렬은 두 가지 — 고정 위치(펌웨어 이미지) / 삽입 감지(사이에 바이트가 끼어도 뒤가 어긋나지 않게). 정책의 <c>binary</c> 구역이 기본을 정하고
/// 무시 구간은 양쪽을 같게 가려서 비교하되 화면에는 흐리게 보인다. 파일 전체를 복사하는 일은 여기서도 없다 (읽기 전용).
/// 큰 파일은 메모리에 올리지 않고 위치로 읽는다 (<see cref="FileByteSource"/>) — 보이는 행만 읽어 그린다.
/// </summary>
public sealed class BinaryCompareView : UserControl, ICompareTab
{
    sealed class Side(bool left)
    {
        public readonly bool Left = left;
        public string? Path;
        public BinarySource? Source;
        public readonly TextBox PathText = new() { VerticalAlignment = VerticalAlignment.Center };
        public readonly TextBlock InfoText = new() { VerticalAlignment = VerticalAlignment.Center, Classes = { "small" } };
        public readonly HexPane Pane = new(left);
        public readonly TextBlock OverlayText = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 420 };
        public readonly Border Overlay = new() { IsHitTestVisible = false, IsVisible = false };
        public string Name => Path is null ? "" : Locations.NameOf(Path.Replace(ArchivePaths.Separator, "/"));
    }

    readonly Side _left = new(true);
    readonly Side _right = new(false);
    readonly BinaryOverview _overview = new();
    readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _sectionText = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _leftCursor = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _rightCursor = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly DropDownButton _alignButton = new() { VerticalAlignment = VerticalAlignment.Center };

    BinaryDiffResult? _result;
    BinaryRows? _rows;
    BinaryAlignKind? _alignOverride;
    BinaryAlignKind _align = BinaryAlignKind.Fixed;
    IReadOnlyList<ByteRange> _ignore = [];
    (long Row, int Col) _cursor;
    long _top;
    int _version;
    string? _notice;
    bool _recordHistory = true;
    CancellationTokenSource? _cts;
    byte _fill = 0xFF;

    public string? LeftPath => _left.Path;
    public string? RightPath => _right.Path;
    public BinaryDiffResult? Result => _result;
    public BinaryRows? Rows => _rows;
    public BinaryAlignKind Align => _alignOverride ?? _align;

    /// <summary>탭에 넣을 바이너리 비교 보기를 만든다 (아직 읽지 않는다 — 탭이 보일 때 <see cref="Start"/>).</summary>
    public static BinaryCompareView Create(string? left, string? right, SessionTab? restore = null, bool recordHistory = true) =>
        new(left, right, restore) { _recordHistory = recordHistory };

    /// <summary>새 비교 창을 열고 첫 탭으로 둔다.</summary>
    public static CompareHost Open(Window? near, string? left, string? right) =>
        CompareHost.Open(near, Create(left, right));

    Window Owner => TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("not attached");

    BinaryCompareView(string? left, string? right, SessionTab? restore)
    {
        _left.Path = left;
        _right.Path = right;
        if (restore is not null) _alignOverride = restore.Align == "insertions" ? BinaryAlignKind.Insertions : restore.Align == "fixed" ? BinaryAlignKind.Fixed : null;
        Content = BuildLayout();
        UpdateTitle(raise: false);
        foreach (var side in new[] { _left, _right })
        {
            side.Pane.ViewportChanged += () => { UpdateViewport(); };
            side.Pane.CursorRequested += (row, col) => MoveCursor(row, col, scroll: false);
            side.Pane.WheelRequested += lines => Scroll(lines);
        }
        _overview.ScrollRequested += top => SetTop((long)Math.Floor(top));
        _overview.WheelRequested += lines => Scroll(lines);
        Strings.Changed += OnLanguageChanged;
    }

    // --- 탭 호스트가 부르는 부분 (ICompareTab) ---

    public Control View => this;
    public bool IsFolder => false;
    public string TabIcon => "memory";
    public event Action? TitleChanged;
    public string WindowTitle { get; private set; } = "";
    public string TabTitle { get; private set; } = "";
    public string? TabToolTip => string.Join('\n', new[] { _left.Path, _right.Path }.Where(p => !string.IsNullOrEmpty(p)));
    public bool IsDirty => false;

    public SessionTab Capture() => new()
    {
        Kind = SessionTab.Binary,
        Left = _left.Path ?? "",
        Right = _right.Path ?? "",
        Align = _alignOverride switch { BinaryAlignKind.Insertions => "insertions", BinaryAlignKind.Fixed => "fixed", _ => "" },
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

    bool _disposed;

    public Task<bool> ConfirmCloseAsync() => Task.FromResult(true);

    public void Dispose()
    {
        _disposed = true;
        Strings.Changed -= OnLanguageChanged;
        _cts?.Cancel();
        _left.Source?.Dispose();
        _right.Source?.Dispose();
    }

    void OnLanguageChanged() => Dispatcher.UIThread.Post(() =>
    {
        UpdateTitle();
        UpdateAlignButton();
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
        Border Separator() => new() { Width = 1, Margin = new Thickness(4, 8), [!Border.BackgroundProperty] = this.GetResourceObservable("AppStrokeStrong").ToBinding() };
        ToolTip.SetTip(_alignButton, s.BinaryAlignTooltip);
        UpdateAlignButton();
        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _alignButton,
                Separator(),
                Tool("up", Tip(s.ComparePrevSection, "Ctrl+P"), () => GoDifference(forward: false)),
                Tool("down", Tip(s.CompareNextSection, "Ctrl+N"), () => GoDifference(forward: true)),
                Tool("goto-line", Tip(s.BinaryGoTo, "Ctrl+G"), () => _ = GoToAsync()),
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
            ColumnDefinitions = new ColumnDefinitions("*,32,*,22"),
        };
        Place(grid, appBar, 0, 0, 4);
        foreach (var side in new[] { _left, _right })
        {
            var col = side.Left ? 0 : 2;
            Place(grid, PathRow(side), 1, col, side.Left ? 1 : 2);
            side.Overlay.Child = side.OverlayText;
            side.OverlayText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppEdMid"));
            var body = new Grid { Children = { side.Pane, side.Overlay } };
            Place(grid, body, 2, col, 1);
        }
        var gutter = new Border { BorderThickness = new Thickness(1, 0, 1, 0) };
        gutter.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurfaceHi"));
        gutter.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStrokeStrong"));
        Place(grid, gutter, 1, 1, 1, rowSpan: 2);
        Place(grid, _overview, 2, 3, 1);
        Place(grid, BuildStatusBar(), 3, 0, 4);
        return grid;
    }

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
        var icon = new DuoIcon { Icon = "memory", Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        var open = UiScale.Enable(new Button { Content = s.CompareOpen, Classes = { "compact" } });
        open.Click += async (_, _) => await PickAsync(side);
        side.PathText.PlaceholderText = side.Left ? s.CompareOpenLeftTitle : s.CompareOpenRightTitle;
        side.PathText.AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await ApplyTypedPathAsync(side); }
            else if (e.Key == Key.Escape) { e.Handled = true; side.Pane.Focus(); }
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
        foreach (var t in new[] { _sectionText, _leftCursor, _rightCursor })
            t.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        var bar = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 3), MinHeight = 26 };
        bar.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurfaceHi"));
        bar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStrokeStrong"));
        var panes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 32, Margin = new Thickness(24, 0), Children = { _leftCursor, _rightCursor } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { _sectionText, panes } };
        Grid.SetColumn(panes, 1);
        bar.Child = grid;
        return bar;
    }

    void UpdateAlignButton()
    {
        var s = Strings.Current;
        _alignButton.Content = Align == BinaryAlignKind.Insertions ? s.BinaryAlignInsertions : s.BinaryAlignFixed;
        var flyout = new MenuFlyout();
        foreach (var kind in new[] { BinaryAlignKind.Fixed, BinaryAlignKind.Insertions })
        {
            var item = new MenuItem
            {
                Header = kind == BinaryAlignKind.Insertions ? s.BinaryAlignInsertions : s.BinaryAlignFixed,
                ToggleType = MenuItemToggleType.Radio, IsChecked = Align == kind, GroupName = "binary-align",
            };
            var target = kind;
            item.Click += async (_, _) => await SetAlignAsync(target);
            flyout.Items.Add(item);
        }
        _alignButton.Flyout = flyout;
    }

    // --- 읽기·비교 ---

    /// <summary>정렬을 바꿔 다시 비교한다 (이 비교 창에만 — 정책 파일은 그대로).</summary>
    public async Task SetAlignAsync(BinaryAlignKind kind)
    {
        if (Align == kind) return;
        _alignOverride = kind;
        UpdateAlignButton();
        await CompareAsync(++_version);
    }

    public async Task ReloadAsync()
    {
        var version = ++_version;
        _cts?.Cancel();
        _notice = null;
        _status.Text = Strings.Current.BinaryComparing;
        var oldL = _left.Source;
        var oldR = _right.Source;
        _left.Source = _right.Source = null;
        _rows = null;
        SetPanes();
        oldL?.Dispose();
        oldR?.Dispose();
        // 이름에 맞는 정책 형식에 변환기가 있으면(Intel HEX·S-Record) 이미지로 풀어서 연다 — 그 밖의 파일은 그대로.
        var policy = await App.Policy.LoadAsync();
        async Task<BinarySource?> OpenSide(string? path)
        {
            if (path is not { Length: > 0 }) return null;
            var effective = policy.Resolve(System.IO.Path.GetFileName(path.Replace(ArchivePaths.Separator, "/")), namesIgnoreCase: PolicyStore.NamesIgnoreCase);
            _fill = effective.Binary.FillByte;
            return effective.Converter?.Builtin is "ihex-to-bin" or "srec-to-bin"
                ? await BinarySources.OpenConvertedAsync(path, effective.Converter.Builtin, effective.Binary.FillByte)
                : await BinarySources.OpenAsync(path);
        }
        var sources = await Task.WhenAll(OpenSide(_left.Path), OpenSide(_right.Path));
        if (version != _version)
        {
            foreach (var x in sources) x?.Dispose();
            return;
        }
        // 둘 다 펌웨어 이미지면 같은 주소 범위로 맞춰서 주소끼리 비교한다 (시작 주소가 달라도 어긋나지 않게).
        if (sources[0] is { ConvertedFrom: not null, Source: MemoryByteSource lm } l0 && sources[1] is { ConvertedFrom: not null, Source: MemoryByteSource rm } r0)
        {
            var (a, b) = FirmwareFormats.Align(new FirmwareImage(lm.Data, l0.BaseAddress, l0.Records, l0.Warnings), new FirmwareImage(rm.Data, r0.BaseAddress, r0.Records, r0.Warnings), _fill);
            if (a.Data != lm.Data) sources[0] = l0 with { Source = new MemoryByteSource(a.Data), BaseAddress = a.BaseAddress };
            if (b.Data != rm.Data) sources[1] = r0 with { Source = new MemoryByteSource(b.Data), BaseAddress = b.BaseAddress };
        }
        _left.Source = sources[0];
        _right.Source = sources[1];
        foreach (var side in new[] { _left, _right }) UpdateSideChrome(side);
        UpdateTitle();
        await CompareAsync(version);
    }

    async Task CompareAsync(int version)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        if (_left.Source is not { Ok: true } l || _right.Source is not { Ok: true } r)
        {
            _result = null;
            _rows = null;
            SetPanes();
            UpdateStatus();
            return;
        }
        _status.Text = Strings.Current.BinaryComparing;
        var policy = await App.Policy.LoadAsync();
        var effective = policy.Resolve(System.IO.Path.GetFileName(_left.Path ?? _right.Path ?? ""), namesIgnoreCase: PolicyStore.NamesIgnoreCase);
        _align = effective.Binary.Align;
        _ignore = effective.Binary.IgnoreRanges;
        var align = Align;
        var ignore = _ignore;
        IByteSource left = l.Source!, right = r.Source!;
        BinaryDiffResult result;
        try
        {
            result = await Task.Run(() => BinaryDiff.Compare(
                ignore.Count > 0 ? new MaskedByteSource(left, ignore) : left,
                ignore.Count > 0 ? new MaskedByteSource(right, ignore) : right, align, cts.Token), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (version != _version) return;
        _result = result;
        _rows = new BinaryRows(result);
        UpdateAlignButton();
        SetPanes();
        _top = 0;
        _cursor = (0, 0);
        // 처음 차이로: 없으면 처음 그대로
        if (_rows.NextDifferenceRow(-1, forward: true) is { } first) { _cursor = (first, 0); }
        UpdateStatus();
        Dispatcher.UIThread.Post(() =>
        {
            if (_rows is null) return;
            if (_rows.RowCount > 0 && _result is { IsIdentical: false }) ScrollRowIntoView(_cursor.Row, center: true);
            UpdateViewport();
            UpdateCursorUi();
        }, DispatcherPriority.Background);
    }

    void SetPanes()
    {
        var ok = _left.Source is { Ok: true } && _right.Source is { Ok: true };
        _left.Pane.AddressBase = _left.Source?.BaseAddress ?? 0;
        _right.Pane.AddressBase = _right.Source?.BaseAddress ?? 0;
        _left.Pane.SetData(ok ? _rows : null, _left.Source?.Source, _right.Source?.Source, _ignore);
        _right.Pane.SetData(ok ? _rows : null, _right.Source?.Source, _left.Source?.Source, _ignore);
        _overview.Rows = ok ? _rows : null;
        foreach (var side in new[] { _left, _right }) UpdateOverlay(side);
    }

    void UpdateOverlay(Side side)
    {
        var s = Strings.Current;
        (side.Overlay.IsVisible, side.OverlayText.Text) = side.Source switch
        {
            null => (true, s.CompareEmptySide),
            { Error: { } e } => (true, s.CompareLoadFailed(e.ToString())),
            { Ok: true } when _rows is not null && _rows.RowCount == 0 => (true, s.BinaryEmptyFile),
            _ => (false, ""),
        };
    }

    void UpdateSideChrome(Side side)
    {
        var s = Strings.Current;
        if (!side.PathText.IsFocused) side.PathText.Text = side.Path ?? "";
        ToolTip.SetTip(side.PathText, side.Path);
        side.InfoText.Text = side.Source is { Ok: true } src ? s.CompareHeaderInfo(src.Modified.ToLocalTime(), src.Length) + ConversionInfo(src) : "";
        ToolTip.SetTip(side.InfoText, side.Source is { Warnings.Count: > 0 } w ? string.Join('\n', w.Warnings.Take(15)) : null);
        UpdateOverlay(side);
    }

    /// <summary>머리글 둘째 줄에 덧붙이는 변환 안내: 펌웨어 파일을 이미지로 풀었으면 주소 범위·레코드 수·경고 수, 풀지 못했으면 그 이유.</summary>
    static string ConversionInfo(BinarySource src)
    {
        var s = Strings.Current;
        if (src.ConvertedFrom is { } kind) return "   ·   " + s.BinaryConverted(kind, src.BaseAddress, src.BaseAddress + src.Length, src.Records, src.Warnings.Count);
        return src.ConvertError is { } error ? "   ·   " + s.BinaryConvertFailed(error) : "";
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
        side.Pane.Focus();
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

    long VisibleRows => Math.Max(1, _left.Pane.VisibleRows);

    long MaxTop => Math.Max(0, (_rows?.RowCount ?? 0) - VisibleRows);

    void SetTop(long top)
    {
        top = Math.Clamp(top, 0, MaxTop);
        _top = top;
        _left.Pane.TopRow = top;
        _right.Pane.TopRow = top;
        UpdateViewport();
    }

    void Scroll(double lines) => SetTop(_top + (long)Math.Round(lines));

    void ScrollRowIntoView(long row, bool center)
    {
        if (_rows is null) return;
        if (center) SetTop(row - VisibleRows / 3);
        else if (row < _top) SetTop(row);
        else if (row >= _top + VisibleRows) SetTop(row - VisibleRows + 1);
    }

    void UpdateViewport() => _overview.Viewport = (_top, _left.Pane.VisibleRows);

    /// <summary>커서를 옮긴다 (그 행·칸이 있는 바이트가 없으면 가장 가까운 바이트로).</summary>
    public void MoveCursor(long row, int col, bool scroll = true)
    {
        if (_rows is null || _rows.RowCount == 0) return;
        row = Math.Clamp(row, 0, _rows.RowCount - 1);
        var r = _rows.At(row);
        col = Math.Clamp(col, 0, Math.Max(0, Math.Max(r.LeftCount, r.RightCount) - 1));
        _cursor = (row, col);
        if (scroll) ScrollRowIntoView(row, center: false);
        UpdateCursorUi();
    }

    void UpdateCursorUi()
    {
        _left.Pane.CursorCell = _rows is null ? null : _cursor;
        _right.Pane.CursorCell = _rows is null ? null : _cursor;
        _overview.CaretRow = _rows is null ? null : _cursor.Row;
        UpdateStatus();
    }

    public void GoDifference(bool forward)
    {
        if (_rows?.NextDifferenceRow(_cursor.Row, forward) is not { } row) return;
        MoveCursor(row, 0, scroll: false);
        ScrollRowIntoView(row, center: true);
    }

    /// <summary>위치 이동 (Ctrl+G): 바이트 위치 — <c>0x1A2B</c>처럼 적으면 16진수, 아니면 10진수. 왼쪽 파일 기준.</summary>
    async Task GoToAsync()
    {
        var s = Strings.Current;
        var typed = await InputDialog.ShowAsync(Owner, s.BinaryGoTo, s.BinaryGoToPrompt, "", s.CommonOk, s.CommonCancel);
        if (typed is null || !TryParseOffset(typed, out var offset) || _rows is null) return;
        offset -= _left.Source?.BaseAddress ?? 0; // 펌웨어 이미지는 주소로 적는다
        if (offset < 0) return;
        var row = _rows.RowOfLeftOffset(offset);
        var r = _rows.At(row);
        MoveCursor(row, (int)Math.Clamp(offset - r.LeftOffset, 0, BinaryRows.Width - 1), scroll: false);
        ScrollRowIntoView(row, center: true);
    }

    public static bool TryParseOffset(string text, out long offset)
    {
        text = text.Trim().Replace("_", "").Replace(",", "");
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, null, out offset) && offset >= 0;
        return long.TryParse(text, out offset) && offset >= 0;
    }

    // --- 상태 ---

    void UpdateStatus()
    {
        var s = Strings.Current;
        if (_notice is { } n) _status.Text = n;
        else if (_left.Source is not { Ok: true } || _right.Source is not { Ok: true }) _status.Text = "";
        else if (_result is not { } result) _status.Text = s.BinaryComparing;
        else _status.Text = result.IsIdentical ? s.CompareIdentical
            : result.DifferenceCount == 0 ? s.BinaryLengthDiffers(result.LeftLength, result.RightLength)
            : s.BinaryDifferences(result.DifferenceCount, result.DifferentBytes);

        if (_rows is null || _rows.RowCount == 0 || _result is null) { _sectionText.Text = ""; _leftCursor.Text = _rightCursor.Text = ""; return; }
        var differences = _rows.Blocks.Select((b, i) => (b, i)).Where(x => x.b.IsDifference).ToList();
        var block = _rows.BlockOfRow(_cursor.Row);
        var index = differences.FindIndex(x => x.i == block);
        _sectionText.Text = index >= 0 ? s.CompareSectionPosition(index + 1, differences.Count) : s.CompareSectionCount(differences.Count);
        var (lo, ro) = _rows.OffsetsAt(_cursor.Row, _cursor.Col);
        _leftCursor.Text = CursorText(s.HomeLeft, _left.Source?.Source, lo, _left.Source?.BaseAddress ?? 0);
        _rightCursor.Text = CursorText(s.HomeRight, _right.Source?.Source, ro, _right.Source?.BaseAddress ?? 0);
    }

    /// <summary>커서 바이트의 위치와 값: <c>왼쪽 0x00001A2B  41 (65)  LE16 4241 BE16 4142  LE32 … BE32 …</c> — 리틀·빅 엔디언 값 (BC Hex Compare의 값 보기).</summary>
    static string CursorText(string name, IByteSource? source, long? offset, long baseAddress)
    {
        if (source is null || offset is not { } o) return $"{name}  —";
        Span<byte> b = stackalloc byte[4];
        var n = source.Read(o, b);
        if (n <= 0) return $"{name}  0x{o + baseAddress:X8}  —";
        var text = $"{name}  0x{o + baseAddress:X8}  {b[0]:X2} ({b[0]})";
        if (n >= 2) text += $"  LE16 {(b[1] << 8 | b[0]):X4}  BE16 {(b[0] << 8 | b[1]):X4}";
        if (n >= 4) text += $"  LE32 {(uint)(b[3] << 24 | b[2] << 16 | b[1] << 8 | b[0]):X8}  BE32 {(uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]):X8}";
        return text;
    }

    // --- 키 (Beyond Compare 기본 키: BC_COMPAT.md) ---

    public void HandleKey(KeyEventArgs e)
    {
        if (_left.PathText.IsFocused || _right.PathText.IsFocused) return;
        var primary = Mods.Primary(e.KeyModifiers);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var handled = true;
        var page = (int)VisibleRows;
        switch (e.Key)
        {
            case Key.N when primary: GoDifference(forward: true); break;
            case Key.P when primary: GoDifference(forward: false); break;
            case Key.F8: GoDifference(forward: !shift); break;
            case Key.Down when alt: GoDifference(forward: true); break;
            case Key.Up when alt: GoDifference(forward: false); break;
            case Key.G when primary: _ = GoToAsync(); break;
            case Key.U when primary && !shift: _ = SwapAsync(); break;
            case Key.F5: _ = ReloadAsync(); break;
            case Key.OemComma when primary: SettingsWindow.Open(Owner); break;
            case Key.Left: StepCursor(-1); break;
            case Key.Right: StepCursor(+1); break;
            case Key.Up: MoveCursor(_cursor.Row - 1, _cursor.Col); break;
            case Key.Down: MoveCursor(_cursor.Row + 1, _cursor.Col); break;
            case Key.PageUp: MoveCursor(_cursor.Row - page, _cursor.Col); break;
            case Key.PageDown: MoveCursor(_cursor.Row + page, _cursor.Col); break;
            case Key.Home when primary: MoveCursor(0, 0); break;
            case Key.End when primary: MoveCursor(long.MaxValue / 2, 0); break;
            case Key.Home: MoveCursor(_cursor.Row, 0); break;
            case Key.End: MoveCursor(_cursor.Row, BinaryRows.Width - 1); break;
            default: handled = false; break;
        }
        e.Handled = handled;
    }

    /// <summary>한 바이트씩 앞뒤로 (줄 끝에서는 다음·이전 행으로).</summary>
    void StepCursor(int delta)
    {
        if (_rows is null) return;
        var (row, col) = _cursor;
        col += delta;
        if (col < 0) { row--; col = BinaryRows.Width - 1; }
        else if (col >= BinaryRows.Width) { row++; col = 0; }
        MoveCursor(row, col);
    }
}
