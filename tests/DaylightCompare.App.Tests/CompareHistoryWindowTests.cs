using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaylightCompare.App.Compare;
using DaylightCompare.Domain.Settings;

namespace DaylightCompare.App.Tests;

/// <summary>최근 비교: 창을 열면 기록되고, 홈 창에 한 줄씩 보이며, 없어진 경로는 흐리게 보인다.</summary>
public sealed class CompareHistoryWindowTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-hist").FullName;

    public void Dispose()
    {
        App.Settings.Update(s => s with { CompareHistory = [], LastPickDir = null });
        Directory.Delete(_dir, recursive: true);
    }

    static void Reset() => App.Settings.Update(s => s with { CompareHistory = [], LastPickDir = null });

    static async Task Settle(int rounds = 30)
    {
        for (var i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(15);
        }
        Dispatcher.UIThread.RunJobs();
    }

    string File_(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    static List<Border> Rows(Window w) => [.. w.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("historyrow"))];

    [AvaloniaFact]
    public async Task 비교_창을_열면_최근_비교에_기록된다()
    {
        Reset();
        var l = File_("l.txt", "a\n");
        var r = File_("r.txt", "b\n");
        var w = CompareWindow.Open(null, l, r);
        await Settle();
        var h = App.Settings.Value.CompareHistory;
        Assert.Single(h);
        Assert.Equal((l, r, CompareHistoryEntry.Files), (h[0].Left, h[0].Right, h[0].Kind));

        // 같은 쌍을 다시 열어도 하나, 폴더 비교는 따로.
        CompareWindow.Open(null, l, r).Close();
        await Settle();
        Assert.Single(App.Settings.Value.CompareHistory);

        Directory.CreateDirectory(Path.Combine(_dir, "a"));
        Directory.CreateDirectory(Path.Combine(_dir, "b"));
        var f = FolderCompareWindow.Open(null, Path.Combine(_dir, "a"), Path.Combine(_dir, "b"));
        await Settle();
        Assert.Equal(2, App.Settings.Value.CompareHistory.Count);
        Assert.True(App.Settings.Value.CompareHistory[0].IsFolders); // 가장 최근이 맨 앞
        f.Close();
        w.Close();
    }

    [AvaloniaFact]
    public async Task 붙여_넣은_글처럼_경로가_없는_쪽이_있으면_기록하지_않는다()
    {
        Reset();
        var w = CompareWindow.Open(null, File_("only.txt", "x\n"), null);
        await Settle();
        Assert.Empty(App.Settings.Value.CompareHistory);
        w.Close();
    }

    [AvaloniaFact]
    public async Task 홈_창에_최근_비교가_보이고_없어진_경로는_흐리게_보인다()
    {
        Reset();
        var l = File_("l2.txt", "a\n");
        var r = File_("r2.txt", "b\n");
        var gone = Path.Combine(_dir, "gone.txt");
        App.Settings.Update(s => s with
        {
            CompareHistory =
            [
                new CompareHistoryEntry { Left = l, Right = r, OpenedAt = DateTimeOffset.Now },
                new CompareHistoryEntry { Left = l, Right = gone, OpenedAt = DateTimeOffset.Now.AddDays(-1) },
                new CompareHistoryEntry { Left = _dir, Right = _dir, Kind = CompareHistoryEntry.Folders, OpenedAt = DateTimeOffset.Now.AddDays(-5) },
            ],
        });
        var home = new HomeWindow();
        home.Show();
        await Settle(40);

        var rows = Rows(home);
        Assert.Equal(3, rows.Count);
        Assert.Equal(1, rows[0].Opacity);
        Assert.Equal(0.5, rows[1].Opacity); // gone.txt가 없다
        Assert.Equal(1, rows[2].Opacity);
        using (var frame = home.CaptureRenderedFrame())
            frame!.Save(Path.Combine(TestApp.Shots, "home-history.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        // 목록이 바뀌면 다시 그린다 (모두 지우면 구역이 사라진다).
        App.Settings.Update(s => s with { CompareHistory = [] });
        await Settle(10);
        Assert.Empty(Rows(home));
        home.Close();
    }
}
