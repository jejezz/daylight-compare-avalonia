using System.Globalization;
using System.Text;

namespace DaylightCompare.Services;

/// <summary>
/// 파일을 내려받지 않고 <b>서버에서</b> SHA-256을 구할 수 있는 파일 시스템 (ROADMAP §8-1 원격 해시, 4c). 지금은 SFTP(SSH exec로 <c>sha256sum</c>).
/// 폴더 비교의 해시 단계가 쓴다 — 서버가 못 하면(도구 없음·Windows 서버·exec 거부) null을 돌려주고, 부르는 쪽은 내려받아 해시한다.
/// </summary>
public interface IServerHasher
{
    /// <summary>
    /// 여러 파일의 SHA-256을 서버에서 구한다 (한 번에 묶어 보내 왕복을 줄인다). 결과는 경로 → 해시(32바이트). 서버가 못 읽은 파일은 결과에 없다.
    /// 서버에서 해시를 할 수 없으면 null.
    /// </summary>
    Task<IReadOnlyDictionary<string, byte[]>?> HashSha256Async(IReadOnlyList<string> paths, CancellationToken ct = default);
}

/// <summary>서버 해시 명령 만들기·결과 읽기 (서버 없이 시험할 수 있게 따로 둔다).</summary>
public static class ServerHashCommands
{
    /// <summary>쓸 도구를 찾는 명령. 첫 줄이 도구 경로, 없으면 빈 출력.</summary>
    public const string Probe = "command -v sha256sum 2>/dev/null || command -v shasum 2>/dev/null";

    /// <summary>한 명령에 넣는 파일 수·글자 수 상한 (명령줄 길이 제한 안쪽으로).</summary>
    public const int MaxFilesPerCommand = 200, MaxCharsPerCommand = 32 * 1024;

    /// <summary>찾은 도구 경로를 실행할 명령 앞부분으로. 모르는 도구면 null.</summary>
    public static string? ToolCommand(string probeOutput)
    {
        var line = probeOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrEmpty(line)) return null;
        var name = line[(line.LastIndexOf('/') + 1)..];
        return name switch
        {
            "sha256sum" => Quote(line),
            "shasum" => Quote(line) + " -a 256",
            _ => null,
        };
    }

    /// <summary>POSIX 셸의 작은따옴표 인용.</summary>
    public static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>서버 경로들을 명령 여러 개로 나눈다 (각각 <c>도구 '경로' '경로' …</c>). 경로는 절대 경로라 <c>-</c>로 시작하지 않는다.</summary>
    public static IEnumerable<(string Command, IReadOnlyList<string> Paths)> Batches(string tool, IReadOnlyList<string> serverPaths)
    {
        var sb = new StringBuilder(tool);
        var batch = new List<string>();
        foreach (var path in serverPaths)
        {
            var arg = " " + Quote(path);
            if (batch.Count > 0 && (batch.Count >= MaxFilesPerCommand || sb.Length + arg.Length > MaxCharsPerCommand))
            {
                yield return (sb.ToString(), batch);
                sb = new StringBuilder(tool);
                batch = [];
            }
            sb.Append(arg);
            batch.Add(path);
        }
        if (batch.Count > 0) yield return (sb.ToString(), batch);
    }

    /// <summary>
    /// <c>sha256sum</c>·<c>shasum</c> 출력(<c>해시  이름</c> 또는 <c>해시 *이름</c>)을 이름 → 해시로 읽는다.
    /// 이름에 줄바꿈·역슬래시가 있어 GNU가 <c>\</c>로 시작해 이스케이프한 줄은 건너뛴다 (그 파일은 내려받아 해시한다).
    /// </summary>
    public static Dictionary<string, byte[]> Parse(string output)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length < 67 || line[0] == '\\' || line[64] != ' ' || line[65] is not (' ' or '*')) continue;
            if (TryHex(line.AsSpan(0, 64)) is { } hash) result[line[66..]] = hash;
        }
        return result;
    }

    static byte[]? TryHex(ReadOnlySpan<char> hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            if (!byte.TryParse(hex.Slice(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i])) return null;
        return bytes;
    }
}
