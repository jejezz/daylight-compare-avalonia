using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DaylightCompare.App.Dialogs;
using DaylightCompare.App.Views;
using DaylightCompare.Domain;
using DaylightCompare.Domain.Window;

namespace DaylightCompare.App;

/// <summary>
/// 홈 창: 새 비교·저장한 세션·최근 목록이 올 자리 (ROADMAP §6). 0단계에서는 앱 바(설정 | 정보)와 빈 상태만 있다.
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
