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
/// 홈 창 (Daylight PDF 홈과 같은 구성): 끌어다 놓거나 열면 맞는 비교 창이 열리고, 아래 카드로 빈 폴더·텍스트 비교 창을 바로 연다. 최근 비교 목록.
/// 창 위치·크기는 window-placement.md 규칙으로 기억한다 (Daylight Commander의 MainWindow에서 가져옴).
/// </summary>
public partial class HomeWindow : Window
{
    public HomeWindow()
    {
        InitializeComponent();
        Title = AppIdentity.DisplayName;
        AppTitle.Text = AppIdentity.DisplayName;
        HeaderTitle.Text = AppIdentity.DisplayName;
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

        OpenFilesButton.Click += async (_, _) => await OpenFilesAsync();
        OpenFoldersButton.Click += async (_, _) => await OpenFoldersAsync();
        FolderCompareCard.Click += (_, _) => FolderCompareView.Open(this, null, null);
        TextCompareCard.Click += (_, _) => CompareView.Open(this, null, null);
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            // 디스크를 만지지 않고 개수만 본다 (종류는 놓은 뒤 백그라운드에서 가린다).
            var ok = DroppedPaths(e).Count is > 0 and <= 3;
            e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            SetDropHighlight(ok);
        });
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetDropHighlight(false));
        AddHandler(DragDrop.DropEvent, OnDrop);
        ClearHistoryButton.Click += (_, _) => App.Settings.Update(s => s with { CompareHistory = [] });
        Strings.Changed += RefreshHistoryOnUi;
        App.Settings.Changed += OnSettingsChanged;
        Closed += (_, _) =>
        {
            Strings.Changed -= RefreshHistoryOnUi;
            App.Settings.Changed -= OnSettingsChanged;
        };
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
        ApplyLayout(compact: all.Count > 0);
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
        if (e.IsFolders) FolderCompareView.Open(this, e.Left, e.Right);
        else CompareView.Open(this, e.Left, e.Right);
    }

    // --- 놓기 구역의 모양 (Daylight PDF 홈: 최근 목록이 생기면 한 줄로 작아진다) ---

    /// <summary>최근 비교가 없으면 놓기 구역이 남는 자리를 다 쓰고, 있으면 한 줄로 줄고 그 자리를 목록이 쓴다.</summary>
    void ApplyLayout(bool compact)
    {
        Body.RowDefinitions[1].Height = compact ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        Body.RowDefinitions[2].Height = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        // 모서리: 넓을 때 24(card), 한 줄일 때 20(tile) — Daylight PDF의 DropZone과 같다.
        var radius = compact ? 20 : 24;
        DropZoneFill.CornerRadius = new CornerRadius(radius);
        DropZoneStroke.RadiusX = DropZoneStroke.RadiusY = radius;
        DropBig.IsVisible = !compact;
        DropCompact.IsVisible = compact;
        // 한 줄 모양에서는 [파일 열기]·[폴더 열기]를 위아래 두 줄로, 같은 폭으로 쌓는다.
        OpenButtons.Orientation = compact ? Avalonia.Layout.Orientation.Vertical : Avalonia.Layout.Orientation.Horizontal;
        var target = compact ? DropCompactButtons : DropBigButtons;
        if (OpenButtons.Parent is Panel from && !ReferenceEquals(from, target))
        {
            from.Children.Remove(OpenButtons);
            target.Children.Add(OpenButtons);
        }
    }

    void SetDropHighlight(bool on)
    {
        DropZoneStroke.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, this.GetResourceObservable(on ? "AppPrimary" : "AppStrokeStrong"));
        DropZoneStroke.StrokeThickness = on ? 2 : 1.5;
        DropZoneFill.Bind(Border.BackgroundProperty, this.GetResourceObservable(on ? "AppSurfaceHi" : "AppSurface"));
    }

    // --- 열기: 놓거나 고른 것에 맞는 비교 창을 연다 ---

    static List<string> DroppedPaths(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];

    async void OnDrop(object? sender, DragEventArgs e)
    {
        SetDropHighlight(false);
        await OpenPathsAsync(DroppedPaths(e));
    }

    async Task OpenFilesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Current.HomeOpenFiles,
            AllowMultiple = true,
            SuggestedStartLocation = await PickLocations.StartAsync(StorageProvider),
        });
        var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0) PickLocations.Remember(paths[0], isFolder: false);
        await OpenPathsAsync(paths);
    }

    async Task OpenFoldersAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.Current.HomeChooseFolder,
            AllowMultiple = true,
            SuggestedStartLocation = await PickLocations.StartAsync(StorageProvider),
        });
        var paths = folders.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0) PickLocations.Remember(paths[0], isFolder: true);
        await OpenPathsAsync(paths);
    }

    /// <summary>
    /// 놓거나 고른 경로로 비교 창을 연다 (<see cref="DropPlanner"/>의 규칙).
    /// 하나면 그 종류에 맞는 창(폴더·압축 파일 → 폴더 비교, 파일 → 텍스트 비교)을 왼쪽만 채워 열고, 오른쪽은 그 창에서 고르거나 놓는다.
    /// 두 개면 바로 비교한다. 셋 이상(병합)은 아직 없다.
    /// </summary>
    /// <returns>연 창 (안내만 하고 열지 않았으면 null). 두 개를 놓았을 때 파일·폴더 짝이면 폴더 안의 같은 이름 파일과 비교한다.</returns>
    public async Task<Window?> OpenPathsAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return null;
        if (paths.Count > 3)
        {
            await MessageDialog.ShowAsync(this, AppIdentity.DisplayName, Strings.Current.HomeDropOpenTooMany, [Strings.Current.CommonClose], 0, 0);
            return null;
        }
        // 폴더인지 파일인지는 디스크를 만져야 안다 — 네트워크 드라이브에서 UI가 멈추지 않게 백그라운드에서 가린다.
        var items = await Task.Run(() => DropClassifier.Classify(paths));
        var plan = DropPlanner.Plan(items, DropTarget.Window);
        if (plan is DropPlan.Pending { Left: { } one })
            return one.IsContainer ? FolderCompareView.Open(this, one.Path, null) : CompareView.Open(this, one.Path, null);
        if (plan is DropPlan.Compare { Mode: CompareMode.Folders } folders)
            return FolderCompareView.Open(this, folders.Left.Path, folders.Right.Path);
        Window? opened = null;
        await CompareView.ApplyDropAsync(this, plan, (l, r) =>
        {
            opened = CompareView.Open(this, l, r);
            return Task.CompletedTask;
        });
        return opened;
    }

    /// <summary>공통 단축키 (ui-ux.md §7).</summary>
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
