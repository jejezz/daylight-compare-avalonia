using System.Text.Json;
using DaylightCompare.Data;
using DaylightCompare.Domain.Settings;
using DaylightCompare.Domain.Theme;
using DaylightCompare.Domain.Window;

namespace DaylightCompare.Data.Tests;

public sealed class JsonSettingsRepositoryTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dc-settings-test").FullName;
    string File_ => Path.Combine(_dir, "sub", "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    JsonSettingsRepository Repo() => new(File_);

    void WriteRaw(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
        File.WriteAllText(File_, json);
    }

    JsonElement Saved()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(File_));
        return doc.RootElement.Clone();
    }

    static readonly AppSettings Full = new()
    {
        Theme = ThemeMode.Dark,
        Palette = ColorPalette.Rose,
        TextScale = 1.15,
        Locale = AppLocale.English,
        UiFontFamily = "Malgun Gothic",
        Window = new WindowPlacement { X = 10, Y = 20, Width = 1000, Height = 600, Scale = 1.5, Maximized = true },
    };

    [Fact]
    public void 저장한_값을_그대로_다시_읽는다()
    {
        Repo().Save(Full);
        var back = Repo().Load();
        Assert.Equal(Full.Theme, back.Theme);
        Assert.Equal(Full.Palette, back.Palette);
        Assert.Equal(Full.TextScale, back.TextScale);
        Assert.Equal(Full.Locale, back.Locale);
        Assert.Equal(Full.UiFontFamily, back.UiFontFamily);
        Assert.Equal(Full.Window, back.Window);
    }

    [Fact]
    public void 파일이_없으면_기본값()
    {
        var s = Repo().Load();
        Assert.Equal(ThemeMode.System, s.Theme);
        Assert.Equal(ColorPalette.Saturn, s.Palette);
    }

    [Fact]
    public void 저장하면_폴더를_만들고_임시_파일을_남기지_않는다()
    {
        Repo().Save(Full);
        Assert.True(File.Exists(File_));
        Assert.False(File.Exists(File_ + ".tmp"));
    }

    [Fact]
    public void 깨진_파일은_기본값으로_읽고_broken으로_옮겨_둔다()
    {
        WriteRaw("{ 이건 JSON이 아니다");
        var s = Repo().Load();
        Assert.Equal(ThemeMode.System, s.Theme);
        Assert.False(File.Exists(File_));
        Assert.Equal("{ 이건 JSON이 아니다", File.ReadAllText(File_ + ".broken"));
    }

    [Fact]
    public void 모르는_키는_무시하고_없는_키는_기본값이다()
    {
        WriteRaw("""{ "theme_mode": "dark", "future_setting": { "a": 1 } }""");
        var s = Repo().Load();
        Assert.Equal(ThemeMode.Dark, s.Theme);
        Assert.Equal(ColorPalette.Saturn, s.Palette);
    }

    [Fact]
    public void 모르는_열거값은_기본값으로_읽는다()
    {
        WriteRaw("""{ "theme_mode": "sepia", "accent_palette": "neon" }""");
        var s = Repo().Load();
        Assert.Equal(ThemeMode.System, s.Theme);
        Assert.Equal(ColorPalette.Saturn, s.Palette);
    }

    [Fact]
    public void 저장_키와_값은_모든_앱_공통_규약을_따른다()
    {
        Repo().Save(Full);
        var j = Saved();
        Assert.Equal("dark", j.GetProperty("theme_mode").GetString());
        Assert.Equal("rose", j.GetProperty("accent_palette").GetString());
        Assert.Equal(1.15, j.GetProperty("text_scale").GetDouble());
        Assert.Equal("en", j.GetProperty("app_locale").GetString());
        var w = j.GetProperty("window_placement");
        Assert.Equal(10, w.GetProperty("x").GetDouble());
        Assert.Equal(20, w.GetProperty("y").GetDouble());
        Assert.Equal(1000, w.GetProperty("w").GetDouble());
        Assert.Equal(600, w.GetProperty("h").GetDouble());
        Assert.Equal(1.5, w.GetProperty("scale").GetDouble());
        Assert.True(w.GetProperty("max").GetBoolean());
        Assert.Equal("Malgun Gothic", j.GetProperty("ui_font_family").GetString());
    }

    [Fact]
    public void highContrast는_camelCase_그대로_저장된다()
    {
        Repo().Save(new AppSettings { Palette = ColorPalette.HighContrast });
        Assert.Equal("highContrast", Saved().GetProperty("accent_palette").GetString());
        Assert.Equal(ColorPalette.HighContrast, Repo().Load().Palette);
    }

    [Fact]
    public void 범위_밖_글자_배율은_범위_안으로_읽는다()
    {
        WriteRaw("""{ "text_scale": 3.5 }""");
        Assert.Equal(TextScales.Max, Repo().Load().TextScale);
    }

    [Fact]
    public void 손상된_창_위치_하나가_설정_전체를_못_읽게_만들지_않는다()
    {
        WriteRaw("""{ "theme_mode": "light", "window_placement": { "x": "abc" } }""");
        // 숫자가 아닌 값은 읽기에 실패하지만, 설정은 기본값으로라도 시작해야 한다 (깨진 파일 처리).
        var s = Repo().Load();
        Assert.Equal(ThemeMode.System, s.Theme);
        Assert.True(File.Exists(File_ + ".broken"));
    }

    [Fact]
    public void 필드가_빠진_창_위치는_읽히지만_쓸_수_없는_값이다()
    {
        WriteRaw("""{ "window_placement": { "x": 1 } }""");
        var w = Repo().Load().Window;
        Assert.NotNull(w);
        Assert.False(w!.IsValid);
    }

    [Fact]
    public void 언어는_app_locale_키에_ko_또는_en으로_저장되고_없으면_시스템이다()
    {
        Repo().Save(new AppSettings { Locale = AppLocale.Korean });
        Assert.Equal("ko", Saved().GetProperty("app_locale").GetString());
        Assert.Equal(AppLocale.Korean, Repo().Load().Locale);

        Repo().Save(new AppSettings());
        Assert.Equal(JsonValueKind.Null, Saved().GetProperty("app_locale").ValueKind);
        Assert.Null(Repo().Load().Locale);
    }

    [Fact]
    public void 모르는_언어_값은_시스템으로_읽는다()
    {
        WriteRaw("""{ "app_locale": "fr" }""");
        Assert.Null(Repo().Load().Locale);
    }

    [Fact]
    public void 덮어쓰기()
    {
        Repo().Save(new AppSettings { Theme = ThemeMode.Dark });
        Repo().Save(new AppSettings { Theme = ThemeMode.Light });
        Assert.Equal(ThemeMode.Light, Repo().Load().Theme);
    }

    [Fact]
    public void 설정_파일_위치는_앱_이름_폴더_아래의_settings_json이다()
    {
        var file = JsonSettingsRepository.DefaultFile("DaylightCompare");
        Assert.Equal("settings.json", Path.GetFileName(file));
        Assert.Equal("DaylightCompare", Path.GetFileName(Path.GetDirectoryName(file)));
    }

    [Fact]
    public void DCMP_DATA_DIR가_있으면_OS와_상관없이_그_폴더를_쓴다()
    {
        var before = Environment.GetEnvironmentVariable(JsonSettingsRepository.DataDirOverride);
        try
        {
            Environment.SetEnvironmentVariable(JsonSettingsRepository.DataDirOverride, "/tmp/dcmp-x");
            Assert.Equal(Path.Combine("/tmp/dcmp-x", "DaylightCompare", "settings.json"), JsonSettingsRepository.DefaultFile("DaylightCompare"));
        }
        finally { Environment.SetEnvironmentVariable(JsonSettingsRepository.DataDirOverride, before); }
    }

}

