namespace DaylightCompare.Domain.Cli;

/// <summary>
/// 프로세스 종료 코드 — Beyond Compare와 같은 값 (BC_COMPAT.md §4, BC5 도움말 command_line_reference).
/// 창으로 비교하면 0(성공), 빠른 비교(<c>/qc</c>)는 결과에 따라 1·2·11·12·13, 오류는 100.
/// </summary>
public enum ExitCode
{
    Success = 0,
    /// <summary>바이트까지 같다.</summary>
    BinarySame = 1,
    /// <summary>바이트는 다르지만 규칙(Compare Policy)으로 같다 — 인코딩·BOM·줄바꿈만 다르다.</summary>
    RulesSame = 2,
    /// <summary>바이트가 다르다 (바이너리·크기·CRC 비교).</summary>
    BinaryDifferent = 11,
    /// <summary>중요하지 않은 차이만 있다 (<c>/iu</c>를 주면 <see cref="RulesSame"/>).</summary>
    Similar = 12,
    /// <summary>규칙으로 봐도 다르다 (중요한 차이가 있다).</summary>
    RulesDifferent = 13,
    Conflicts = 14,
    Error = 100,
}

/// <summary>BC <c>/qc</c>의 비교 방식. 값 없이 <c>/qc</c>면 규칙 기준.</summary>
public enum QuickCompareKind { Rules, Size, Crc, Binary }

public enum CliError
{
    None,
    /// <summary>모르는 옵션 (<see cref="CliRequest.ErrorArgument"/>).</summary>
    UnknownOption,
    /// <summary>BC에는 있지만 아직 하지 않는 옵션 (병합·폴더·스크립트 등 — 들어갈 단계에서 받는다).</summary>
    UnsupportedOption,
    /// <summary>경로가 셋 이상 (BC는 셋째가 가운데 = 3-way 병합, 7단계).</summary>
    TooManyPaths,
    /// <summary><c>/qc</c>는 경로 둘이 있어야 한다.</summary>
    QuickCompareNeedsTwoPaths,
}

/// <summary>해석한 실행 인자. 경로는 받은 그대로다 (절대 경로로 바꾸는 것은 부르는 쪽 — 현재 폴더가 필요하다).</summary>
/// <param name="Paths">비교할 경로 0~2개 (왼쪽, 오른쪽).</param>
/// <param name="Wait">비교 창을 닫을 때까지 끝나지 않는다 (BC의 <c>BComp</c>처럼 — git difftool용).</param>
/// <param name="QuickCompare">창 없이 비교만 하고 종료 코드로 알린다 (BC <c>/qc</c>).</param>
/// <param name="Help">사용법을 보여 준다.</param>
public sealed record CliRequest(
    IReadOnlyList<string> Paths, bool Wait, QuickCompareKind? QuickCompare, bool Help,
    string? LeftTitle = null, string? RightTitle = null, bool LeftReadOnly = false, bool RightReadOnly = false, bool IgnoreUnimportant = false,
    CliError Error = CliError.None, string? ErrorArgument = null)
{
    public static readonly CliRequest Empty = new([], false, null, false);

    public bool IsValid => Error == CliError.None;
    public bool IsCompare => Paths.Count > 0;
}

/// <summary>
/// Beyond Compare와 같은 명령줄: <c>DaylightCompare [스위치] [&lt;왼쪽&gt; [&lt;오른쪽&gt;]]</c>.
/// 스위치는 Windows에서 <c>/qc</c>·<c>-qc</c>·<c>--qc</c>, macOS·Linux에서 <c>-qc</c>·<c>--qc</c>(BC처럼 <c>/</c>는 경로라서 받지 않는다).
/// 값은 <c>/title1="이름"</c>처럼 <c>=</c> 뒤에. 이름은 대소문자를 가리지 않는다. <c>--</c> 뒤는 모두 경로다.
/// </summary>
public static class CommandLine
{
    /// <summary>BC에 있지만 아직 받지 않는 스위치 — 모르는 옵션과 달리 "아직 지원하지 않음"으로 알린다.</summary>
    static readonly HashSet<string> Unsupported =
    [
        "filters", "expandall", "sync", "edit", "savetarget", "nobackups", "silent", "solo", "closescript",
        "automerge", "force", "reviewconflicts", "favorleft", "favorright", "center", "mergeoutput",
        "title3", "title4", "centertitle", "outputtitle", "ro3", "ro4",
        "vcs1", "vcs2", "vcs3", "vcs4", "vcsleft", "vcsright", "vcscenter", "vcsoutput",
    ];

