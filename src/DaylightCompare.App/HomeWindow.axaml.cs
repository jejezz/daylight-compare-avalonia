using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DaylightCompare.App.Compare;
using DaylightCompare.App.Dialogs;
using DaylightCompare.App.Views;
using DaylightCompare.Domain;
using DaylightCompare.Domain.Drop;
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
        CompareButton.Click += (_, _) => OpenCompare();
        PasteCompareButton.Click += (_, _) => CompareWindow.Open(this, _left, _right);
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            var ok = ItemsOf(e).Count is > 0 and <= 3;
            e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            DropHint.IsVisible = ok;
        });
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropHint.IsVisible = false);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Strings.Changed += RefreshPicks;
        Closed += (_, _) => Strings.Changed -= RefreshPicks;
        RefreshPicks();
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
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        if (left) _left = path; else _right = path;
        RefreshPicks();
        if (_left is not null && _right is not null) OpenCompare();
    }

    async Task PickFolderAsync(bool left)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = left ? Strings.Current.FolderChooseLeftTitle : Strings.Current.FolderChooseRightTitle,
            AllowMultiple = false,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path) return;
        if (left) _left = path; else _right = path;
        RefreshPicks();
        if (_left is not null && _right is not null) OpenCompare();
    }

    /// <summary>둘 다 폴더(또는 압축 파일)이면 폴더 비교, 둘 다 파일이면 텍스트 비교 (BC처럼 종류는 고른 것이 정한다).</summary>
    void OpenCompare()
    {
        if (_left is null || _right is null) return;
        var leftContainer = FolderCompareWindow.CanOpen(_left);
        if (leftContainer != FolderCompareWindow.CanOpen(_right))
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
            if (l is not null && r is not null) OpenCompare();
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
