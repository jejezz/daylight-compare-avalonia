using Avalonia;
using Avalonia.Headless;
using DaylightCompare.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace DaylightCompare.App.Tests;

/// <summary>
/// 시험용 앱: 실제 <see cref="App"/>을 Headless 플랫폼 + Skia로 띄운다 (창은 화면에 나오지 않지만 픽셀은 그려진다).
/// 사용자 설정·정책 파일을 건드리지 않게 사용자 데이터 폴더(APPDATA·XDG_CONFIG_HOME)를 임시 폴더로 돌린다.
/// </summary>
public static class TestApp
{
    public static readonly string DataDir = Directory.CreateTempSubdirectory("dcmp-ui-data").FullName;

    /// <summary>화면 사진을 남길 폴더 (환경 변수 DCMP_SHOTS, 없으면 임시 폴더).</summary>
    public static readonly string Shots = Environment.GetEnvironmentVariable("DCMP_SHOTS") is { Length: > 0 } s
        ? Directory.CreateDirectory(s).FullName
        : Directory.CreateTempSubdirectory("dcmp-shots").FullName;

    public static AppBuilder BuildAvaloniaApp()
    {
        Environment.SetEnvironmentVariable("APPDATA", DataDir);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", DataDir);
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
