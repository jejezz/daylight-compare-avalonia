using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using DaylightCompare.App.Icons;
using DaylightCompare.App.Views;
using DaylightCompare.App.Dialogs;
using DaylightCompare.Domain;
using DaylightCompare.Domain.Settings;
using DaylightCompare.Services;

namespace DaylightCompare.App.Compare;

/// <summary>
/// 비교 창: 탭으로 폴더 비교와 파일 비교를 담는다 (BC처럼 — 폴더 비교에서 파일을 열면 같은 창의 새 탭).
/// 탭이 하나뿐이면 머리글 줄을 숨겨 예전의 단일 비교 창과 같게 보인다. 보이지 않는 탭도 트리에 그대로 둔다(상태·스크롤을 잃지 않게 <c>IsVisible</c>만 끈다).
/// <para>
/// 키는 호스트가 터널로 먼저 받는다: 탭 키(<c>Ctrl+W</c>·<c>Ctrl+F4</c> 닫기, <c>Ctrl+Tab</c>·<c>Ctrl+Shift+Tab</c>·<c>Ctrl+PageUp/Down</c> 이동)를 처리하고
/// 나머지는 지금 탭에 건넨다.
/// </para>
/// </summary>
public sealed class CompareHost : Window
{
    sealed class Tab(ICompareTab content)
    {
        public readonly ICompareTab Content = content;
        public readonly Border Header = new() { BorderThickness = new Thickness(0, 0, 0, 2), Padding = new Thickness(10, 0, 4, 0), Background = Brushes.Transparent };
        public readonly TextBlock Text = new() { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis };
        public readonly Button CloseButton = new() { Classes = { "tool" }, Width = 22, Height = 22, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        public Action? OnTitleChanged;
    }

    readonly List<Tab> _tabs = [];
    readonly StackPanel _strip = new() { Orientation = Avalonia.Layout.Orientation.Horizontal };
    readonly Border _stripBar = new() { IsVisible = false, BorderThickness = new Thickness(0, 0, 0, 1), Height = 34 };
    readonly Grid _body = new();
    readonly HashSet<ICompareTab> _startedTabs = [];
    Tab? _active;
    static readonly List<CompareHost> Hosts = [];
    bool _closeConfirmed;
    bool _opened;

    public CompareHost()
    {
        Width = 1280;
        Height = 780;
        MinWidth = 900;
        MinHeight = 520;
        _stripBar.Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _strip,
        };
        _stripBar.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurface"));
        _stripBar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStrokeStrong"));
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(_body, 1);
        root.Children.Add(_stripBar);
        root.Children.Add(_body);
        Content = root;

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Strings.Changed += OnLanguageChanged;
        ActualThemeVariantChanged += (_, _) => UpdateStrip();
        Closing += OnClosing;
        Hosts.Add(this);
        Closed += (_, _) =>
        {
            Hosts.Remove(this);
            Strings.Changed -= OnLanguageChanged;
            foreach (var tab in _tabs.ToList()) tab.Content.Dispose();
        };
        Opened += (_, _) =>
        {
            _opened = true;
            if (_active is not null) StartIfNeeded(_active, now: true);
        };
    }

    /// <summary>호스트 창을 만들어 <paramref name="first"/>를 첫 탭으로 열고 보인다.</summary>
    public static CompareHost Open(Window? near, ICompareTab first)
    {
        var host = new CompareHost();
        host.Add(first);
        if (near is not null) WindowPlacementHelper.OpenNear(near, host);
        else host.Show();
        return host;
    }

    // --- 세션 (BC Sessions) ---

    /// <summary>지금 열려 있는 비교 창들 (앱을 끌 때 [이전 창 복원]용으로 적어 둔다).</summary>
    public static IReadOnlyList<CompareHost> OpenHosts => Hosts;

    /// <summary>이 창이 저장한 세션에서 열렸거나 저장한 적 있으면 그 이름 (다시 저장할 때 이름 칸의 처음 값).</summary>
    public string? SessionName { get; private set; }

    /// <summary>지금의 탭들(과 보고 있는 탭)을 세션에 적을 모양으로.</summary>
    public SessionWindow Capture() => new()
    {
        Tabs = [.. _tabs.Select(t => t.Content.Capture())],
        Active = Math.Max(0, _tabs.FindIndex(t => t == _active)),
    };

