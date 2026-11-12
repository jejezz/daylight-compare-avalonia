using System.Text.Json.Nodes;

namespace DaylightCompare.Domain.Policy;

/// <summary>
/// 정책 JSON을 읽는 작은 도우미. 정책 파일은 사람이 고치는 파일이라 <b>관대하게</b> 읽는다: 모양이 틀린 값은 기본값을 쓰고 경고를 남긴다.
/// <see cref="JsonNode"/>만 쓰므로 리플렉션이 없어 NativeAOT에서 동작한다.
/// </summary>
public static class PolicyJson
{
    public static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static bool? Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    public static double? Num(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<int>(out var i)) return i;
        return null;
    }

    public static int? Int(JsonNode? n) => Num(n) is { } d && d == Math.Floor(d) && d is >= int.MinValue and <= int.MaxValue ? (int)d : null;

    /// <summary>문자열 목록. 문자열 하나만 있어도 목록으로 받는다(<c>"masks": "*.c"</c>). 모양이 틀리면 null.</summary>
    public static List<string>? StrList(JsonNode? n) => n switch
    {
        null => null,
        JsonValue when Str(n) is { } single => [single],
        JsonArray a when a.All(x => Str(x) is not null) => a.Select(x => Str(x)!).ToList(),
        _ => null,
    };

    public static List<int>? IntList(JsonNode? n) =>
        n is JsonArray a && a.All(x => Int(x) is not null) ? a.Select(x => Int(x)!.Value).ToList() : null;

    /// <summary>
    /// 깊은 합치기: <paramref name="over"/>의 키가 <paramref name="baseObj"/>를 덮는다. 둘 다 객체인 키는 안으로 들어가 합치고,
    /// 배열·값은 통째로 바꾼다 (목록을 이어 붙이지 않는다 — 형식에서 <c>unimportant</c>를 정하면 그것이 전부다).
    /// 원본은 바꾸지 않고 새 객체를 돌려준다.
    /// </summary>
    public static JsonObject Merge(JsonObject? baseObj, JsonObject? over)
    {
        var result = baseObj?.DeepClone() as JsonObject ?? new JsonObject();
        if (over is null) return result;
        foreach (var (key, value) in over)
        {
            if (value is JsonObject o && result[key] is JsonObject existing) result[key] = Merge(existing, o);
            else result[key] = value?.DeepClone();
        }
        return result;
    }

    public static JsonObject Merge(params JsonObject?[] layers) => layers.Aggregate(new JsonObject(), (acc, l) => Merge(acc, l));

    public static JsonObject? Obj(JsonNode? n) => n as JsonObject;
}
