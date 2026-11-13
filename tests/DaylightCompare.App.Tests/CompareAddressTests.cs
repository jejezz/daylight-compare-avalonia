using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaylightCompare.App.Compare;

namespace DaylightCompare.App.Tests;

/// <summary>텍스트 비교의 주소 창: 직접 고쳐 Enter를 누르면 그 파일을 열고, 없으면 알리고 지금 파일을 지킨다.</summary>
public sealed class CompareAddressTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-addr").FullName;

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

    string File_(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>주소 창 두 개 (찾기·바꾸기 칸은 폭 300): 왼쪽이 먼저.</summary>
    static TextBox Address(Window w, bool left) => w.GetVisualDescendants().OfType<TextBox>()
        .Where(t => t.Width != 300 && t.PlaceholderText is not null).ElementAt(left ? 0 : 1);

    [AvaloniaFact]
    public async Task 주소_창에_쓴_파일을_Enter로_연다()
    {
        var (l, r, other) = (File_("l.txt", "a\n"), File_("r.txt", "b\n"), File_("other.txt", "c\n"));
        var w = CompareWindow.Open(null, l, r);
        w.Show();
        await Settle();
        var box = Address(w, left: false);
        Assert.Equal(r, box.Text);

        box.Focus();
        await Settle(5);
        box.Text = other;
        w.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await Settle();
        Assert.Equal(other, w.RightPath);
        Assert.Equal(other, box.Text);
        Assert.Equal(l, w.LeftPath);
        w.Close();
    }

    [AvaloniaFact]
    public async Task 없는_파일이면_지금_파일을_그대로_둔다()
    {
        var (l, r) = (File_("l.txt", "a\n"), File_("r.txt", "b\n"));
        var w = CompareWindow.Open(null, l, r);
        w.Show();
        await Settle();
        var box = Address(w, left: true);
        box.Focus();
        await Settle(5);
        var gone = Path.Combine(_dir, "gone.txt");
        box.Text = gone;
        w.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await Settle();
        Assert.Equal(l, w.LeftPath); // 알림 대화상자가 뜨고 왼쪽은 그대로
        box.Text = _dir; // 폴더도 열지 않는다
        w.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await Settle();
        Assert.Equal(l, w.LeftPath);
        w.Close();
    }
}