    /// <summary>
    /// 저장한 세션(또는 이전 창)을 비교 창 하나로 다시 연다: 탭마다 같은 비교·필터·보기 설정으로. 보던 탭이 앞에 오고, 나머지 탭은 처음 볼 때 읽는다.
    /// 탭이 하나도 없으면 null.
    /// </summary>
    public static CompareHost? OpenSession(Window? near, SessionWindow session, string? name = null)
    {
        if (session.Tabs.Count == 0) return null;
        var host = new CompareHost { SessionName = name };
        foreach (var tab in session.Tabs)
        {
            host.Add(tab.Kind == SessionTab.Binary
                ? BinaryCompareView.Create(tab.Left.Length > 0 ? tab.Left : null, tab.Right.Length > 0 ? tab.Right : null, tab)
                : tab.IsFolder
                ? FolderCompareView.Create(tab.Left, tab.Right, tab)
                : CompareView.Create(tab.Left.Length > 0 ? tab.Left : null, tab.Right.Length > 0 ? tab.Right : null, CompareOptions.None, recordHistory: true, restore: tab));
        }
        host.Select(host._tabs[Math.Clamp(session.Active, 0, host._tabs.Count - 1)]);
        if (near is not null) WindowPlacementHelper.OpenNear(near, host);
        else host.Show();
        return host;
    }

    /// <summary>
    /// [세션 저장…]: 이름을 물어 이 창의 탭들을 저장한다. 같은 이름이 있으면 바꿀지 묻고, 저장하면 이 창의 세션 이름이 된다.
    /// (BC의 Save Session. 키는 BC의 <c>Shift+Ctrl+S</c>가 우리 [모두 저장]과 겹쳐서 메뉴로만 둔다.)
    /// </summary>
    public async Task SaveSessionAsync()
    {
        if (_active is null) return;
        var s = Strings.Current;
        var suggestion = SessionName ?? _active.Content.TabTitle.TrimStart('●', ' ');
        var typed = await InputDialog.ShowAsync(this, s.SessionSaveTitle, s.SessionNamePrompt, suggestion, s.CommonSave, s.CommonCancel);
        var name = SessionList.Clean(typed);
        if (name.Length == 0) return;
        if (SessionList.Contains(App.Settings.Value.Sessions, name))
        {
            var choice = await MessageDialog.ShowAsync(this, s.SessionSaveTitle, s.SessionOverwriteBody(name), [s.CommonCancel, s.SessionOverwrite], 1, 0);
            if (choice != 1) return;
        }
        SaveSession(name);
    }

    /// <summary>이 창의 탭들을 <paramref name="name"/>이라는 세션으로 저장한다 (같은 이름이 있으면 바꾼다).</summary>
    public void SaveSession(string name)
    {
        if (_active is null || SessionList.Clean(name).Length == 0) return;
        name = SessionList.Clean(name);
        var session = new SavedSession { Name = name, SavedAt = DateTimeOffset.Now, Window = Capture() };
        App.Settings.Update(x => x with { Sessions = SessionList.Save(x.Sessions, session) });
        SessionName = name;
        _active.Content.Announce(Strings.Current.SessionSaved(name));
    }

    // --- 탭 ---

    public ICompareTab? ActiveTab => _active?.Content;
    public IReadOnlyList<ICompareTab> Tabs => _tabs.Select(t => t.Content).ToList();
    /// <summary>지금 탭이 파일 비교이면 그것.</summary>
    public CompareView? ActiveFile => _active?.Content as CompareView;
    public FolderCompareView? ActiveFolder => _active?.Content as FolderCompareView;

    // 지금 탭에 대한 바로가기 (시험·명령줄 처리가 쓴다).
    public string? LeftPath => ActiveFile?.LeftPath;
    public string? RightPath => ActiveFile?.RightPath;
    public string? LeftRoot => ActiveFolder?.LeftRoot;
    public string? RightRoot => ActiveFolder?.RightRoot;

    /// <summary>파일 짝을 이 창의 새 탭으로 연다. 같은 짝이 이미 열려 있으면 그 탭으로 간다.</summary>
    public ICompareTab OpenFileTab(string? left, string? right, CompareOptions? options = null, bool recordHistory = false)
    {
        if (options is null && left is not null && right is not null)
            foreach (var t in _tabs)
            {
                var (l, r) = t.Content switch { CompareView v => (v.LeftPath, v.RightPath), BinaryCompareView b => (b.LeftPath, b.RightPath), _ => ((string?)null, (string?)null) };
                if (string.Equals(l, left, StringComparison.OrdinalIgnoreCase) && string.Equals(r, right, StringComparison.OrdinalIgnoreCase))
                {
                    Select(t);
                    return t.Content;
                }
            }
        var view = CompareView.Create(left, right, options ?? CompareOptions.None, recordHistory);
        Add(view);
        return view;
    }

