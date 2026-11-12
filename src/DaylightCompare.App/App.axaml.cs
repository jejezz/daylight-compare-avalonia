using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DaylightCompare.App.Cli;
using DaylightCompare.App.Dialogs;
using DaylightCompare.App.Updates;
using DaylightCompare.Data;
using DaylightCompare.Domain;
using DaylightCompare.Domain.Cli;
using DaylightCompare.Domain.Policy;
using DaylightCompare.Domain.Settings;
using DaylightCompare.Services;
using DaylightCompare.Services.Cli;
using DaylightCompare.Services.Updates;

namespace DaylightCompare.App;

public partial class App : Application
{
    /// <summary>앱 설정의 단일 출처. 모든 창이 이것을 읽고 바꾼다.</summary>
    public static SettingsController Settings { get; private set; } = null!;

    /// <summary>Compare Policy 파일 (설정과 같은 폴더의 <c>policy.json</c>). 비교 화면이 열 때마다 <see cref="PolicyStore.LoadAsync"/>로 다시 읽는다.</summary>
    public static PolicyStore Policy { get; private set; } = null!;

    /// <summary>시작할 때 읽은 정책 (경고·깨진 파일 안내용). 비교 화면은 최신 파일을 다시 읽는다.</summary>
    public static ComparePolicy? StartupPolicy { get; private set; }

    /// <summary>업데이트 확인·설치. 서버 주소가 없는 빌드(<c>-p:AppUpdateServer=</c>)나 로컬 빌드는 null.</summary>
    internal static UpdateService? Updates { get; private set; }

    /// <summary>화면에 이미 적용한 설정. 방문 기록 같은 다른 설정이 바뀔 때마다 외형을 다시 적용하지 않으려고 둔다.</summary>
    AppSettings? _applied;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Settings = new SettingsController(new JsonSettingsRepository(JsonSettingsRepository.DefaultFile(AppIdentity.FileName)));
        Policy = new PolicyStore(Path.GetDirectoryName(JsonSettingsRepository.DefaultFile(AppIdentity.FileName))!);
        // 처음 실행이면 내장 정책으로 policy.json을 만든다. 화면을 늦추지 않게 UI 스레드 밖에서.
        _ = Task.Run(async () => StartupPolicy = await Policy.LoadAsync());

