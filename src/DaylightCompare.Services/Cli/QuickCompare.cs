using DaylightCompare.Domain;
using DaylightCompare.Domain.Cli;
using DaylightCompare.Domain.Diff;
using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Services.Cli;

/// <summary>
/// 창 없이 두 경로를 비교해 종료 코드를 정한다 (<c>--silent</c>, 그리고 <c>--wait</c>로 연 비교 창을 닫을 때 — 저장한 뒤의 디스크 내용으로).
/// 둘 다 텍스트면 Compare Policy로 비교하고(중요한 차이가 없으면 같음), 아니면 바이트로 비교한다.
/// 읽지 못하거나 폴더면(폴더 비교는 4단계) 오류.
/// </summary>
public static class QuickCompare
{
    public static async Task<ExitCode> RunAsync(string left, string right, ComparePolicy policy, CancellationToken ct = default)
    {
        try
        {
            if (Directory.Exists(left) || Directory.Exists(right)) return ExitCode.Error;
            var sources = await Task.WhenAll(CompareSources.LoadAsync(left, ct: ct), CompareSources.LoadAsync(right, ct: ct));
            var (l, r) = (sources[0], sources[1]);
            if (l is CompareSource.Failed || r is CompareSource.Failed) return ExitCode.Error;
            if (l is CompareSource.Text lt && r is CompareSource.Text rt)
            {
                var effective = policy.Resolve(Path.GetFileName(left), namesIgnoreCase: PolicyStore.NamesIgnoreCase);
                var diff = TextDiff.Compare(lt.File.Text, rt.File.Text, effective);
                return diff.IsEquivalent ? ExitCode.Same : ExitCode.Different;
            }
            return await SameBytesAsync(left, right, l, r, ct) is { } same
                ? same ? ExitCode.Same : ExitCode.Different
                : ExitCode.Error;
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