    /// <summary>탭을 더하고 고른다.</summary>
    public void Add(ICompareTab content, int? index = null)
    {
        var tab = new Tab(content);
        tab.Text.Text = content.TabTitle;
        var icon = new DuoIcon
        {
            Icon = content.TabIcon, ColorKey = content.IsFolder ? "AppIconFolder" : null,
            Width = 16, Height = 16, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        tab.CloseButton.Content = new DuoIcon { Icon = "close", Width = 12, Height = 12 };
        ToolTip.SetTip(tab.CloseButton, Strings.Current.TabClose);
        tab.CloseButton.Click += async (_, e) => { e.Handled = true; await CloseTabAsync(tab); };
        tab.Header.Child = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6,
            Children = { icon, tab.Text, tab.CloseButton },
        };
        ToolTip.SetTip(tab.Header, content.TabToolTip);
        tab.Header.PointerPressed += async (_, e) =>
        {
            var p = e.GetCurrentPoint(tab.Header).Properties;
            if (p.IsMiddleButtonPressed) { e.Handled = true; await CloseTabAsync(tab); }
            else if (p.IsLeftButtonPressed) Select(tab);
        };
        tab.Header.ContextMenu = BuildMenu(tab);
        tab.OnTitleChanged = () =>
        {
            tab.Text.Text = content.TabTitle;
            ToolTip.SetTip(tab.Header, content.TabToolTip);
            if (_active == tab) Title = FullTitle(tab);
        };
        content.TitleChanged += tab.OnTitleChanged;

        content.View.IsVisible = false;
        _body.Children.Add(content.View);
        if (index is { } at && at >= 0 && at <= _tabs.Count) { _tabs.Insert(at, tab); _strip.Children.Insert(at, tab.Header); }
        else { _tabs.Add(tab); _strip.Children.Add(tab.Header); }
        Select(tab);
    }

    ContextMenu BuildMenu(Tab tab)
    {
        var s = Strings.Current;
        var close = new MenuItem { Header = s.TabClose };
        close.Click += async (_, _) => await CloseTabAsync(tab);
        var others = new MenuItem { Header = s.TabCloseOthers };
        others.Click += async (_, _) =>
        {
            foreach (var other in _tabs.Where(t => t != tab).ToList())
                if (!await CloseTabAsync(other)) return;
        };
        var detach = new MenuItem { Header = s.TabDetach };
        detach.Click += (_, _) => Detach(tab);
        var menu = new ContextMenu();
        menu.Opening += (_, _) => others.IsEnabled = detach.IsEnabled = _tabs.Count > 1;
        menu.Items.Add(close);
        menu.Items.Add(others);
        menu.Items.Add(new Separator());
        menu.Items.Add(detach);
        return menu;
    }

    void OnLanguageChanged() => Dispatcher.UIThread.Post(() =>
    {
        foreach (var tab in _tabs)
        {
            tab.Header.ContextMenu = BuildMenu(tab);
            ToolTip.SetTip(tab.CloseButton, Strings.Current.TabClose);
        }
    });

    static string FullTitle(Tab tab) => $"{tab.Content.WindowTitle} — {AppIdentity.DisplayName}";

    void UpdateStrip()
    {
        _stripBar.IsVisible = _tabs.Count > 1;
        IBrush accent = this.TryFindResource("AppPrimary", ActualThemeVariant, out var found) && found is IBrush b ? b : Brushes.DodgerBlue;
        foreach (var tab in _tabs)
        {
            var on = tab == _active;
            tab.Header.BorderBrush = on ? accent : Brushes.Transparent;
            tab.Text.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
        }
    }

    public void Select(ICompareTab content)
    {
        if (_tabs.FirstOrDefault(t => t.Content == content) is { } t) Select(t);
    }

    void Select(Tab tab)
    {
        _active = tab;
        foreach (var t in _tabs) t.Content.View.IsVisible = t == tab;
        Title = FullTitle(tab);
        UpdateStrip();
        StartIfNeeded(tab, now: false);
        Dispatcher.UIThread.Post(() => tab.Content.View.Focus(), DispatcherPriority.Input);
    }

