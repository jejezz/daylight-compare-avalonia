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
    readonly TextBox _filterBox = new() { MinWidth = 320, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _leftPath = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.PathSegmentEllipsis };
    readonly TextBlock _rightPath = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.PathSegmentEllipsis };
    readonly DropDownButton _viewFilter = new() { VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(10, 4) };
    readonly DialogHost _dialogs = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };

    public string LeftRoot => _leftRoot;
    public string RightRoot => _rightRoot;

    /// <summary>한쪽 경로가 폴더(또는 압축 파일)인가.</summary>
    public static bool CanOpen(string path) => Directory.Exists(path) || (File.Exists(path) && ArchivePaths.IsArchiveName(path));

    static (IFileSystem Fs, string Root) Resolve(string path) =>
        !Directory.Exists(path) && ArchivePaths.IsArchiveName(path)
            ? (new ArchiveFileSystem(path), ArchivePaths.Format(path))
            : (LocalFileSystem.Instance, path);

    public static FolderCompareWindow Open(Window? near, string left, string right)
    {
        var w = new FolderCompareWindow(left, right);
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
        _show = (FolderShow)settings.FolderShowFlags & FolderShow.All;
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
            var x = e.GetPosition(_list).X;
            _activeLeft = x < _list.Bounds.Width / 2;
        }, RoutingStrategies.Tunnel);
        _filterBox.KeyDown += (_, e) =>
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
        Rebuild();
    });

    /// <summary>최근 비교 목록에 기록한다. 압축 파일은 안쪽 경로(<c>a.zip!/</c>)가 아니라 압축 파일 경로로.</summary>
    void RecordHistory() => App.RecordHistory(Original(_leftRoot), Original(_rightRoot), folders: true);

    static string Original(string root) => ArchivePaths.TryParse(root)?.Archive ?? root;

    void UpdateTitle()
    {
        _leftPath.Text = _leftRoot;
        _rightPath.Text = _rightRoot;
        Title = $"{Strings.Current.FolderWindowTitle(Locations.NameOf(_leftRoot.Replace(ArchivePaths.Separator, "/")), Locations.NameOf(_rightRoot.Replace(ArchivePaths.Separator, "/")))} — {AppIdentity.DisplayName}";
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

        // 툴바: 보기 필터 | 복사 | 이전·다음 차이 | 좌우 바꾸기 · 새로 고침 · 모든 명령 | 설정 · 정보 — BC 폴더 비교의 묶음 순서.
        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _viewFilter,
                Separator(),
                Tool("back", Tip(s.CompareCopyToLeft, "Ctrl+L"), () => _ = CopyAsync(toRight: false)),
                Tool("forward", Tip(s.CompareCopyToRight, "Ctrl+R"), () => _ = CopyAsync(toRight: true)),
                Separator(),
                Tool("up", Tip(s.ComparePrevLine, "Ctrl+P"), () => GoDifference(next: false)),
                Tool("down", Tip(s.CompareNextLine, "Ctrl+N"), () => GoDifference(next: true)),
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
        var filterBar = new Border
        {
            Padding = new Thickness(12, 6), BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new DuoIcon { Icon = "search", Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center }, _filterBox } },
        };
        filterBar.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurfaceHi"));
        filterBar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStrokeStrong"));

        var header = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = new Grid { ColumnDefinitions = new ColumnDefinitions("*,1,*"), Children = { PathHeader(left: true), PathHeader(left: false) } } };
        header.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStrokeStrong"));
        header.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurfaceHi"));
        _list.Bind(ListBox.BackgroundProperty, this.GetResourceObservable("AppEditorBg")); // 목록 바탕 — 위의 틀 띠와 한 단 다르게
        _list.Bind(ListBox.FontSizeProperty, this.GetResourceObservable("FsBody"));

        var grid = new Grid { RowDefinitions = new RowDefinitions("44,Auto,Auto,*") };
        Place(grid, appBar, 0);
        Place(grid, filterBar, 1);
        Place(grid, header, 2);
        Place(grid, _list, 3);
        grid.Children.Add(_dialogs);
        Grid.SetRowSpan(_dialogs, 4);
        return grid;
    }

    static void Place(Grid grid, Control c, int row)
    {
        Grid.SetRow(c, row);
        grid.Children.Add(c);
    }

    Control PathHeader(bool left)
    {
        var s = Strings.Current;
        var pick = UiScale.Enable(new Button { Content = s.HomeChooseFolder, Padding = new Thickness(12, 4) });
        pick.Click += async (_, _) => await ChooseAsync(left);
        var text = left ? _leftPath : _rightPath;
        var icon = new DuoIcon { Icon = "folder", ColorKey = "AppIconFolder", Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8, Margin = new Thickness(12, 6), Children = { icon, text, pick } };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(pick, 2);
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

    Control BuildRow(FolderRow row)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,1,*"), Height = RowHeight };
        var divider = new Border();
        divider.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppStroke"));
        Grid.SetColumn(divider, 1);
        var left = Side(row, left: true);
        var right = Side(row, left: false);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(divider);
        grid.Children.Add(right);
        return grid;
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
        var chevron = new DuoIcon { Icon = row.IsExpanded ? "caret-down" : "caret-right", Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center, IsVisible = row.IsFolder };
        chevron.PointerPressed += (_, e) => { e.Handled = true; Toggle(node); };
        var icon = new DuoIcon { Icon = row.IsFolder ? (row.IsExpanded ? "folder-open" : "folder") : "file", ColorKey = row.IsFolder ? "AppIconFolder" : null, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        var lead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(row.Depth * 16 + 6, 0, 0, 0), Children = { new Border { Width = 12, Child = chevron }, icon } };
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
        _viewFilter.Content = FolderShowPresets.IdOf(_show) is { } id ? s.FolderShowName(id) : s.FolderCustomFilter;
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
            item.Click += (_, _) => SetShow(_show ^ f);
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
        CompareWindow.Open(this, row.Node.Left?.Path, row.Node.Right?.Path);
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
        _filter = new FileMask(_filterBox.Text ?? "");
        _ = StartCompareAsync();
        _list.Focus();
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

    async Task ChooseAsync(bool left)
    {
        var s = Strings.Current;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = left ? s.FolderChooseLeftTitle : s.FolderChooseRightTitle,
            AllowMultiple = false,
            SuggestedStartLocation = await PickLocations.StartAsync(StorageProvider),
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path) return;
        PickLocations.Remember(path, isFolder: true);
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
        if (_dialogs.IsOpen || _filterBox.IsFocused) return;
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
