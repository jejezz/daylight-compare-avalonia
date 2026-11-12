using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static DaylightCompare.Domain.Policy.PolicyJson;

namespace DaylightCompare.Domain.Policy;

// 정책의 구역별 규칙 (ROADMAP §4-2). 각 구역은 겹친(합친) JSON 객체 하나에서 읽고, 없는 키는 내장 기본값,
// 모양이 틀린 키도 기본값을 쓰고 경고를 남긴다. 경고 문장은 개발자·고급 사용자용(정책 파일을 고치는 사람)이라 한국어 하나로 둔다.

/// <summary>파일 하나를 어떤 비교 화면으로 열지.</summary>
public enum ViewKind { Text, Binary, Image, Table }

/// <summary>폴더 비교 판정 단계 종류.</summary>
public enum CriterionKind { Size, Timestamp, Hash, Content }

public enum HashAlgorithmKind { Sha256, Crc32 }

/// <summary>
/// 폴더 비교 판정 단계 하나. 단계는 순서대로 보고, 앞 단계에서 "다르다"면 거기서 끝, "같다"면(또는 판정할 수 없으면) 다음 단계로 간다.
/// 마지막까지 같으면 같다. 예: 크기 → 시각 → 내용.
/// </summary>
public sealed record FolderCriterion(
    CriterionKind By,
    double ToleranceSeconds = 2,
    bool IgnoreHourOffsets = false,
    HashAlgorithmKind Algorithm = HashAlgorithmKind.Sha256);

public sealed record RenameRule(Regex Left, string Right);

public sealed record FolderRules(
    IReadOnlyList<FolderCriterion> Criteria,
    FileMask Files,
    FileMask Folders,
    bool ShowHidden,
    long? MinSize,
    long? MaxSize,
    bool IgnoreCase,
    bool IgnoreExtension,
    IReadOnlyList<RenameRule> Renames)
{
    /// <param name="namesIgnoreCase">pairing.ignoreCase가 없을 때의 값: 파일 시스템이 이름의 대소문자를 가리지 않으면 true (Windows·macOS). 판단은 호출하는 쪽(Services)이 한다.</param>
    public static FolderRules Parse(JsonObject o, List<string> warnings, bool namesIgnoreCase)
    {
        var criteria = new List<FolderCriterion>();
        if (o["criteria"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                var c = Obj(item);
                var by = Str(c?["by"]) switch
                {
                    "size" => CriterionKind.Size,
                    "timestamp" => CriterionKind.Timestamp,
                    "hash" => CriterionKind.Hash,
                    "content" => CriterionKind.Content,
                    _ => (CriterionKind?)null,
                };
                if (by is null) { warnings.Add($"folder.criteria: 모르는 판정 단계 {item?.ToJsonString()} 를 건너뜀"); continue; }
                var algo = Str(c!["algorithm"]) switch { "crc32" => HashAlgorithmKind.Crc32, _ => HashAlgorithmKind.Sha256 };
                criteria.Add(new FolderCriterion(by.Value, Math.Max(0, Num(c["toleranceSec"]) ?? 2), Bool(c["ignoreHourOffsets"]) ?? false, algo));
            }
        }
        else if (o["criteria"] is not null) warnings.Add("folder.criteria: 목록이 아니라 기본값을 씀");
        if (criteria.Count == 0) criteria = [new(CriterionKind.Size), new(CriterionKind.Timestamp), new(CriterionKind.Content)];

        var filters = Obj(o["filters"]);
        var files = new List<string>();
        files.AddRange(StrList(filters?["include"]) ?? []);
        files.AddRange((StrList(filters?["exclude"]) ?? []).Select(p => "-" + p));
        var folders = (StrList(filters?["excludeFolders"]) ?? []).Select(p => "-" + p).ToList();

        var pairing = Obj(o["pairing"]);
        var renames = new List<RenameRule>();
        if (pairing?["renames"] is JsonArray rr)
        {
            foreach (var r in rr)
            {
                var left = Str(Obj(r)?["left"]);
                var right = Str(Obj(r)?["right"]);
                if (left is null || right is null) { warnings.Add("folder.pairing.renames: left·right가 없는 항목을 건너뜀"); continue; }
                if (TryRegex(left, warnings, "folder.pairing.renames") is { } re) renames.Add(new RenameRule(re, right));
            }
        }

        long? Size(string key) => Num(filters?[key]) is { } d && d >= 0 ? (long)d : null;
        return new FolderRules(criteria, new FileMask(files), new FileMask(folders), Bool(filters?["showHidden"]) ?? false,
            Size("minSize"), Size("maxSize"),
            Bool(pairing?["ignoreCase"]) ?? namesIgnoreCase, Bool(pairing?["ignoreExtension"]) ?? false, renames);
    }

    internal static Regex? TryRegex(string pattern, List<string> warnings, string where, RegexOptions options = RegexOptions.None)
    {
        try { return new Regex(pattern, options | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)); }
        catch (ArgumentException e)
        {
            warnings.Add($"{where}: 정규식 \"{pattern}\" 이 틀려 건너뜀 ({e.Message})");
            return null;
        }
    }
}

