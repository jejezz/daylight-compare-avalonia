using DaylightCompare.Domain;
using DaylightCompare.Domain.Cli;
using DaylightCompare.Domain.Diff;
using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Services.Cli;

/// <summary>
/// 창 없이 두 경로를 비교해 BC와 같은 종료 코드를 정한다 (BC <c>/qc</c>, BC_COMPAT.md §4).
/// <list type="bullet">
/// <item>규칙 기준(기본): 바이트가 같으면 1, 둘 다 텍스트이고 Compare Policy로 글이 같으면(인코딩·BOM·줄바꿈만 다름) 2,
/// 중요하지 않은 차이만 있으면 12(<paramref name="ignoreUnimportant"/>면 2), 중요한 차이가 있으면 13. 텍스트가 아니면 바이트로 1 / 11.</item>
/// <item>size: 크기만, crc·binary: 바이트로 — 같으면 1, 다르면 11.</item>
/// <item>읽지 못하거나 폴더면(폴더 비교는 4단계) 100.</item>
/// </list>
/// </summary>
public static class QuickCompare
{
    public static async Task<ExitCode> RunAsync(string left, string right, ComparePolicy policy,
        QuickCompareKind kind = QuickCompareKind.Rules, bool ignoreUnimportant = false, CancellationToken ct = default)
    {
        try
        {
            if (Directory.Exists(left) || Directory.Exists(right)) return ExitCode.Error;
            if (kind == QuickCompareKind.Size && !ArchivePaths.IsInArchive(left) && !ArchivePaths.IsInArchive(right))
            {
                var (a, b) = (new FileInfo(left), new FileInfo(right));
                if (!a.Exists || !b.Exists) return ExitCode.Error;
                return a.Length == b.Length ? ExitCode.BinarySame : ExitCode.BinaryDifferent;
            }
            var sources = await Task.WhenAll(CompareSources.LoadAsync(left, ct: ct), CompareSources.LoadAsync(right, ct: ct));
            var (l, r) = (sources[0], sources[1]);
            if (l is CompareSource.Failed || r is CompareSource.Failed) return ExitCode.Error;
            var bytesSame = await SameBytesAsync(left, right, l, r, ct);
            if (bytesSame is null) return ExitCode.Error;
            if (bytesSame == true) return ExitCode.BinarySame;
            if (kind != QuickCompareKind.Rules || l is not CompareSource.Text lt || r is not CompareSource.Text rt) return ExitCode.BinaryDifferent;
            var effective = policy.Resolve(Path.GetFileName(left), namesIgnoreCase: PolicyStore.NamesIgnoreCase);
            var diff = TextDiff.Compare(lt.File.Text, rt.File.Text, effective);
            if (diff.IsIdentical) return ExitCode.RulesSame;
            if (diff.IsEquivalent) return ignoreUnimportant ? ExitCode.RulesSame : ExitCode.Similar;
            return ExitCode.RulesDifferent;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ExitCode.Error;
        }
    }

    /// <summary>바이트가 같은가. 압축 파일 안의 바이너리는 아직 못 읽는다(null).</summary>
    static async Task<bool?> SameBytesAsync(string left, string right, CompareSource l, CompareSource r, CancellationToken ct)
    {
        if (l is CompareSource.Text lt && r is CompareSource.Text rt) return lt.Bytes.AsSpan().SequenceEqual(rt.Bytes);
        if (ArchivePaths.IsInArchive(left) || ArchivePaths.IsInArchive(right)) return null;
        await using var a = new FileStream(left, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, useAsync: true);
        await using var b = new FileStream(right, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, useAsync: true);
        if (a.Length != b.Length) return false;
        var bufA = new byte[1 << 16];
        var bufB = new byte[1 << 16];
        while (true)
        {
            var n = await a.ReadAtLeastAsync(bufA, bufA.Length, throwOnEndOfStream: false, ct);
            var m = await b.ReadAtLeastAsync(bufB, n, throwOnEndOfStream: false, ct);
            if (n != m || !bufA.AsSpan(0, n).SequenceEqual(bufB.AsSpan(0, m))) return false;
            if (n < bufA.Length) return true;
        }
    }

    /// <summary>
    /// 받은 경로를 <paramref name="cwd"/> 기준 절대 경로로. 압축 파일 안 경로(<c>a.zip!/inner</c>)는 압축 파일 부분만 바꾼다.
    /// </summary>
    public static string ResolvePath(string path, string cwd)
    {
        if (ArchivePaths.TryParse(path) is { } loc) return ArchivePaths.Format(Path.GetFullPath(loc.Archive, cwd), loc.Inner);
        return Path.GetFullPath(path, cwd);
    }
}
