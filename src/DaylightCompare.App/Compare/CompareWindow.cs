using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using DaylightCompare.App.Dialogs;
using DaylightCompare.App.Icons;
using DaylightCompare.App.Views;
using DaylightCompare.Domain;
using DaylightCompare.Domain.Diff;
using DaylightCompare.Domain.Drop;
using DaylightCompare.Domain.Policy;
using DaylightCompare.Services;
using TextMateSharp.Grammars;

namespace DaylightCompare.App.Compare;

/// <summary>
/// 텍스트 비교 창 (ROADMAP 3단계, 창 하나 = 비교 하나). 좌우 편집기는 <see cref="AlignedDiff"/>로 같은 행에 맞춘 글을 보이므로
/// 스크롤 위치만 같게 두면 늘 나란히 보인다. 이 판은 <b>읽기 전용</b>이다 — 비교 창 안에서 바로 고치기는 다음 단계.
/// </summary>
public sealed class CompareWindow : Window
{
    static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, Monaco, DejaVu Sans Mono, monospace");

    /// <summary>강조(TextMate)를 켜는 크기 상한 — 큰 생성 파일에서 화면이 느려지지 않게.</summary>
    const int HighlightMaxChars = 2 * 1024 * 1024;

    sealed class Side(bool left)
    {
        public readonly bool Left = left;
        public string? Path;
        public CompareSource? Source;
        public TextEncodingKind? ForcedEncoding;
        public readonly TextEditor Editor = new();
        public RowNumberMargin Margin = null!;
        public readonly TextBlock PathText = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.PathSegmentEllipsis };
        public readonly TextBlock EncodingText = new() { VerticalAlignment = VerticalAlignment.Center, Classes = { "small" } };
        public readonly Border Overlay = new() { IsHitTestVisible = false, IsVisible = false };
        public readonly TextBlock OverlayText = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 420 };
        public readonly Border Banner = new() { IsVisible = false, Padding = new Thickness(12, 6) };
        public readonly TextBlock BannerText = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        public readonly StackPanel BannerActions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        public TextMate.Installation? TextMate;
        public string? Language;
        public MixedRepair? Repair;

        public CompareSource.Text? Text => Source as CompareSource.Text;
    }

    readonly Side _left = new(true);
    readonly Side _right = new(false);
    readonly DiffViewState _leftState = new();
    readonly DiffViewState _rightState = new();
    readonly OverviewBar _overview = new();
    readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly ToggleButton _unimportant = new() { Classes = { "tool" }, IsChecked = true };
    readonly Border _dropHint = new() { IsVisible = false, IsHitTestVisible = false, BorderThickness = new Thickness(2) };
    bool _syncing;
    int _version;

    public static CompareWindow Open(Window? near, string? left, string? right)
    {
        var w = new CompareWindow(left, right);
        if (near is not null) WindowPlacementHelper.OpenNear(near, w);
        else w.Show();
        return w;
    }

    CompareWindow(string? left, string? right)
    {
        Width = 1280;
        Height = 780;
        MinWidth = 900;
        MinHeight = 520;
        _left.Path = left;
        _right.Path = right;

        foreach (var side in new[] { _left, _right }) SetupEditor(side);
        Content = BuildLayout();
        UpdateTitle();

        _left.Editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => SyncScroll(_left, _right);
        _right.Editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => SyncScroll(_right, _left);
        _overview.RowRequested += row => ScrollToRow(row, center: true);
        _unimportant.IsCheckedChanged += (_, _) =>
        {
            _leftState.ShowUnimportant = _rightState.ShowUnimportant = _unimportant.IsChecked == true;
            Redraw();
        };

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => _dropHint.IsVisible = false);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Strings.Changed += OnLanguageChanged;
        Closed += (_, _) =>
        {
            Strings.Changed -= OnLanguageChanged;
            _left.TextMate?.Dispose();
            _right.TextMate?.Dispose();
        };
        Opened += async (_, _) => await ReloadAsync();
    }

    void OnLanguageChanged() => Dispatcher.UIThread.Post(() =>
    {
        UpdateTitle();
        UpdateStatus();
        foreach (var side in new[] { _left, _right }) UpdateSideChrome(side);
    });

    // --- 배치 ---

    Control BuildLayout()
    {
        var s = Strings.Current;
        Button Tool(string icon, string tip, Action click)
        {
            var b = new Button { Classes = { "tool" }, Content = new DuoIcon { Icon = icon } };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => click();
            return b;
        }
        _unimportant.Content = new DuoIcon { Icon = "visible" };
        ToolTip.SetTip(_unimportant, s.CompareToggleUnimportant);
        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Tool("up", s.ComparePrevDiff, () => GoDiff(next: false)),
                Tool("down", s.CompareNextDiff, () => GoDiff(next: true)),
                Tool("refresh", s.CompareRecompare, () => _ = ReloadAsync()),
                Tool("swap", s.CompareSwap, () => _ = SwapAsync()),
                _unimportant,
                new Border { Width = 1, Margin = new Thickness(4, 8), [!Border.BackgroundProperty] = this.GetResourceObservable("AppStroke").ToBinding() },
                Tool("settings", s.SettingsTooltip, () => SettingsWindow.Open(this)),
                Tool("info", s.AboutTooltip, () => AboutDialog.Open(this)),
            },
        };
        var appBar = new Border
        {
            Height = 44,
            Padding = new Thickness(14, 0),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children = { _status, tools },
            },
        };
        Grid.SetColumn(tools, 1);
        appBar.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurface"));
        appBar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStroke"));

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("44,Auto,Auto,*"),
            ColumnDefinitions = new ColumnDefinitions("*,1,*,14"),
        };
        Place(grid, appBar, 0, 0, 4);
        foreach (var side in new[] { _left, _right })
        {
            var col = side.Left ? 0 : 2;
            Place(grid, PathRow(side), 1, col, side.Left ? 1 : 2);
            Place(grid, BannerFor(side), 2, col, side.Left ? 1 : 2);
            var body = new Grid { Children = { side.Editor, side.Overlay } };
            side.Overlay.Child = side.OverlayText;
            side.OverlayText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
            Place(grid, body, 3, col, 1);
        }
        var divider = new Border();
        divider.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppStroke"));
        Place(grid, divider, 1, 1, 1, rowSpan: 3);
        Place(grid, _overview, 3, 3, 1);
        _overview.State = _leftState;

        _dropHint.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppPrimary"));
        Place(grid, _dropHint, 1, 0, 1, rowSpan: 3);
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
        var open = UiScale.Enable(new Button { Content = Strings.Current.CompareOpen, Height = 28, Padding = new Thickness(10, 0) });
        open.Click += async (_, _) => await PickAsync(side);
        var icon = new DuoIcon { Icon = "file", Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(12, 6),
            Children = { icon, side.PathText, side.EncodingText, open },
        };
        Grid.SetColumn(side.PathText, 1);
        Grid.SetColumn(side.EncodingText, 2);
        Grid.SetColumn(open, 3);
        side.EncodingText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        var border = new Border { Child = row, BorderThickness = new Thickness(0, 0, 0, 1) };
        border.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStroke"));
        return border;
    }

    Control BannerFor(Side side)
    {
        side.Banner.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppDiffChanged"));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12, Children = { side.BannerText, side.BannerActions } };
        Grid.SetColumn(side.BannerActions, 1);
        side.Banner.Child = grid;
        return side.Banner;
    }

    void SetupEditor(Side side)
    {
        var e = side.Editor;
        e.IsReadOnly = true;
        e.FontFamily = Mono;
        e.Bind(TextEditor.FontSizeProperty, this.GetResourceObservable("FsBody"));
        e.Bind(TextEditor.ForegroundProperty, this.GetResourceObservable("AppTextHi"));
        e.Background = Brushes.Transparent;
        e.ShowLineNumbers = false;
        e.WordWrap = false; // 줄바꿈하면 좌우 행이 어긋난다
        e.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        e.VerticalScrollBarVisibility = side.Left ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;
        e.Options.EnableHyperlinks = false;
        e.Options.EnableEmailHyperlinks = false;
        e.Options.HighlightCurrentLine = false; // 현재 줄 강조가 차이 색과 섞여 색이 달라 보인다
        var state = side.Left ? _leftState : _rightState;
        side.Margin = new RowNumberMargin(state, side.Left);
        e.TextArea.LeftMargins.Insert(0, side.Margin);
        e.TextArea.TextView.BackgroundRenderers.Add(new DiffBackgroundRenderer(state, side.Left));
        e.TextArea.TextView.LineTransformers.Add(new IntralineColorizer(state, side.Left));
        e.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (_syncing) return;
            var other = side.Left ? _right : _left;
            _syncing = true;
            other.Editor.TextArea.Caret.Line = e.TextArea.Caret.Line; // 같은 행 = 맞은편의 대응 줄
            _syncing = false;
        };
        e.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextEditor.FontSizeProperty) side.Margin.SetFont(Mono, e.FontSize * 0.9);
        };
    }

    // --- 읽기·비교 ---

    async Task ReloadAsync()
    {
        var version = ++_version;
        _status.Text = Strings.Current.CompareLoading;
        var leftTask = _left.Path is { } lp ? CompareSources.LoadAsync(lp, _left.ForcedEncoding) : Task.FromResult<CompareSource?>(null)!;
        var rightTask = _right.Path is { } rp ? CompareSources.LoadAsync(rp, _right.ForcedEncoding) : Task.FromResult<CompareSource?>(null)!;
        var sources = await Task.WhenAll(leftTask!, rightTask!);
        if (version != _version) return;
        _left.Source = sources[0];
        _right.Source = sources[1];
        foreach (var side in new[] { _left, _right })
            if (side.Repair is not null && side.Text is { } t)
            {
                // 섞인 인코딩 복구를 고른 쪽은 복구한 글을 쓴다 (다시 비교해도 유지).
                var (file, repair) = TextFiles.DecodeMixed(t.Bytes, t.File.Modified, t.File.Encoding, OtherEncoding(t.File.Encoding));
                side.Source = t with { File = file with { Text = TextFiles.NormalizeLineBreaks(file.Text) } };
                side.Repair = repair;
            }
        await RecompareAsync(version);
    }

    async Task RecompareAsync(int version)
    {
        var row = TopRow();
        AlignedDiff? aligned = null;
        if (_left.Text is { } l && _right.Text is { } r)
        {
            var policy = await App.Policy.LoadAsync();
            var name = Path.GetFileName(_left.Path ?? _right.Path ?? "");
            var effective = policy.Resolve(name, namesIgnoreCase: PolicyStore.NamesIgnoreCase);
            aligned = await Task.Run(() => new AlignedDiff(TextDiff.Compare(l.File.Text, r.File.Text, effective)));
            if (version != _version) return;
        }
        foreach (var (side, state) in new[] { (_left, _leftState), (_right, _rightState) })
        {
            state.Diff = aligned;
            state.Intraline.Clear();
            var text = aligned is null ? side.Text?.File.Text ?? "" : side.Left ? aligned.LeftText : aligned.RightText;
            side.Editor.Document = new AvaloniaEdit.Document.TextDocument(text);
            InstallHighlighting(side, text.Length);
            UpdateSideChrome(side);
        }
        UpdateTitle();
        UpdateStatus();
        Redraw();
        Dispatcher.UIThread.Post(() =>
        {
            if (aligned is not null && row == 0 && aligned.Next(-1) is { } first) ScrollToRow(first.StartRow, center: true);
            else ScrollToRow(row, center: false);
        }, DispatcherPriority.Background);
    }

    void InstallHighlighting(Side side, int length)
    {
        side.TextMate?.Dispose();
        side.TextMate = null;
        if (length > HighlightMaxChars || side.Path is null) return;
        if (EditorLanguages.ExtensionFor(Path.GetFileName(side.Path.Replace(ArchivePaths.Separator, "/"))) is not { } ext) return;
        try
        {
            var options = new RegistryOptions(ThemeService.IsDark(Application.Current!) ? ThemeName.DarkPlus : ThemeName.LightPlus);
            if (options.GetLanguageByExtension(ext) is not { } language) return;
            side.TextMate = side.Editor.InstallTextMate(options);
            side.TextMate.SetGrammar(options.GetScopeByLanguageId(language.Id));
            side.Language = language.Id;
            side.Editor.Background = Brushes.Transparent;
            // 차이 강조가 강조(TextMate) 색 위에 오도록 줄 안 강조를 맨 뒤로 옮긴다.
            var transformers = side.Editor.TextArea.TextView.LineTransformers;
            var mine = transformers.OfType<IntralineColorizer>().ToList();
            foreach (var m in mine) { transformers.Remove(m); transformers.Add(m); }
        }
        catch (Exception)
        {
            side.TextMate?.Dispose();
            side.TextMate = null;
        }
    }

    static TextEncodingKind OtherEncoding(TextEncodingKind primary) =>
        primary == TextEncodingKind.Utf8 ? TextEncodingKind.Cp949 : TextEncodingKind.Utf8;

    void UpdateSideChrome(Side side)
    {
        var s = Strings.Current;
        side.PathText.Text = side.Path ?? s.HomeNotChosen;
        ToolTip.SetTip(side.PathText, side.Path);
        side.EncodingText.Text = side.Text is { } t ? TextEncodings.DisplayName(t.File.Encoding, t.File.HasBom) : "";

        (side.Overlay.IsVisible, side.OverlayText.Text) = side.Source switch
        {
            null => (true, s.CompareEmptySide),
            CompareSource.Binary => (true, s.CompareBinary),
            CompareSource.TooLarge big => (true, s.CompareTooLarge(BytesFormat.Format(big.Length))),
            CompareSource.Failed f => (true, s.CompareLoadFailed(f.Error.ToString())),
            _ => (false, ""),
        };

        // 인코딩 안내 띠 (ROADMAP §6-1 ②).
        side.BannerActions.Children.Clear();
        if (side.Text is { } text && (text.File.DecodeErrors > 0 || side.Repair is { BrokenLines.Count: > 0 } || side.Repair is { RepairedLines.Count: > 0 }))
        {
            side.Banner.IsVisible = true;
            if (side.Repair is { } repair)
            {
                side.BannerText.Text = s.CompareRepaired(repair.RepairedLines.Count, repair.BrokenLines.Count);
            }
            else
            {
                var errors = TextEncodings.FindErrors(text.Bytes, text.File.Encoding, max: 1);
                side.BannerText.Text = s.CompareEncodingDamaged(TextEncodings.DisplayName(text.File.Encoding), text.File.DecodeErrors, errors.Count > 0 ? errors[0].Line : 1);
                var other = OtherEncoding(text.File.Encoding);
                if (text.File.Encoding is not (TextEncodingKind.Utf16Le or TextEncodingKind.Utf16Be))
                {
                    var repairButton = UiScale.Enable(new Button { Content = s.CompareRepairMixed(TextEncodings.DisplayName(text.File.Encoding), TextEncodings.DisplayName(other)), Height = 28 });
                    repairButton.Click += async (_, _) =>
                    {
                        side.Repair = new MixedRepair("", [], []); // 다음 읽기에서 실제로 복구한다
                        await ReloadAsync();
                    };
                    side.BannerActions.Children.Add(repairButton);
                }
            }
            side.BannerActions.Children.Add(EncodingMenuButton(side));
        }
        else side.Banner.IsVisible = false;
        if (side.Text is not null && !side.Banner.IsVisible)
            ToolTip.SetTip(side.EncodingText, s.CompareReopenEncoding);
        side.EncodingText.PointerPressed -= OnEncodingPressed;
        side.EncodingText.Tag = side;
        side.EncodingText.PointerPressed += OnEncodingPressed;
        side.EncodingText.Cursor = side.Text is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
    }

    void OnEncodingPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is TextBlock { Tag: Side side } block && side.Text is not null) EncodingFlyout(side).ShowAt(block);
    }

    Button EncodingMenuButton(Side side)
    {
        var button = UiScale.Enable(new Button { Content = Strings.Current.CompareReopenEncoding, Height = 28 });
        button.Click += (_, _) => EncodingFlyout(side).ShowAt(button);
        return button;
    }

    /// <summary>"다른 인코딩으로 다시 열기": 후보마다 깨진 곳 수와 앞부분 미리보기 (<see cref="TextEncodings.Previews"/>).</summary>
    MenuFlyout EncodingFlyout(Side side)
    {
        var flyout = new MenuFlyout();
        if (side.Text is not { } text) return flyout;
        foreach (var p in TextEncodings.Previews(text.Bytes, previewChars: 60))
        {
            var preview = p.Preview.Replace('\n', ' ').Replace('\r', ' ');
            var item = new MenuItem
            {
                Header = $"{TextEncodings.DisplayName(p.Kind)} — {Strings.Current.CompareEncodingErrors(p.Errors)} · {preview}",
            };
            var kind = p.Kind;
            item.Click += async (_, _) =>
            {
                side.ForcedEncoding = kind;
                side.Repair = null;
                await ReloadAsync();
            };
            flyout.Items.Add(item);
        }
        return flyout;
    }

    void UpdateTitle()
    {
        static string Name(string? p) => p is null ? "—" : Locations.NameOf(p.Replace(ArchivePaths.Separator, "/"));
        Title = $"{Strings.Current.CompareWindowTitle(Name(_left.Path), Name(_right.Path))} — {AppIdentity.DisplayName}";
    }

    void UpdateStatus()
    {
        var s = Strings.Current;
        if (_leftState.Diff is not { } d) { _status.Text = ""; return; }
        var important = d.Diff.DifferenceCount;
        var unimportant = d.Diff.Blocks.Count(b => b.Kind == DiffBlockKind.Unimportant);
        var text = d.Diff.IsIdentical ? s.CompareIdentical
            : important == 0 ? s.CompareEquivalent(unimportant)
            : s.CompareDifferences(important, unimportant);
        if (_left.Text is { } l && _right.Text is { } r && (l.File.Encoding != r.File.Encoding || l.File.HasBom != r.File.HasBom))
            text += "  ·  " + s.CompareEncodingDiffers(TextEncodings.DisplayName(l.File.Encoding, l.File.HasBom), TextEncodings.DisplayName(r.File.Encoding, r.File.HasBom));
        _status.Text = text;
    }

    void Redraw()
    {
        foreach (var side in new[] { _left, _right })
        {
            side.Editor.TextArea.TextView.Redraw();
            side.Margin.InvalidateVisual();
        }
        UpdateViewport();
    }

    // --- 스크롤·이동 ---

    void SyncScroll(Side from, Side to)
    {
        if (_syncing) return;
        _syncing = true;
        to.Editor.ScrollToVerticalOffset(from.Editor.VerticalOffset);
        to.Editor.ScrollToHorizontalOffset(from.Editor.HorizontalOffset);
        _syncing = false;
        UpdateViewport();
    }

    int TopRow()
    {
        var tv = _right.Editor.TextArea.TextView;
        return tv.DefaultLineHeight > 0 ? (int)(tv.VerticalOffset / tv.DefaultLineHeight) : 0;
    }

    void UpdateViewport()
    {
        var tv = _right.Editor.TextArea.TextView;
        if (tv.DefaultLineHeight <= 0) return;
        _overview.Viewport = (tv.VerticalOffset / tv.DefaultLineHeight, tv.Bounds.Height / tv.DefaultLineHeight);
        _overview.InvalidateVisual();
    }

    void ScrollToRow(int row, bool center)
    {
        var lines = _right.Editor.Document?.LineCount ?? 0;
        if (lines == 0) return;
        var line = Math.Clamp(row + 1, 1, lines);
        var tv = _right.Editor.TextArea.TextView;
        var offset = (line - 1) * tv.DefaultLineHeight - (center ? tv.Bounds.Height / 3 : 0);
        _right.Editor.ScrollToVerticalOffset(Math.Max(0, offset));
        _syncing = true;
        _right.Editor.TextArea.Caret.Line = line;
        _left.Editor.TextArea.Caret.Line = Math.Min(line, _left.Editor.Document?.LineCount ?? 1);
        _syncing = false;
    }

    int CurrentRow()
    {
        var focused = _left.Editor.TextArea.IsFocused ? _left : _right;
        return focused.Editor.TextArea.Caret.Line - 1;
    }

    void GoDiff(bool next)
    {
        if (_leftState.Diff is not { } d) return;
        var row = CurrentRow();
        var include = _unimportant.IsChecked == true && d.Diff.DifferenceCount == 0;
        var target = next ? d.Next(row, include) : d.Previous(row, include);
        // 지금 차이 구간 안에 있으면 그 구간 끝/처음을 기준으로 다음 것을 찾는다.
        if (next && d.RangeAt(row) is { } cur && target?.StartRow == cur.StartRow) target = d.Next(cur.StartRow + cur.RowCount - 1, include);
        if (target is { } t) ScrollToRow(t.StartRow, center: true);
    }

    async Task SwapAsync()
    {
        (_left.Path, _right.Path) = (_right.Path, _left.Path);
        (_left.ForcedEncoding, _right.ForcedEncoding) = (_right.ForcedEncoding, _left.ForcedEncoding);
        (_left.Repair, _right.Repair) = (_right.Repair, _left.Repair);
        await ReloadAsync();
    }

    async Task PickAsync(Side side)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = side.Left ? Strings.Current.CompareOpenLeftTitle : Strings.Current.CompareOpenRightTitle,
            AllowMultiple = false,
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        await SetSideAsync(side, path);
    }

    async Task SetSideAsync(Side side, string path)
    {
        side.Path = path;
        side.ForcedEncoding = null;
        side.Repair = null;
        await ReloadAsync();
    }

    // --- 끌어다 놓기 (ROADMAP §6-1 ①) ---

    DropTarget TargetAt(DragEventArgs e)
    {
        var x = e.GetPosition(this).X;
        return x < Bounds.Width * 0.45 ? DropTarget.Left : x > Bounds.Width * 0.55 ? DropTarget.Right : DropTarget.Window;
    }

    static IReadOnlyList<DroppedItem> ItemsOf(DragEventArgs e) =>
        DropClassifier.Classify(e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>() ?? []);

    void OnDragOver(object? sender, DragEventArgs e)
    {
        var items = ItemsOf(e);
        if (items.Count == 0 || items.Count > 3) { e.DragEffects = DragDropEffects.None; _dropHint.IsVisible = false; return; }
        e.DragEffects = DragDropEffects.Copy;
        var target = items.Count == 2 ? DropTarget.Window : TargetAt(e);
        Grid.SetColumn(_dropHint, target == DropTarget.Right ? 2 : 0);
        Grid.SetColumnSpan(_dropHint, target == DropTarget.Window ? 3 : target == DropTarget.Right ? 2 : 1);
        _dropHint.IsVisible = true;
    }

    async void OnDrop(object? sender, DragEventArgs e)
    {
        _dropHint.IsVisible = false;
        var plan = DropPlanner.Plan(ItemsOf(e), TargetAt(e), Item(_left), Item(_right));
        await ApplyDropAsync(this, plan, async (l, r) =>
        {
            _left.Path = l;
            _right.Path = r;
            foreach (var side in new[] { _left, _right }) { side.ForcedEncoding = null; side.Repair = null; }
            await ReloadAsync();
        });
    }

    static DroppedItem? Item(Side side) => side.Path is { } p ? new DroppedItem(p, DroppedKind.File) : null;

    /// <summary>
    /// 놓기 결과를 처리한다 (홈 창과 함께 쓴다): 파일 비교는 <paramref name="apply"/>로 양쪽을 정하고, 아직 없는 기능(폴더 비교·병합)과
    /// 거절은 안내 대화상자로 알린다. 파일 + 폴더면 폴더 안의 같은 이름 파일을 찾는다.
    /// </summary>
    internal static async Task ApplyDropAsync(Window owner, DropPlan plan, Func<string?, string?, Task> apply)
    {
        var s = Strings.Current;
        async Task Tell(string message) => await MessageDialog.ShowAsync(owner, AppIdentity.DisplayName, message, [s.CommonClose], 0, 0);
        switch (plan)
        {
            case DropPlan.Pending p when p.Left is not { IsContainer: true } && p.Right is not { IsContainer: true }:
                await apply(p.Left?.Path, p.Right?.Path);
                break;
            case DropPlan.Compare { Mode: CompareMode.Files } c:
                await apply(c.Left.Path, c.Right.Path);
                break;
            case DropPlan.Compare { Mode: CompareMode.FileAgainstFolder } c:
            {
                var (file, container, fileOnLeft) = DropPlanner.Split(c);
                var name = Path.GetFileName(file.Path);
                var counterpart = DropPlanner.Counterpart(container, name);
                var exists = container.Kind == DroppedKind.Archive
                    ? await new ArchiveFileSystem(container.Path).StatAsync(counterpart) is { IsDirectory: false }
                    : File.Exists(counterpart);
                if (!exists) { await Tell(s.DropNoCounterpart(name)); return; }
                await apply(fileOnLeft ? file.Path : counterpart, fileOnLeft ? counterpart : file.Path);
                break;
            }
            case DropPlan.Rejected { Reason: DropRejection.Empty }:
                break;
            case DropPlan.Rejected or DropPlan.Merge:
                await Tell(s.DropTooMany);
                break;
            default: // 폴더 비교 (4단계)
                await Tell(s.DropFolderLater);
                break;
        }
    }

    // --- 단축키 (UI_UX.md) ---

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var primary = Mods.Primary(e.KeyModifiers);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var handled = true;
        switch (e.Key)
        {
            case Key.F8: GoDiff(next: !shift); break;
            case Key.Down when alt: GoDiff(next: true); break;
            case Key.Up when alt: GoDiff(next: false); break;
            case Key.F5: _ = ReloadAsync(); break;
            case Key.R when primary: _ = ReloadAsync(); break;
            case Key.U when primary: _ = SwapAsync(); break;
            case Key.O when primary: _ = PickAsync(_right.Path is null || _left.Path is not null && _right.Editor.TextArea.IsFocused ? _right : _left); break;
            case Key.W when primary: Close(); break;
            case Key.OemComma when primary: SettingsWindow.Open(this); break;
            default: handled = false; break;
        }
        e.Handled = handled;
    }
}
