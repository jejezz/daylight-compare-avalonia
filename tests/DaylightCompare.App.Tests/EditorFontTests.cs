using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using DaylightCompare.App.Compare;
using DaylightCompare.App.Views;

namespace DaylightCompare.App.Tests;

/// <summary>편집기 글꼴: 기본은 고정폭, 설정에서 고정폭 글꼴만 고를 수 있고 바꾸면 열린 비교 창이 바로 따른다.</summary>
public sealed class EditorFontTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-font").FullName;

    public void Dispose()
    {
        App.Settings.Update(x => x with { EditorFontFamily = null });
        Directory.Delete(_dir, recursive: true);
    }

    static async Task Settle()
    {
        for (var i = 0; i < 30; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(15);
        }
        Dispatcher.UIThread.RunJobs();
    }

    string Write(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    static List<TextEditor> Editors(Window w) => w.GetVisualDescendants().OfType<TextEditor>().ToList();

    [AvaloniaFact]
    public async Task 기본_편집기_글꼴은_고정폭이다()
    {
        App.Settings.Update(x => x with { EditorFontFamily = null });
        var w = CompareWindow.Open(null, Write("a.txt", "il.WM0\n"), Write("b.txt", "il.WM1\n"));
        await Settle();
        var editors = Editors(w);
        Assert.Equal(2, editors.Count);
        foreach (var e in editors)
        {
            Assert.Equal(EditorFonts.Default().Name, e.FontFamily.Name);
            if (EditorFonts.DefaultName is { Length: > 0 } name) Assert.True(EditorFonts.IsMonospaced(name), name);
        }
        w.Close();
    }

    [AvaloniaFact]
    public async Task 설정에서_바꾸면_열린_창이_따르고_고정폭이_아니면_기본으로_돌아간다()
    {
        var monos = await EditorFonts.MonospacedAsync();
        Assert.NotEmpty(monos);
        Assert.All(monos, n => Assert.True(EditorFonts.IsMonospaced(n), n));
        var other = monos.FirstOrDefault(n => n != EditorFonts.DefaultName) ?? monos[0];

        var w = CompareWindow.Open(null, Write("a.txt", "x\n"), Write("b.txt", "y\n"));
        await Settle();
        App.Settings.Update(x => x with { EditorFontFamily = other });
        await Settle();
        Assert.All(Editors(w), e => Assert.Equal(other, e.FontFamily.Name));

        // 비례 글꼴 이름이 파일에 들어 있어도 편집기는 고정폭으로 남는다.
        var proportional = Avalonia.Media.FontManager.Current.SystemFonts.Select(f => f.Name)
            .FirstOrDefault(n => !EditorFonts.IsMonospaced(n));
        if (proportional is not null)
        {
            App.Settings.Update(x => x with { EditorFontFamily = proportional });
            await Settle();
            Assert.All(Editors(w), e => Assert.Equal(EditorFonts.Default().Name, e.FontFamily.Name));
        }
        w.Close();
    }

    [AvaloniaFact]
    public async Task 설정_창에_편집기_글꼴_칸이_있다()
    {
        App.Settings.Update(x => x with { EditorFontFamily = null });
        var home = new Window();
        home.Show();
        SettingsWindow.Open(home);
        await Settle();
        var settings = SettingsWindow.Current;
        Assert.NotNull(settings);
        var texts = settings!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.True(texts.Contains("편집기 글꼴") || texts.Contains("Editor font"));
        var name = EditorFonts.DefaultName!;
        Assert.True(texts.Contains($"{name} (기본)") || texts.Contains($"{name} (default)"));
        using (var frame = settings.CaptureRenderedFrame())
            frame?.Save(Path.Combine(TestApp.Shots, "settings-editor-font.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        settings.Close();
        home.Close();
    }
}
