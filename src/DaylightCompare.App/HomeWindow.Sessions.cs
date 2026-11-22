using Avalonia.Controls;
using DaylightCompare.App.Compare;
using DaylightCompare.App.Dialogs;
using DaylightCompare.App.Icons;
using DaylightCompare.Domain;
using DaylightCompare.Domain.Settings;
using DaylightCompare.Services;

namespace DaylightCompare.App;

/// <summary>홈 창의 저장한 세션 목록 (BC Sessions)과 [이전 창 복원].</summary>
public sealed partial class HomeWindow
{
    IReadOnlyList<SavedSession>? _sessionsShown;
    IReadOnlyList<SessionWindow>? _lastWindowsShown;

    void RefreshSessions(AppSettings settings)
    {
        _sessionsShown = settings.Sessions;
        _lastWindowsShown = settings.LastWindows;
        var s = Strings.Current;
        SessionRows.Children.Clear();
        SessionBlock.IsVisible = settings.Sessions.Count > 0 || settings.LastWindows.Count > 0;
        SessionsTitle.IsVisible = settings.Sessions.Count > 0;
        RestoreWindowsButton.IsVisible = settings.LastWindows.Count > 0;
        RestoreWindowsButton.Content = s.HomeRestoreWindows(settings.LastWindows.Count);
        foreach (var session in settings.Sessions) SessionRows.Children.Add(SessionRow(session));
    }

    bool SessionsChanged(AppSettings s) =>
        !ReferenceEquals(s.Sessions, _sessionsShown) || !ReferenceEquals(s.LastWindows, _lastWindowsShown);

    Control SessionRow(SavedSession session)
    {
        var s = Strings.Current;
        var first = session.Window.Tabs.FirstOrDefault();
        var folders = first?.IsFolder == true;
        var icon = new DuoIcon { Icon = folders ? "folder" : "file", ColorKey = folders ? "AppIconFolder" : null, Width = 18, Height = 18, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        var name = new TextBlock { Text = session.Name, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
        // 둘째 줄: 탭 수 · 첫 탭의 두 이름 (A ↔ B)
        var pair = first is null ? "" : $"{Locations.NameOf(Plain(first.Left))} ↔ {Locations.NameOf(Plain(first.Right))}";
        var detail = new TextBlock { Text = $"{s.SessionTabs(session.Window.Tabs.Count)}  ·  {pair}", FontSize = 11, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
        detail.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        var text = new StackPanel { Spacing = 2, Children = { name, detail } };
        var time = new TextBlock { Text = TimeText(session.SavedAt), FontSize = 11, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        time.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        var remove = new Button { Classes = { "tool" }, Content = new DuoIcon { Icon = "close", Width = 12, Height = 12 }, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        ToolTip.SetTip(remove, s.SessionDelete);
        remove.Click += (_, _) => DeleteSession(session);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 10, Children = { icon, text, time, remove } };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(time, 2);
        Grid.SetColumn(remove, 3);
        var row = new Border { Classes = { "historyrow" }, Child = grid, Tag = session.Name };
        ToolTip.SetTip(row, string.Join('\n', session.Window.Tabs.Select(t => $"{t.Left} ↔ {t.Right}")));
        row.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(row).Properties.IsLeftButtonPressed && !remove.IsPointerOver) OpenSession(session);
        };
        var menu = new ContextMenu();
        var rename = new MenuItem { Header = s.SessionRename };
        rename.Click += async (_, _) => await RenameSessionAsync(session);
        var delete = new MenuItem { Header = s.SessionDelete };
        delete.Click += (_, _) => DeleteSession(session);
        menu.Items.Add(rename);
        menu.Items.Add(delete);
        row.ContextMenu = menu;
        return row;
    }

    void DeleteSession(SavedSession session) =>
        App.Settings.Update(st => st with { Sessions = SessionList.Remove(st.Sessions, session.Name) });

    /// <summary>저장한 세션을 새 비교 창으로 연다 (탭마다 저장해 둔 비교·필터·보기 설정으로).</summary>
    public CompareHost? OpenSession(SavedSession session)
    {
        HistoryNotice.Text = "";
        return CompareHost.OpenSession(this, session.Window, session.Name);
    }

    async Task RenameSessionAsync(SavedSession session)
    {
        var s = Strings.Current;
        var typed = await InputDialog.ShowAsync(this, s.SessionRenameTitle, s.SessionNamePrompt, session.Name, s.CommonOk, s.CommonCancel);
        var name = SessionList.Clean(typed);
        if (name.Length == 0 || name == session.Name) return;
        var current = App.Settings.Value.Sessions;
        if (ReferenceEquals(SessionList.Rename(current, session.Name, name), current)) { HistoryNotice.Text = s.SessionNameTaken; return; }
        HistoryNotice.Text = "";
        App.Settings.Update(st => st with { Sessions = SessionList.Rename(st.Sessions, session.Name, name) });
    }

    /// <summary>앱을 끌 때 열려 있던 비교 창들을 다시 연다.</summary>
    public void RestoreWindows()
    {
        HistoryNotice.Text = "";
        foreach (var window in App.Settings.Value.LastWindows) CompareHost.OpenSession(this, window);
    }
}