    /// <summary>탭이 처음 보일 때 한 번 시작한다. 창이 열리기 전이면 <c>Opened</c>가 시작한다. 열린 뒤에 더한 탭은 보이게 된 다음 시작한다
    /// (보이지 않는 편집기는 배치가 없어 스크롤을 맞출 수 없다).</summary>
    void StartIfNeeded(Tab tab, bool now)
    {
        if (!_opened || !_startedTabs.Add(tab.Content)) return;
        if (now) tab.Content.Start();
        else Dispatcher.UIThread.Post(tab.Content.Start, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// 탭 하나를 다른 보기로 바꾼다 (같은 자리, 보던 탭이었으면 새 탭을 본다). 텍스트로 열었더니 바이너리 파일이어서 바이너리 비교로 바꿀 때 쓴다.
    /// 바꿔진 탭은 묻지 않고 정리한다 — 아직 고친 것이 없는 탭이어야 한다.
    /// </summary>
    public void ReplaceTab(ICompareTab old, ICompareTab replacement)
    {
        var index = _tabs.FindIndex(t => t.Content == old);
        if (index < 0) return;
        var oldTab = _tabs[index];
        var previous = _active; // Add가 새 탭을 고르므로 보던 탭을 적어 둔다
        Add(replacement, index + 1);
        if (previous is not null && previous != oldTab) Select(previous); // 보던 탭이 바꿔지는 것이 아니면 보던 탭으로 돌아간다
        Remove(oldTab, dispose: true);
    }

    // --- 닫기 ---

    /// <summary>탭을 닫는다 (고친 것이 있으면 묻는다). 마지막 탭이면 창도 닫는다. 닫았으면 true.</summary>
    public async Task<bool> CloseTabAsync(ICompareTab content) =>
        _tabs.FirstOrDefault(t => t.Content == content) is { } t && await CloseTabAsync(t);

    async Task<bool> CloseTabAsync(Tab tab)
    {
        if (!await tab.Content.ConfirmCloseAsync()) return false;
        Remove(tab, dispose: true);
        return true;
    }

    void Remove(Tab tab, bool dispose)
    {
        var index = _tabs.IndexOf(tab);
        if (index < 0) return;
        tab.Content.TitleChanged -= tab.OnTitleChanged;
        _tabs.RemoveAt(index);
        _strip.Children.Remove(tab.Header);
        _body.Children.Remove(tab.Content.View);
        _startedTabs.Remove(tab.Content);
        if (dispose) tab.Content.Dispose();
        if (_tabs.Count == 0) { _closeConfirmed = true; Close(); return; }
        if (_active == tab) Select(_tabs[Math.Min(index, _tabs.Count - 1)]);
        else UpdateStrip();
    }

    async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed) return;
        if (!_tabs.Any(t => t.Content.IsDirty)) return;
        e.Cancel = true;
        foreach (var tab in _tabs.ToList())
        {
            if (!tab.Content.IsDirty) continue;
            Select(tab);
            if (!await tab.Content.ConfirmCloseAsync()) return;
        }
        _closeConfirmed = true;
        Close();
    }

    // --- 분리 (탭을 새 창으로) ---

    void Detach(Tab tab)
    {
        if (_tabs.Count < 2) return;
        var content = tab.Content;
        Remove(tab, dispose: false);
        var host = new CompareHost();
        host._startedTabs.Add(content); // 이미 시작한 탭이다 — 다시 읽지 않는다
        host.Add(content);
        WindowPlacementHelper.OpenNear(this, host);
    }

    // --- 키 ---

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var primary = Mods.Primary(e.KeyModifiers);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (_active is not null && primary && !shift && e.Key is Key.W or Key.F4)
        {
            e.Handled = true;
            _ = CloseTabAsync(_active);
            return;
        }
        if (_tabs.Count > 1 && _active is not null && primary && (e.Key == Key.Tab || e.Key is Key.PageUp or Key.PageDown))
        {
            var backward = e.Key == Key.PageUp || e.Key == Key.Tab && shift;
            var i = (_tabs.IndexOf(_active) + (backward ? -1 : 1) + _tabs.Count) % _tabs.Count;
            Select(_tabs[i]);
            e.Handled = true;
            return;
        }
        _active?.Content.HandleKey(e);
    }
}
