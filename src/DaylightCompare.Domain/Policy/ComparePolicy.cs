// Compare Policy 문서 (schema 1, ROADMAP §4).
//
//   { "schema": 1,
//     "defaults": { "folder": {…}, "text": {…}, "binary": {…}, "image": {…}, "table": {…} },
//     "formats":  [ { "id": "c-family", "masks": ["*.c", "*.h"], "view": "text", "text": {…}, "converter": "builtin:…" }, … ],
//     "grammars": { "c-like": { "comment": ["//.*$"], "string": […], "whitespace": ["[ \\t]+"] } } }
//
// 겹치는 순서: 내장 기본값 → 사용자 정책의 defaults → 맞는 파일 형식 하나(목록에서 위에 있는 것) → 세션 덮어쓰기.
// 겹치기는 JSON에서 한다(PolicyJson.Merge) — 형식은 바꾸고 싶은 키만 적으면 된다. 그 뒤에 구역별 타입(PolicyRules.cs)으로 읽는다.
// 읽을 때는 관대하다: 틀린 형식·문법·값은 건너뛰거나 기본값을 쓰고 Warnings에 남긴다(Jack's Bean 정책 파일과 같은 태도).

using System.Text.Json.Nodes;
using static DaylightCompare.Domain.Policy.PolicyJson;

namespace DaylightCompare.Domain.Policy;

/// <summary>파일 형식 하나: 마스크에 맞는 파일에 이 형식의 보기·규칙을 쓴다. 구역(<c>text</c> 등)은 바꿀 키만 담은 JSON이다.</summary>
public sealed record FileFormat(string Id, FileMask Masks, ViewKind? View, JsonObject Sections, ConverterSpec? Converter);

/// <summary>파일 하나(또는 폴더 비교 하나)에 실제로 쓰는 규칙.</summary>
public sealed record EffectivePolicy(
    FileFormat? Format,
    ViewKind View,
    FolderRules Folder,
    TextRules Text,
    BinaryRules Binary,
    ImageRules Image,
    TableRules Table,
    Grammar? Grammar,
    ConverterSpec? Converter,
    IReadOnlyList<string> Warnings);

public sealed class ComparePolicy
{
    public const int SchemaVersion = 1;

    static readonly string[] Sections = ["folder", "text", "binary", "image", "table"];

    ComparePolicy(JsonObject defaults, IReadOnlyList<FileFormat> formats, IReadOnlyDictionary<string, Grammar> grammars,
        IReadOnlyList<string> warnings, bool isFallback)
    {
        Defaults = defaults;
        Formats = formats;
        Grammars = grammars;
        Warnings = warnings;
        IsFallback = isFallback;
    }

    /// <summary>사용자 정책의 <c>defaults</c> (내장 기본값과 아직 합치지 않은 것).</summary>
    public JsonObject Defaults { get; }

    /// <summary>파일 형식. 위에서부터 처음 맞는 것이 쓰인다.</summary>
    public IReadOnlyList<FileFormat> Formats { get; }

    public IReadOnlyDictionary<string, Grammar> Grammars { get; }

    /// <summary>읽으면서 건너뛴 것과 그 이유. 화면(정책 편집 창·홈 창)이 보여 준다.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>파일 전체를 못 읽어서(깨짐·schema 다름) 내장 기본 정책을 대신 쓰는 중. 파일은 그대로 두었다.</summary>
    public bool IsFallback { get; }

    public static ComparePolicy Builtin { get; } = FromJson(BuiltinPolicy.Json());

    /// <summary>내장 기본 정책으로 대신한다 (파일을 못 읽었을 때).</summary>
    public static ComparePolicy Fallback(string reason)
    {
        var b = Builtin;
        return new ComparePolicy(b.Defaults, b.Formats, b.Grammars, [reason], isFallback: true);
    }