public enum AlignmentKind { Histogram, Patience, Myers }

public sealed record Replacement(Regex Pattern, string With);

/// <summary>텍스트 비교 규칙. <see cref="Unimportant"/>는 문법(<see cref="Grammar"/>)의 토큰 종류 이름(예: <c>comment</c>, <c>whitespace</c>).</summary>
public sealed record TextRules(
    AlignmentKind Alignment,
    bool IgnoreCase,
    bool IgnoreTrailingWhitespace,
    bool IgnoreAllWhitespace,
    bool IgnoreLineBreaks,
    bool IgnoreEncoding,
    int TabWidth,
    string? Grammar,
    IReadOnlyList<string> Unimportant,
    IReadOnlyList<Replacement> Replacements,
    string? Encoding)
{
    public static TextRules Parse(JsonObject o, List<string> warnings)
    {
        var alignment = Str(o["alignment"]) switch
        {
            null or "histogram" => AlignmentKind.Histogram,
            "patience" => AlignmentKind.Patience,
            "myers" => AlignmentKind.Myers,
            var other => Warn(warnings, $"text.alignment: 모르는 값 \"{other}\" — histogram을 씀", AlignmentKind.Histogram),
        };
        var n = Obj(o["normalize"]);
        var replacements = new List<Replacement>();
        if (o["replacements"] is JsonArray ra)
        {
            foreach (var r in ra)
            {
                var find = Str(Obj(r)?["find"]);
                var with = Str(Obj(r)?["with"]) ?? "";
                if (find is null) { warnings.Add("text.replacements: find가 없는 항목을 건너뜀"); continue; }
                var options = Bool(Obj(r)!["ignoreCase"]) == true ? RegexOptions.IgnoreCase : RegexOptions.None;
                if (FolderRules.TryRegex(find, warnings, "text.replacements", options) is { } re) replacements.Add(new Replacement(re, with));
            }
        }
        var importance = Obj(o["importance"]);
        return new TextRules(alignment,
            Bool(n?["case"]) ?? false,
            Bool(n?["trailingSpace"]) ?? true,
            Bool(n?["allWhitespace"]) ?? false,
            Bool(n?["lineBreaks"]) ?? true,
            Bool(n?["encoding"]) ?? true,
            Math.Clamp(Int(o["tabWidth"]) ?? 4, 1, 16),
            Str(o["grammar"]),
            StrList(importance?["unimportant"]) ?? [],
            replacements,
            Str(o["encoding"]));
    }

    static T Warn<T>(List<string> warnings, string message, T value)
    {
        warnings.Add(message);
        return value;
    }
}

public enum BinaryAlignKind { Fixed, Insertions }

/// <summary>비교에서 뺄 바이트 구간 [Start, Start+Length).</summary>
public sealed record ByteRange(long Start, long Length);

public sealed record BinaryRules(BinaryAlignKind Align, IReadOnlyList<ByteRange> IgnoreRanges, byte FillByte)
{
    public static BinaryRules Parse(JsonObject o, List<string> warnings)
    {
        var ranges = new List<ByteRange>();
        if (o["ignoreRanges"] is JsonArray arr)
        {
            foreach (var r in arr)
            {
                var start = Num(Obj(r)?["start"]);
                var length = Num(Obj(r)?["length"]);
                if (start is not >= 0 || length is not > 0) { warnings.Add($"binary.ignoreRanges: {r?.ToJsonString()} 를 건너뜀 (start ≥ 0, length > 0)"); continue; }
                ranges.Add(new ByteRange((long)start, (long)length));
            }
        }
        var align = Str(o["align"]) == "insertions" ? BinaryAlignKind.Insertions : BinaryAlignKind.Fixed;
        return new BinaryRules(align, ranges, (byte)Math.Clamp(Int(o["fill"]) ?? 0xFF, 0, 255));
    }
}

