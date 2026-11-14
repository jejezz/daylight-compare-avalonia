using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaylightCompare.App.Compare;
using DaylightCompare.App.Dialogs;
using DaylightCompare.Domain.Folder;

namespace DaylightCompare.App.Tests;

/// <summary>폴더 비교 창 (4b): 줄·보기 필터·복사 확인(창 안 모달)·그림.</summary>
public sealed class FolderCompareWindowTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-fw").FullName;
    string L => Path.Combine(_dir, "left");
    string R => Path.Combine(_dir, "right");

    public void Dispose()
    {
        App.Settings.Update(s => s with { FolderShowFlags = (int)FolderShow.All, FolderAlwaysShowFolders = false });
        Directory.Delete(_dir, recursive: true);
    }

    /// <summary>설정은 앱 전체가 함께 쓰므로 테스트마다 처음 값으로 되돌려 놓고 시작한다 (앞 테스트의 보기 필터가 새지 않게).</summary>
    static void ResetSettings(FolderShow show = FolderShow.All) =>
        App.Settings.Update(s => s with { FolderShowFlags = (int)show, FolderAlwaysShowFolders = false });

    void Put(string root, string rel, string text, int minutesAgo = 0)
    {
        var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc).AddMinutes(-minutesAgo));
    }

    void MakeTrees()
    {
        Put(L, "same.txt", "a\nb\n"); Put(R, "same.txt", "a\nb\n");
        Put(L, "changed.txt", "left version\n", 5); Put(R, "changed.txt", "right version!\n");
        Put(L, "onlyLeft.txt", "x"); Put(R, "onlyRight.txt", "y");
        Put(L, "sub/deep.txt", "1\n"); Put(R, "sub/deep.txt", "2\n", 9);
        Put(L, "tidy/ok.txt", "same\n"); Put(R, "tidy/ok.txt", "same\n");
    }

    static async Task Settle(int rounds = 60)
    {
        for (var i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(15);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static string Shot(Window w, string name)
    {
        var path = Path.Combine(TestApp.Shots, name + ".png");
        using var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame!.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        return path;
    }

    static ListBox ListOf(Window w) => w.GetVisualDescendants().OfType<ListBox>().First();

    static string[] Names(Window w) => [.. ((IEnumerable<FolderRow>)ListOf(w).ItemsSource!).Select(r => new string(' ', r.Depth) + r.Node.Name)];

    [AvaloniaFact]
    public async Task 두_폴더를_BC처럼_나란히_보이고_같지_않은_폴더는_펼친다()
    {
        MakeTrees();
        ResetSettings();
        var w = FolderCompareWindow.Open(null, L, R);
        await Settle();

        // 폴더 먼저: 같지 않은 sub는 펴고, 모두 같은 tidy는 접는다.
        Assert.Equal(["sub", " deep.txt", "tidy", "changed.txt", "onlyLeft.txt", "onlyRight.txt", "same.txt"], Names(w));
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is "같음 2 · 다름 2 · 왼쪽에만 1 · 오른쪽에만 1" or "Same 2 · Different 2 · Left only 1 · Right only 1");
        Shot(w, "folder-compare");
        w.Close();
    }

    [AvaloniaFact]
    public async Task 보기_필터는_설정을_따르고_같은_것을_숨긴다()
    {
        MakeTrees();
        ResetSettings(FolderShow.All & ~FolderShow.Same);
        var w = FolderCompareWindow.Open(null, L, R);
        await Settle();
        Assert.Equal(["sub", " deep.txt", "changed.txt", "onlyLeft.txt", "onlyRight.txt"], Names(w)); // tidy(모두 같음)·same.txt는 숨는다
        Shot(w, "folder-compare-differences");
        w.Close();
    }

    [AvaloniaFact]
    public async Task Ctrl_R은_확인한_뒤_왼쪽을_오른쪽으로_복사하고_다시_비교한다()
    {
        MakeTrees();
        ResetSettings();
        var w = FolderCompareWindow.Open(null, L, R);
        await Settle();
        var list = ListOf(w);
        var rows = ((IEnumerable<FolderRow>)list.ItemsSource!).ToList();
        list.SelectedItems!.Add(rows.Single(r => r.Node.Name == "changed.txt"));
        list.SelectedItems.Add(rows.Single(r => r.Node.Name == "onlyLeft.txt"));
        list.Focus();

        w.KeyPressQwerty(PhysicalKey.R, RawInputModifiers.Control);
        await Settle(10);
        var host = w.GetVisualDescendants().OfType<DialogHost>().Single();
        Assert.True(host.IsOpen); // 창 안 모달 (OS 창이 아니다)
        Assert.Equal("right version!\n", File.ReadAllText(Path.Combine(R, "changed.txt")).Replace("\r\n", "\n")); // 확인 전에는 그대로
        Shot(w, "folder-compare-copy-confirm");

        var primary = host.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("accent"));
        primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Settle(80);

        Assert.Equal("left version", File.ReadAllText(Path.Combine(R, "changed.txt")).Trim());
        Assert.Equal("x", File.ReadAllText(Path.Combine(R, "onlyLeft.txt")));
        // 다시 비교가 끝나 줄 목록이 새로 만들어질 때까지 기다린다.
        Dictionary<string, FolderState> after = [];
        for (var i = 0; i < 100 && !(after.TryGetValue("changed.txt", out var st) && st == FolderState.Same); i++)
        {
            await Settle(5);
            after = ((IEnumerable<FolderRow>)ListOf(w).ItemsSource!).GroupBy(r => r.Node.Name).ToDictionary(g => g.Key, g => g.First().Node.State);
        }
        Assert.Equal(FolderState.Same, after["changed.txt"]);
        Assert.Equal(FolderState.Same, after["onlyLeft.txt"]);
        Assert.Equal(FolderState.RightOrphan, after["onlyRight.txt"]);
        Shot(w, "folder-compare-after-copy");
        w.Close();
    }

    [AvaloniaFact]
    public async Task 복사_확인을_취소하면_아무것도_바뀌지_않는다()
    {
        MakeTrees();
        ResetSettings();
        var w = FolderCompareWindow.Open(null, L, R);
        await Settle();
        var list = ListOf(w);
        list.SelectedItems!.Add(((IEnumerable<FolderRow>)list.ItemsSource!).Single(r => r.Node.Name == "onlyLeft.txt"));
        list.Focus();

        w.KeyPressQwerty(PhysicalKey.R, RawInputModifiers.Control);
        await Settle(10);
        var host = w.GetVisualDescendants().OfType<DialogHost>().Single();
        Assert.True(host.IsOpen);
        w.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await Settle(10);
        Assert.False(host.IsOpen);
        Assert.False(File.Exists(Path.Combine(R, "onlyLeft.txt")));
        w.Close();
    }

    [AvaloniaFact]
    public async Task 저장된_보기_필터가_아무것도_안_보이는_상태여도_목록은_보인다()
    {
        MakeTrees();
        ResetSettings(FolderShow.None); // 토글을 모두 끈 채로 저장된 설정 — 예전에는 창을 열 때마다 빈 목록이 나왔다
        var w = FolderCompareWindow.Open(null, L, R);
        await Settle();
        Assert.NotEmpty(Names(w));
        w.Close();
    }

    [AvaloniaFact]
    public async Task 필터가_모든_항목을_숨기면_이유와_모두_보기_단추를_보인다()
    {
        MakeTrees();
        ResetSettings(FolderShow.LeftOrphan); // 왼쪽에만 있는 것만 보기
        File.Delete(Path.Combine(L, "onlyLeft.txt")); // 그런데 왼쪽에만 있는 것이 없다 → 모두 숨겨진다
        var w = FolderCompareWindow.Open(null, L, R);
        await Settle();
        Assert.Empty(Names(w));
        var showAll = w.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ShowAllButton");
        Assert.True(showAll.IsEffectivelyVisible, "모든 항목이 숨겨졌는데 이유와 [모두 보기]가 보이지 않는다");
        using (var frame = w.CaptureRenderedFrame())
            frame!.Save(Path.Combine(TestApp.Shots, "folder-compare-all-hidden.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        showAll.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Settle(20);
        Assert.NotEmpty(Names(w));
        Assert.Equal((int)FolderShow.All, App.Settings.Value.FolderShowFlags);
        w.Close();
    }

    [AvaloniaFact]
    public async Task 주소_창에_경로를_쓰고_Enter를_누르면_그_폴더로_다시_비교한다()
    {
        MakeTrees();
        ResetSettings();
        var other = Path.Combine(_dir, "other");
        Put(other, "only-in-other.txt", "z");
        var w = FolderCompareWindow.Open(null, L, R);
        await Settle();
        Assert.DoesNotContain("only-in-other.txt", Names(w));
        var box = w.GetVisualDescendants().OfType<TextBox>().First(t => t.Text == L);
        box.Focus();
        box.Text = other;
        w.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await Settle();
        Assert.Equal(other, w.LeftRoot);
        Assert.Contains("only-in-other.txt", Names(w));
        w.Close();
    }

    [AvaloniaFact]
    public async Task 포함_칸과_제외_칸을_따로_적용한다()
    {
        MakeTrees();
        ResetSettings();
        var w = FolderCompareWindow.Open(null, L, R);
        await Settle();
        var boxes = w.GetVisualDescendants().OfType<TextBox>().Where(t => t.PlaceholderText is { Length: > 0 }).ToList();
        var include = boxes.First(t => t.PlaceholderText!.StartsWith("포함"));
        var exclude = boxes.First(t => t.PlaceholderText!.StartsWith("제외"));

        include.Focus();
        include.Text = "*.txt";
        exclude.Text = "only*;changed.txt";
        w.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await Settle();
        var names = Names(w);
        Assert.Contains("same.txt", names);
        Assert.DoesNotContain("onlyLeft.txt", names);
        Assert.DoesNotContain("onlyRight.txt", names);
        Assert.DoesNotContain("changed.txt", names);
        w.Close();
    }

    static async Task Invoke(Window w, string method)
    {
        var m = typeof(FolderCompareWindow).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var args = m.GetParameters().Length == 0 ? null : new object[] { -1 };
        await (Task)m.Invoke(w, args)!;
        await Settle();
    }

    static void Select(Window w, string name)
    {
        var list = ListOf(w);
        var row = ((IEnumerable<FolderRow>)list.ItemsSource!).First(r => r.Node.Name == name);
        list.UnselectAll();
        list.SelectedItem = row;
    }

    [AvaloniaFact]
    public async Task 지금_비교는_양쪽에_있는_폴더면_양쪽을_그_폴더로_바꾼다()
    {
        MakeTrees();
        ResetSettings();
        var w = FolderCompareWindow.Open(null, L, R);
        await Settle();
        Select(w, "sub");
        await Invoke(w, "CompareNowAsync");
        Assert.Equal(Path.Combine(L, "sub"), w.LeftRoot);
        Assert.Equal(Path.Combine(R, "sub"), w.RightRoot);
        Assert.Contains("deep.txt", Names(w));
        w.Close();
    }

    [AvaloniaFact]
    public async Task 지금_비교는_한쪽에만_있는_폴더면_있는_쪽만_바꾼다()
    {
        MakeTrees();
        Put(L, "lonely/a.txt", "1");
        ResetSettings();
        var w = FolderCompareWindow.Open(null, L, R);
        await Settle();
        Select(w, "lonely");
        await Invoke(w, "CompareNowAsync");
        Assert.Equal(Path.Combine(L, "lonely"), w.LeftRoot);
        Assert.Equal(R, w.RightRoot);
        w.Close();
    }

    [AvaloniaFact]
    public async Task 이전_위치로_되돌아가고_다음_위치로_다시_간다()
    {
        MakeTrees();
        ResetSettings();
        var w = FolderCompareWindow.Open(null, L, R);
        await Settle();
        Select(w, "sub");
        await Invoke(w, "CompareNowAsync");
        Assert.Equal(Path.Combine(L, "sub"), w.LeftRoot);
        await Invoke(w, "NavigateAsync");   // -1: 이전 위치
        Assert.Equal(L, w.LeftRoot);
        Assert.Equal(R, w.RightRoot);
        Assert.Contains("same.txt", Names(w));
        w.Close();
    }

    [AvaloniaFact]
    public async Task 한쪽에만_있는_폴더는_접힌_채로_보이고_펼치면_안쪽이_나온다()
    {
        MakeTrees();
        Put(L, "lonely/a.txt", "1");
        ResetSettings();
        var w = FolderCompareWindow.Open(null, L, R);
        await Settle();
        Assert.Contains("lonely", Names(w));
        Assert.DoesNotContain(" a.txt", Names(w)); // 들여쓴 이름 (깊이 1)
        Select(w, "lonely");
        w.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await Settle();
        Assert.Contains(" a.txt", Names(w));
        w.Close();
    }
}
