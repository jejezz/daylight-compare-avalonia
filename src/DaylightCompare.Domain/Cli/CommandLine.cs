namespace DaylightCompare.Domain.Cli;

/// <summary>
/// 프로세스 종료 코드 (ROADMAP 3b). git difftool·스크립트가 읽는다.
/// "같음"은 Compare Policy로 본 결과다 — 중요하지 않은 차이(공백·줄바꿈·BOM 등)만 있으면 같음.
/// </summary>
public enum ExitCode
{
    Same = 0,
    Different = 1,
    Error = 2,
}

public enum CliError
{
    None,
    /// <summary>모르는 옵션 (<see cref="CliRequest.ErrorArgument"/>).</summary>
    UnknownOption,
    /// <summary>경로가 셋 이상.</summary>
    TooManyPaths,
    /// <summary><c>--silent</c>는 경로 둘이 있어야 한다.</summary>
    SilentNeedsTwoPaths,
}

/// <summary>해석한 실행 인자. 경로는 받은 그대로다 (절대 경로로 바꾸는 것은 부르는 쪽 — 현재 폴더가 필요하다).</summary>
/// <param name="Paths">비교할 경로 0~2개 (왼쪽, 오른쪽).</param>
/// <param name="Wait">비교 창을 닫을 때까지 끝나지 않고, 닫을 때의 결과를 종료 코드로 돌려준다 (git difftool용).</param>
/// <param name="Silent">창 없이 비교만 하고 종료 코드로 알린다 (스크립트용).</param>
/// <param name="Help">사용법을 보여 준다.</param>
public sealed record CliRequest(IReadOnlyList<string> Paths, bool Wait, bool Silent, bool Help, CliError Error = CliError.None, string? ErrorArgument = null)
{
    public static readonly CliRequest Empty = new([], false, false, false);

    public bool IsValid => Error == CliError.None;
    public bool IsCompare => Paths.Count > 0;
}

/// <summary>
/// <c>DaylightCompare [--wait] [--silent] [&lt;왼쪽&gt; [&lt;오른쪽&gt;]]</c>.
/// 옵션은 <c>--</c>로 시작하고(Unix 경로 <c>/…</c>와 헷갈리지 않게 <c>/wait</c> 꼴은 받지 않는다), <c>--</c> 뒤는 모두 경로다.
/// </summary>
public static class CommandLine
{
    public static CliRequest Parse(IReadOnlyList<string> args)
    {
        var paths = new List<string>();
        bool wait = false, silent = false, help = false, onlyPaths = false;
        foreach (var arg in args)
        {
            if (!onlyPaths && arg.Length > 1 && arg[0] == '-')
            {
                switch (arg)
                {
                    case "--": onlyPaths = true; continue;
                    case "--wait" or "-w": wait = true; continue;
                    case "--silent" or "-s": silent = true; continue;
                    case "--help" or "-h" or "-?": help = true; continue;
                    default: return new CliRequest(paths, wait, silent, help, CliError.UnknownOption, arg);
                }
            }
            if (arg.Length == 0) continue;
            paths.Add(arg);
            if (paths.Count > 2) return new CliRequest(paths, wait, silent, help, CliError.TooManyPaths);
        }
        if (silent && paths.Count != 2 && !help) return new CliRequest(paths, wait, silent, help, CliError.SilentNeedsTwoPaths);
        return new CliRequest(paths, wait, silent, help);
    }

    /// <summary>이미 떠 있는 앱에 넘길 인자 (경로는 절대 경로로 바꾼 것). <see cref="Parse"/>로 다시 읽힌다.</summary>
    public static string[] Format(CliRequest request) =>
        [.. request.Wait ? ["--wait"] : Array.Empty<string>(), "--", .. request.Paths];
}