        // 창을 만들기 전에 색·글꼴·언어를 정해 둔다 (첫 프레임부터 올바른 모습).
        ApplyAppearance(Settings.Value);
        Settings.Changed += s => Dispatcher.UIThread.Post(() => ApplyAppearance(s));

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { } standalone && Program.Mode == LaunchMode.WaitStandalone)
        {
            var desktop = standalone;
            // --wait인데 본체가 없다: 비교 창 하나만 띄우고, 닫으면 결과를 종료 코드로 남기고 끝난다 (업데이트 확인도 하지 않는다).
            var request = Program.Request;
            var window = Compare.CompareWindow.Open(null, request.Paths[0], request.Paths.ElementAtOrDefault(1), OptionsOf(request));
            desktop.MainWindow = window;
            window.Closed += async (_, _) => desktop.Shutdown(await ResultOfAsync(window));
            desktop.Exit += (_, _) => Settings.Dispose();
        }
        else if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { } desktop)
        {
            var home = new HomeWindow();
            desktop.MainWindow = home;
            home.Opened += async (_, _) => await HandleAsync(desktop, Program.Request, home);
            // 다른 실행(CLI·git difftool·탐색기)이 넘긴 인자. 응답(종료 코드)은 그 실행이 --wait일 때 의미가 있다.
            SingleInstance.Listen(args => Dispatcher.UIThread.InvokeAsync(() => HandleAsync(desktop, CommandLine.Parse(args, slashSwitches: OperatingSystem.IsWindows()), null)));
            Updates = UpdateService.Create(
                FileUpdateStateStore.BesideSettings(JsonSettingsRepository.DefaultFile(AppIdentity.FileName)),
                owner: () => desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow,
                // 동기화 작업이 생기면(7단계) 진행 중에는 알리지도, 설치하지도 않게 여기서 막는다.
                isBusy: () => false);
            Updates?.StartAutomaticCheck();
            desktop.Exit += (_, _) => Settings.Dispose(); // 종료할 때 마지막 변경까지 저장
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 실행 인자 하나를 처리한다 — 처음 실행한 인자(<paramref name="near"/> = 홈 창)든 다른 실행이 넘긴 것이든.
    /// 돌려주는 값은 그 실행의 종료 코드: <c>--wait</c>면 비교 창을 닫을 때의 결과, 아니면 0(잘못된 인자는 2).
    /// </summary>
    static async Task<int> HandleAsync(IClassicDesktopStyleApplicationLifetime desktop, CliRequest request, Window? near)
    {
        near ??= desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.Windows.LastOrDefault(w => w.IsVisible);
        if (!request.IsValid || request.Help)
        {
            var owner = near ?? ShowHome(desktop);
            var s = Strings.Current;
            var message = request.IsValid ? s.CliUsage : $"{CliErrorText(request)}\n\n{s.CliUsage}";
            await MessageDialog.ShowAsync(owner, s.CliUsageTitle, message, [s.CommonClose], 0, 0);
            return (int)(request.IsValid ? ExitCode.Success : ExitCode.Error);
        }
        if (!request.IsCompare)
        {
            // 인자 없이 다시 실행했다 — 떠 있는 창을 앞으로 (홈 창을 닫았으면 다시 연다).
            BringToFront(desktop.Windows.OfType<HomeWindow>().FirstOrDefault() ?? ShowHome(desktop));
            return 0;
        }
        var window = Compare.CompareWindow.Open(near, request.Paths[0], request.Paths.ElementAtOrDefault(1), OptionsOf(request));
        BringToFront(window);
        if (!request.Wait) return 0;
        var closed = new TaskCompletionSource();
        window.Closed += (_, _) => closed.TrySetResult();
        await closed.Task;
        return await ResultOfAsync(window);
    }

    /// <summary>
    /// 닫은 비교 창의 종료 코드. BC처럼 창으로 비교하면 결과와 상관없이 0(성공)이다 — 결과가 필요하면 <c>/qc</c>.
    /// (git difftool은 종료 코드를 보지 않는다. <c>trustExitCode</c>를 켜도 멈추지 않는다.)
    /// </summary>
    static Task<int> ResultOfAsync(Compare.CompareWindow window) => Task.FromResult((int)ExitCode.Success);

    static Compare.CompareOptions OptionsOf(CliRequest r) =>
        new(r.LeftTitle, r.RightTitle, r.LeftReadOnly, r.RightReadOnly, r.IgnoreUnimportant ? true : null);

    static string CliErrorText(CliRequest request) => request.Error switch
    {
        CliError.UnknownOption => Strings.Current.CliUnknownOption(request.ErrorArgument ?? ""),
        CliError.TooManyPaths => Strings.Current.CliTooManyPaths,
        CliError.UnsupportedOption => Strings.Current.CliUnsupportedOption(request.ErrorArgument ?? ""),
        CliError.QuickCompareNeedsTwoPaths => Strings.Current.CliQuickCompareNeedsTwoPaths,
        _ => "",
    };

    static HomeWindow ShowHome(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var home = new HomeWindow();
        home.Show();
        return home;
    }

    static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    void ApplyAppearance(AppSettings s)
    {
        var previous = _applied;
        _applied = s;
        if (previous is null || previous.Theme != s.Theme || previous.Palette != s.Palette
            || previous.TextScale != s.TextScale || previous.UiFontFamily != s.UiFontFamily)
            ThemeService.Apply(s);
        if (previous is null || previous.Locale != s.Locale) Strings.Apply(s.Locale);
    }
}
