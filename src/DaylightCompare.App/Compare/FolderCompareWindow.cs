using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaylightCompare.App.Dialogs;
using DaylightCompare.App.Icons;
using DaylightCompare.App.Views;
using DaylightCompare.Domain;
using DaylightCompare.Domain.Folder;
using DaylightCompare.Domain.Policy;
using DaylightCompare.Services;
using DaylightCompare.Services.Folder;

namespace DaylightCompare.App.Compare;

/// <summary>
/// 폴더 비교 창 (ROADMAP 4b, 창 하나 = 폴더 비교 하나). 모습은 Beyond Compare의 폴더 비교를 따른다(BC_COMPAT.md §2): 좌우 나무를 같은 줄에 나란히,
/// 같음(보통 글자색)·다름과 더 새것(빨강, 더 오래된 쪽은 흐리게)·한쪽에만(보라)·아직 모름(회색), 보기 필터·필터 막대·복사·삭제·고르기 명령.
/// 비교는 <see cref="FolderComparer"/>가 백그라운드에서 하고, 읽는 대로 보인다 (알림은 모아서 0.15초마다 한 번 줄 목록을 다시 만든다).
/// 파일 짝을 열면 텍스트 비교 창(<see cref="CompareWindow"/>)이 열린다.
/// </summary>
public sealed class FolderCompareWindow : Window
{
    const double RowHeight = 26;

    /// <summary>복사·삭제는 로컬 폴더끼리만 한다 (압축 파일은 읽기 전용, 원격은 연결 화면이 생길 때).</summary>
    static readonly FileOperations Ops = new();

    string _leftRoot, _rightRoot;
    IFileSystem _leftFs, _rightFs;
    FolderComparer? _comparer;
    CancellationTokenSource? _cts;
    FileMask _filter = FileMask.All;
    FolderShow _show;
    bool _alwaysFolders;
    bool _activeLeft = true;
    bool _dirty;
    string? _notice;
    List<FolderRow> _rows = [];
    readonly Dictionary<string, bool> _expanded = [];

