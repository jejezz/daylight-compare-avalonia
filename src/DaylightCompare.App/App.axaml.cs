using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DaylightCompare.App.Updates;
using DaylightCompare.Data;
using DaylightCompare.Domain;
using DaylightCompare.Domain.Policy;
using DaylightCompare.Domain.Settings;
using DaylightCompare.Services;
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

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new HomeWindow();
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
