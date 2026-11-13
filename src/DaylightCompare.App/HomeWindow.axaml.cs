using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DaylightCompare.App.Compare;
using DaylightCompare.App.Dialogs;
using DaylightCompare.App.Icons;
using DaylightCompare.App.Views;
using DaylightCompare.Domain;
using DaylightCompare.Domain.Drop;
using DaylightCompare.Domain.Settings;
using DaylightCompare.Domain.Window;
using DaylightCompare.Services;

namespace DaylightCompare.App;

/// <summary>
/// 홈 창: 새 비교(왼쪽·오른쪽 고르기, 끌어다 놓기). 저장한 세션·최근 목록은 4단계 (ROADMAP §6).
/// 창 위치·크기는 window-placement.md 규칙으로 기억한다 (Daylight Commander의 MainWindow에서 가져옴).
/// </summary>
public partial class HomeWindow : Window
{
    public HomeWindow()
    {
        InitializeComponent();
        Title = AppIdentity.DisplayName;
        AppTitle.Text = AppIdentity.DisplayName;
        RestorePlacement();

        PositionChanged += (_, _) => SavePlacement();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ClientSizeProperty || e.Property == WindowStateProperty) SavePlacement();
        };
        Closing += (_, _) => App.Settings.Flush();
        Opened += async (_, _) =>
        {
            // 복원하는 동안의 크기 변화가 저장되지 않게, 열린 뒤에 추적을 시작한다.
            await Task.Delay(300);
            _trackPlacement = true;
        };

        SettingsButton.Click += (_, _) => SettingsWindow.Open(this);
        AboutButton.Click += (_, _) => AboutDialog.Open(this);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        PickLeft.Click += async (_, _) => await PickAsync(left: true);
        PickRight.Click += async (_, _) => await PickAsync(left: false);
        PickLeftFolder.Click += async (_, _) => await PickFolderAsync(left: true);
        PickRightFolder.Click += async (_, _) => await PickFolderAsync(left: false);
        CompareButton.Click += async (_, _) => await OpenCompareAsync();
        PasteCompareButton.Click += (_, _) => CompareWindow.Open(this, _left, _right);
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            var ok = ItemsOf(e).Count is > 0 and <= 3;
            e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            DropHint.IsVisible = ok;
        });
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropHint.IsVisible = false);
        AddHandler(DragDrop.DropEvent, OnDrop);
        ClearHistoryButton.Click += (_, _) => App.Settings.Update(s => s with { CompareHistory = [] });
        Strings.Changed += RefreshPicks;
        Strings.Changed += RefreshHistoryOnUi;
        App.Settings.Changed += OnSettingsChanged;
        Closed += (_, _) =>
        {
            Strings.Changed -= RefreshPicks;
            Strings.Changed -= RefreshHistoryOnUi;
            App.Settings.Changed -= OnSettingsChanged;
        };
        RefreshPicks();
        RefreshHistory();
    }

    // --- 최근 비교 (ROADMAP §6-1: 같은 쌍을 매번 다시 고르지 않게) ---

    /// <summary>화면에 올린 목록 (설정이 바뀔 때 이 목록이 바뀐 경우에만 다시 그린다 — 창 위치 같은 다른 설정에는 반응하지 않는다).</summary>
    IReadOnlyList<CompareHistoryEntry>? _historyShown;
    int _historyVersion;

    void OnSettingsChanged(AppSettings s)
    {
        if (!ReferenceEquals(s.CompareHistory, _historyShown)) Avalonia.Threading.Dispatcher.UIThread.Post(RefreshHistory);
    }

    void RefreshHistoryOnUi() => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshHistory);

    void RefreshHistory()
    {
        var all = App.Settings.Value.CompareHistory;
        _historyShown = all;
        HistorySection.IsVisible = all.Count > 0;
        HistoryList.Children.Clear();
        HistoryNotice.Text = "";
        var rows = new List<(CompareHistoryEntry Entry, Control Row)>();
        foreach (var entry in all)
        {
            var row = HistoryRow(entry);
            rows.Add((entry, row));
            HistoryList.Children.Add(row);
        }
        _ = MarkMissingAsync(rows);
    }

    /// <summary>한 파일(또는 폴더): "A." 표시 + 이름(굵게, 길면 끝을 줄임), 그 아래 작은 글씨의 전체 경로(길면 가운데를 줄임).</summary>
    Control HistoryPair(string label, string path)
    {
        var tag = new TextBlock { Text = label, FontWeight = Avalonia.Media.FontWeight.Bold, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        tag.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppPrimary"));
        var name = new TextBlock { Text = Locations.NameOf(Plain(path)), FontWeight = Avalonia.Media.FontWeight.SemiBold, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
        var full = new TextBlock { Text = path, FontSize = 11, TextTrimming = Avalonia.Media.TextTrimming.PathSegmentEllipsis };
        full.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), Children = { tag, name, full } };
        Grid.SetColumn(name, 1);
        Grid.SetColumn(full, 1);
        Grid.SetRow(full, 1);
        ToolTip.SetTip(grid, path);
        return grid;
    }

    Control HistoryRow(CompareHistoryEntry e)
    {
        var s = Strings.Current;
        var icon = new DuoIcon { Icon = e.IsFolders ? "folder" : "file", ColorKey = e.IsFolders ? "AppIconFolder" : null, Width = 18, Height = 18, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        // 왼쪽(A)과 오른쪽(B)을 위아래 두 블록으로: 파일 이름(굵게) + 그 아래 작은 글씨의 전체 경로.
        // 한 줄에 나란히 두면 이름이 길 때 두 번째 파일이 아예 안 보였다.
        var text = new StackPanel { Spacing = 5, Children = { HistoryPair("A.", e.Left), HistoryPair("B.", e.Right) } };
        var time = new TextBlock { Text = TimeText(e.OpenedAt), FontSize = 11, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        time.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        var remove = new Button { Classes = { "tool" }, Content = new DuoIcon { Icon = "close", Width = 12, Height = 12 }, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        ToolTip.SetTip(remove, s.HomeRecentRemove);
        remove.Click += (_, _) => App.Settings.Update(st => st with { CompareHistory = CompareHistoryList.Remove(st.CompareHistory, e, PolicyStore.NamesIgnoreCase) });
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 10, Children = { icon, text, time, remove } };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(time, 2);
        Grid.SetColumn(remove, 3);
        var row = new Border { Classes = { "historyrow" }, Child = grid };
        ToolTip.SetTip(row, $"{e.Left}\n{e.Right}");
        row.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(row).Properties.IsLeftButtonPressed) _ = OpenHistoryAsync(e);
        };
        return row;
    }

    /// <summary>압축 파일 안 경로(<c>a.zip!/x</c>)는 이름만 보일 때 압축 파일 이름으로.</summary>
    static string Plain(string path) => path.Replace(ArchivePaths.Separator, "/");

    static string TimeText(DateTimeOffset at)
    {
        var local = at.ToLocalTime();
        var today = DateTime.Today;
        return local.Date == today ? local.ToString("HH:mm")
            : local.Date == today.AddDays(-1) ? Strings.Current.HomeRecentYesterday(local.ToString("HH:mm"))
            : local.ToString("yyyy-MM-dd");
    }

    /// <summary>기록의 두 경로 중 지금 없는 첫 경로 (모두 있으면 null). 압축 파일 안 경로는 압축 파일이 있는지만 본다.</summary>
    static string? FirstMissing(CompareHistoryEntry e)
    {
        foreach (var path in new[] { e.Left, e.Right })
        {
            var real = ArchivePaths.TryParse(path)?.Archive ?? path;
            if (!(Directory.Exists(real) || File.Exists(real))) return path;
        }
        return null;
    }

    /// <summary>없어진 경로의 줄은 흐리게. 느린 네트워크 경로가 화면을 막지 않게 백그라운드에서 확인한다.</summary>
    async Task MarkMissingAsync(List<(CompareHistoryEntry Entry, Control Row)> rows)
    {
        var version = ++_historyVersion;
        var missing = await Task.Run(() => rows.Select(r => FirstMissing(r.Entry)).ToArray());
        if (version != _historyVersion) return;
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Row.Opacity = missing[i] is null ? 1 : 0.5;
            if (missing[i] is { } path) ToolTip.SetTip(rows[i].Row, Strings.Current.HomeRecentMissing(path));
        }
    }

    async Task OpenHistoryAsync(CompareHistoryEntry e)
    {
        if (await Task.Run(() => FirstMissing(e)) is { } missing)
        {
            HistoryNotice.Text = Strings.Current.HomeRecentMissing(missing);
            return;
        }
        HistoryNotice.Text = "";
        if (e.IsFolders) FolderCompareWindow.Open(this, e.Left, e.Right);
        else CompareWindow.Open(this, e.Left, e.Right);
    }

    // --- 새 비교: 왼쪽·오른쪽 고르기와 끌어다 놓기 (ROADMAP §6-1 ①) ---

    string? _left, _right;

    void RefreshPicks()
    {
        LeftPath.Text = _left ?? Strings.Current.HomeNotChosen;
        RightPath.Text = _right ?? Strings.Current.HomeNotChosen;
        CompareButton.IsEnabled = _left is not null && _right is not null;
    }

    async Task PickAsync(bool left)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = left ? Strings.Current.CompareOpenLeftTitle : Strings.Current.CompareOpenRightTitle,
            AllowMultiple = false,
            SuggestedStartLocation = await PickLocations.StartAsync(StorageProvider),
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        PickLocations.Remember(path, isFolder: false);
        if (left) _left = path; else _right = path;
        RefreshPicks();
        if (_left is not null && _right is not null) await OpenCompareAsync();
    }

    async Task PickFolderAsync(bool left)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = left ? Strings.Current.FolderChooseLeftTitle : Strings.Current.FolderChooseRightTitle,
            AllowMultiple = false,
            SuggestedStartLocation = await PickLocations.StartAsync(StorageProvider),
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path) return;
        PickLocations.Remember(path, isFolder: true);
        if (left) _left = path; else _right = path;
        RefreshPicks();
        if (_left is not null && _right is not null) await OpenCompareAsync();
    }

    /// <summary>둘 다 폴더(또는 압축 파일)이면 폴더 비교, 둘 다 파일이면 텍스트 비교 (BC처럼 종류는 고른 것이 정한다).</summary>
    async Task OpenCompareAsync()
    {
        if (_left is null || _right is null) return;
        // 폴더인지 파일인지는 디스크를 만져야 알 수 있다 — 네트워크 드라이브에서 UI가 멈추지 않게 백그라운드에서 확인한다.
        var (leftPath, rightPath) = (_left, _right);
        var (leftContainer, rightContainer) = await Task.Run(() => (FolderCompareWindow.CanOpen(leftPath), FolderCompareWindow.CanOpen(rightPath)));
        if (leftContainer != rightContainer)
        {
            _ = MessageDialog.ShowAsync(this, AppIdentity.DisplayName, Strings.Current.FolderFileVsFolderLater, [Strings.Current.CommonClose], 0, 0);
            return;
        }
        if (leftContainer) FolderCompareWindow.Open(this, _left, _right);
        else CompareWindow.Open(this, _left, _right);
        _left = _right = null; // 홈 창은 다음 비교를 위해 비운다
        RefreshPicks();
    }

    static IReadOnlyList<DroppedItem> ItemsOf(DragEventArgs e) =>
        DropClassifier.Classify(e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>() ?? []);

    async void OnDrop(object? sender, DragEventArgs e)
    {
        DropHint.IsVisible = false;
        var x = e.GetPosition(this).X;
        var target = x < Bounds.Width * 0.45 ? DropTarget.Left : x > Bounds.Width * 0.55 ? DropTarget.Right : DropTarget.Window;
        static DroppedItem? Item(string? p) => p is null ? null : DropClassifier.Classify([p]).FirstOrDefault();
        var plan = DropPlanner.Plan(ItemsOf(e), target, Item(_left), Item(_right));
        await CompareWindow.ApplyDropAsync(this, plan, (l, r) =>
        {
            (_left, _right) = (l, r);
            RefreshPicks();
            if (l is not null && r is not null) return OpenCompareAsync();
            return Task.CompletedTask;
        }, allowPendingFolders: true);
    }

    /// <summary>공통 단축키 (ui-ux.md §7). 비교 전용 단축키는 비교 창이 생기는 3단계에서 더한다.</summary>
    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!Mods.Primary(e.KeyModifiers)) return;
        switch (e.Key)
        {
            case Key.OemComma: SettingsWindow.Open(this); break;
            case Key.W: Close(); break;
            default: return;
        }
        e.Handled = true;
    }

    // --- 창 위치·크기 (window-placement.md) ---

    const double DefaultWidth = 1200, DefaultHeight = 720;
    bool _trackPlacement;

    IReadOnlyList<DisplayArea> DisplayAreas() =>
        Screens.All.Select(sc => new DisplayArea(
            new RectD(sc.WorkingArea.X, sc.WorkingArea.Y, sc.WorkingArea.Width, sc.WorkingArea.Height),
            sc.Scaling, sc.IsPrimary)).ToList();

    /// <summary>저장한 위치로 연다. 기억한 곳이 없거나 그 모니터가 없어졌으면 주 모니터 가운데에 연다. 보이기 전에 정한다.</summary>
    void RestorePlacement()
    {
        var displays = DisplayAreas();
        var saved = App.Settings.Value.Window;
        var restored = saved is null ? null : PlacementMath.Restore(saved, displays);
        var target = restored ?? (PlacementMath.PrimaryOf(displays) is { } d ? PlacementMath.CenterOn(d, DefaultWidth, DefaultHeight) : (PlacementTarget?)null);
        if (target is not { } t) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint((int)Math.Round(t.X), (int)Math.Round(t.Y));
        Width = t.Width;
        Height = t.Height;
        if (restored is not null && saved!.Maximized) WindowState = WindowState.Maximized;
    }

    /// <summary>창이 움직이거나 크기·상태가 바뀌면 (모아서) 설정에 반영한다. 최소화·전체 화면 중에는 저장하지 않는다.</summary>
    void SavePlacement()
    {
        if (!_trackPlacement || WindowState is WindowState.Minimized or WindowState.FullScreen) return;
        var next = PlacementMath.Updated(
            App.Settings.Value.Window, Position.X, Position.Y, ClientSize.Width, ClientSize.Height, RenderScaling,
            maximized: WindowState == WindowState.Maximized);
        if (next is not null && next != App.Settings.Value.Window) App.Settings.Update(s => s with { Window = next });
    }
}