    readonly ListBox _list = new() { SelectionMode = SelectionMode.Multiple, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBox _filterBox = new() { MinWidth = 240, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBox _excludeBox = new() { MinWidth = 240, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBox _leftPath = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBox _rightPath = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly DropDownButton _viewFilter = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly DialogHost _dialogs = new();

    /// <summary>보기 필터가 모든 항목을 숨겼을 때 목록 자리에 뜨는 안내와 [모두 보기] — 비교는 끝났는데 목록이 비어 "사라진" 것처럼 보이지 않게.</summary>
    readonly TextBlock _allHiddenText = new() { TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    readonly Button _showAllButton = new() { Name = "ShowAllButton", Classes = { "accent" }, HorizontalAlignment = HorizontalAlignment.Center };
    readonly StackPanel _allHidden = new() { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };

    public string LeftRoot => _leftRoot;
    public string RightRoot => _rightRoot;

    /// <summary>한쪽 경로가 폴더(또는 압축 파일)인가.</summary>
    public static bool CanOpen(string path) => Directory.Exists(path) || (File.Exists(path) && ArchivePaths.IsArchiveName(path));

    static (IFileSystem Fs, string Root) Resolve(string path) =>
        !Directory.Exists(path) && ArchivePaths.IsArchiveName(path)
            ? (new ArchiveFileSystem(path), ArchivePaths.Format(path))
            : (LocalFileSystem.Instance, path);

    /// <summary>폴더 비교 창을 연다. 한쪽(또는 양쪽)이 비어 있으면 빈 창으로 열고, 그쪽은 주소 창에서 고르거나 끌어다 놓아 채운다.</summary>
    public static FolderCompareWindow Open(Window? near, string? left, string? right)
    {
        var w = new FolderCompareWindow(left ?? "", right ?? "");
        if (near is not null) WindowPlacementHelper.OpenNear(near, w);
        else w.Show();
        return w;
    }

    FolderCompareWindow(string left, string right)
    {
        // 경로가 폴더인지 압축 파일인지는 열 때 UI 스레드에서 디스크를 만져 알아내지 않는다 (네트워크 드라이브에서는 이것만으로 앱이 멈출 수 있다).
        // 일단 받은 그대로 두고, 첫 비교를 시작할 때 백그라운드에서 정한다 (ResolveOnce).
        (_leftFs, _leftRoot) = (LocalFileSystem.Instance, left);
        (_rightFs, _rightRoot) = (LocalFileSystem.Instance, right);
        var settings = App.Settings.Value;
        // 저장된 토글이 하나도 켜져 있지 않으면(예전에 모두 끈 채 저장됨) 창을 열 때마다 목록이 비므로 모두 보기로 되돌린다.
        _show = (FolderShow)settings.FolderShowFlags & FolderShow.All;
        if (_show == FolderShow.None) _show = FolderShow.All;
        _alwaysFolders = settings.FolderAlwaysShowFolders;
        Width = 1280;
        Height = 780;
        MinWidth = 900;
        MinHeight = 480;

        // 줄 높이는 우리가 정한다 (ListBoxItem의 기본 안쪽 여백·최소 높이를 없앤다).
        Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(PaddingProperty, new Thickness(0)), new Setter(MinHeightProperty, 0.0), new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch) },
        });
        Content = BuildLayout();
        _list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<FolderRow>((row, _) => row is null ? null : BuildRow(row), supportsRecycling: false);
        _list.DoubleTapped += (_, e) => { if (e.Source is not DuoIcon) OpenSelected(); };
        _list.AddHandler(PointerPressedEvent, (_, e) =>
        {
            // 오른쪽 단추: 눌린 줄이 선택에 들어 있지 않으면 그 줄만 고른다 (탐색기와 같다).
            if (e.GetCurrentPoint(_list).Properties.IsRightButtonPressed
                && (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>()?.DataContext is FolderRow hit
                && !Selected.Contains(hit))
            {
                _list.UnselectAll();
                _list.SelectedItem = hit;
            }
            var x = e.GetPosition(_list).X;
            _activeLeft = x < _list.Bounds.Width / 2;
        }, RoutingStrategies.Tunnel);
        // 목록 자리에 폴더를 놓으면: 하나는 놓은 쪽(왼쪽·오른쪽 절반)에, 두 개는 양쪽에 (빈 창으로 열었을 때 고르는 길).
        DragDrop.SetAllowDrop(_list, true);
        _list.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            var n = DroppedPaths(e).Count;
            e.DragEffects = n is 1 or 2 ? DragDropEffects.Copy : DragDropEffects.None;
        });
        _list.AddHandler(DragDrop.DropEvent, OnListDrop);
        var rowMenu = new ContextMenu();
        rowMenu.Opening += (_, e) =>
        {
            if (!Selected.Any()) { e.Cancel = true; return; }
            RebuildRowMenu(rowMenu);
        };
        _list.ContextMenu = rowMenu;
        foreach (var box in new[] { _filterBox, _excludeBox })
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { ApplyFilter(); e.Handled = true; }
                else if (e.Key == Key.Escape) { _list.Focus(); e.Handled = true; }
            };
        _timer.Tick += (_, _) => { if (_dirty) { _dirty = false; Rebuild(); } };
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Strings.Changed += OnLanguageChanged;
        ActualThemeVariantChanged += (_, _) => Rebuild();
        Closed += (_, _) =>
        {
            Strings.Changed -= OnLanguageChanged;
            _timer.Stop();
            _cts?.Cancel();
        };
        Opened += (_, _) =>
        {
            _timer.Start();
            RecordHistory();
            _ = StartCompareAsync();
        };
        UpdateTitle();
        UpdateViewFilterButton();
    }

    void OnLanguageChanged() => Dispatcher.UIThread.Post(() =>
    {
        UpdateTitle();
        UpdateViewFilterButton();
        _filterBox.PlaceholderText = Strings.Current.FolderFilterPlaceholder;
        _excludeBox.PlaceholderText = Strings.Current.FolderFilterExcludePlaceholder;
        Rebuild();
    });

    /// <summary>최근 비교 목록에 기록한다. 압축 파일은 안쪽 경로(<c>a.zip!/</c>)가 아니라 압축 파일 경로로.</summary>
    void RecordHistory()
    {
        if (BothChosen) App.RecordHistory(Original(_leftRoot), Original(_rightRoot), folders: true);
    }

    /// <summary>양쪽 폴더가 모두 정해졌나 (홈 창에서 한쪽만 놓거나 빈 창으로 열면 아직 아니다).</summary>
    bool BothChosen => _leftRoot.Length > 0 && _rightRoot.Length > 0;

    static string Original(string root) => ArchivePaths.TryParse(root)?.Archive ?? root;

    void UpdateTitle()
    {
        _leftPath.Text = _leftRoot;
        _rightPath.Text = _rightRoot;
        static string Name(string root) => root.Length == 0 ? Strings.Current.HomeNotChosen : Locations.NameOf(root.Replace(ArchivePaths.Separator, "/"));
        Title = $"{Strings.Current.FolderWindowTitle(Name(_leftRoot), Name(_rightRoot))} — {AppIdentity.DisplayName}";
    }

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
        var commands = new Button { Classes = { "tool" }, Content = new DuoIcon { Icon = "view-list" } };
        ToolTip.SetTip(commands, s.CompareCommandsTooltip);
        commands.Click += (_, _) => CommandsMenu().ShowAt(commands);
        ToolTip.SetTip(_viewFilter, s.FolderViewFilterTooltip);

        // 툴바: 보기 필터 | 복사 | 이전·다음 차이 | 모두 펴기·접기 | 좌우 바꾸기 · 새로 고침 · 모든 명령 | 설정 · 정보 — BC 폴더 비교의 묶음 순서.
        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Tool("parent-folder", s.FolderUpLevel, () => _ = GoUpAsync()),
                Separator(),
                _viewFilter,
                Separator(),
                Tool("back", Tip(s.CompareCopyToLeft, "Ctrl+L"), () => _ = CopyAsync(toRight: false)),
                Tool("forward", Tip(s.CompareCopyToRight, "Ctrl+R"), () => _ = CopyAsync(toRight: true)),
                Separator(),
                Tool("up", Tip(s.ComparePrevLine, "Ctrl+P"), () => GoDifference(next: false)),
                Tool("down", Tip(s.CompareNextLine, "Ctrl+N"), () => GoDifference(next: true)),
                Separator(),
                Tool("expand-all", s.FolderExpandAll, () => ExpandAll(true)),
                Tool("collapse-all", s.FolderCollapseAll, () => ExpandAll(false)),
                Separator(),
                Tool("swap", Tip(s.CompareSwap, "Ctrl+U"), () => _ = SwapAsync()),
                Tool("refresh", Tip(s.FolderRefresh, "F5"), () => _ = StartCompareAsync()),
                commands,
                Separator(),
                Tool("settings", s.SettingsTooltip, () => SettingsWindow.Open(this)),
                Tool("info", s.AboutTooltip, () => AboutDialog.Open(this)),
            },
        };
        var appBar = new Border
        {
            Height = 44, Padding = new Thickness(14, 0), BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12, Children = { _status, tools } },
        };
        Grid.SetColumn(tools, 1);
        appBar.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurfaceHi"));
        appBar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStrokeStrong"));

        // 필터 막대 (BC Filters toolbar edit): 이름 필터를 BC 문법으로 적는다.
        _filterBox.PlaceholderText = s.FolderFilterPlaceholder;
        ToolTip.SetTip(_filterBox, s.FolderFilterTooltip);
        _excludeBox.PlaceholderText = s.FolderFilterExcludePlaceholder;
        ToolTip.SetTip(_excludeBox, s.FolderFilterExcludeTooltip);
        var importButton = Tool("import", s.FolderFilterImport, () => _ = ImportFilterAsync());
        var exportButton = Tool("export", s.FolderFilterExport, () => _ = ExportFilterAsync());
        var filterBar = new Border
        {
            Padding = new Thickness(12, 6), BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { new DuoIcon { Icon = "search", Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center }, FilterLabel("include"), _filterBox, FilterLabel("exclude"), _excludeBox, importButton, exportButton },
            },
        };
        filterBar.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurfaceHi"));
        filterBar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStrokeStrong"));

        var header = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = new Grid { ColumnDefinitions = new ColumnDefinitions($"*,{GutterWidth},*"), Children = { PathHeader(left: true), PathHeader(left: false) } } };
        header.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStrokeStrong"));
        header.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurfaceHi"));
        _list.Bind(ListBox.BackgroundProperty, this.GetResourceObservable("AppEditorBg")); // 목록 바탕 — 위의 틀 띠와 한 단 다르게
        _list.Bind(ListBox.FontSizeProperty, this.GetResourceObservable("FsBody"));
        _list.Bind(ListBox.FontFamilyProperty, this.GetResourceObservable("AppEditorFont")); // 파일 비교와 같은 편집기 글꼴 (고정폭)

        var grid = new Grid { RowDefinitions = new RowDefinitions("44,Auto,Auto,*") };
        Place(grid, appBar, 0);
        Place(grid, filterBar, 1);
        Place(grid, header, 2);
        Place(grid, _list, 3);
        _allHidden.Children.Add(_allHiddenText);
        _allHidden.Children.Add(_showAllButton);
        _allHiddenText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        _showAllButton.Click += (_, _) => SetShow(FolderShow.All);
        Place(grid, _allHidden, 3);
        grid.Children.Add(_dialogs);
        Grid.SetRowSpan(_dialogs, 4);
        return grid;
    }

    static TextBlock FilterLabel(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 };

    static void Place(Grid grid, Control c, int row)
    {
        Grid.SetRow(c, row);
        grid.Children.Add(c);
    }

    Control PathHeader(bool left)
    {
        var s = Strings.Current;
        var pick = UiScale.Enable(new Button { Classes = { "tool" }, Content = new DuoIcon { Icon = "folder-open" } });
        ToolTip.SetTip(pick, left ? s.FolderChooseLeftTitle : s.FolderChooseRightTitle);
        pick.Click += async (_, _) => await ChooseAsync(left);
        var text = left ? _leftPath : _rightPath;
        text.PlaceholderText = left ? s.FolderChooseLeftTitle : s.FolderChooseRightTitle;
        text.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await ApplyTypedPathAsync(left, text); }
            else if (e.Key == Key.Escape) { e.Handled = true; text.Text = left ? _leftRoot : _rightRoot; }
        };
        var icon = new DuoIcon { Icon = "folder", ColorKey = "AppIconFolder", Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8, Margin = new Thickness(12, 6), Children = { icon, text, pick } };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(pick, 2);
        // 폴더(또는 압축 파일)를 주소 창에 끌어다 놓으면 그 폴더를 연다. 드래그 이벤트는 올라오기(bubble)만 있고
        // 글 상자가 먼저 처리해 버리므로, 글 상자와 바깥 줄 모두에 "처리된 것도 받기"로 건다.
        static string? DroppedPath(DragEventArgs e) => e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath();
        void OnOver(object? _, DragEventArgs e)
        {
            e.DragEffects = DroppedPath(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
            e.Handled = true;
        }
        DragEventArgs? lastDrop = null; // 글 상자에서 올라온 같은 이벤트를 바깥 줄이 또 받지 않게
        async void OnDropped(object? _, DragEventArgs e)
        {
            e.Handled = true;
            if (ReferenceEquals(lastDrop, e)) return;
            lastDrop = e;
            if (DroppedPath(e) is not { } path) return;
            if (!await Task.Run(() => CanOpen(path))) return;
            PickLocations.Remember(path, isFolder: true);
            await ApplyRootAsync(left, path);
        }
        foreach (Control c in new Control[] { grid, text })
        {
            DragDrop.SetAllowDrop(c, true);
            c.AddHandler(DragDrop.DragOverEvent, OnOver, RoutingStrategies.Bubble, handledEventsToo: true);
            c.AddHandler(DragDrop.DropEvent, OnDropped, RoutingStrategies.Bubble, handledEventsToo: true);
        }
        if (!left) Grid.SetColumn(grid, 2);
        return grid;
    }

    // --- 줄 ---

    IBrush Brush(string key) =>
        Application.Current!.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.Gray;

    /// <summary>BC 글자색: 같음 = 보통, 다름 = 빨강(더 오래된 쪽은 흐리게), 한쪽에만 = 보라, 모름 = 회색.</summary>
    IBrush ColorFor(FolderNode n, bool left) => n.State switch
    {
        FolderState.Same => Brush("AppTextHi"),
        FolderState.Different => n.Newer == NewerSide.None || (n.Newer == NewerSide.Left) == left ? Brush("AppFolderDifferent") : Brush("AppTextMid"),
        FolderState.Error => Brush("AppFolderDifferent"),
        FolderState.LeftOrphan or FolderState.RightOrphan => Brush("AppFolderOrphan"),
        _ => Brush("AppFolderPending"),
    };

    /// <summary>두 패널 사이 상태 기호 칸의 폭 (머리글과 같아야 줄이 맞는다).</summary>
    const double GutterWidth = 28;

    Control BuildRow(FolderRow row)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"*,{GutterWidth},*"), Height = RowHeight };
        var left = Side(row, left: true);
        var right = Side(row, left: false);
        var gutter = Gutter(row.Node);
        Grid.SetColumn(gutter, 1);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(gutter);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>BC의 가운데 칸: 같음 <c>=</c>, 다름 <c>≠</c>(빨강), 비교 중 <c>…</c>, 읽기 실패 <c>!</c>. 한쪽에만 있는 줄은 비운다.</summary>
    Control Gutter(FolderNode n)
    {
        var (glyph, key) = n.State switch
        {
            FolderState.Same => ("=", "AppTextMid"),
            FolderState.Different => ("≠", "AppFolderDifferent"),
            FolderState.Error => ("!", "AppFolderDifferent"),
            FolderState.Pending => ("…", "AppFolderPending"),
            _ => ("", "AppTextMid"),
        };
        var host = new Border { BorderThickness = new Thickness(1, 0), IsHitTestVisible = false };
        host.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStroke"));
        host.Child = new TextBlock { Text = glyph, Foreground = Brush(key), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
        return host;
    }

    Control Side(FolderRow row, bool left)
    {
        var node = row.Node;
        var entry = left ? node.Left : node.Right;
        var host = new Border();
        if (entry is null)
        {
            host.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppDiffFiller")); // 이쪽에는 없다 (비교 창의 채움 줄과 같은 모양)
            return host;
        }
        var brush = ColorFor(node, left);
        var chevron = new DuoIcon { Icon = row.IsExpanded ? "caret-down" : "caret-right", Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsVisible = row.IsFolder };
        // 누르는 자리는 줄 높이 전체 × 24px (투명 배경이라 빈 곳도 눌린다) — 아이콘 크기보다 훨씬 넓게
        var chevronHit = new Border { Width = 24, Height = RowHeight, Background = Brushes.Transparent, Child = chevron };
        if (row.IsFolder) chevronHit.PointerPressed += (_, e) => { e.Handled = true; Toggle(node); };
        var icon = new DuoIcon { Icon = row.IsFolder ? (row.IsExpanded ? "folder-open" : "folder") : "file", ColorKey = row.IsFolder ? "AppIconFolder" : null, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        var lead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(row.Depth * 16, 0, 0, 0), Children = { chevronHit, icon } };
        var name = new TextBlock { Text = node.Name, Foreground = brush, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(name, node.Error is { } err ? $"{entry.Path}\n{err}" : entry.Path);
        var size = new TextBlock { Text = row.IsFolder ? "" : BytesFormat.Format(entry.SizeBytes), Foreground = brush, Width = 76, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var modified = new TextBlock { Text = entry.ModifiedAt is { } m ? m.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "", Foreground = brush, Width = 128, VerticalAlignment = VerticalAlignment.Center };
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 8, Margin = new Thickness(0, 0, 8, 0), Children = { lead, name, size, modified } };
        Grid.SetColumn(name, 1);
        Grid.SetColumn(size, 2);
        Grid.SetColumn(modified, 3);
        host.Child = g;
        return host;
    }

    // --- 비교 ---

    Task? _resolveTask;

    /// <summary>양쪽 경로가 폴더인지 압축 파일인지 백그라운드에서 한 번만 정한다 (디스크를 만지므로 UI 스레드에서 하지 않는다).</summary>
    Task ResolveOnce() => _resolveTask ??= ResolveAsync();

    async Task ResolveAsync()
    {
        var (left, right) = await Task.Run(() => (Resolve(_leftRoot), Resolve(_rightRoot)));
        (_leftFs, _leftRoot) = left;
        (_rightFs, _rightRoot) = right;
        UpdateTitle();
    }

    async Task StartCompareAsync()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        await ResolveOnce();
        if (!BothChosen)
        {
            // 한쪽이 비어 있으면 비교하지 않고 고르라고 알린다 (목록은 비운다).
            _comparer = null;
            _rows = [];
            _list.ItemsSource = _rows;
            _allHiddenText.Text = Strings.Current.FolderChooseBoth; // 목록 자리 가운데에도 안내한다
            _showAllButton.IsVisible = false;
            _allHidden.IsVisible = true;
            _status.Text = Strings.Current.FolderChooseBoth;
            return;
        }
        var policy = await App.Policy.LoadAsync();
        if (cts.IsCancellationRequested) return;
        var comparer = _comparer = new FolderComparer(_leftFs, _leftRoot, _rightFs, _rightRoot, policy, PolicyStore.NamesIgnoreCase, _filter);
        comparer.Changed += _ => _dirty = true;
        _notice = null;
        _status.Text = Strings.Current.FolderLoading;
        _dirty = true;
        try
        {
            await comparer.RunAsync(cts.Token);
        }
        catch (OperationCanceledException) { return; }
        if (!ReferenceEquals(comparer, _comparer)) return;
        _dirty = false;
        Rebuild();
    }

    /// <summary>지금 결과로 줄 목록·상태 줄을 다시 만든다 (선택·스크롤 위치는 지킨다).</summary>
    void Rebuild()
    {
        if (_comparer is not { } c) return;
        List<FolderRow> rows;
        (int Same, int Different, int LeftOrphan, int RightOrphan, int Pending) count;
        lock (c.Gate)
        {
            rows = FolderView.Flatten(c.Root, _show, _alwaysFolders, _expanded);
            count = FolderView.Count(c.Root);
        }
        var selected = _list.SelectedItems?.OfType<FolderRow>().Select(r => r.Node.RelativePath).ToHashSet() ?? [];
        var scroll = _list.Scroll as ScrollViewer;
        var offset = scroll?.Offset ?? default;
        _rows = rows;
        _list.ItemsSource = rows;
        // 항목은 있는데 필터가 전부 숨겼다면 이유와 [모두 보기]를 보인다.
        var anyItems = count.Same + count.Different + count.LeftOrphan + count.RightOrphan + count.Pending > 0;
        _allHidden.IsVisible = rows.Count == 0 && anyItems && _show != FolderShow.All;
        _allHiddenText.Text = Strings.Current.FolderAllHidden;
        _showAllButton.Content = Strings.Current.FolderShowName("all");
        _showAllButton.IsVisible = true;
        if (selected.Count > 0)
            foreach (var r in rows.Where(r => selected.Contains(r.Node.RelativePath)))
                _list.SelectedItems?.Add(r);
        if (scroll is not null) Dispatcher.UIThread.Post(() => scroll.Offset = offset, DispatcherPriority.Background);
        UpdateStatus(count, c.Completed);
    }

    void UpdateStatus((int Same, int Different, int LeftOrphan, int RightOrphan, int Pending) c, bool completed)
    {
        if (_notice is { } n) { _status.Text = n; return; }
        var s = Strings.Current;
        _status.Text = s.FolderStatus(c.Same, c.Different, c.LeftOrphan, c.RightOrphan) + (completed || c.Pending == 0 ? "" : s.FolderStatusPending(c.Pending));
    }

    void Notice(string text)
    {
        _notice = text;
        _status.Text = text;
        DispatcherTimer.RunOnce(() =>
        {
            if (_notice == text) { _notice = null; Rebuild(); }
        }, TimeSpan.FromSeconds(4));
    }

    // --- 보기 필터 (BC View) ---

    void SetShow(FolderShow show)
    {
        _show = show;
        App.Settings.Update(x => x with { FolderShowFlags = (int)show });
        UpdateViewFilterButton();
        Rebuild();
    }

    void UpdateViewFilterButton()
    {
        var s = Strings.Current;
        // BC 이름이 없는 조합은 "사용자 지정 (켜진 토글 수/6)"로 — 얼마나 걸러지고 있는지 단추만 봐도 알 수 있게.
        _viewFilter.Content = FolderShowPresets.IdOf(_show) is { } id
            ? s.FolderShowName(id)
            : $"{s.FolderCustomFilter} ({System.Numerics.BitOperations.PopCount((uint)(_show & FolderShow.All))}/6)";
        var flyout = new MenuFlyout();
        foreach (var item in ViewFilterItems()) flyout.Items.Add(item);
        _viewFilter.Flyout = flyout;
    }

    /// <summary>BC 보기 필터 이름들(라디오) + 여섯 토글(체크) + 폴더 항상 보이기. 툴바 드롭다운과 모든 명령 메뉴의 보기 묶음이 같이 쓴다.</summary>
    IEnumerable<Control> ViewFilterItems()
    {
        var s = Strings.Current;
        foreach (var (id, show) in FolderShowPresets.All)
        {
            var item = new MenuItem { Header = s.FolderShowName(id), ToggleType = MenuItemToggleType.Radio, IsChecked = _show == show, GroupName = "folder-show" };
            var target = show;
            item.Click += (_, _) => SetShow(target);
            yield return item;
        }
        yield return new Separator();
        var toggles = new MenuItem { Header = s.FolderToggles };
        foreach (var (flag, name) in new[]
        {
            (FolderShow.Same, s.FolderToggleSame), (FolderShow.Different, s.FolderToggleDifferent),
            (FolderShow.LeftNewer, s.FolderToggleLeftNewer), (FolderShow.RightNewer, s.FolderToggleRightNewer),
            (FolderShow.LeftOrphan, s.FolderToggleLeftOrphan), (FolderShow.RightOrphan, s.FolderToggleRightOrphan),
        })
        {
            var item = new MenuItem { Header = name, ToggleType = MenuItemToggleType.CheckBox, IsChecked = (_show & flag) != 0 };
            var f = flag;
            item.Click += (_, _) =>
            {
                var next = _show ^ f;
                if ((next & FolderShow.All) == FolderShow.None)
                {
                    // 모두 끄면 아무것도 안 보인다 — 막고 안내한다 (메뉴의 체크 표시도 되돌린다).
                    Notice(Strings.Current.FolderFilterKeepOne);
                    UpdateViewFilterButton();
                    return;
                }
                SetShow(next);
            };
            toggles.Items.Add(item);
        }
        yield return toggles;
        var always = new MenuItem { Header = s.FolderAlwaysShowFolders, ToggleType = MenuItemToggleType.CheckBox, IsChecked = _alwaysFolders };
        always.Click += (_, _) =>
        {
            _alwaysFolders = !_alwaysFolders;
            App.Settings.Update(x => x with { FolderAlwaysShowFolders = _alwaysFolders });
            UpdateViewFilterButton();
            Rebuild();
        };
        yield return always;
    }

    // --- 모든 명령 (BC의 메뉴: Session · Actions · Edit · Search · View) ---

    MenuFlyout CommandsMenu()
    {
        var s = Strings.Current;
        MenuItem Item(string header, string? gesture, Action run)
        {
            var item = new MenuItem { Header = header };
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
        var flyout = new MenuFlyout();
        flyout.Items.Add(Group(s.CompareMenuSession,
            Item(s.FolderUpLevel, null, () => _ = GoUpAsync()),
            Item(s.CompareSwap, "Ctrl+U", () => _ = SwapAsync()),
            Item(s.FolderRefresh, "F5", () => _ = StartCompareAsync())));
        flyout.Items.Add(Group(s.FolderMenuActions,
            Item(s.FolderOpen, "Enter", OpenSelected),
            new Separator(),
            Item(s.CompareCopyToRight, "Ctrl+R", () => _ = CopyAsync(toRight: true)),
            Item(s.CompareCopyToLeft, "Ctrl+L", () => _ = CopyAsync(toRight: false)),
            new Separator(),
            Item(s.FolderDeleteLeft, null, () => _ = DeleteAsync(onLeft: true)),
            Item(s.FolderDeleteRight, null, () => _ = DeleteAsync(onLeft: false))));
        flyout.Items.Add(Group(s.CompareMenuEdit,
            Item(s.FolderExpandAll, null, () => ExpandAll(true)),
            Item(s.FolderCollapseAll, null, () => ExpandAll(false)),
            new Separator(),
            Item(s.FolderSelectAll, "Ctrl+A", () => _list.SelectAll()),
            Group(s.FolderSelectNewer,
                Item(s.FolderSideLeft, null, () => SelectWhere(r => r.Node.Newer == NewerSide.Left)),
                Item(s.FolderSideRight, null, () => SelectWhere(r => r.Node.Newer == NewerSide.Right)),
                Item(s.FolderSideBoth, null, () => SelectWhere(r => r.Node.Newer != NewerSide.None))),
            Group(s.FolderSelectOrphans,
                Item(s.FolderSideLeft, null, () => SelectWhere(r => r.Node.State == FolderState.LeftOrphan)),
                Item(s.FolderSideRight, null, () => SelectWhere(r => r.Node.State == FolderState.RightOrphan)),
                Item(s.FolderSideBoth, null, () => SelectWhere(r => r.Node.State is FolderState.LeftOrphan or FolderState.RightOrphan))),
            Item(s.FolderInvertSelection, null, InvertSelection)));
        flyout.Items.Add(Group(s.CompareMenuSearch,
            Item(s.CompareNextLine, "Ctrl+N", () => GoDifference(next: true)),
            Item(s.ComparePrevLine, "Ctrl+P", () => GoDifference(next: false))));
        flyout.Items.Add(Group(s.CompareMenuView, [.. ViewFilterItems()]));
        return flyout;
    }

    // --- 줄의 오른쪽 단추 메뉴 ---

    void RebuildRowMenu(ContextMenu menu)
    {
        var s = Strings.Current;
        MenuItem Item(string header, Action run)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => run();
            return item;
        }
        menu.Items.Clear();
        menu.Items.Add(Item(s.FolderCompareNow, () => _ = CompareNowAsync()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(s.FolderFilterExcludeItem, () => AddToFilter(_excludeBox)));
        menu.Items.Add(Item(s.FolderFilterIncludeItem, () => AddToFilter(_filterBox)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(s.CompareCopyToRight, () => _ = CopyAsync(toRight: true)));
        menu.Items.Add(Item(s.CompareCopyToLeft, () => _ = CopyAsync(toRight: false)));
    }

    /// <summary>지금 비교: 파일 짝은 파일 비교 창으로, 폴더 짝은 그 폴더를 루트로 삼아 이 창에서 다시 비교한다.</summary>
    async Task CompareNowAsync()
    {
        if (Selected.FirstOrDefault() is not { } row) return;
        var node = row.Node;
        if (node.Left is null || node.Right is null) { Notice(Strings.Current.FolderCompareNeedBoth); return; }
        if (!node.IsDirectory) { CompareWindow.Open(this, node.Left.Path, node.Right.Path, recordHistory: false); return; }
        await ResolveOnce();
        _leftRoot = node.Left.Path;
        _rightRoot = node.Right.Path;
        _expanded.Clear();
        UpdateTitle();
        RecordHistory();
        await StartCompareAsync();
    }

    /// <summary>고른 줄의 이름을 필터 칸에 더하고 바로 적용한다 (폴더는 <c>이름\</c>, 파일은 이름 그대로 — BC 문법).</summary>
    void AddToFilter(TextBox box)
    {
        var masks = Selected.Select(r => r.Node).Where(n => n.Name.Length > 0)
            .Select(n => n.IsDirectory ? n.Name + "\\" : n.Name).Distinct();
        var existing = (box.Text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        foreach (var m in masks) if (!existing.Contains(m, StringComparer.OrdinalIgnoreCase)) existing.Add(m);
        box.Text = string.Join(';', existing);
        ApplyFilter();
    }

    // --- 선택·이동·펼치기 ---

    IEnumerable<FolderRow> Selected => _list.SelectedItems?.OfType<FolderRow>() ?? [];

    void SelectWhere(Func<FolderRow, bool> predicate)
    {
        _list.UnselectAll();
        foreach (var r in _rows.Where(predicate)) _list.SelectedItems?.Add(r);
    }

    void InvertSelection()
    {
        var current = Selected.Select(r => r.Node.RelativePath).ToHashSet();
        SelectWhere(r => !current.Contains(r.Node.RelativePath));
    }

    void Toggle(FolderNode folder)
    {
        var open = _rows.FirstOrDefault(r => ReferenceEquals(r.Node, folder))?.IsExpanded ?? false;
        _expanded[folder.RelativePath] = !open;
        Rebuild();
    }

    void ExpandAll(bool open)
    {
        if (_comparer is not { } c) return;
        lock (c.Gate)
            foreach (var f in c.Root.Descendants().Where(n => n.IsDirectory)) _expanded[f.RelativePath] = open;
        Rebuild();
    }

    /// <summary>BC Next / Previous Difference (Ctrl+N / Ctrl+P): 지금 줄 다음(앞)의 같지 않은 파일 줄로.</summary>
    void GoDifference(bool next)
    {
        var from = _list.SelectedIndex;
        var range = next ? Enumerable.Range(from + 1, Math.Max(0, _rows.Count - from - 1)) : Enumerable.Range(0, Math.Max(0, from)).Reverse();
        foreach (var i in range)
        {
            if (_rows[i].IsFolder || _rows[i].Node.State == FolderState.Same) continue;
            _list.SelectedIndex = i;
            _list.ScrollIntoView(i);
            _list.Focus();
            return;
        }
    }

    /// <summary>Enter·더블클릭: 파일 짝은 텍스트 비교 창으로, 폴더는 펴고 접는다.</summary>
    void OpenSelected()
    {
        if (Selected.FirstOrDefault() is not { } row) return;
        if (row.IsFolder) { Toggle(row.Node); return; }
        CompareWindow.Open(this, row.Node.Left?.Path, row.Node.Right?.Path, recordHistory: false);
    }

    // --- 복사·삭제 (BC Copy to Right / Left, Delete) ---

    bool InArchive => ArchivePaths.IsInArchive(_leftRoot) || ArchivePaths.IsInArchive(_rightRoot);

    async Task CopyAsync(bool toRight)
    {
        var s = Strings.Current;
        if (InArchive) { Notice(s.FolderArchiveReadOnly); return; }
        List<CopyItem> plan;
        lock (_comparer?.Gate ?? this)
            plan = FolderOps.PlanCopy(Selected.Select(r => r.Node), toRight, _leftRoot, _rightRoot);
        if (plan.Count == 0) { Notice(s.FolderNothingSelected); return; }
        var target = toRight ? _rightRoot : _leftRoot;
        var choice = await _dialogs.ShowAsync(s.FolderCopyTitle(toRight), s.FolderCopyBody(plan.Count, plan.Count(i => i.Overwrites), target),
            [s.CommonCancel, s.FolderCopyAction], primaryIndex: 1, cancelIndex: 0);
        if (choice != 1) return;

        var failures = new List<OperationFailure>();
        var copied = 0;
        foreach (var group in plan.GroupBy(i => i.TargetDir))
        {
            try { Directory.CreateDirectory(group.Key); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failures.AddRange(group.Select(i => new OperationFailure(i.Source, FileErrors.From(e, group.Key))));
                continue;
            }
            // 확인한 덮어쓰기는 모두 허용한다 (위 대화상자가 몇 개를 덮어쓰는지 알렸다).
            var result = await Ops.CopyAsync([.. group.Select(i => i.Source)], group.Key,
                (_, _, _) => Task.FromResult(new ConflictDecision(ConflictChoice.Overwrite, ApplyToAll: true)));
            copied += result.Processed;
            failures.AddRange(result.Failures);
        }
        await StartCompareAsync();
        Notice(failures.Count == 0 ? s.FolderCopied(copied) : s.FolderOperationFailed(failures.Count, failures[0].Error.ToString()));
    }

    async Task DeleteAsync(bool onLeft)
    {
        var s = Strings.Current;
        if (InArchive) { Notice(s.FolderArchiveReadOnly); return; }
        List<string> paths;
        lock (_comparer?.Gate ?? this)
            paths = FolderOps.PlanDelete(Selected.Select(r => r.Node), onLeft);
        if (paths.Count == 0) { Notice(s.FolderNothingSelected); return; }
        var toTrash = Ops.CanTrash;
        var side = onLeft ? s.FolderSideLeft : s.FolderSideRight;
        var body = s.FolderDeleteBody(paths.Count, toTrash) + (toTrash ? "" : "\n" + s.FolderDeletePermanent);
        var choice = await _dialogs.ShowAsync(s.FolderDeleteTitle(side), body, [s.CommonCancel, s.FolderDeleteAction], primaryIndex: 1, cancelIndex: 0, danger: !toTrash);
        if (choice != 1) return;
        var result = await Ops.DeleteAsync(paths, toTrash);
        await StartCompareAsync();
        Notice(result.Failures.Count == 0 ? s.FolderDeleted(result.Processed) : s.FolderOperationFailed(result.Failures.Count, result.Failures[0].Error.ToString()));
    }

    // --- 필터·폴더 바꾸기 ---

    void ApplyFilter()
    {
        _filter = CurrentFilter.ToMask();
        _ = StartCompareAsync();
        _list.Focus();
    }

    FilterSpec CurrentFilter => new(_filterBox.Text ?? "", _excludeBox.Text ?? "");

    /// <summary>필터(포함·제외)를 평문 파일(.dcfilter)로 저장한다.</summary>
    async Task ExportFilterAsync()
    {
        var s = Strings.Current;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = s.FolderFilterExport,
            SuggestedFileName = "filter",
            DefaultExtension = "dcfilter",
            FileTypeChoices = [new FilePickerFileType(s.FolderFilterFileType) { Patterns = ["*.dcfilter"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        var text = CurrentFilter.Format();
        try { await Task.Run(() => File.WriteAllText(path, text)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Notice(e.Message); }
    }

    /// <summary>파일에서 필터를 읽어 두 칸에 채우고 바로 적용한다.</summary>
    async Task ImportFilterAsync()
    {
        var s = Strings.Current;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = s.FolderFilterImport,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(s.FolderFilterFileType) { Patterns = ["*.dcfilter"] }, FilePickerFileTypes.All],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        FilterSpec? spec;
        try { spec = await Task.Run(() => FilterSpec.Parse(File.ReadAllText(path))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Notice(e.Message); return; }
        if (spec is null) { Notice(s.FolderFilterImportFailed); return; }
        (_filterBox.Text, _excludeBox.Text) = (spec.Include, spec.Exclude);
        ApplyFilter();
    }

    async Task SwapAsync()
    {
        await ResolveOnce();
        (_leftRoot, _rightRoot) = (_rightRoot, _leftRoot);
        (_leftFs, _rightFs) = (_rightFs, _leftFs);
        UpdateTitle();
        RecordHistory();
        await StartCompareAsync();
    }

    /// <summary>한 칸 위로: 양쪽 루트를 각각 부모 폴더로 (압축 파일 안이면 안쪽 부모, 압축 파일 루트면 그것이 든 폴더). 한쪽이라도 더 못 올라가면 그대로 둔다.</summary>
    async Task GoUpAsync()
    {
        await ResolveOnce();
        var left = ParentOf(_leftRoot);
        var right = ParentOf(_rightRoot);
        if ((_leftRoot.Length > 0 && left is null) || (_rightRoot.Length > 0 && right is null))
        {
            Notice(Strings.Current.FolderCannotGoUp);
            return;
        }
        if (left is not null) (_leftFs, _leftRoot) = (FsFor(left), left);
        if (right is not null) (_rightFs, _rightRoot) = (FsFor(right), right);
        _expanded.Clear();
        UpdateTitle();
        RecordHistory();
        await StartCompareAsync();
    }

    /// <summary>부모 경로 (문자열만 다룬다 — 디스크를 만지지 않는다). 비어 있거나 더 올라갈 곳이 없으면 null.</summary>
    static string? ParentOf(string root)
    {
        if (root.Length == 0) return null;
        if (ArchivePaths.TryParse(root) is { } loc)
            return ArchivePaths.ParentInner(loc.Inner) is { } up
                ? ArchivePaths.Format(loc.Archive, up)
                : Path.GetDirectoryName(loc.Archive) is { Length: > 0 } dir ? dir : null;
        var trimmed = root.Length > 3 ? root.TrimEnd('\\', '/') : root;
        return Path.GetDirectoryName(trimmed) is { Length: > 0 } parent ? parent : null;
    }

    IFileSystem FsFor(string root) =>
        ArchivePaths.TryParse(root) is { } loc
            ? (ArchivePaths.IsInArchive(_leftRoot) && ArchivePaths.TryParse(_leftRoot)!.Archive == loc.Archive ? _leftFs
                : ArchivePaths.IsInArchive(_rightRoot) && ArchivePaths.TryParse(_rightRoot)!.Archive == loc.Archive ? _rightFs
                : new ArchiveFileSystem(loc.Archive))
            : LocalFileSystem.Instance;

    async Task ChooseAsync(bool left)
    {
        var s = Strings.Current;
        var start = await CurrentFolderAsync(left ? _leftRoot : _rightRoot) ?? await PickLocations.StartAsync(StorageProvider);
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = left ? s.FolderChooseLeftTitle : s.FolderChooseRightTitle,
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path) return;
        PickLocations.Remember(path, isFolder: true);
        await ApplyRootAsync(left, path);
    }

    /// <summary>고르기 창이 현재 주소창의 폴더에서 시작하게 한다 (압축 파일이면 그것이 든 폴더). 없으면 null.</summary>
    async Task<IStorageFolder?> CurrentFolderAsync(string root)
    {
        var path = Original(root);
        var dir = await Task.Run(() => Directory.Exists(path) ? path : Path.GetDirectoryName(path) is { Length: > 0 } d && Directory.Exists(d) ? d : null);
        if (dir is null) return null;
        try { return await StorageProvider.TryGetFolderFromPathAsync(dir); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    /// <summary>주소 창에 직접 쓴 경로를 적용한다. 폴더(또는 압축 파일)가 아니면 원래 경로로 되돌린다.</summary>
    async Task ApplyTypedPathAsync(bool left, TextBox box)
    {
        var path = (box.Text ?? "").Trim().Trim('"');
        var current = left ? _leftRoot : _rightRoot;
        if (path.Length == 0 || path == current || !await Task.Run(() => CanOpen(path))) { box.Text = current; return; }
        PickLocations.Remember(path, isFolder: true);
        await ApplyRootAsync(left, path);
    }

    static List<string> DroppedPaths(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];

    async void OnListDrop(object? sender, DragEventArgs e)
    {
        var paths = DroppedPaths(e);
        if (paths.Count is not (1 or 2)) return;
        var onLeft = e.GetPosition(_list).X < _list.Bounds.Width / 2;
        // 폴더인지 압축 파일인지는 디스크를 만져야 안다 — 백그라운드에서.
        if (!await Task.Run(() => paths.All(CanOpen))) { Notice(Strings.Current.FolderDropFoldersOnly); return; }
        foreach (var p in paths) PickLocations.Remember(p, isFolder: true);
        if (paths.Count == 1) { await ApplyRootAsync(onLeft, paths[0]); return; }
        await ResolveOnce();
        var (l, r) = await Task.Run(() => (Resolve(paths[0]), Resolve(paths[1])));
        ((_leftFs, _leftRoot), (_rightFs, _rightRoot)) = (l, r);
        _expanded.Clear();
        UpdateTitle();
        RecordHistory();
        await StartCompareAsync();
    }

    async Task ApplyRootAsync(bool left, string path)
    {
        await ResolveOnce();
        var (fs, root) = await Task.Run(() => Resolve(path));
        if (left) (_leftFs, _leftRoot) = (fs, root); else (_rightFs, _rightRoot) = (fs, root);
        _expanded.Clear();
        UpdateTitle();
        RecordHistory();
        await StartCompareAsync();
    }

    // --- 단축키: Beyond Compare 기본 키 (BC_COMPAT.md §2) ---

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_dialogs.IsOpen || _filterBox.IsFocused || _excludeBox.IsFocused) return;
        // 주소 창에 쓰는 중이면 Enter·Delete·Ctrl+A 등은 글 상자의 것이다 (터널로 먼저 받아 가로채지 않는다).
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox) return;
        var primary = Mods.Primary(e.KeyModifiers);
        var handled = true;
        switch (e.Key)
        {
            case Key.R when primary: _ = CopyAsync(toRight: true); break;
            case Key.L when primary: _ = CopyAsync(toRight: false); break;
            case Key.N when primary: GoDifference(next: true); break;
            case Key.P when primary: GoDifference(next: false); break;
            case Key.A when primary: _list.SelectAll(); break;
            case Key.F5: _ = StartCompareAsync(); break;
            case Key.U when primary: _ = SwapAsync(); break;
            case Key.Enter: OpenSelected(); break;
            case Key.Delete: _ = DeleteAsync(_activeLeft); break;
            case Key.Right when !primary && Selected.FirstOrDefault() is { IsFolder: true, IsExpanded: false } r: Toggle(r.Node); break;
            case Key.Left when !primary && Selected.FirstOrDefault() is { IsFolder: true, IsExpanded: true } r: Toggle(r.Node); break;
            case Key.W when primary: Close(); break;
            case Key.OemComma when primary: SettingsWindow.Open(this); break;
            default: handled = false; break;
        }
        e.Handled = handled;
    }
}
