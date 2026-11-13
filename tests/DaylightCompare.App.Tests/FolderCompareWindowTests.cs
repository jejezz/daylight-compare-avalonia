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
            await Task.Delay(25);
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
}
