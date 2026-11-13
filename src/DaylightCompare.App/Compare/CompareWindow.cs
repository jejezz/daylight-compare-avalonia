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
using Avalonia.Input.Platform;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using AvaloniaEdit.TextMate;
using DaylightCompare.App.Dialogs;
using DaylightCompare.App.Icons;
using DaylightCompare.App.Views;
using DaylightCompare.Domain;
using DaylightCompare.Domain.Diff;
using DaylightCompare.Domain.Drop;
using DaylightCompare.Domain.Settings;
using DaylightCompare.Services;
using TextMateSharp.Grammars;

namespace DaylightCompare.App.Compare;

/// <summary>
/// 텍스트 비교 창 (ROADMAP 3단계, 창 하나 = 비교 하나). 좌우 편집기는 <see cref="AlignedDiff"/>로 같은 행에 맞춘 글을 보이므로
/// 스크롤 위치만 같게 두면 늘 나란히 보인다.
/// <para>
/// <b>편집</b>: 양쪽 다 바로 고칠 수 있다(압축 파일 안의 파일은 읽기 전용). 화면 글에는 채움 줄이 섞여 있어서, 채움 줄의 시작에 앵커를 두고
/// "앵커가 있고 아직 빈 줄"을 빼서 실제 글을 꺼낸다(<see cref="DiffEdits.RealText"/>). 손을 멈추고 0.5초 뒤 다시 비교한다.
/// 다시 비교할 때 문서를 통째로 바꾸므로 편집기 자체의 실행 취소 대신 <b>실제 글 한 쌍</b>을 쌓는 실행 취소를 쓴다(Ctrl+Z / Ctrl+Y).
/// </para>
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
        /// <summary>BC <c>/title1</c>·<c>/title2</c>: 경로 대신 보일 이름 (git 임시 파일 이름 대신). 다른 파일을 열면 지운다.</summary>
        public string? Title;
        /// <summary>BC <c>/ro</c>·<c>/ro1</c>·<c>/ro2</c>.</summary>
        public bool ReadOnly;
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
        public string? HighlightedPath;
        public bool RepairRequested;
        public MixedRepair? Repair;

        /// <summary>지금의 실제 글 (LF). 고치면 바뀌고, 저장하면 <see cref="SavedText"/>가 된다.</summary>
        public string Real = "";
        public string SavedText = "";
        public bool EndsWithNewline;

        /// <summary>화면 글의 채움 줄 시작에 둔 앵커 (편집 뒤에도 그 줄을 따라간다).</summary>
        public readonly List<TextAnchor> Fillers = [];

        public FileSystemWatcher? Watcher;
        public DateTime IgnoreWatchUntil;
        public bool ChangedOutside;

        /// <summary>차이만 보기의 접기 (양쪽이 같은 행을 접어서 나란히 남는다).</summary>
        public FoldingManager Folding = null!;

        public CompareSource.Text? Text => Source as CompareSource.Text;
        public bool HasText => Text is not null;

        /// <summary>파일이 없는 칸: 붙여 넣거나 바로 쓴 글을 비교한다 (클립보드 비교). 저장하지 않으므로 고쳐도 "고친 상태"가 아니다.</summary>
        public bool IsScratch => Path is null;
        public bool CanEdit => Text is not null && !ReadOnly && (Path is null || !ArchivePaths.IsInArchive(Path));
        public bool Dirty => HasText && !IsScratch && Real != SavedText;
        public string Name => Title is { Length: > 0 } t ? t : Path is null ? Strings.Current.CompareScratchName : Locations.NameOf(Path.Replace(ArchivePaths.Separator, "/"));
    }

    readonly Side _left = new(true);
    readonly Side _right = new(false);
    readonly DiffViewState _leftState = new();
    readonly DiffViewState _rightState = new();
    readonly OverviewBar _overview = new();
    readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center };
    /// <summary>BC View > Ignore Unimportant Differences (켜면 중요하지 않은 차이를 차이로 보이지 않는다).</summary>
    readonly ToggleButton _ignoreUnimportant = new() { Classes = { "tool" } };
    readonly Button _save = new() { Classes = { "tool" }, IsEnabled = false };

    /// <summary>BC 보기 필터 (Show All / Differences / Same / Context) — 드롭다운 하나.</summary>
    readonly DropDownButton _viewFilter = new() { VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(10, 4) };
    CompareViewFilter _filter;
    int _contextLines;
    static readonly int[] ContextChoices = [0, 1, 2, 3, 5, 10, 20];

    // 바꾸기 (Ctrl+H): 찾기 막대 아래 줄.
    readonly TextBox _replaceBox = new() { Width = 300, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel _replaceRow = new() { Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false, Margin = new Thickness(24, 6, 0, 0) };

    // 찾기 막대 (Ctrl+F): 마지막으로 초점이 있던 쪽에서 찾는다.
    readonly Border _findBar = new() { IsVisible = false, Padding = new Thickness(12, 6), BorderThickness = new Thickness(0, 0, 0, 1) };
    readonly TextBox _findBox = new() { Width = 300, VerticalAlignment = VerticalAlignment.Center };
    readonly ToggleButton _findCase = new() { Classes = { "tool" }, Content = "Aa" };
    readonly TextBlock _findCount = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 72 };
    readonly TextBlock _findWhere = new() { VerticalAlignment = VerticalAlignment.Center, Classes = { "small" } };
    Side? _findSide;
    readonly Border _dropHint = new() { IsVisible = false, IsHitTestVisible = false, BorderThickness = new Thickness(2) };
    readonly DispatcherTimer _recompareTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly Stack<(string Left, string Right)> _undo = new();
    readonly Stack<(string Left, string Right)> _redo = new();
    bool _syncing;
    bool _applying;
    bool _editBatchOpen;
    bool _closeConfirmed;
    string? _notice;
    int _version;

    /// <summary>지금 비교 중인 경로 (좌우 바꾸기·다른 파일 열기 뒤의 값).</summary>
    public string? LeftPath => _left.Path;
    public string? RightPath => _right.Path;

    public static CompareWindow Open(Window? near, string? left, string? right, CompareOptions? options = null)
    {
        var w = new CompareWindow(left, right, options ?? CompareOptions.None);
        if (near is not null) WindowPlacementHelper.OpenNear(near, w);
        else w.Show();
        return w;
    }

    CompareWindow(string? left, string? right, CompareOptions options)
    {
        _left.Title = options.LeftTitle;
        _right.Title = options.RightTitle;
        _left.ReadOnly = options.LeftReadOnly;
        _right.ReadOnly = options.RightReadOnly;
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
        var settings = App.Settings.Value;
        _filter = settings.CompareViewFilter;
        _contextLines = Math.Clamp(settings.CompareContextLines, 0, AppSettings.MaxContextLines);
        var ignore = options.IgnoreUnimportant ?? settings.CompareIgnoreUnimportant; // /iu는 이 창에만
        _ignoreUnimportant.IsChecked = ignore;
        _leftState.ShowUnimportant = _rightState.ShowUnimportant = !ignore;
        UpdateViewFilterButton();
        _ignoreUnimportant.IsCheckedChanged += (_, _) =>
        {
            var ignore = _ignoreUnimportant.IsChecked == true;
            _leftState.ShowUnimportant = _rightState.ShowUnimportant = !ignore;
            App.Settings.Update(x => x with { CompareIgnoreUnimportant = ignore });
            UpdateStatus();
            Redraw();
        };
        _findBox.TextChanged += (_, _) => Find(forward: true, fromSelectionStart: true);
        _findCase.IsCheckedChanged += (_, _) => Find(forward: true, fromSelectionStart: true);
        _recompareTimer.Tick += async (_, _) =>
        {
            _recompareTimer.Stop();
            await CommitEditsAsync();
        };

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => _dropHint.IsVisible = false);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Strings.Changed += OnLanguageChanged;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            Strings.Changed -= OnLanguageChanged;
            _recompareTimer.Stop();
            foreach (var side in new[] { _left, _right })
            {
                side.TextMate?.Dispose();
                side.Watcher?.Dispose();
            }
        };
        Opened += async (_, _) =>
        {
            await ReloadAsync();
            RecordHistory();
        };
    }

    /// <summary>두 쪽이 모두 파일이면 최근 비교 목록에 기록한다 (붙여 넣은 글은 경로가 없어 건너뛴다).</summary>
    void RecordHistory() => App.RecordHistory(_left.Path, _right.Path, folders: false);

    void OnLanguageChanged() => Dispatcher.UIThread.Post(() =>
    {
        UpdateTitle();
        UpdateStatus();
        foreach (var side in new[] { _left, _right }) UpdateSideChrome(side);
        UpdateViewFilterButton();
        _findBox.PlaceholderText = Strings.Current.CompareFindPlaceholder;
        _replaceBox.PlaceholderText = Strings.Current.CompareReplacePlaceholder;
        UpdateFindWhere();
        ApplyFolds(); // 접힌 표시 문구
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
        _ignoreUnimportant.Content = new DuoIcon { Icon = "visible-off" };
        ToolTip.SetTip(_ignoreUnimportant, s.CompareIgnoreUnimportant);
        _save.Content = new DuoIcon { Icon = "save" };
        ToolTip.SetTip(_save, s.CompareSave);
        _save.Click += async (_, _) => await SaveAllAsync();
        ToolTip.SetTip(_viewFilter, s.CompareViewFilterTooltip);
        var commands = new Button { Classes = { "tool" }, Content = new DuoIcon { Icon = "view-list" } };
        ToolTip.SetTip(commands, s.CompareCommandsTooltip);
        commands.Click += (_, _) => CommandsMenu().ShowAt(commands);
        Border Separator() => new() { Width = 1, Margin = new Thickness(4, 8), [!Border.BackgroundProperty] = this.GetResourceObservable("AppStroke").ToBinding() };
        // 툴바는 BC 텍스트 비교의 묶음 순서를 따른다: 보기 필터 · 중요하지 않은 차이 무시 | 복사 · 저장 | 이전·다음 구간 | 좌우 바꾸기 · 다시 읽기 | 찾기 · 모든 명령.
        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _viewFilter,
                _ignoreUnimportant,
                Separator(),
                Tool("back", Tip(s.CompareCopyToLeft, "Ctrl+L"), () => _ = CopyBlockAsync(toRight: false)),
                Tool("forward", Tip(s.CompareCopyToRight, "Ctrl+R"), () => _ = CopyBlockAsync(toRight: true)),
                _save,
                Separator(),
                Tool("up", Tip(s.ComparePrevSection, "Ctrl+P"), () => GoDiff(next: false)),
                Tool("down", Tip(s.CompareNextSection, "Ctrl+N"), () => GoDiff(next: true)),
                Separator(),
                Tool("swap", Tip(s.CompareSwap, "Ctrl+U"), () => _ = SwapAsync()),
                Tool("refresh", Tip(s.CompareReload, "F5"), () => _ = ReloadWithConfirmAsync()),
                Tool("search", Tip(s.CompareFindMenu, "Ctrl+F"), () => OpenFind()),
                commands,
                Separator(),
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
            RowDefinitions = new RowDefinitions("44,Auto,Auto,Auto,*"),
            ColumnDefinitions = new ColumnDefinitions("*,1,*,14"),
        };
        Place(grid, appBar, 0, 0, 4);
        Place(grid, BuildFindBar(), 1, 0, 4);
        foreach (var side in new[] { _left, _right })
        {
            var col = side.Left ? 0 : 2;
            Place(grid, PathRow(side), 2, col, side.Left ? 1 : 2);
            Place(grid, BannerFor(side), 3, col, side.Left ? 1 : 2);
            var body = new Grid { Children = { side.Editor, side.Overlay } };
            side.Overlay.Child = side.OverlayText;
            side.OverlayText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
            Place(grid, body, 4, col, 1);
        }
        var divider = new Border();
        divider.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppStroke"));
        Place(grid, divider, 2, 1, 1, rowSpan: 3);
        Place(grid, _overview, 4, 3, 1);
        _overview.State = _leftState;

        _dropHint.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppPrimary"));
        Place(grid, _dropHint, 2, 0, 1, rowSpan: 3);
        return grid;
    }

    Control BuildFindBar()
    {
        var s = Strings.Current;
        Button Tool(string icon, string tip, Action click)
        {
            var b = new Button { Classes = { "tool" }, Content = new DuoIcon { Icon = icon } };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => click();
            return b;
        }
        _findBox.PlaceholderText = s.CompareFindPlaceholder;
        ToolTip.SetTip(_findCase, s.CompareFindMatchCase);
        _findCount.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        _findWhere.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        var close = Tool("close", s.CommonClose, CloseFind);
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new DuoIcon { Icon = "search", Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center },
                        _findBox,
                        Tool("up", Tip(s.CompareFindPreviousMenu, "Shift+F3"), () => Find(forward: false)),
                        Tool("down", Tip(s.CompareFindNextMenu, "F3"), () => Find(forward: true)),
                        _findCase,
                        _findCount,
                        _findWhere,
                    },
                },
                close,
            },
        };
        Grid.SetColumn(close, 2);
        // 바꾸기 줄 (BC Search > Replace, Ctrl+H): 찾을 쪽에서 지금 고른 일치를 바꾸거나 모두 바꾼다.
        _replaceBox.PlaceholderText = s.CompareReplacePlaceholder;
        var replaceOne = UiScale.Enable(new Button { Content = s.CompareReplaceOne, Padding = new Thickness(12, 4) });
        var replaceAll = UiScale.Enable(new Button { Content = s.CompareReplaceAll, Padding = new Thickness(12, 4) });
        replaceOne.Click += (_, _) => ReplaceOne();
        replaceAll.Click += (_, _) => ReplaceAll();
        _replaceRow.Children.AddRange([new DuoIcon { Icon = "replace", Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center }, _replaceBox, replaceOne, replaceAll]);
        _findBar.Child = new StackPanel { Children = { row, _replaceRow } };
        _findBar.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurface"));
        _findBar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStroke"));
        return _findBar;
    }

    // --- 보기 필터 (BC View > Show All / Show Differences / Show Same / Show Context) ---

    string FilterName(CompareViewFilter f) => f switch
    {
        CompareViewFilter.Differences => Strings.Current.CompareShowDifferences,
        CompareViewFilter.Same => Strings.Current.CompareShowSame,
        CompareViewFilter.Context => Strings.Current.CompareShowContext,
        _ => Strings.Current.CompareShowAll,
    };

    void UpdateViewFilterButton()
    {
        var s = Strings.Current;
        _viewFilter.Content = _filter == CompareViewFilter.Context ? $"{FilterName(_filter)} ({_contextLines})" : FilterName(_filter);
        var flyout = new MenuFlyout();
        foreach (var item in ViewFilterItems()) flyout.Items.Add(item);
        _viewFilter.Flyout = flyout;
    }

    /// <summary>보기 필터 항목들 (툴바 드롭다운과 명령 메뉴의 보기 묶음이 같이 쓴다).</summary>
    IEnumerable<Control> ViewFilterItems()
    {
        var s = Strings.Current;
        foreach (var f in new[] { CompareViewFilter.All, CompareViewFilter.Differences, CompareViewFilter.Same, CompareViewFilter.Context })
        {
            var item = new MenuItem { Header = FilterName(f), ToggleType = MenuItemToggleType.Radio, IsChecked = _filter == f, GroupName = "view-filter" };
            var target = f;
            item.Click += (_, _) => SetViewFilter(target);
            yield return item;
        }
        var lines = new MenuItem { Header = s.CompareContextLinesMenu };
        foreach (var n in ContextChoices)
        {
            var item = new MenuItem { Header = s.CompareContextLines(n), ToggleType = MenuItemToggleType.Radio, IsChecked = _contextLines == n, GroupName = "context-lines" };
            var count = n;
            item.Click += (_, _) =>
            {
                _contextLines = count;
                App.Settings.Update(x => x with { CompareContextLines = count });
                SetViewFilter(CompareViewFilter.Context);
            };
            lines.Items.Add(item);
        }
        yield return new Separator();
        yield return lines;
    }

    void SetViewFilter(CompareViewFilter filter)
    {
        _filter = filter;
        App.Settings.Update(x => x with { CompareViewFilter = filter });
        UpdateViewFilterButton();
        ApplyFolds();
    }

    // --- 모든 명령 (BC의 메뉴: Session · File · Edit · Search · View) ---

    /// <summary>
    /// 툴바에 없는 명령까지 BC 메뉴 묶음대로 모아 단축키와 함께 보인다. 아이콘은 BC와 달라도 기능과 키는 같게 (BC_COMPAT.md).
    /// </summary>
    MenuFlyout CommandsMenu()
    {
        var s = Strings.Current;
        MenuItem Item(string header, string? gesture, Action run, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            if (gesture is not null) item.InputGesture = KeyGesture.Parse(gesture);
            item.Click += (_, _) => run();
            return item;
        }
        MenuItem Group(string header, params Control[] items)
        {
            var g = new MenuItem { Header = header };
            foreach (var i in items) g.Items.Add(i);
            return g;
        }
        var focused = FocusedSide();
        var view = Group(s.CompareMenuView, [.. ViewFilterItems(), new Separator(),
            Item(s.CompareIgnoreUnimportant, null, () => _ignoreUnimportant.IsChecked = _ignoreUnimportant.IsChecked != true)]);
        var flyout = new MenuFlyout();
        flyout.Items.Add(Group(s.CompareMenuSession,
            Item(s.CompareSwap, "Ctrl+U", () => _ = SwapAsync()),
            Item(s.CompareReload, "F5", () => _ = ReloadWithConfirmAsync()),
            Item(s.CompareRecompareNow, "Ctrl+F5", () => _ = RecompareNowAsync()),
            Item(s.CompareInfo, "Ctrl+I", () => _ = ShowInfoAsync())));
        flyout.Items.Add(Group(s.CompareMenuFile,
            Item(s.CompareOpenFile, "Ctrl+O", () => _ = PickAsync(focused)),
            Item(s.CompareOpenClipboard, "Ctrl+Shift+V", () => _ = PasteAsync(focused)),
            Item(s.CompareSaveFile, "Ctrl+S", () => _ = SaveFocusedAsync(), focused.Dirty || (focused.Left ? _right : _left).Dirty),
            Item(s.CompareSaveFileAs, "F12", () => _ = SaveAsAsync(focused), focused.HasText)));
        flyout.Items.Add(Group(s.CompareMenuEdit,
            Item(s.CompareUndo, "Ctrl+Z", () => _ = UndoAsync(redo: false)),
            Item(s.CompareRedo, "Ctrl+Y", () => _ = UndoAsync(redo: true)),
            new Separator(),
            Item(s.CompareCopyToRight, "Ctrl+R", () => _ = CopyBlockAsync(toRight: true)),
            Item(s.CompareCopyToLeft, "Ctrl+L", () => _ = CopyBlockAsync(toRight: false)),
            Item(s.CompareCopyToOther, null, () => _ = CopyBlockAsync(toRight: focused.Left)),
            Item(s.CompareCopyLineToRight, "Ctrl+Shift+R", () => _ = CopyLineAsync(toRight: true)),
            Item(s.CompareCopyLineToLeft, "Ctrl+Shift+L", () => _ = CopyLineAsync(toRight: false)),
            new Separator(),
            Item(s.CompareSelectSection, "Ctrl+D", SelectSection)));
        flyout.Items.Add(Group(s.CompareMenuSearch,
            Item(s.CompareNextSection, "Ctrl+N", () => GoDiff(next: true)),
            Item(s.ComparePrevSection, "Ctrl+P", () => GoDiff(next: false)),
            Item(s.CompareNextLine, "Ctrl+Shift+N", () => GoDiffLine(next: true)),
            Item(s.ComparePrevLine, "Ctrl+Shift+P", () => GoDiffLine(next: false)),
            new Separator(),
            Item(s.CompareFindMenu, "Ctrl+F", OpenFind),
            Item(s.CompareReplaceMenu, "Ctrl+H", () => OpenFind(replace: true)),
            Item(s.CompareFindNextMenu, "F3", () => Find(forward: true)),
            Item(s.CompareFindPreviousMenu, "Shift+F3", () => Find(forward: false)),
            Item(s.CompareGoTo, "Ctrl+G", () => _ = GoToLineAsync())));
        flyout.Items.Add(view);
        return flyout;
    }

    Side FocusedSide() => _left.Editor.TextArea.IsFocused ? _left : _right.Editor.TextArea.IsFocused ? _right : _findSide ?? _right;

    /// <summary>툴팁: 이름 (단축키). macOS는 Ctrl을 ⌘로 보인다.</summary>
    static string Tip(string name, string key) => $"{name} ({(OperatingSystem.IsMacOS() ? key.Replace("Ctrl+", "⌘") : key)})";

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
        var open = UiScale.Enable(new Button { Content = Strings.Current.CompareOpen, Padding = new Thickness(12, 4) });
        open.Click += async (_, _) => await PickAsync(side);
        var paste = UiScale.Enable(new Button { Content = Strings.Current.CompareOpenClipboard, Padding = new Thickness(12, 4) });
        ToolTip.SetTip(paste, Tip(Strings.Current.CompareOpenClipboard, "Ctrl+Shift+V"));
        paste.Click += async (_, _) => await PasteAsync(side);
        var icon = new DuoIcon { Icon = "file", Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(12, 6),
            Children = { icon, side.PathText, side.EncodingText, paste, open },
        };
        Grid.SetColumn(side.PathText, 1);
        Grid.SetColumn(side.EncodingText, 2);
        Grid.SetColumn(paste, 3);
        Grid.SetColumn(open, 4);
        side.EncodingText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        side.EncodingText.PointerPressed += (_, _) => { if (side.HasText && !side.IsScratch) EncodingFlyout(side).ShowAt(side.EncodingText); };
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
        e.Document = new AvaloniaEdit.Document.TextDocument();
        var state = side.Left ? _leftState : _rightState;
        side.Margin = new RowNumberMargin(state, side.Left);
        e.TextArea.LeftMargins.Insert(0, side.Margin);
        e.TextArea.TextView.BackgroundRenderers.Add(new DiffBackgroundRenderer(state, side.Left));
        e.TextArea.TextView.LineTransformers.Add(new IntralineColorizer(state, side.Left));
        side.Folding = FoldingManager.Install(e.TextArea);
        // 접기 여백의 [+]/[−]로 한쪽을 펴거나 접으면 맞은편도 똑같이 (그래야 행이 나란히 남는다).
        foreach (var margin in e.TextArea.LeftMargins.OfType<FoldingMargin>())
            margin.AddHandler(PointerPressedEvent, (_, _) => Dispatcher.UIThread.Post(() => MirrorFolds(side)), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        e.TextArea.GotFocus += (_, _) =>
        {
            _findSide = side;
            UpdateFindWhere();
        };
        e.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (_syncing || _applying) return;
            var other = side.Left ? _right : _left;
            _syncing = true;
            other.Editor.TextArea.Caret.Line = Math.Min(e.TextArea.Caret.Line, other.Editor.Document.LineCount); // 같은 행 = 맞은편의 대응 줄
            _syncing = false;
        };
        e.Document.TextChanged += (_, _) => OnEdited(side);
        e.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextEditor.FontSizeProperty) side.Margin.SetFont(Mono, e.FontSize * 0.9);
        };
    }

    // --- 읽기·비교 ---

    /// <summary>두 파일을 디스크에서 다시 읽고 비교한다. 고친 내용은 버려진다 (부르는 쪽이 먼저 확인한다).</summary>
    async Task ReloadAsync()
    {
        _recompareTimer.Stop();
        var version = ++_version;
        _status.Text = Strings.Current.CompareLoading;
        // 파일이 없는 칸은 지금 글(붙여 넣거나 쓴 것)을 그대로 쓴다.
        var sources = await Task.WhenAll(
            _left.Path is { } lp ? CompareSources.LoadAsync(lp, _left.ForcedEncoding) : Task.FromResult(ScratchSource(_left.Real)),
            _right.Path is { } rp ? CompareSources.LoadAsync(rp, _right.ForcedEncoding) : Task.FromResult(ScratchSource(_right.Real)));
        if (version != _version) return;
        foreach (var (side, source) in new[] { (_left, sources[0]), (_right, sources[1]) })
        {
            side.Source = source;
            side.Repair = null;
            side.ChangedOutside = false;
            if (side.RepairRequested && side.Text is { } t)
            {
                // 섞인 인코딩 복구를 고른 쪽은 복구한 글을 쓴다 (다시 읽어도 유지). 복구한 글은 UTF-8로 저장된다.
                var (file, repair) = TextFiles.DecodeMixed(t.Bytes, t.File.Modified, t.File.Encoding, OtherEncoding(t.File.Encoding));
                side.Source = t with { File = file with { Text = TextFiles.NormalizeLineBreaks(file.Text) } };
                side.Repair = repair;
            }
            side.Real = side.Text?.File.Text ?? "";
            // 복구한 글은 디스크와 다르므로 "고친 상태"로 시작한다 — 저장해야 파일이 고쳐진다.
            side.SavedText = side.Repair is not null ? (sources[side.Left ? 0 : 1] as CompareSource.Text)?.File.Text ?? "" : side.Real;
            side.EndsWithNewline = side.Real.EndsWith('\n');
            Watch(side);
        }
        _undo.Clear();
        _redo.Clear();
        await RecompareAsync(version, keepCaret: null, jumpToFirst: true);
    }

    static CompareSource ScratchSource(string text) =>
        new CompareSource.Text("", new EditorTextFile(text, HasBom: false, LineBreak: "\n", DateTime.MinValue, text.Length, Binary: false), []);

    /// <summary>
    /// 클립보드의 글로 그 칸을 바꾼다 (파일 없는 칸이 된다 — ROADMAP §6-1 클립보드 비교). 고친 것이 있으면 먼저 묻는다.
    /// 빈 칸에는 그냥 붙여 넣어도(Ctrl+V) 된다.
    /// </summary>
    async Task PasteAsync(Side side)
    {
        var text = Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
        if (string.IsNullOrEmpty(text))
        {
            Notice(Strings.Current.CompareClipboardEmpty);
            return;
        }
        if (!await ConfirmDiscardAsync()) return;
        side.Path = null;
        side.Title = null;
        side.ForcedEncoding = null;
        side.RepairRequested = false;
        side.Real = TextFiles.NormalizeLineBreaks(text);
        await ReloadAsync();
    }

    /// <summary>지금의 실제 글로 다시 비교하고 화면 글을 바꾼다. <paramref name="keepCaret"/>이 있으면 그 쪽의 그 실제 줄로 캐럿을 옮긴다.</summary>
    async Task RecompareAsync(int version, (bool Left, int Line, int Column)? keepCaret, bool jumpToFirst = false)
    {
        var offset = _right.Editor.VerticalOffset;
        AlignedDiff? aligned = null;
        if (_left.HasText && _right.HasText)
        {
            var policy = await App.Policy.LoadAsync();
            var name = System.IO.Path.GetFileName(_left.Path ?? _right.Path ?? "");
            var effective = policy.Resolve(name, namesIgnoreCase: PolicyStore.NamesIgnoreCase);
            var (l, r) = (_left.Real, _right.Real);
            aligned = await Task.Run(() => new AlignedDiff(TextDiff.Compare(l, r, effective)));
            if (version != _version) return;
        }
        _applying = true;
        foreach (var (side, state) in new[] { (_left, _leftState), (_right, _rightState) })
        {
            state.Diff = aligned;
            state.Editing = false;
            state.Intraline.Clear();
            var text = aligned is null ? side.Real : side.Left ? aligned.LeftText : aligned.RightText;
            var doc = side.Editor.Document;
            if (doc.Text != text) doc.Text = text;
            doc.UndoStack.ClearAll(); // 실행 취소는 창이 실제 글 단위로 한다
            side.Fillers.Clear();
            if (aligned is not null)
                for (var row = 0; row < aligned.Rows.Count; row++)
                    if ((side.Left ? aligned.Rows[row].Left : aligned.Rows[row].Right) is null)
                    {
                        var anchor = doc.CreateAnchor(doc.GetLineByNumber(row + 1).Offset);
                        anchor.MovementType = AnchorMovementType.BeforeInsertion;
                        side.Fillers.Add(anchor);
                    }
            side.Editor.IsReadOnly = !side.CanEdit;
            if (side.HighlightedPath != side.Path) InstallHighlighting(side, text.Length);
            UpdateSideChrome(side);
        }
        _applying = false;
        ApplyFolds(redraw: false);
        UpdateTitle();
        UpdateStatus();
        Redraw();

        if (keepCaret is { } k && aligned is not null)
        {
            var row = k.Left ? aligned.RowOfLeftLine(k.Line) : aligned.RowOfRightLine(k.Line);
            var side = k.Left ? _left : _right;
            _right.Editor.ScrollToVerticalOffset(offset);
            Dispatcher.UIThread.Post(() =>
            {
                _syncing = true;
                side.Editor.TextArea.Caret.Line = Math.Min(row + 1, side.Editor.Document.LineCount);
                side.Editor.TextArea.Caret.Column = k.Column;
                _syncing = false;
            }, DispatcherPriority.Background);
        }
        else if (!jumpToFirst) Dispatcher.UIThread.Post(() => _right.Editor.ScrollToVerticalOffset(offset), DispatcherPriority.Background);
        else Dispatcher.UIThread.Post(() =>
        {
            if (aligned?.Next(-1) is { } first) ScrollToRow(first.StartRow, center: true);
        }, DispatcherPriority.Background);
    }

    void InstallHighlighting(Side side, int length)
    {
        side.TextMate?.Dispose();
        side.TextMate = null;
        side.HighlightedPath = side.Path;
        if (length > HighlightMaxChars || side.Path is null) return;
        if (EditorLanguages.ExtensionFor(System.IO.Path.GetFileName(side.Path.Replace(ArchivePaths.Separator, "/"))) is not { } ext) return;
        try
        {
            var options = new RegistryOptions(ThemeService.IsDark(Application.Current!) ? ThemeName.DarkPlus : ThemeName.LightPlus);
            if (options.GetLanguageByExtension(ext) is not { } language) return;
            side.TextMate = side.Editor.InstallTextMate(options);
            side.TextMate.SetGrammar(options.GetScopeByLanguageId(language.Id));
            side.Editor.Background = Brushes.Transparent;
            // 차이 강조가 강조(TextMate) 색 위에 오도록 줄 안 강조를 맨 뒤로 옮긴다.
            var transformers = side.Editor.TextArea.TextView.LineTransformers;
            foreach (var m in transformers.OfType<IntralineColorizer>().ToList()) { transformers.Remove(m); transformers.Add(m); }
        }
        catch (Exception)
        {
            side.TextMate?.Dispose();
            side.TextMate = null;
        }
    }

    static TextEncodingKind OtherEncoding(TextEncodingKind primary) =>
        primary == TextEncodingKind.Utf8 ? TextEncodingKind.Cp949 : TextEncodingKind.Utf8;

    // --- 편집 ---

    void OnEdited(Side side)
    {
        if (_applying) return;
        if (!_editBatchOpen)
        {
            // 한 번에 몰아 친 글자들은 실행 취소 한 번으로 되돌린다 (다시 비교할 때까지가 한 묶음).
            _undo.Push((_left.Real, _right.Real));
            _redo.Clear();
            _editBatchOpen = true;
        }
        _leftState.Editing = _rightState.Editing = true;
        _save.IsEnabled = true;
        _recompareTimer.Stop();
        _recompareTimer.Start();
    }

    /// <summary>화면 글에서 실제 글을 꺼낸다 (채움 줄 앵커 중 지워지지 않았고 아직 빈 줄인 것만 뺀다).</summary>
    static string ExtractReal(Side side)
    {
        var doc = side.Editor.Document;
        var fillers = side.Fillers.Where(a => !a.IsDeleted).Select(a => a.Line - 1).ToHashSet();
        if (fillers.Count == 0 && side.Fillers.Count == 0 && !side.HasText) return side.Real;
        return DiffEdits.RealText(doc.Text, fillers, side.EndsWithNewline);
    }

    /// <summary>캐럿이 있는 쪽의 실제 줄 번호 (화면 행에서 그 앞의 채움 줄을 뺀 것).</summary>
    (bool Left, int Line, int Column) CaretReal()
    {
        var side = _left.Editor.TextArea.IsFocused ? _left : _right;
        var row = side.Editor.TextArea.Caret.Line - 1;
        var fillersBefore = side.Fillers.Count(a => !a.IsDeleted && a.Line - 1 < row && side.Editor.Document.GetLineByNumber(a.Line).Length == 0);
        return (side.Left, Math.Max(0, row - fillersBefore), side.Editor.TextArea.Caret.Column);
    }

    /// <summary>고친 내용을 실제 글로 옮기고 다시 비교한다 (손을 멈춘 뒤, 또는 저장·복사·실행 취소 전).</summary>
    async Task CommitEditsAsync()
    {
        _recompareTimer.Stop();
        if (!_editBatchOpen) return;
        _editBatchOpen = false;
        var caret = CaretReal();
        foreach (var side in new[] { _left, _right })
            if (side.CanEdit) side.Real = ExtractReal(side);
        await RecompareAsync(++_version, caret);
    }

    async Task SetRealAsync(string left, string right)
    {
        var caret = CaretReal();
        _left.Real = left;
        _right.Real = right;
        await RecompareAsync(++_version, caret);
    }

    async Task UndoAsync(bool redo)
    {
        await CommitEditsAsync();
        var (from, to) = redo ? (_redo, _undo) : (_undo, _redo);
        if (from.Count == 0) return;
        to.Push((_left.Real, _right.Real));
        var (l, r) = from.Pop();
        await SetRealAsync(l, r);
    }

    /// <summary>캐럿이 있는 차이 구간을 반대쪽으로 복사한다 (ROADMAP 3단계 블록 복사).</summary>
    async Task CopyBlockAsync(bool toRight)
    {
        await CommitEditsAsync();
        if (_leftState.Diff is not { } d) return;
        var target = toRight ? _right : _left;
        if (!target.CanEdit) return;
        var row = (_left.Editor.TextArea.IsFocused ? _left : _right).Editor.TextArea.Caret.Line - 1;
        if (d.RangeAt(row) is not { } range)
        {
            Notice(Strings.Current.CompareNothingToCopy);
            return;
        }
        _undo.Push((_left.Real, _right.Real));
        _redo.Clear();
        var (l, r) = DiffEdits.CopyBlock(d.Diff, range.Block, toRight);
        await SetRealAsync(l, r);
        Dispatcher.UIThread.Post(() => ScrollToRow(range.StartRow, center: false), DispatcherPriority.Background);
    }

    // --- 저장 (ROADMAP §6-1 ② 손실 확인, ③ 자동 백업) ---

    string BackupRoot => System.IO.Path.Combine(App.Policy.Directory, "backups");

    async Task SaveAllAsync()
    {
        await CommitEditsAsync();
        foreach (var side in new[] { _left, _right })
            if (side.Dirty && !await SaveAsync(side)) return;
    }

    async Task SaveFocusedAsync()
    {
        await CommitEditsAsync();
        var focused = _left.Editor.TextArea.IsFocused ? _left : _right;
        var side = focused.Dirty ? focused : (focused.Left ? _right : _left).Dirty ? (focused.Left ? _right : _left) : focused;
        if (side.Dirty) await SaveAsync(side);
    }

    /// <summary>한쪽을 저장한다. 물어볼 것이 있으면 묻고, 취소하면 false.</summary>
    async Task<bool> SaveAsync(Side side)
    {
        if (!side.CanEdit || side.Text is not { } text || side.Path is not { } path) return false;
        var s = Strings.Current;
        TextEncodingKind? encoding = null;
        var check = CompareSave.Check(path, text.File, side.Real);
        if (check.NeedsConfirmation)
        {
            var lines = new List<string>();
            if (check.ChangedOnDisk) lines.Add(s.CompareSaveChangedOnDisk);
            if (check.Lost.Count > 0) lines.Add(s.CompareSaveLost(TextEncodings.DisplayName(text.File.Encoding), string.Join(' ', check.Lost.Select(l => l.Text).Distinct())));
            if (check.Damaged > 0) lines.Add(s.CompareSaveDamaged(check.Damaged));
            lines.Add(s.CompareSaveBackupNote);
            var offerUtf8 = check.Lost.Count > 0 && text.File.Encoding != TextEncodingKind.Utf8;
            string[] buttons = offerUtf8 ? [s.CommonCancel, s.CompareSaveAnyway, s.CompareSaveAsUtf8] : [s.CommonCancel, s.CommonSave];
            var choice = await MessageDialog.ShowAsync(this, s.CompareSaveTitle(side.Name), string.Join("\n\n", lines), buttons, buttons.Length - 1, 0);
            if (choice <= 0) return false;
            if (offerUtf8 && choice == 2) encoding = TextEncodingKind.Utf8;
        }
        try
        {
            side.IgnoreWatchUntil = DateTime.UtcNow.AddSeconds(2);
            // 섞인 인코딩을 복구한 글은 UTF-8로 통일해 저장한다.
            var (saved, _) = await CompareSave.SaveAsync(path, side.Real, text.File, BackupRoot, encoding ?? (side.Repair is not null ? TextEncodingKind.Utf8 : null));
            side.Source = text with { File = saved with { Text = side.Real } };
            side.SavedText = side.Real;
            side.Repair = null;
            side.RepairRequested = false;
            side.ChangedOutside = false;
            UpdateSideChrome(side);
            UpdateTitle();
            Notice(s.CompareSaved(side.Name));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(this, s.CompareSaveTitle(side.Name), s.CompareSaveFailed(e.Message), [s.CommonClose], 0, 0);
            return false;
        }
    }

    async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed) return;
        if (_editBatchOpen) { _left.Real = _left.CanEdit ? ExtractReal(_left) : _left.Real; _right.Real = _right.CanEdit ? ExtractReal(_right) : _right.Real; _editBatchOpen = false; }
        var dirty = new[] { _left, _right }.Where(x => x.Dirty).ToList();
        if (dirty.Count == 0) return;
        e.Cancel = true;
        var s = Strings.Current;
        var choice = await MessageDialog.ShowAsync(this, s.CompareUnsavedTitle, s.CompareUnsavedBody(string.Join(", ", dirty.Select(d => d.Name))),
            [s.CommonCancel, s.CompareDontSave, s.CommonSave], 2, 0);
        if (choice <= 0) return;
        if (choice == 2)
            foreach (var side in dirty)
                if (!await SaveAsync(side)) return;
        _closeConfirmed = true;
        Close();
    }

    // --- 바깥 변경 감지 (ROADMAP §6-1 ③ 자동 다시 비교) ---

    void Watch(Side side)
    {
        side.Watcher?.Dispose();
        side.Watcher = null;
        if (side.Path is not { } path || ArchivePaths.IsInArchive(path) || System.IO.Path.GetDirectoryName(path) is not { Length: > 0 } dir || !Directory.Exists(dir)) return;
        try
        {
            var w = new FileSystemWatcher(dir, System.IO.Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            };
            void Changed(object? _, FileSystemEventArgs __) => Dispatcher.UIThread.Post(() => OnChangedOutside(side));
            w.Changed += Changed;
            w.Created += Changed;
            w.Renamed += (o, a) => Changed(o, a);
            w.EnableRaisingEvents = true;
            side.Watcher = w;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // 감시를 못 해도 비교는 된다 (네트워크 드라이브 등).
        }
    }

    DispatcherTimer? _outsideTimer;

    void OnChangedOutside(Side side)
    {
        if (DateTime.UtcNow < side.IgnoreWatchUntil) return;
        // 한 번 저장에 이벤트가 여러 번 온다 → 잠깐 모아서 한 번만.
        _outsideTimer?.Stop();
        _outsideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _outsideTimer.Tick += async (_, _) =>
        {
            _outsideTimer!.Stop();
            if (side.Text is { } t && side.Path is { } p && !TextFiles.ChangedOnDisk(p, t.File)) return;
            if (_left.Dirty || _right.Dirty || _editBatchOpen)
            {
                side.ChangedOutside = true; // 고친 것이 있으면 덮어쓰지 않고 알린다
                UpdateSideChrome(side);
            }
            else await ReloadAsync();
        };
        _outsideTimer.Start();
    }

    // --- 표시 ---

    void UpdateSideChrome(Side side)
    {
        var s = Strings.Current;
        side.PathText.Text = (side.Dirty ? "● " : "") + (side.Title is { Length: > 0 } title ? title : side.Path ?? (side.Real.Length > 0 ? s.CompareScratchName : s.HomeNotChosen));
        ToolTip.SetTip(side.PathText, side.Path ?? (side.Real.Length > 0 ? s.CompareScratchTooltip : null));
        side.EncodingText.Text = side.Text is { } t && !side.IsScratch
            ? TextEncodings.DisplayName(t.File.Encoding, t.File.HasBom) + (side.HasText && !side.CanEdit ? " · " + s.CompareReadOnly : "")
            : "";
        ToolTip.SetTip(side.EncodingText, side.HasText && !side.IsScratch ? s.CompareReopenEncoding : null);
        side.EncodingText.Cursor = side.HasText && !side.IsScratch ? new Cursor(StandardCursorType.Hand) : Cursor.Default;

        (side.Overlay.IsVisible, side.OverlayText.Text) = side.Source switch
        {
            null => (true, s.CompareEmptySide),
            CompareSource.Text when side.IsScratch && side.Real.Length == 0 => (true, s.CompareEmptySide),
            CompareSource.Binary => (true, s.CompareBinary),
            CompareSource.TooLarge big => (true, s.CompareTooLarge(BytesFormat.Format(big.Length))),
            CompareSource.Failed f => (true, s.CompareLoadFailed(f.Error.ToString())),
            _ => (false, ""),
        };

        // 안내 띠: 바깥 변경 > 인코딩 (ROADMAP §6-1 ②·③).
        side.BannerActions.Children.Clear();
        side.Banner.IsVisible = false;
        if (side.ChangedOutside)
        {
            side.Banner.IsVisible = true;
            side.BannerText.Text = s.CompareChangedOutside;
            var reload = UiScale.Enable(new Button { Content = s.CompareReloadDiscard, Padding = new Thickness(12, 4) });
            reload.Click += async (_, _) => await ReloadAsync();
            side.BannerActions.Children.Add(reload);
            return;
        }
        if (side.Text is not { } text) return;
        if (side.Repair is { } repair)
        {
            side.Banner.IsVisible = true;
            side.BannerText.Text = s.CompareRepaired(repair.RepairedLines.Count, repair.BrokenLines.Count);
            side.BannerActions.Children.Add(EncodingMenuButton(side));
        }
        else if (text.File.DecodeErrors > 0)
        {
            side.Banner.IsVisible = true;
            var errors = TextEncodings.FindErrors(text.Bytes, text.File.Encoding, max: 1);
            side.BannerText.Text = s.CompareEncodingDamaged(TextEncodings.DisplayName(text.File.Encoding), text.File.DecodeErrors, errors.Count > 0 ? errors[0].Line : 1);
            if (text.File.Encoding is not (TextEncodingKind.Utf16Le or TextEncodingKind.Utf16Be))
            {
                var other = OtherEncoding(text.File.Encoding);
                var repairButton = UiScale.Enable(new Button { Content = s.CompareRepairMixed(TextEncodings.DisplayName(text.File.Encoding), TextEncodings.DisplayName(other)), Padding = new Thickness(12, 4) });
                repairButton.Click += async (_, _) =>
                {
                    side.RepairRequested = true;
                    await ReloadAsync();
                };
                side.BannerActions.Children.Add(repairButton);
            }
            side.BannerActions.Children.Add(EncodingMenuButton(side));
        }
    }

    Button EncodingMenuButton(Side side)
    {
        var button = UiScale.Enable(new Button { Content = Strings.Current.CompareReopenEncoding, Padding = new Thickness(12, 4) });
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
            var item = new MenuItem { Header = $"{TextEncodings.DisplayName(p.Kind)} — {Strings.Current.CompareEncodingErrors(p.Errors)} · {preview}" };
            var kind = p.Kind;
            item.Click += async (_, _) =>
            {
                side.ForcedEncoding = kind;
                side.RepairRequested = false;
                await ReloadAsync();
            };
            flyout.Items.Add(item);
        }
        return flyout;
    }

    void UpdateTitle()
    {
        var dirty = _left.Dirty || _right.Dirty ? "● " : "";
        Title = $"{dirty}{Strings.Current.CompareWindowTitle(_left.Name, _right.Name)} — {AppIdentity.DisplayName}";
        _save.IsEnabled = _left.Dirty || _right.Dirty;
    }

    /// <summary>잠깐 보이는 알림 (저장함 등) — 상태 표시 자리에 4초.</summary>
    void Notice(string text)
    {
        _notice = text;
        UpdateStatus();
        DispatcherTimer.RunOnce(() =>
        {
            if (_notice == text) { _notice = null; UpdateStatus(); }
        }, TimeSpan.FromSeconds(4));
    }

    void UpdateStatus()
    {
        var s = Strings.Current;
        if (_notice is { } n) { _status.Text = n; return; }
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

    void UpdateViewport()
    {
        var tv = _right.Editor.TextArea.TextView;
        var doc = _right.Editor.Document;
        if (tv.DefaultLineHeight <= 0 || doc.LineCount == 0) return;
        // 접힌 줄이 있으면 화면 높이와 행 수가 비례하지 않으므로 맨 위·맨 아래에 보이는 행을 직접 찾는다.
        var top = tv.GetDocumentLineByVisualTop(tv.VerticalOffset)?.LineNumber ?? 1;
        var bottom = tv.GetDocumentLineByVisualTop(tv.VerticalOffset + Math.Max(0, tv.Bounds.Height - 1))?.LineNumber ?? doc.LineCount;
        _overview.Viewport = (top - 1, Math.Max(1, bottom - top + 1));
        _overview.InvalidateVisual();
    }

    void ScrollToRow(int row, bool center, bool moveCaret = true)
    {
        var lines = _right.Editor.Document.LineCount;
        if (lines == 0) return;
        var line = Math.Clamp(row + 1, 1, lines);
        var tv = _right.Editor.TextArea.TextView;
        var offset = tv.GetVisualTopByDocumentLine(line) - (center ? tv.Bounds.Height / 3 : 0);
        _right.Editor.ScrollToVerticalOffset(Math.Max(0, offset));
        if (!moveCaret) return;
        _syncing = true;
        _right.Editor.TextArea.Caret.Line = line;
        _left.Editor.TextArea.Caret.Line = Math.Min(line, _left.Editor.Document.LineCount);
        _syncing = false;
    }

    // --- 차이만 보기 (같은 줄 접기) ---

    /// <summary>차이만 보기가 켜져 있으면 양쪽에서 같은 행 구간을 접는다 (<see cref="AlignedDiff.CollapsibleRuns"/>). 꺼져 있으면 모두 편다.</summary>
    void ApplyFolds(bool redraw = true)
    {
        var aligned = _leftState.Diff;
        // Show Differences = 문맥 없이, Show Context = 문맥 n줄, Show Same = 차이를 접는다.
        var runs = aligned is null ? [] : _filter switch
        {
            CompareViewFilter.Differences => aligned.CollapsibleRuns(0),
            CompareViewFilter.Context => aligned.CollapsibleRuns(_contextLines),
            CompareViewFilter.Same => aligned.CollapsibleRuns(0, showSame: true),
            _ => (IReadOnlyList<(int StartRow, int RowCount)>)[],
        };
        foreach (var side in new[] { _left, _right })
        {
            side.Folding.Clear();
            var doc = side.Editor.Document;
            foreach (var (start, count) in runs)
            {
                if (start + count > doc.LineCount) break;
                var first = doc.GetLineByNumber(start + 1);
                var last = doc.GetLineByNumber(start + count);
                var fold = side.Folding.CreateFolding(first.Offset, last.EndOffset);
                fold.Title = Strings.Current.CompareFolded(count);
                fold.IsFolded = true;
            }
        }
        if (redraw) Dispatcher.UIThread.Post(() =>
        {
            SyncScroll(_right, _left);
            Redraw();
        }, DispatcherPriority.Background);
    }

    /// <summary><paramref name="from"/> 쪽의 접힘 상태를 맞은편에 그대로 옮긴다 (두 쪽은 같은 순서로 같은 행 구간을 접는다).</summary>
    void MirrorFolds(Side from)
    {
        var to = from.Left ? _right : _left;
        var a = from.Folding.AllFoldings.ToList();
        var b = to.Folding.AllFoldings.ToList();
        var changed = false;
        for (var i = 0; i < Math.Min(a.Count, b.Count); i++)
            if (b[i].IsFolded != a[i].IsFolded) { b[i].IsFolded = a[i].IsFolded; changed = true; }
        if (!changed) return;
        Dispatcher.UIThread.Post(() =>
        {
            SyncScroll(from, to);
            Redraw();
        }, DispatcherPriority.Background);
    }

    /// <summary><paramref name="offset"/>이 접힌 곳 안이면 양쪽의 그 접기를 편다 (찾기·이동이 숨은 줄에 닿을 때).</summary>
    void Unfold(Side side, int offset)
    {
        var line = side.Editor.Document.GetLineByOffset(offset).LineNumber;
        foreach (var s in new[] { _left, _right })
        {
            var doc = s.Editor.Document;
            if (line > doc.LineCount) continue;
            var at = doc.GetLineByNumber(line).Offset;
            foreach (var fold in s.Folding.GetFoldingsContaining(at)) fold.IsFolded = false;
        }
    }

    // --- 찾기 (Ctrl+F) ---

    void OpenFind() => OpenFind(replace: false);

    /// <summary>찾기 막대를 연다. <paramref name="replace"/>면 바꾸기 줄도 (BC Ctrl+H).</summary>
    void OpenFind(bool replace)
    {
        _findSide = _left.Editor.TextArea.IsFocused ? _left : _right.Editor.TextArea.IsFocused ? _right : _findSide ?? _right;
        var selected = _findSide.Editor.SelectedText;
        _findBar.IsVisible = true;
        _replaceRow.IsVisible = replace || _replaceRow.IsVisible && _findBar.IsVisible;
        if (selected.Length > 0 && !selected.Contains('\n')) _findBox.Text = selected;
        UpdateFindWhere();
        Dispatcher.UIThread.Post(() =>
        {
            _findBox.Focus();
            _findBox.SelectAll();
        });
    }

    void CloseFind()
    {
        _findBar.IsVisible = false;
        _replaceRow.IsVisible = false;
        _findCount.Text = "";
        (_findSide ?? _right).Editor.TextArea.Focus();
    }

    StringComparison FindComparison => _findCase.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>지금 고른 글이 찾는 글이면 바꾸고 다음 일치로 간다. 아니면 먼저 다음 일치를 고른다.</summary>
    void ReplaceOne()
    {
        var side = _findSide ??= _right;
        var needle = _findBox.Text ?? "";
        if (needle.Length == 0) return;
        if (!side.CanEdit) { Notice(Strings.Current.CompareReadOnly); return; }
        if (string.Equals(side.Editor.SelectedText, needle, FindComparison))
            side.Editor.Document.Replace(side.Editor.SelectionStart, side.Editor.SelectionLength, _replaceBox.Text ?? "");
        Find(forward: true);
    }

    /// <summary>찾을 쪽의 모든 일치를 바꾼다 (실행 취소 한 번으로 되돌린다 — 한 편집 묶음).</summary>
    void ReplaceAll()
    {
        var side = _findSide ??= _right;
        var needle = _findBox.Text ?? "";
        if (needle.Length == 0) return;
        if (!side.CanEdit) { Notice(Strings.Current.CompareReadOnly); return; }
        var doc = side.Editor.Document;
        var hay = doc.Text;
        var matches = new List<int>();
        for (var i = hay.IndexOf(needle, FindComparison); i >= 0; i = hay.IndexOf(needle, i + needle.Length, FindComparison)) matches.Add(i);
        if (matches.Count == 0) { _findCount.Text = Strings.Current.CompareFindNone; return; }
        var replacement = _replaceBox.Text ?? "";
        doc.BeginUpdate();
        try
        {
            for (var k = matches.Count - 1; k >= 0; k--) doc.Replace(matches[k], needle.Length, replacement);
        }
        finally { doc.EndUpdate(); }
        _findCount.Text = "";
        Notice(Strings.Current.CompareReplaced(matches.Count));
    }

    void UpdateFindWhere() =>
        _findWhere.Text = _findSide is null ? "" : _findSide.Left ? Strings.Current.CompareFindInLeft : Strings.Current.CompareFindInRight;

    /// <summary>
    /// 찾을 쪽에서 다음(앞) 일치로 간다. <paramref name="fromSelectionStart"/>면 지금 고른 곳 자체부터 (입력할 때마다 찾기).
    /// 끝까지 가면 처음으로 돌아간다. 접힌 곳 안이면 편다.
    /// </summary>
    void Find(bool forward, bool fromSelectionStart = false)
    {
        var s = Strings.Current;
        var needle = _findBox.Text ?? "";
        var side = _findSide ??= _right;
        if (needle.Length == 0) { _findCount.Text = ""; return; }
        var hay = side.Editor.Document.Text;
        var comparison = _findCase.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var matches = new List<int>();
        for (var i = hay.IndexOf(needle, comparison); i >= 0 && matches.Count < 100_000; i = hay.IndexOf(needle, i + Math.Max(1, needle.Length), comparison))
            matches.Add(i);
        if (matches.Count == 0) { _findCount.Text = s.CompareFindNone; return; }
        var start = side.Editor.SelectionLength > 0 ? side.Editor.SelectionStart : side.Editor.CaretOffset;
        var index = forward
            ? matches.FindIndex(m => fromSelectionStart ? m >= start : m > start)
            : matches.FindLastIndex(m => m < start);
        if (index < 0) index = forward ? 0 : matches.Count - 1;
        var offset = matches[index];
        Unfold(side, offset);
        side.Editor.Select(offset, needle.Length);
        var line = side.Editor.Document.GetLineByOffset(offset).LineNumber;
        _syncing = true;
        var other = side.Left ? _right : _left;
        other.Editor.TextArea.Caret.Line = Math.Min(line, other.Editor.Document.LineCount);
        _syncing = false;
        Dispatcher.UIThread.Post(() =>
        {
            var tv = _right.Editor.TextArea.TextView;
            var top = tv.GetVisualTopByDocumentLine(line);
            if (top < tv.VerticalOffset || top > tv.VerticalOffset + tv.Bounds.Height - tv.DefaultLineHeight * 2)
                ScrollToRow(line - 1, center: true, moveCaret: false);
            side.Editor.TextArea.Caret.BringCaretToView(); // 긴 줄이면 가로로도 보이게
        }, DispatcherPriority.Background);
        _findCount.Text = s.CompareFindCount(index + 1, matches.Count);
    }

    int CurrentRow() => (_left.Editor.TextArea.IsFocused ? _left : _right).Editor.TextArea.Caret.Line - 1;

    void GoDiff(bool next)
    {
        if (_leftState.Diff is not { } d) return;
        var row = CurrentRow();
        var include = IncludeUnimportant(d);
        var target = next ? d.Next(row, include) : d.Previous(row, include);
        // 지금 차이 구간 안에 있으면 그 구간 끝/처음을 기준으로 다음 것을 찾는다.
        if (next && d.RangeAt(row) is { } cur && target?.StartRow == cur.StartRow) target = d.Next(cur.StartRow + cur.RowCount - 1, include);
        if (target is { } t) ScrollToRow(t.StartRow, center: true);
    }

    /// <summary>중요하지 않은 차이도 이동 대상인가: 무시하지 않고, 중요한 차이가 하나도 없을 때.</summary>
    bool IncludeUnimportant(AlignedDiff d) => _ignoreUnimportant.IsChecked != true && d.Diff.DifferenceCount == 0;

    /// <summary>BC Next / Previous Difference (줄 단위, Shift+Ctrl+N / P): 구간 안에서도 한 행씩.</summary>
    void GoDiffLine(bool next)
    {
        if (_leftState.Diff is not { } d) return;
        var row = CurrentRow();
        var include = IncludeUnimportant(d);
        if ((next ? d.NextDifferenceRow(row, include) : d.PreviousDifferenceRow(row, include)) is { } target)
            ScrollToRow(target, center: true);
    }

    /// <summary>BC Copy Line to Right / Left (Shift+Ctrl+R / L): 캐럿이 있는 행 하나만.</summary>
    async Task CopyLineAsync(bool toRight)
    {
        await CommitEditsAsync();
        if (_leftState.Diff is not { } d || !(toRight ? _right : _left).CanEdit) return;
        var row = CurrentRow();
        if (row < 0 || row >= d.Rows.Count || d.Rows[row].Kind == DiffBlockKind.Equal) { Notice(Strings.Current.CompareNothingToCopy); return; }
        _undo.Push((_left.Real, _right.Real));
        _redo.Clear();
        var (l, r) = DiffEdits.CopyRow(d, row, toRight);
        await SetRealAsync(l, r);
    }

    /// <summary>BC Recompare Files (Ctrl+F5): 고친 것을 그대로 두고 지금 다시 비교한다 (Reload Files와 달리 디스크를 읽지 않는다).</summary>
    async Task RecompareNowAsync()
    {
        if (_editBatchOpen) { await CommitEditsAsync(); return; }
        await RecompareAsync(++_version, CaretReal());
    }

    /// <summary>BC Select Section (Ctrl+D): 캐럿이 있는 차이 구간을 초점 쪽에서 고른다.</summary>
    void SelectSection()
    {
        if (_leftState.Diff is not { } d) return;
        var side = FocusedSide();
        if (d.RangeAt(CurrentRow()) is not { } range) return;
        var doc = side.Editor.Document;
        var first = doc.GetLineByNumber(Math.Min(range.StartRow + 1, doc.LineCount));
        var last = doc.GetLineByNumber(Math.Min(range.StartRow + range.RowCount, doc.LineCount));
        side.Editor.Select(first.Offset, last.EndOffset - first.Offset);
    }

    /// <summary>BC Go To (Ctrl+G): 초점 쪽 파일의 실제 줄 번호로 간다.</summary>
    async Task GoToLineAsync()
    {
        var side = FocusedSide();
        var s = Strings.Current;
        var input = await InputDialog.ShowAsync(this, s.CompareGoTo, s.CompareGoToPrompt(side.Name), "", s.CommonOk, s.CommonCancel);
        if (input is null || !int.TryParse(input.Trim(), out var line) || line < 1) return;
        var row = _leftState.Diff is { } d ? (side.Left ? d.RowOfLeftLine(line - 1) : d.RowOfRightLine(line - 1)) : line - 1;
        var doc = side.Editor.Document;
        if (row + 1 <= doc.LineCount) Unfold(side, doc.GetLineByNumber(row + 1).Offset);
        ScrollToRow(row, center: true);
        side.Editor.TextArea.Focus();
    }

    /// <summary>BC Text Compare Info (Ctrl+I): 양쪽 파일의 크기·시각·인코딩·줄바꿈·줄 수.</summary>
    async Task ShowInfoAsync()
    {
        var s = Strings.Current;
        string Describe(Side side)
        {
            if (side.IsScratch) return $"{side.Name}\n{s.CompareInfoLines(DiffEdits.Split(side.Real).Lines.Count)}";
            if (side.Text is not { } t) return $"{side.Path}\n—";
            var lineBreak = t.File.LineBreak switch { "\r\n" => "CRLF (Windows)", "\r" => "CR (Mac)", _ => "LF (Unix)" };
            return string.Join("\n",
                side.Path,
                s.CompareInfoSize(BytesFormat.Format(t.File.Length), t.File.Modified.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
                $"{TextEncodings.DisplayName(t.File.Encoding, t.File.HasBom)} · {lineBreak}",
                s.CompareInfoLines(DiffEdits.Split(side.Real).Lines.Count));
        }
        await MessageDialog.ShowAsync(this, s.CompareInfo, $"{s.HomeLeft}\n{Describe(_left)}\n\n{s.HomeRight}\n{Describe(_right)}", [s.CommonClose], 0, 0);
    }

    /// <summary>
    /// BC Save File As (F12): 다른 이름으로 저장한다. 붙여 넣은 글(파일 없는 칸)은 이것으로 파일이 된다 — UTF-8, 이 OS의 줄바꿈.
    /// 저장한 뒤 그 칸은 새 파일을 비교한다.
    /// </summary>
    async Task SaveAsAsync(Side side)
    {
        await CommitEditsAsync();
        if (!side.HasText) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Current.CompareSaveFileAs,
            SuggestedFileName = side.Path is null ? "pasted.txt" : System.IO.Path.GetFileName(side.Path.Replace(ArchivePaths.Separator, "/")),
            SuggestedStartLocation = await PickLocations.StartAsync(StorageProvider),
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        var template = side.Text is { } t && !side.IsScratch
            ? t.File
            : new EditorTextFile("", HasBom: false, LineBreak: Environment.NewLine, DateTime.MinValue, 0, Binary: false);
        try
        {
            var saved = await TextFiles.SaveAsync(path, side.Real, template with { DecodeErrors = 0 });
            side.Path = path;
        side.Title = null;
            side.Title = null;
            side.Source = new CompareSource.Text(path, saved with { Text = side.Real }, await File.ReadAllBytesAsync(path));
            side.SavedText = side.Real;
            side.Repair = null;
            side.RepairRequested = false;
            Watch(side);
            UpdateSideChrome(side);
            UpdateTitle();
            Notice(Strings.Current.CompareSaved(side.Name));
            await RecompareAsync(++_version, CaretReal()); // 이름이 바뀌면 정책(파일 형식)·강조가 달라질 수 있다
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(this, Strings.Current.CompareSaveFileAs, Strings.Current.CompareSaveFailed(e.Message), [Strings.Current.CommonClose], 0, 0);
        }
    }

    /// <summary>고친 것이 있으면 버려도 되는지 묻는다 (다시 읽기·바꾸기 전).</summary>
    async Task<bool> ConfirmDiscardAsync()
    {
        await CommitEditsAsync();
        var dirty = new[] { _left, _right }.Where(x => x.Dirty).ToList();
        if (dirty.Count == 0) return true;
        var s = Strings.Current;
        var choice = await MessageDialog.ShowAsync(this, s.CompareUnsavedTitle, s.CompareUnsavedBody(string.Join(", ", dirty.Select(d => d.Name))),
            [s.CommonCancel, s.CompareDontSave, s.CommonSave], 2, 0);
        if (choice <= 0) return false;
        if (choice == 2)
            foreach (var side in dirty)
                if (!await SaveAsync(side)) return false;
        return true;
    }

    async Task SwapAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        (_left.Path, _right.Path) = (_right.Path, _left.Path);
        (_left.Title, _right.Title) = (_right.Title, _left.Title);
        (_left.ReadOnly, _right.ReadOnly) = (_right.ReadOnly, _left.ReadOnly);
        (_left.Real, _right.Real) = (_right.Real, _left.Real); // 파일 없는 칸의 글도 함께 옮긴다
        (_left.ForcedEncoding, _right.ForcedEncoding) = (_right.ForcedEncoding, _left.ForcedEncoding);
        (_left.RepairRequested, _right.RepairRequested) = (_right.RepairRequested, _left.RepairRequested);
        await ReloadAsync();
        RecordHistory();
    }

    async Task PickAsync(Side side)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = side.Left ? Strings.Current.CompareOpenLeftTitle : Strings.Current.CompareOpenRightTitle,
            AllowMultiple = false,
            SuggestedStartLocation = await PickLocations.StartAsync(StorageProvider),
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        PickLocations.Remember(path, isFolder: false);
        if (!await ConfirmDiscardAsync()) return;
        side.Path = path;
        side.ForcedEncoding = null;
        side.RepairRequested = false;
        await ReloadAsync();
        RecordHistory();
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
            if (!await ConfirmDiscardAsync()) return;
            _left.Path = l;
            _left.Title = _right.Title = null;
            _right.Path = r;
            foreach (var side in new[] { _left, _right }) { side.ForcedEncoding = null; side.RepairRequested = false; }
            await ReloadAsync();
            RecordHistory();
        });
    }

    static DroppedItem? Item(Side side) => side.Path is { } p ? new DroppedItem(p, DroppedKind.File) : null;

    /// <summary>
    /// 놓기 결과를 처리한다 (홈 창과 함께 쓴다): 파일 비교는 <paramref name="apply"/>로 양쪽을 정하고, 아직 없는 기능(폴더 비교·병합)과
    /// 거절은 안내 대화상자로 알린다. 파일 + 폴더면 폴더 안의 같은 이름 파일을 찾는다.
    /// </summary>
    internal static async Task ApplyDropAsync(Window owner, DropPlan plan, Func<string?, string?, Task> apply, bool allowPendingFolders = false)
    {
        var s = Strings.Current;
        async Task Tell(string message) => await MessageDialog.ShowAsync(owner, AppIdentity.DisplayName, message, [s.CommonClose], 0, 0);
        switch (plan)
        {
            case DropPlan.Pending p when allowPendingFolders || p.Left is not { IsContainer: true } && p.Right is not { IsContainer: true }:
                await apply(p.Left?.Path, p.Right?.Path);
                break;
            case DropPlan.Compare { Mode: CompareMode.Folders } c:
                FolderCompareWindow.Open(owner, c.Left.Path, c.Right.Path);
                break;
            case DropPlan.Compare { Mode: CompareMode.Files } c:
                await apply(c.Left.Path, c.Right.Path);
                break;
            case DropPlan.Compare { Mode: CompareMode.FileAgainstFolder } c:
            {
                var (file, container, fileOnLeft) = DropPlanner.Split(c);
                var name = System.IO.Path.GetFileName(file.Path);
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
            default: // 파일과 폴더의 짝은 아직 지원하지 않는다
                await Tell(s.FolderFileVsFolderLater);
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
        if (_findBox.IsFocused || _replaceBox.IsFocused)
        {
            // 찾기·바꾸기 칸에서는 글자 편집 키(Ctrl+Z·Ctrl+←→·Ctrl+L 등)를 칸에 맡기고, 찾기 키만 받는다.
            switch (e.Key)
            {
                case Key.Enter when _replaceBox.IsFocused: if (primary) ReplaceAll(); else ReplaceOne(); break;
                case Key.Enter: Find(forward: !shift); break;
                case Key.F3: Find(forward: !shift); break;
                case Key.Escape: CloseFind(); break;
                case Key.F when primary: _findBox.Focus(); _findBox.SelectAll(); break;
                case Key.H when primary: _replaceRow.IsVisible = true; _replaceBox.Focus(); break;
                case Key.N when primary: if (shift) GoDiffLine(next: true); else GoDiff(next: true); break;
                case Key.P when primary: if (shift) GoDiffLine(next: false); else GoDiff(next: false); break;
                default: handled = false; break;
            }
            e.Handled = handled;
            return;
        }
        // 키는 Beyond Compare 기본값을 따른다 (BC_COMPAT.md §1). F8·Alt+↑↓는 덧붙인 키.
        switch (e.Key)
        {
            case Key.N when primary: if (shift) GoDiffLine(next: true); else GoDiff(next: true); break;
            case Key.P when primary: if (shift) GoDiffLine(next: false); else GoDiff(next: false); break;
            case Key.F8: GoDiff(next: !shift); break;
            case Key.Down when alt: GoDiff(next: true); break;
            case Key.Up when alt: GoDiff(next: false); break;
            case Key.R when primary: _ = shift ? CopyLineAsync(toRight: true) : CopyBlockAsync(toRight: true); break;
            case Key.L when primary: _ = shift ? CopyLineAsync(toRight: false) : CopyBlockAsync(toRight: false); break;
            case Key.D when primary && !shift: SelectSection(); break;
            case Key.F when primary: OpenFind(); break;
            case Key.H when primary: OpenFind(replace: true); break;
            case Key.F3: if (_findBox.Text is { Length: > 0 }) Find(forward: !shift); else OpenFind(); break;
            case Key.G when primary: _ = GoToLineAsync(); break;
            case Key.I when primary && !shift: _ = ShowInfoAsync(); break;
            case Key.Escape when _findBar.IsVisible: CloseFind(); break;
            case Key.V when primary && shift: _ = PasteAsync(FocusedSide()); break;
            case Key.S when primary: _ = shift ? SaveAllAsync() : SaveFocusedAsync(); break;
            case Key.F12: _ = SaveAsAsync(FocusedSide()); break;
            case Key.Z when primary && !shift: _ = UndoAsync(redo: false); break;
            case Key.Z when primary && shift: _ = UndoAsync(redo: true); break;
            case Key.Y when primary: _ = UndoAsync(redo: true); break;
            case Key.F5 when primary: _ = RecompareNowAsync(); break;
            case Key.F5: _ = ReloadWithConfirmAsync(); break;
            case Key.U when primary && !shift: _ = SwapAsync(); break;
            case Key.O when primary: _ = PickAsync(_right.Path is null || _left.Path is not null && _right.Editor.TextArea.IsFocused ? _right : _left); break;
            case Key.W when primary: Close(); break;
            case Key.OemComma when primary: SettingsWindow.Open(this); break;
            default: handled = false; break;
        }
        e.Handled = handled;
    }

    async Task ReloadWithConfirmAsync()
    {
        if (await ConfirmDiscardAsync()) await ReloadAsync();
    }
}

/// <summary>명령줄(BC 스위치)로 연 비교 창의 선택 — 칸 제목, 읽기 전용, 중요하지 않은 차이 무시(null이면 설정을 따른다).</summary>
public sealed record CompareOptions(string? LeftTitle = null, string? RightTitle = null, bool LeftReadOnly = false, bool RightReadOnly = false, bool? IgnoreUnimportant = null)
{
    public static readonly CompareOptions None = new();
}
