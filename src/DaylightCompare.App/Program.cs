using Avalonia;
using DaylightCompare.App.Cli;
using DaylightCompare.Data;
using DaylightCompare.Domain;
using DaylightCompare.Domain.Cli;
using DaylightCompare.Services;
using DaylightCompare.Services.Cli;

namespace DaylightCompare.App;

/// <summary>실행 방식 (ROADMAP 3b).</summary>
internal enum LaunchMode
{
    /// <summary>본체: 홈 창을 띄우고 다른 실행의 인자를 받는다.</summary>
    Primary,
    /// <summary>
    /// <c>--wait</c>인데 본체가 없다: 비교 창 하나만 띄우고, 닫으면 결과를 종료 코드로 남기고 끝난다 (본체가 되지 않는다 —
    /// git difftool이 파일마다 띄웠다 닫는 창이 앱 전체의 수명을 쥐지 않게).
    /// </summary>
    WaitStandalone,
}

class Program
{
    /// <summary>해석한 실행 인자 (경로는 절대 경로).</summary>
    internal static CliRequest Request { get; private set; } = CliRequest.Empty;
    internal static LaunchMode Mode { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        var request = CommandLine.Parse(args);
        if (request.IsValid)
            request = request with { Paths = [.. request.Paths.Select(p => QuickCompare.ResolvePath(p, Environment.CurrentDirectory))] };
        Request = request;

        // --silent는 어떤 경우에도 창을 띄우지 않는다 (스크립트가 멈추지 않게) — 잘못된 인자면 오류 코드만.
        var silent = request.Silent || (!request.IsValid && args.Any(a => a is "--silent" or "-s")); // 오류 뒤의 --silent도
        if (silent && !request.Help) return (int)(request.IsValid ? RunSilent(request) : ExitCode.Error);

        if (request.IsValid && request.Wait && request.IsCompare && !request.Help)
        {
            // 떠 있는 본체가 있으면 거기서 열고 그 창이 닫히기를 기다린다. 없으면 혼자 뜬다.
            if (SingleInstance.Forward(CommandLine.Format(request), TimeSpan.Zero) is { } code) return code;
            Mode = LaunchMode.WaitStandalone;
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }

        if (!SingleInstance.TryBecomePrimary())
        {
            // 이미 떠 있다 — 인자를 넘기고(없으면 창을 앞으로) 끝낸다. 본체가 응답하지 않으면 그냥 뜬다.
            if (SingleInstance.Forward(request.IsValid ? CommandLine.Format(request) : args, TimeSpan.FromSeconds(5)) is { } code) return code;
        }
        Mode = LaunchMode.Primary;
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            SingleInstance.Shutdown();
        }
    }

    /// <summary><c>--silent</c>: 창 없이 비교하고 종료 코드만 남긴다.</summary>
    static ExitCode RunSilent(CliRequest request)
    {
        var dir = Path.GetDirectoryName(JsonSettingsRepository.DefaultFile(AppIdentity.FileName))!;
        return Task.Run(async () =>
        {
            var policy = await new PolicyStore(dir).LoadAsync();
            return await QuickCompare.RunAsync(request.Paths[0], request.Paths[1], policy);
        }).GetAwaiter().GetResult();
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
