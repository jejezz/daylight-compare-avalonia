using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaylightCompare.App.Compare;
using DaylightCompare.Domain.Folder;
using DaylightCompare.Domain.Settings;

namespace DaylightCompare.App.Tests;

/// <summary>이름 있는 세션(BC Sessions): 탭·필터·보기 설정을 저장하고 다시 열면 그대로 복원된다. 앱을 끌 때 열려 있던 창은 [이전 창 복원]으로.</summary>
public sealed class SessionTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-session").FullName;

    static void Reset() => App.Settings.Update(s => s with { Sessions = [], LastWindows = [], CompareHistory = [] });

    public void Dispose()
    {
        Reset();
        Directory.Delete(_dir, recursive: true);
    }

    static async Task Settle(int rounds = 40)
    {
        for (var i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(15);
        }
        Dispatcher.UIThread.RunJobs();
    }

    string Write(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    static string Shot(Window w, string name)
    {
        var dir = Environment.GetEnvironmentVariable("DCMP_SHOTS");
        if (string.IsNullOrEmpty(dir)) return "";
        Directory.CreateDirectory(dir);
        Dispatcher.UIThread.RunJobs();
        var path = Path.Combine(dir, name + ".png");
        w.CaptureRenderedFrame()?.Save(path);
        return path;
    }

    [AvaloniaFact]
    public async Task 파일_비교의_보기_설정이_세션으로_적히고_그대로_복원된다()
    {
        Reset();
        var l = Write("a/x.c", "int a; // one\n");
        var r = Write("b/x.c", "int a; // two\n");
        var tab = new SessionTab { Kind = SessionTab.File, Left = l, Right = r, ViewFilter = "context", ContextLines = 7, IgnoreUnimportant = true, FormatId = "" };
        var view = CompareView.Create(l, r, CompareOptions.None, recordHistory: false, restore: tab);
        Assert.Equal(tab, view.Capture());

        // 복원한 설정이 설정 파일(전역 값)을 바꾸지 않는다.
        var before = App.Settings.Value;
        var host = CompareHost.Open(null, view);
        await Settle();
        Assert.Equal(before.CompareViewFilter, App.Settings.Value.CompareViewFilter);
        Assert.Equal(before.CompareContextLines, App.Settings.Value.CompareContextLines);
        host.Close();
    }

    [AvaloniaFact]
    public async Task 폴더_비교의_이름_필터와_보기_필터가_세션으로_적히고_그대로_복원된다()
    {
        Reset();
        Write("a/x.txt", "1\n");
        Write("b/x.txt", "2\n");
        var tab = new SessionTab
        {
            Kind = SessionTab.Folder, Left = Path.Combine(_dir, "a"), Right = Path.Combine(_dir, "b"),
            Include = "*.txt", Exclude = "obj/", Show = (int)(FolderShow.Different | FolderShow.LeftOrphan), AlwaysFolders = true,
        };
        var view = FolderCompareView.Create(tab.Left, tab.Right, tab);
        Assert.Equal(tab, view.Capture());
        var host = CompareHost.Open(null, view);
        await Settle();
        Assert.Equal(tab, host.ActiveFolder!.Capture());
        host.Close();
    }

    [AvaloniaFact]
    public async Task 창의_탭들과_보던_탭이_저장되고_새_창으로_복원된다()
    {
        Reset();
        var l = Write("a/x.txt", "one\n");
        var r = Write("b/x.txt", "two\n");
        var host = FolderCompareView.Open(null, Path.Combine(_dir, "a"), Path.Combine(_dir, "b"));
        await Settle();
        host.OpenFileTab(l, r);
        await Settle();
        Assert.NotNull(host.ActiveFile);

        host.SaveSession("내 비교");
        var saved = App.Settings.Value.Sessions.Single();
        Assert.Equal("내 비교", saved.Name);
        Assert.Equal(["folder", "file"], saved.Window.Tabs.Select(t => t.Kind));
        Assert.Equal(1, saved.Window.Active);
        Assert.Equal("내 비교", host.SessionName);

        var restored = CompareHost.OpenSession(null, saved.Window, saved.Name)!;
        await Settle();
        Assert.Equal(2, restored.Tabs.Count);
        Assert.NotNull(restored.ActiveFile);            // 보던 탭이 앞에 온다
        Assert.Equal(l, restored.LeftPath);
        Assert.Equal(saved.Window.Tabs, restored.Capture().Tabs);
        Assert.Equal("내 비교", restored.SessionName);
        Shot(restored, "session-restored");
        restored.Close();
        host.Close();
    }

    [AvaloniaFact]
    public async Task 같은_이름으로_다시_저장하면_바뀌고_앱을_끄기_전의_창이_이전_창으로_적힌다()
    {
        Reset();
        var l = Write("a/x.txt", "one\n");
        var r = Write("b/x.txt", "two\n");
        var w = CompareView.Open(null, l, r);
        await Settle();
        w.SaveSession("A");
        w.SaveSession(" a ");
        Assert.Single(App.Settings.Value.Sessions);

        App.RememberOpenWindows();
        Assert.Contains(App.Settings.Value.LastWindows, x => x.Tabs.Any(t => t.Left == l && t.Right == r));
        w.Close();
        await Settle();
        App.RememberOpenWindows(); // 닫은 창은 더는 적히지 않는다
        Assert.DoesNotContain(App.Settings.Value.LastWindows, x => x.Tabs.Any(t => t.Left == l && t.Right == r));
    }

    [AvaloniaFact]
    public async Task 홈_창에_저장한_세션과_이전_창_복원이_보이고_열면_복원된다()
    {
        Reset();
        var l = Write("a/x.txt", "one\n");
        var r = Write("b/x.txt", "two\n");
        var tabs = new SessionWindow { Tabs = [new SessionTab { Left = l, Right = r }, new SessionTab { Kind = SessionTab.Folder, Left = Path.Combine(_dir, "a"), Right = Path.Combine(_dir, "b") }] };
        App.Settings.Update(s => s with
        {
            Sessions = [new SavedSession { Name = "펌웨어 비교", SavedAt = DateTimeOffset.Now, Window = tabs }],
            LastWindows = [tabs],
        });
        var home = new HomeWindow();
        home.Show();
        await Settle();
        var texts = home.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text ?? "").ToList();
        Assert.Contains("펌웨어 비교", texts);
        Assert.Contains(texts, t => t.StartsWith("탭 2개") || t.StartsWith("2 tabs"));
        Assert.Contains(home.GetVisualDescendants().OfType<Button>(), b => b.IsVisible && b.Content is string c && (c.Contains("이전에 열려 있던 창 복원 (1)") || c.Contains("Restore previously open windows (1)")));
        Shot(home, "home-sessions");

        var opened = home.OpenSession(App.Settings.Value.Sessions[0])!;
        await Settle();
        Assert.Equal(2, opened.Tabs.Count);
        opened.Close();
        var hostsBefore = CompareHost.OpenHosts.ToList();
        home.RestoreWindows();
        await Settle();
        var restoredHost = Assert.Single(CompareHost.OpenHosts.Except(hostsBefore));
        Assert.Equal(2, restoredHost.Tabs.Count);
        restoredHost.Close();

        // 지우면 목록에서 사라진다.
        App.Settings.Update(s => s with { Sessions = SessionList.Remove(s.Sessions, "펌웨어 비교") });
        await Settle();
        Assert.DoesNotContain("펌웨어 비교", home.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? ""));
        home.Close();
    }
}
