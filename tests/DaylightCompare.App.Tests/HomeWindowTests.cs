using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DaylightCompare.App.Compare;
using DaylightCompare.Domain.Settings;

namespace DaylightCompare.App.Tests;

/// <summary>홈 창 (Daylight PDF 홈 구성): 놓거나 연 것에 맞는 비교 창이 열리고, 최근 비교가 있으면 놓기 구역이 한 줄로 작아진다.</summary>
public sealed class HomeWindowTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-home").FullName;

    public HomeWindowTests() => App.Settings.Update(s => s with { CompareHistory = [] });

    public void Dispose()
    {
        App.Settings.Update(s => s with { CompareHistory = [] });
        Directory.Delete(_dir, recursive: true);
    }

    static async Task Settle(int rounds = 30)
    {
        for (var i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(15);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static void Shot(Window w, string name)
    {
        using var frame = w.CaptureRenderedFrame();
        frame!.Save(Path.Combine(TestApp.Shots, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    string File_(string name, string text = "x\n")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    string Folder(string name) => Directory.CreateDirectory(Path.Combine(_dir, name)).FullName;

    [AvaloniaFact]
    public void macOS_앱_메뉴에_앱_이름이_보인다() =>
        Assert.Equal("Daylight Compare", Avalonia.Application.Current!.Name); // 기본값 "Avalonia Application"이 아니다

    [AvaloniaFact]
    public async Task 폴더_하나를_놓으면_왼쪽만_채운_폴더_비교_창이_열린다()
    {
        var home = new HomeWindow();
        home.Show();
        var w = Assert.IsType<FolderCompareWindow>(await home.OpenPathsAsync([Folder("a")]));
        await Settle();
        Assert.Equal(Path.Combine(_dir, "a"), w.LeftRoot);
        Assert.Equal("", w.RightRoot);
        Assert.Empty(App.Settings.Value.CompareHistory); // 한쪽뿐이면 기록하지 않는다
        Shot(w, "folder-one-side");
        w.Close();
        home.Close();
    }

    [AvaloniaFact]
    public async Task 파일_하나를_놓으면_왼쪽만_채운_텍스트_비교_창이_열린다()
    {
        var home = new HomeWindow();
        home.Show();
        var l = File_("l.txt");
        var w = Assert.IsType<CompareWindow>(await home.OpenPathsAsync([l]));
        await Settle();
        Assert.Equal(l, w.LeftPath);
        Assert.Null(w.RightPath);
        w.Close();
        home.Close();
    }

    [AvaloniaFact]
    public async Task 두_개를_놓으면_바로_종류에_맞게_비교한다()
    {
        var home = new HomeWindow();
        home.Show();
        var f = Assert.IsType<FolderCompareWindow>(await home.OpenPathsAsync([Folder("a"), Folder("b")]));
        Assert.Equal((Path.Combine(_dir, "a"), Path.Combine(_dir, "b")), (f.LeftRoot, f.RightRoot));
        var (l, r) = (File_("l.txt"), File_("r.txt"));
        var c = Assert.IsType<CompareWindow>(await home.OpenPathsAsync([l, r]));
        Assert.Equal((l, r), (c.LeftPath, c.RightPath));
        await Settle();
        f.Close();
        c.Close();
        home.Close();
    }

    [AvaloniaFact]
    public async Task 빈_폴더_비교_창은_고르라고_알리고_한쪽씩_채울_수_있다()
    {
        var w = FolderCompareWindow.Open(null, null, null);
        await Settle();
        Assert.Equal(("", ""), (w.LeftRoot, w.RightRoot));
        Shot(w, "folder-empty");
        w.Close();
    }

    [AvaloniaFact]
    public async Task 최근_비교가_있으면_놓기_구역이_한_줄로_작아진다()
    {
        var home = new HomeWindow();
        home.Show();
        await Settle(10);
        Assert.True(home.FindControl<Control>("DropBig")!.IsVisible);
        Assert.False(home.FindControl<Control>("DropCompact")!.IsVisible);
        Shot(home, "home-empty");

        var (l, r) = (File_("l.txt"), File_("r.txt"));
        App.Settings.Update(s => s with { CompareHistory = [new CompareHistoryEntry { Left = l, Right = r, OpenedAt = DateTimeOffset.Now }] });
        await Settle(20);
        Assert.False(home.FindControl<Control>("DropBig")!.IsVisible);
        Assert.True(home.FindControl<Control>("DropCompact")!.IsVisible);
        Shot(home, "home-compact");
        home.Close();
    }
}
