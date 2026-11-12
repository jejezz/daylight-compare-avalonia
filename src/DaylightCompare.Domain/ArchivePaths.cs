namespace DaylightCompare.Domain;

/// <summary>압축 파일 안의 경로: <c>&lt;압축 파일 경로&gt;!/&lt;안쪽 경로&gt;</c> (안쪽은 늘 <c>/</c>로 나눈다).</summary>
/// <param name="Archive">압축 파일 자체의 경로 (로컬).</param>
/// <param name="Inner">압축 파일 안의 경로. 루트는 빈 문자열, 앞뒤 <c>/</c> 없음.</param>
public sealed record ArchiveLocation(string Archive, string Inner);

/// <summary>
/// 압축 파일을 폴더처럼 다룰 때의 경로 규칙 (ROADMAP 1단계). Java의 jar URL처럼 <c>!/</c>로 바깥과 안쪽을 나눈다 —
/// Windows 경로에도 원격 주소에도 쓰이지 않는 조합이라 헷갈리지 않는다. 압축 파일 안의 압축 파일은 다루지 않는다.
/// </summary>
public static class ArchivePaths
{
    public const string Separator = "!/";

    /// <summary>압축 파일로 열 수 있는 이름인가 (확장자, 대소문자 무시).</summary>
    public static bool IsArchiveName(string name) => KindOf(name) is not null;

    /// <summary>이름으로 고른 압축 형식. 모르면 null.</summary>
    public static ArchiveKind? KindOf(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.EndsWith(".zip") || n.EndsWith(".jar") || n.EndsWith(".apk") || n.EndsWith(".nupkg")) return ArchiveKind.Zip;
        if (n.EndsWith(".tar.gz") || n.EndsWith(".tgz")) return ArchiveKind.TarGz;
        if (n.EndsWith(".tar")) return ArchiveKind.Tar;
        return null;
    }

    public static bool IsInArchive(string path) => path.Contains(Separator, StringComparison.Ordinal);

    public static ArchiveLocation? TryParse(string path)
    {
        var i = path.IndexOf(Separator, StringComparison.Ordinal);
        return i < 0 ? null : new ArchiveLocation(path[..i], NormalizeInner(path[(i + Separator.Length)..]));
    }

    /// <summary>압축 파일의 루트(<c>a.zip!/</c>) 또는 그 안의 경로.</summary>
    public static string Format(string archive, string inner = "") => archive + Separator + NormalizeInner(inner);

    /// <summary>
    /// 압축 파일에 적힌 이름을 안쪽 경로로: <c>\</c> → <c>/</c>, 앞의 <c>./</c>·<c>/</c>와 끝의 <c>/</c>를 뗀다.
    /// <c>..</c>이 들어 있으면 null (밖을 가리키는 이름은 목록에 넣지 않는다).
    /// </summary>
    public static string? EntryName(string raw)
    {
        var parts = raw.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Where(p => p != ".").ToArray();
        return parts.Contains("..") ? null : string.Join('/', parts);
    }

    static string NormalizeInner(string inner) => EntryName(inner) ?? "";

    /// <summary>안쪽 경로의 부모 (루트의 부모는 null).</summary>
    public static string? ParentInner(string inner)
    {
        if (inner.Length == 0) return null;
        var i = inner.LastIndexOf('/');
        return i < 0 ? "" : inner[..i];
    }

    public static string NameOfInner(string inner) => inner[(inner.LastIndexOf('/') + 1)..];
}

public enum ArchiveKind { Zip, Tar, TarGz }
