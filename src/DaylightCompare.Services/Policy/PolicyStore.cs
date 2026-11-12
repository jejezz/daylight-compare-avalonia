// Jack's Bean(jacksbean-avalonia @ 1af8cde)의 PolicyStore 패턴을 따른다: 매번 파일을 다시 읽고, 깨진 파일은 덮어쓰지 않으며,
// 쓸 때는 임시 파일 → 교체, 사용자가 적은 모르는 키는 보존, 마지막 변경 한 단계 실행 취소.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Services;

public sealed record PolicySaveResult(string? Error)
{
    public bool Ok => Error is null;
    public static PolicySaveResult Success { get; } = new((string?)null);
}

/// <summary>기본 정책으로 되돌린 결과. <see cref="BackupPath"/>는 이전 파일을 옮겨 둔 곳(없었으면 null).</summary>
public sealed record PolicyResetResult(string? Error, string? BackupPath)
{
    public bool Ok => Error is null;
}

/// <summary>
/// Compare Policy 파일(<c>&lt;사용자 데이터 폴더&gt;/policy.json</c>)을 읽고 쓴다 (ROADMAP §4-4).
/// <list type="bullet">
/// <item><see cref="LoadAsync"/>는 매번 파일을 다시 읽는다 — 손으로 고쳐도 바로 반영된다. 파일이 없으면 내장 정책으로 만든다.</item>
/// <item>파일 전체를 못 읽으면(JSON 깨짐·schema 다름) 내장 정책을 대신 쓰고(<see cref="ComparePolicy.IsFallback"/>) 파일은 그대로 둔다.
/// [기본 정책 다시 만들기](<see cref="ResetToDefaultsAsync"/>)가 이전 파일을 <c>.broken-날짜시각</c>으로 옮기고 새로 만든다.</item>
/// <item>고치기(<see cref="UpsertFormatAsync"/> 등)는 사용자가 적은 모르는 키를 지우지 않는다. 깨진 파일은 고치지 않는다.</item>
/// <item>마지막 변경 하나는 <see cref="UndoAsync"/>로 되돌린다 (그 사이 파일을 손으로 고쳤으면 거절).</item>
/// </list>
/// </summary>
public sealed class PolicyStore(string directory)
{
    public const string FileName = "policy.json";

    static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        // 정규식의 \ · < · ' 를 \ 같은 꼴로 바꾸지 않는다 (사람이 읽고 고치는 파일).
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    string? _previousText;
    string? _lastWrittenText;

    public string Directory { get; } = directory;
    public string FilePath => Path.Combine(Directory, FileName);

    /// <summary>파일 이름 짝짓기의 기본 대소문자 처리: Linux만 대소문자를 가린다.</summary>
    public static bool NamesIgnoreCase => !OperatingSystem.IsLinux();