/// <summary>최근 비교 목록과 마지막 고른 폴더가 설정 파일에 저장되고 (AOT 소스 생성기로) 그대로 읽힌다.</summary>
public sealed class HistorySettingsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dc-history-test").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void 최근_비교와_마지막_폴더를_저장하고_읽는다()
    {
        var path = Path.Combine(_dir, "settings.json");
        var repo = new JsonSettingsRepository(path);
        var at = new DateTimeOffset(2026, 11, 13, 9, 30, 0, TimeSpan.FromHours(9));
        repo.Save(new AppSettings
        {
            LastPickDir = @"C:\work",
            CompareHistory =
            [
                new CompareHistoryEntry { Left = @"C:\a\x.txt", Right = @"C:\b\x.txt", OpenedAt = at },
                new CompareHistoryEntry { Left = @"C:\a", Right = @"C:\b", Kind = CompareHistoryEntry.Folders, OpenedAt = at.AddHours(-1) },
            ],
        });
        var back = new JsonSettingsRepository(path).Load();
        Assert.Equal(@"C:\work", back.LastPickDir);
        Assert.Equal(2, back.CompareHistory.Count);
        Assert.Equal(@"C:\a\x.txt", back.CompareHistory[0].Left);
        Assert.True(back.CompareHistory[1].IsFolders);
        Assert.Equal(at, back.CompareHistory[0].OpenedAt);
        Assert.Contains("\"compare_history\"", File.ReadAllText(path));
    }

    [Fact]
    public void 옛_설정_파일에는_기록이_없어도_읽힌다()
    {
        var path = Path.Combine(_dir, "old.json");
        File.WriteAllText(path, "{ \"theme_mode\": \"dark\" }");
        var back = new JsonSettingsRepository(path).Load();
        Assert.Empty(back.CompareHistory);
        Assert.Null(back.LastPickDir);
    }
}
