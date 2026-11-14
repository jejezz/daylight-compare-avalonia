using DaylightCompare.Data;
using DaylightCompare.Domain.Settings;

namespace DaylightCompare.Data.Tests;

public sealed class SessionSettingsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dc-session-test").FullName;
    string File_ => Path.Combine(_dir, "sub", "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    JsonSettingsRepository Repo() => new(File_);

    void WriteRaw(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
        File.WriteAllText(File_, json);
    }

    [Fact]
    public void 저장한_세션과_마지막_창이_저장하고_읽으면_그대로_돌아온다()
    {
        var window = new SessionWindow
        {
            Active = 1,
            Tabs =
            [
                new SessionTab { Kind = SessionTab.Folder, Left = "/a", Right = "/b", Include = "*.c", Exclude = "obj/", Show = 5, AlwaysFolders = true },
                new SessionTab { Kind = SessionTab.File, Left = "/a/x.c", Right = "/b/x.c", ViewFilter = "context", ContextLines = 7, IgnoreUnimportant = true, FormatId = "" },
            ],
        };
        var settings = new AppSettings
        {
            Sessions = [new SavedSession { Name = "펌웨어 비교", SavedAt = new DateTimeOffset(2026, 11, 14, 3, 0, 0, TimeSpan.Zero), Window = window }],
            LastWindows = [window],
        };
        Repo().Save(settings);
        var loaded = Repo().Load();
        Assert.Equal("펌웨어 비교", loaded.Sessions[0].Name);
        Assert.Equal(window.Tabs, loaded.Sessions[0].Window.Tabs);
        Assert.Equal(1, loaded.Sessions[0].Window.Active);
        Assert.Equal(window.Tabs, loaded.LastWindows[0].Tabs);
        Assert.Equal("", loaded.Sessions[0].Window.Tabs[1].FormatId); // 빈 글(형식 없음)과 null(자동)이 구분된다
    }

    [Fact]
    public void 세션이_없는_예전_파일은_빈_목록으로_읽고_깨진_세션은_버린다()
    {
        WriteRaw("{\"schema\": 1}");
        var old = Repo().Load();
        Assert.Empty(old.Sessions);
        Assert.Empty(old.LastWindows);

        WriteRaw("{\"sessions\":[{\"name\":\"깨짐\"},{\"name\":\"멀쩡\",\"window\":{\"tabs\":[{\"kind\":\"file\",\"left\":\"a\",\"right\":\"b\"}]}}],\"last_windows\":[{}]}");
        var loaded = Repo().Load();
        Assert.Equal(["멀쩡"], loaded.Sessions.Select(x => x.Name));
        Assert.Single(loaded.Sessions[0].Window.Tabs);
    }
}