    /// <summary>JSON에서 읽는다. schema가 다르면 <see cref="FormatException"/> (파일 전체를 못 읽은 것으로 본다).</summary>
    public static ComparePolicy FromJson(JsonObject json)
    {
        var schema = Int(json["schema"]);
        if (schema != SchemaVersion) throw new FormatException($"schema 가 {SchemaVersion} 이 아닙니다: {json["schema"]?.ToJsonString() ?? "없음"}");
        var warnings = new List<string>();

        var defaults = new JsonObject();
        if (json["defaults"] is JsonObject d)
        {
            foreach (var (key, value) in d)
            {
                if (!Sections.Contains(key)) { warnings.Add($"defaults.{key}: 모르는 구역이라 무시함"); continue; }
                if (value is not JsonObject) { warnings.Add($"defaults.{key}: 객체가 아니라 무시함"); continue; }
                defaults[key] = value.DeepClone();
            }
        }
        else if (json["defaults"] is not null) warnings.Add("defaults: 객체가 아니라 무시함");

        var grammars = new Dictionary<string, Grammar>(StringComparer.Ordinal);
        if (json["grammars"] is JsonObject g)
            foreach (var (id, value) in g)
                if (Grammar.Parse(id, value, warnings) is { } grammar) grammars[id] = grammar;

        var formats = new List<FileFormat>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (json["formats"] is JsonArray fa)
        {
            foreach (var raw in fa)
            {
                var o = Obj(raw);
                var id = Str(o?["id"]);
                if (string.IsNullOrEmpty(id)) { warnings.Add("formats: id 없는 형식을 건너뜀"); continue; }
                if (!seen.Add(id)) { warnings.Add($"formats.{id}: id 가 겹쳐 뒤의 것을 건너뜀"); continue; }
                var masks = StrList(o!["masks"]);
                if (masks is null || masks.Count == 0) { warnings.Add($"formats.{id}: masks 가 없어 건너뜀"); continue; }
                ViewKind? view = Str(o["view"]) switch
                {
                    null => null,
                    "text" => ViewKind.Text,
                    "binary" => ViewKind.Binary,
                    "image" => ViewKind.Image,
                    "table" => ViewKind.Table,
                    var other => WarnNull(warnings, $"formats.{id}.view: 모르는 보기 \"{other}\" — 지정하지 않은 것으로 봄"),
                };
                var sections = new JsonObject();
                foreach (var key in Sections.Where(k => k != "folder"))
                    if (o[key] is JsonObject sec) sections[key] = sec.DeepClone();
                // 텍스트 구역의 자주 쓰는 키는 형식에 바로 적어도 된다 ("grammar": "c-like", "importance": {…}).
                foreach (var key in new[] { "grammar", "importance", "encoding" })
                    if (o[key] is { } shortcut)
                        sections["text"] = Merge(sections["text"] as JsonObject, new JsonObject { [key] = shortcut.DeepClone() });
                var grammarId = Str((sections["text"] as JsonObject)?["grammar"]);
                if (grammarId is not null && !grammars.ContainsKey(grammarId) && !BuiltinPolicy.HasGrammar(grammarId))
                    warnings.Add($"formats.{id}: 문법 \"{grammarId}\" 이 없어 문법 없이 비교함");
                formats.Add(new FileFormat(id, new FileMask(masks), view, sections, ConverterSpec.Parse(o["converter"], warnings, $"formats.{id}")));
            }
        }
        else if (json["formats"] is not null) warnings.Add("formats: 목록이 아니라 무시함");

        return new ComparePolicy(defaults, formats, grammars, warnings, isFallback: false);
    }

    static ViewKind? WarnNull(List<string> warnings, string message)
    {
        warnings.Add(message);
        return null;
    }

    /// <summary>
    /// 이 이름(상대 경로)에 맞는 첫 파일 형식. 사용자 정책이 기준이다 — 사용자가 지운 형식을 내장 정책에서 되살리지 않는다
    /// (처음 실행할 때 내장 형식이 파일에 그대로 들어가므로 따로 찾을 필요가 없다).
    /// </summary>
    public FileFormat? FormatFor(string name, string? relativePath = null) =>
        Formats.FirstOrDefault(f => f.Masks.IncludesExplicitly(name, relativePath));

    /// <summary>
    /// 실효 정책: 내장 기본값 → 이 정책의 defaults → 파일 형식(<paramref name="name"/>이 있으면) → <paramref name="session"/>.
    /// <paramref name="session"/>은 정책과 같은 모양의 일부 (<c>{"text": {"normalize": {"case": true}}}</c>, 또는 <c>"view"</c>로 보기 강제).
    /// </summary>
    /// <param name="namesIgnoreCase">파일 이름 짝짓기의 기본 대소문자 처리 (Windows·macOS는 true).</param>
    /// <param name="formatId">형식을 이름으로 고정한다 (BC의 파일 형식 드롭다운): 형식 id면 그 형식, 빈 글이면 형식 없음(기본 규칙만), null이면 이름으로 찾는다.</param>
    public EffectivePolicy Resolve(string? name = null, string? relativePath = null, JsonObject? session = null, bool namesIgnoreCase = true, string? formatId = null)
    {
        var warnings = new List<string>();
        var format = formatId is not null
            ? Formats.FirstOrDefault(f => f.Id == formatId)
            : name is null ? null : FormatFor(name, relativePath);
        var builtinDefaults = BuiltinPolicy.Defaults();

        JsonObject Layer(string section) => Merge(
            Obj(builtinDefaults[section]),
            Obj(Defaults[section]),
            section == "folder" ? null : Obj(format?.Sections[section]),
            Obj(session?[section]));

        var text = TextRules.Parse(Layer("text"), warnings);
        Grammar? grammar = null;
        if (text.Grammar is { } gid)
            grammar = Grammars.GetValueOrDefault(gid) ?? Builtin.Grammars.GetValueOrDefault(gid);

        var view = Str(session?["view"]) switch
        {
            "text" => ViewKind.Text,
            "binary" => ViewKind.Binary,
            "image" => ViewKind.Image,
            "table" => ViewKind.Table,
            _ => format?.View ?? ViewKind.Text,
        };
        var converter = session?["converter"] is { } sc ? ConverterSpec.Parse(sc, warnings, "session") : format?.Converter;

        return new EffectivePolicy(format, view,
            FolderRules.Parse(Layer("folder"), warnings, namesIgnoreCase),
            text,
            BinaryRules.Parse(Layer("binary"), warnings),
            ImageRules.Parse(Layer("image"), warnings),
            TableRules.Parse(Layer("table"), warnings),
            grammar, converter, warnings);
    }
}
