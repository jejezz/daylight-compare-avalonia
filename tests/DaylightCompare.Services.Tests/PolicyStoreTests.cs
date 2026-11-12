using System.Text.Json.Nodes;
using DaylightCompare.Domain.Policy;
using DaylightCompare.Services;

namespace DaylightCompare.Services.Tests;

public sealed class PolicyStoreTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-policy").FullName;
    readonly PolicyStore _store;

    public PolicyStoreTests() => _store = new PolicyStore(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    static JsonObject J(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public async Task 파일이_없으면_내장_정책으로_만든다()
    {
        var p = await _store.LoadAsync();
        Assert.True(File.Exists(_store.FilePath));
        Assert.False(p.IsFallback);
        Assert.Equal("c-family", p.Resolve("a.c").Format?.Id);
        var text = await File.ReadAllTextAsync(_store.FilePath);
        Assert.Contains("\"//.*$\"", text); // 정규식이 \ 같은 꼴로 바뀌지 않고 읽히는 모양으로 저장된다
    }

    [Fact]
    public async Task 손으로_고친_내용이_바로_반영된다_주석과_끝_쉼표도_받는다()
    {
        await File.WriteAllTextAsync(_store.FilePath, """
        // 내 정책
        { "schema": 1, "formats": [ { "id": "mine", "masks": ["*.c"], "view": "binary" }, ], }
        """);
        var p = await _store.LoadAsync();
        Assert.Equal(ViewKind.Binary, p.Resolve("a.c").View);
    }

    [Fact]
    public async Task 깨진_파일은_내장_정책을_쓰고_파일은_그대로_둔다_다시_만들면_broken으로_옮긴다()
    {
        await File.WriteAllTextAsync(_store.FilePath, "{ not json");
        var p = await _store.LoadAsync();
        Assert.True(p.IsFallback);
        Assert.Single(p.Warnings);
        Assert.Equal("{ not json", await File.ReadAllTextAsync(_store.FilePath));

        var failed = await _store.UpsertFormatAsync(J("""{ "id": "x", "masks": ["*.x"] }"""));
        Assert.False(failed.Ok); // 깨진 파일은 고치지 않는다
        Assert.Equal("{ not json", await File.ReadAllTextAsync(_store.FilePath));

        var reset = await _store.ResetToDefaultsAsync(new DateTime(2026, 11, 12, 9, 30, 0));
        Assert.True(reset.Ok);
        Assert.EndsWith(".broken-20261112-093000", reset.BackupPath);
        Assert.Equal("{ not json", await File.ReadAllTextAsync(reset.BackupPath!));
        Assert.False((await _store.LoadAsync()).IsFallback);
    }

    [Fact]
    public async Task 형식을_더하고_바꾸고_지울_때_모르는_키를_보존한다()
    {
        await File.WriteAllTextAsync(_store.FilePath, """
        { "schema": 1, "myNote": "keep me", "formats": [ { "id": "a", "masks": ["*.a"], "custom": 42 } ] }
        """);
        Assert.True((await _store.UpsertFormatAsync(J("""{ "id": "b", "masks": ["*.b"], "view": "image" }"""), position: 0)).Ok);
        Assert.True((await _store.UpsertFormatAsync(J("""{ "id": "a", "masks": ["*.a", "*.aa"], "custom": 43 }"""))).Ok);

        var json = J(await File.ReadAllTextAsync(_store.FilePath));
        Assert.Equal("keep me", (string?)json["myNote"]);
        var formats = (JsonArray)json["formats"]!;
        Assert.Equal(["b", "a"], formats.Select(f => (string?)f!["id"]));
        Assert.Equal(43, (int)formats[1]!["custom"]!);

        Assert.True((await _store.RemoveFormatAsync("b")).Ok);
        Assert.False((await _store.RemoveFormatAsync("nope")).Ok);
        Assert.Equal(["a"], ((await _store.LoadAsync()).Formats).Select(f => f.Id));
    }

    [Fact]
    public async Task 틀린_형식이나_문법은_저장하지_않는다()
    {
        await _store.LoadAsync();
        var before = await File.ReadAllTextAsync(_store.FilePath);
        Assert.False((await _store.UpsertFormatAsync(J("""{ "masks": ["*.x"] }"""))).Ok);
        Assert.False((await _store.UpsertFormatAsync(J("""{ "id": "x", "masks": [] }"""))).Ok);
        Assert.False((await _store.UpsertFormatAsync(J("""{ "id": "x", "masks": ["*.x"], "view": "nope" }"""))).Ok);
        Assert.False((await _store.UpsertGrammarAsync("g", J("""{ "comment": ["(bad"] }"""))).Ok);
        Assert.False((await _store.SetDefaultsAsync("nope", new JsonObject())).Ok);
        Assert.Equal(before, await File.ReadAllTextAsync(_store.FilePath));
    }

    [Fact]
    public async Task 마지막_변경을_되돌리고_다시_실행한다_손으로_고쳤으면_거절한다()
    {
        await _store.LoadAsync();
        var original = await File.ReadAllTextAsync(_store.FilePath);
        await _store.SetDefaultsAsync("text", J("""{ "tabWidth": 2 }"""));
        var changed = await File.ReadAllTextAsync(_store.FilePath);

        Assert.True((await _store.UndoAsync()).Ok);
        Assert.Equal(original, await File.ReadAllTextAsync(_store.FilePath));
        Assert.True((await _store.UndoAsync()).Ok); // 다시 실행
        Assert.Equal(changed, await File.ReadAllTextAsync(_store.FilePath));

        await File.WriteAllTextAsync(_store.FilePath, changed.Replace("\"tabWidth\": 2", "\"tabWidth\": 6"));
        Assert.False((await _store.UndoAsync()).Ok);
    }

    [Fact]
    public async Task 기본_정책으로_되돌리면_모르는_최상위_키는_남기고_백업을_만든다()
    {
        await File.WriteAllTextAsync(_store.FilePath, """{ "schema": 1, "myNote": "keep", "formats": [] }""");
        var r = await _store.RestoreDefaultsAsync(new DateTime(2026, 1, 2, 3, 4, 5));
        Assert.True(r.Ok);
        Assert.EndsWith(".backup-20260102-030405", r.BackupPath);
        var json = J(await File.ReadAllTextAsync(_store.FilePath));
        Assert.Equal("keep", (string?)json["myNote"]);
        Assert.Equal("c-family", (await _store.LoadAsync()).Resolve("a.c").Format?.Id);
        Assert.True(_store.CanUndo);
    }
}