    public async Task<ComparePolicy> LoadAsync()
    {
        if (!File.Exists(FilePath))
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                await WriteAsync(Serialize(BuiltinPolicy.Json()));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return ComparePolicy.Fallback($"{FilePath}: 만들지 못했습니다 ({e.Message}) — 내장 정책을 씁니다");
            }
            return ComparePolicy.Builtin;
        }
        try
        {
            return ComparePolicy.FromJson(ParseObject(await File.ReadAllTextAsync(FilePath)));
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return ComparePolicy.Fallback($"{FilePath}: {e.Message} — 내장 정책을 씁니다(파일은 그대로 둠)");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ComparePolicy.Fallback($"{FilePath}: 읽지 못했습니다 ({e.Message}) — 내장 정책을 씁니다");
        }
    }

    /// <summary>정책 파일을 내장 정책으로 다시 만든다. 지금 파일은 지우지 않고 <c>.broken-날짜시각</c>으로 옮긴다. 실행 취소 기록은 비운다.</summary>
    public async Task<PolicyResetResult> ResetToDefaultsAsync(DateTime? now = null)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            string? backup = null;
            if (File.Exists(FilePath))
            {
                backup = $"{FilePath}.broken-{now ?? DateTime.Now:yyyyMMdd-HHmmss}";
                File.Move(FilePath, backup, overwrite: true);
            }
            await WriteAsync(Serialize(BuiltinPolicy.Json()));
            _previousText = _lastWrittenText = null;
            return new PolicyResetResult(null, backup);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new PolicyResetResult($"정책 파일을 다시 만들지 못했습니다: {e.Message}", null);
        }
    }

    /// <summary>
    /// 멀쩡한 정책을 내장 정책으로 되돌린다: <c>defaults</c>·<c>formats</c>·<c>grammars</c>만 바꾸고 모르는 최상위 키는 둔다.
    /// 이전 내용은 <c>.backup-날짜시각</c>으로 남기고 <see cref="UndoAsync"/>로도 되돌릴 수 있다.
    /// </summary>
    public async Task<PolicyResetResult> RestoreDefaultsAsync(DateTime? now = null)
    {
        var result = await MutateAsync(policy =>
        {
            var builtin = BuiltinPolicy.Json();
            foreach (var key in new[] { "defaults", "formats", "grammars" }) policy[key] = builtin[key]!.DeepClone();
            return null;
        });
        if (!result.Ok) return new PolicyResetResult(result.Error, null);
        try
        {
            var backup = $"{FilePath}.backup-{now ?? DateTime.Now:yyyyMMdd-HHmmss}";
            await File.WriteAllTextAsync(backup, _previousText ?? "");
            return new PolicyResetResult(null, backup);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new PolicyResetResult(null, null); // 되돌리기는 됐다. 백업만 못 남겼다.
        }
    }

    /// <summary>
    /// 파일 형식을 더하거나 같은 id를 바꾼다. <paramref name="position"/>이 있으면 그 자리로 옮긴다(위에 있을수록 먼저 맞는다).
    /// 형식 객체에 사용자가 적은 모르는 키도 그대로 저장된다.
    /// </summary>
    public Task<PolicySaveResult> UpsertFormatAsync(JsonObject format, int? position = null) => MutateAsync(policy =>
    {
        var id = PolicyJson.Str(format["id"]);
        if (string.IsNullOrWhiteSpace(id)) return "형식에 id 가 없습니다";
        if (PolicyJson.StrList(format["masks"]) is not { Count: > 0 }) return $"형식 \"{id}\" 에 masks 가 없습니다";
        var check = new JsonObject { ["schema"] = ComparePolicy.SchemaVersion, ["formats"] = new JsonArray(format.DeepClone()), ["grammars"] = policy["grammars"]?.DeepClone() };
        if (ComparePolicy.FromJson(check).Warnings.FirstOrDefault(w => w.StartsWith($"formats.{id}", StringComparison.Ordinal)) is { } problem)
            return problem;

        var formats = policy["formats"] as JsonArray ?? [];
        policy["formats"] = formats;
        var index = IndexOf(formats, id);
        if (index >= 0) formats.RemoveAt(index);
        var at = Math.Clamp(position ?? (index >= 0 ? index : formats.Count), 0, formats.Count);
        formats.Insert(at, format.DeepClone());
        return null;
    });

    public Task<PolicySaveResult> RemoveFormatAsync(string id) => MutateAsync(policy =>
    {
        var formats = policy["formats"] as JsonArray;
        var index = formats is null ? -1 : IndexOf(formats, id);
        if (index < 0) return $"형식 \"{id}\" 이 없습니다";
        formats!.RemoveAt(index);
        return null;
    });

    /// <summary><c>defaults</c>의 한 구역(<c>folder</c>·<c>text</c>·…)을 통째로 바꾼다.</summary>
    public Task<PolicySaveResult> SetDefaultsAsync(string section, JsonObject value) => MutateAsync(policy =>
    {
        if (section is not ("folder" or "text" or "binary" or "image" or "table")) return $"모르는 구역입니다: {section}";
        var defaults = policy["defaults"] as JsonObject ?? new JsonObject();
        policy["defaults"] = defaults;
        defaults[section] = value.DeepClone();
        return null;
    });

    /// <summary>문법을 더하거나 바꾼다. 틀린 정규식이 있으면 저장하지 않는다.</summary>
    public Task<PolicySaveResult> UpsertGrammarAsync(string id, JsonObject grammar) => MutateAsync(policy =>
    {
        var warnings = new List<string>();
        Grammar.Parse(id, grammar, warnings);
        if (warnings.Count > 0) return warnings[0];
        var grammars = policy["grammars"] as JsonObject ?? new JsonObject();
        policy["grammars"] = grammars;
        grammars[id] = grammar.DeepClone();
        return null;
    });

    public bool CanUndo => _previousText is not null && _lastWrittenText is not null;

    /// <summary>마지막 변경을 되돌린다. 그 뒤 파일을 손으로 고쳤으면 거절한다. 다시 부르면 다시 실행.</summary>
    public async Task<PolicySaveResult> UndoAsync()
    {
        if (!CanUndo) return new PolicySaveResult("되돌릴 변경이 없습니다");
        try
        {
            var current = File.Exists(FilePath) ? await File.ReadAllTextAsync(FilePath) : null;
            if (current != _lastWrittenText) return new PolicySaveResult("정책 파일이 그 사이에 바뀌어 되돌릴 수 없습니다");
            var restore = _previousText!;
            await WriteAsync(restore);
            (_previousText, _lastWrittenText) = (current, restore);
            return PolicySaveResult.Success;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new PolicySaveResult($"정책 파일을 쓰지 못했습니다: {e.Message}");
        }
    }

    async Task<PolicySaveResult> MutateAsync(Func<JsonObject, string?> edit)
    {
        try
        {
            if (!File.Exists(FilePath)) await LoadAsync();
            var before = await File.ReadAllTextAsync(FilePath);
            var policy = ParseObject(before);
            ComparePolicy.FromJson(policy); // schema가 맞는지 (아니면 FormatException → 깨진 파일은 고치지 않는다)
            if (edit(policy) is { } failure) return new PolicySaveResult(failure);
            var after = Serialize(policy);
            await WriteAsync(after);
            (_previousText, _lastWrittenText) = (before, after);
            return PolicySaveResult.Success;
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return new PolicySaveResult($"정책 파일을 읽지 못해 고치지 않았습니다: {e.Message}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new PolicySaveResult($"정책 파일을 쓰지 못했습니다: {e.Message}");
        }
    }

    static int IndexOf(JsonArray formats, string id)
    {
        for (var i = 0; i < formats.Count; i++)
            if (PolicyJson.Str((formats[i] as JsonObject)?["id"]) == id) return i;
        return -1;
    }

    async Task WriteAsync(string text)
    {
        var tmp = FilePath + ".tmp";
        await File.WriteAllTextAsync(tmp, text);
        File.Move(tmp, FilePath, overwrite: true);
    }

    static JsonObject ParseObject(string text) =>
        JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
        ?? throw new FormatException("최상위가 객체가 아닙니다");

    public static string Serialize(JsonObject o) => o.ToJsonString(Indented) + "\n";
}
