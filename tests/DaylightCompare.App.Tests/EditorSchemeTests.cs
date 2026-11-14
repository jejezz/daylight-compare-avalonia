using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using DaylightCompare.App.Compare;
using DaylightCompare.Domain.Settings;
using DaylightCompare.Domain.Theme;
using DaylightCompare.App.Views;
using Avalonia.VisualTree;

namespace DaylightCompare.App.Tests;

/// <summary>편집기 색상 구성표: 바탕·글자·차이 색이 구성표를 따르고, 고르지 않으면 앱 테마를 따른다.</summary>
public sealed class EditorSchemeTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-scheme").FullName;

    public void Dispose()
    {
        App.Settings.Update(s => s with { EditorScheme = null, Theme = ThemeMode.System });
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

    static uint ColorOf(string key) =>
        ((ISolidColorBrush)Application.Current!.Resources[key]!).Color.ToUInt32();

    static string? Shot(Avalonia.Controls.Window w, string name)
    {
        var dir = Environment.GetEnvironmentVariable("DCMP_SHOTS");
        if (string.IsNullOrEmpty(dir)) return null;
        Directory.CreateDirectory(dir);
        Dispatcher.UIThread.RunJobs();
        var path = Path.Combine(dir, name + ".png");
        w.CaptureRenderedFrame()?.Save(path);
        return path;
    }

    [AvaloniaFact]
    public async Task 구성표를_고르면_편집기_바탕_글자색과_차이_색이_그_구성표를_따른다()
    {
        App.Settings.Update(s => s with { Theme = ThemeMode.Light, EditorScheme = null });
        await Settle();
        var appBg = ColorOf("AppEdBg");
        var lightChanged = ColorOf("AppDiffChanged");
        Assert.Equal(DiffColors.For(dark: false, highContrast: false).ChangedLine, lightChanged);

        // 밝은 앱 테마에서 어두운 구성표(Dracula): 바탕은 구성표의 것, 차이 색은 어두운 벌.
        App.Settings.Update(s => s with { EditorScheme = "dracula" });
        await Settle();
        Assert.Equal(0xFF282A36u, ColorOf("AppEdBg"));
        Assert.NotEqual(appBg, ColorOf("AppEdBg"));
        Assert.Equal(DiffColors.For(dark: true, highContrast: false).ChangedLine, ColorOf("AppDiffChanged"));
        Assert.Equal(ThemeService.EditorScheme?.Id, "dracula");

        // 어두운 앱 테마에서 밝은 구성표(Solarized Light): 차이 색은 밝은 벌.
        App.Settings.Update(s => s with { Theme = ThemeMode.Dark, EditorScheme = "solarized-light" });
        await Settle();
        Assert.Equal(0xFFFDF6E3u, ColorOf("AppEdBg"));
        Assert.Equal(DiffColors.For(dark: false, highContrast: false).ChangedLine, ColorOf("AppDiffChanged"));

        // 되돌리면 앱 테마(어두움)의 편집기 색과 차이 색.
        App.Settings.Update(s => s with { EditorScheme = null });
        await Settle();
        Assert.Null(ThemeService.EditorScheme);
        Assert.Equal(DiffColors.For(dark: true, highContrast: false).ChangedLine, ColorOf("AppDiffChanged"));
    }

    [AvaloniaFact]
    public async Task 비교_창이_구성표의_문법_색으로_다시_입혀진다()
    {
        var l = Path.Combine(_dir, "a.c");
        var r = Path.Combine(_dir, "b.c");
        File.WriteAllText(l, "int a = 1; // one\nreturn a;\n");
        File.WriteAllText(r, "int a = 2; // two\nreturn a;\n");
        App.Settings.Update(s => s with { Theme = ThemeMode.Light, EditorScheme = "dracula" });
        var w = CompareView.Open(null, l, r);
        await Settle();
        Shot(w, "scheme-dracula-on-light");
        App.Settings.Update(s => s with { EditorScheme = "solarized-light" });
        await Settle();
        Shot(w, "scheme-solarized-light");
        App.Settings.Update(s => s with { Theme = ThemeMode.Dark, EditorScheme = "solarized-dark" });
        await Settle();
        Shot(w, "scheme-solarized-dark");
        App.Settings.Update(s => s with { EditorScheme = null });
        await Settle();
        Shot(w, "scheme-follow-app-dark");
        w.Close();
    }

    [AvaloniaFact]
    public void 모든_구성표의_색을_읽을_수_있고_견본_색이_있다()
    {
        foreach (var scheme in EditorSchemes.All)
        {
            var colors = EditorSchemes.ColorsOf(scheme);
            Assert.True(colors is not null, scheme.Id);
            Assert.NotEmpty(colors!.Swatch);
        }
        Assert.True(EditorSchemes.ColorsOf(EditorSchemes.Find("dracula")!)!.Dark);
        Assert.False(EditorSchemes.ColorsOf(EditorSchemes.Find("solarized-light")!)!.Dark);
        Assert.Null(EditorSchemes.Find("없는-구성표"));
    }

    [AvaloniaFact]
    public async Task 설정_창에_편집기_색상_구성표_칸이_있고_고른_이름이_보인다()
    {
        App.Settings.Update(x => x with { EditorScheme = "dracula" });
        var home = new Avalonia.Controls.Window();
        home.Show();
        SettingsWindow.Open(home);
        await Settle();
        var settings = SettingsWindow.Current;
        Assert.NotNull(settings);
        var texts = settings!.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Select(t => t.Text).ToList();
        Assert.True(texts.Contains("편집기 색상 구성표") || texts.Contains("Editor color scheme"));
        Assert.Contains("Dracula", texts);

        // 목록 상자를 직접 열어 항목 수를 확인한다 (앱 테마를 따름 + 읽을 수 있는 구성표 전부).
        var picker = settings.GetVisualDescendants().OfType<Avalonia.Controls.Button>().First(b => b.Classes.Contains("picker") && b.Flyout is Avalonia.Controls.Flyout { Content: Avalonia.Controls.ListBox });
        var list = (Avalonia.Controls.ListBox)((Avalonia.Controls.Flyout)picker.Flyout!).Content!;
        Assert.Equal(EditorSchemes.All.Count + 1, list.ItemCount);

        using (var frame = settings.CaptureRenderedFrame())
            frame?.Save(Path.Combine(TestApp.Shots, "settings-editor-scheme.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        settings.Close();
        home.Close();
    }
}