    /// <param name="slashSwitches">Windows면 true — <c>/</c>로 시작하는 인자를 스위치로 읽는다.</param>
    public static CliRequest Parse(IReadOnlyList<string> args, bool slashSwitches)
    {
        var paths = new List<string>();
        var r = CliRequest.Empty;
        var onlyPaths = false;
        foreach (var arg in args)
        {
            if (!onlyPaths && arg == "--") { onlyPaths = true; continue; }
            if (!onlyPaths && SwitchOf(arg, slashSwitches) is { } sw)
            {
                var (name, value) = sw;
                switch (name)
                {
                    case "wait" or "w": r = r with { Wait = true }; break;
                    case "qc" or "quickcompare":
                        QuickCompareKind? kind = value?.ToLowerInvariant() switch
                        {
                            null or "" or "rules" => QuickCompareKind.Rules,
                            "size" => QuickCompareKind.Size,
                            "crc" => QuickCompareKind.Crc,
                            "binary" => QuickCompareKind.Binary,
                            _ => null,
                        };
                        if (kind is null) return Fail(r, paths, CliError.UnknownOption, arg);
                        r = r with { QuickCompare = kind };
                        break;
                    case "title1" or "lefttitle": r = r with { LeftTitle = value ?? "" }; break;
                    case "title2" or "righttitle": r = r with { RightTitle = value ?? "" }; break;
                    case "ro" or "readonly": r = r with { LeftReadOnly = true, RightReadOnly = true }; break;
                    case "ro1" or "lro" or "leftreadonly": r = r with { LeftReadOnly = true }; break;
                    case "ro2" or "rro" or "rightreadonly": r = r with { RightReadOnly = true }; break;
                    case "iu" or "ignoreunimportant": r = r with { IgnoreUnimportant = true }; break;
                    case "fv" or "fileviewer":
                        if (!string.Equals(value?.Trim('"'), "Text Compare", StringComparison.OrdinalIgnoreCase))
                            return Fail(r, paths, CliError.UnsupportedOption, arg);
                        break;
                    case "?" or "h" or "help": r = r with { Help = true }; break;
                    default:
                        return Fail(r, paths, Unsupported.Contains(name) ? CliError.UnsupportedOption : CliError.UnknownOption, arg);
                }
                continue;
            }
            if (arg.Length == 0) continue;
            paths.Add(arg);
            if (paths.Count > 2) return Fail(r, paths, CliError.TooManyPaths, arg);
        }
        r = r with { Paths = paths };
        if (r.QuickCompare is not null && paths.Count != 2 && !r.Help) return r with { Error = CliError.QuickCompareNeedsTwoPaths };
        return r;
    }

    static CliRequest Fail(CliRequest r, List<string> paths, CliError error, string arg) =>
        r with { Paths = paths, Error = error, ErrorArgument = arg };

    /// <summary>스위치면 (소문자 이름, 값). <c>-</c> 하나(BC의 표준 입력)와 경로는 null.</summary>
    static (string Name, string? Value)? SwitchOf(string arg, bool slashSwitches)
    {
        string body;
        if (arg.StartsWith("--", StringComparison.Ordinal) && arg.Length > 2) body = arg[2..];
        else if (arg.Length > 1 && (arg[0] == '-' || slashSwitches && arg[0] == '/')) body = arg[1..];
        else return null;
        var eq = body.IndexOf('=');
        var name = (eq < 0 ? body : body[..eq]).ToLowerInvariant();
        var value = eq < 0 ? null : body[(eq + 1)..];
        if (value is { Length: >= 2 } && value[0] == '"' && value[^1] == '"') value = value[1..^1];
        return (name, value);
    }

    /// <summary>이미 떠 있는 앱에 넘길 인자 (경로는 절대 경로로 바꾼 것). 어느 OS에서든 <see cref="Parse"/>로 다시 읽힌다.</summary>
    public static string[] Format(CliRequest request)
    {
        var args = new List<string>();
        if (request.Wait) args.Add("--wait");
        if (request.LeftTitle is { } lt) args.Add("--title1=" + lt);
        if (request.RightTitle is { } rt) args.Add("--title2=" + rt);
        if (request.LeftReadOnly) args.Add("--ro1");
        if (request.RightReadOnly) args.Add("--ro2");
        if (request.IgnoreUnimportant) args.Add("--iu");
        args.Add("--");
        args.AddRange(request.Paths);
        return [.. args];
    }
}