public enum ImageAlignKind { TopLeft, Center, Fit }

public sealed record ImageRules(int Tolerance, bool IgnoreAlpha, ImageAlignKind Align, double MaxDiffPercent)
{
    public static ImageRules Parse(JsonObject o, List<string> warnings) => new(
        Math.Clamp(Int(o["tolerance"]) ?? 0, 0, 255),
        Bool(o["ignoreAlpha"]) ?? false,
        Str(o["align"]) switch { "center" => ImageAlignKind.Center, "fit" => ImageAlignKind.Fit, _ => ImageAlignKind.TopLeft },
        Math.Clamp(Num(o["maxDiffPercent"]) ?? 0, 0, 100));
}

public sealed record TableRules(string Delimiter, bool Header, IReadOnlyList<int> KeyColumns, double NumericTolerance, IReadOnlyList<int> IgnoreColumns)
{
    public static TableRules Parse(JsonObject o, List<string> warnings)
    {
        var delimiter = Str(o["delimiter"]) ?? ",";
        if (delimiter.Length != 1)
        {
            warnings.Add($"table.delimiter: 한 글자여야 한다 (\"{delimiter}\") — 쉼표를 씀");
            delimiter = ",";
        }
        return new TableRules(delimiter, Bool(o["header"]) ?? true, IntList(o["keyColumns"]) ?? [],
            Math.Max(0, Num(o["numericTolerance"]) ?? 0), IntList(o["ignoreColumns"]) ?? []);
    }
}

/// <summary>
/// 비교 전 변환. 내장(<c>builtin:ihex-to-bin</c> 등) 또는 외부 명령(<c>{in}</c>·<c>{out}</c> 자리 표시).
/// </summary>
public sealed record ConverterSpec(string? Builtin, string? Command, IReadOnlyList<string> Args)
{
    public static readonly IReadOnlySet<string> KnownBuiltins = new HashSet<string>
    {
        "ihex-to-bin", "srec-to-bin", "json-sorted", "xml-pretty",
    };

    public static ConverterSpec? Parse(JsonNode? n, List<string> warnings, string where)
    {
        if (n is null) return null;
        if (Str(n) is { } s)
        {
            if (s.StartsWith("builtin:", StringComparison.Ordinal) && KnownBuiltins.Contains(s["builtin:".Length..]))
                return new ConverterSpec(s["builtin:".Length..], null, []);
            warnings.Add($"{where}.converter: 모르는 변환 \"{s}\" 을 건너뜀 (builtin:{string.Join(", builtin:", KnownBuiltins)})");
            return null;
        }
        if (Obj(n) is { } o && Str(o["command"]) is { Length: > 0 } command)
            return new ConverterSpec(null, command, StrList(o["args"]) ?? ["{in}", "{out}"]);
        warnings.Add($"{where}.converter: 문자열이나 {{\"command\": …}} 이어야 한다 — 건너뜀");
        return null;
    }
}

/// <summary>문법: 토큰 종류 이름 → 정규식 목록. 텍스트 비교가 줄을 토큰으로 나눠 중요하지 않은 토큰을 가린다.</summary>
public sealed record Grammar(string Id, IReadOnlyDictionary<string, IReadOnlyList<Regex>> Tokens)
{
    public static Grammar? Parse(string id, JsonNode? n, List<string> warnings)
    {
        if (Obj(n) is not { } o) { warnings.Add($"grammars.{id}: 객체가 아니라 건너뜀"); return null; }
        var tokens = new Dictionary<string, IReadOnlyList<Regex>>();
        foreach (var (kind, value) in o)
        {
            if (StrList(value) is not { } patterns) { warnings.Add($"grammars.{id}.{kind}: 정규식 목록이 아니라 건너뜀"); continue; }
            var list = patterns.Select(p => FolderRules.TryRegex(p, warnings, $"grammars.{id}.{kind}", RegexOptions.Multiline))
                .OfType<Regex>().ToList();
            if (list.Count > 0) tokens[kind] = list;
        }
        return new Grammar(id, tokens);
    }
}
