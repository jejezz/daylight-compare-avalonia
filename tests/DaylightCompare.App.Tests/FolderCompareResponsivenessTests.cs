using System.Diagnostics;
using Avalonia.Threading;
using DaylightCompare.App.Compare;
using Avalonia.Headless.XUnit;

namespace DaylightCompare.App.Tests;

/// <summary>
/// 폴더 비교가 도는 동안 UI가 멈추지 않는다 (네트워크 드라이브처럼 느린 곳·파일이 많은 폴더에서 앱이 통째로 멈추던 문제).
/// 비교 엔진은 백그라운드 스레드에서 돌아야 한다 — UI 스레드에서 이어지면 파일마다의 읽기·해석·diff가 UI 스레드를 점유하고,
/// 일반 우선순위의 이어지기가 입력·그리기보다 먼저 처리되어 화면이 굳는다.
/// </summary>
public sealed class FolderCompareResponsivenessTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dcmp-freeze").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [AvaloniaFact]
    public async Task 내용_비교가_많아도_UI의_입력_처리가_밀리지_않는다()
    {
        // 줄바꿈만 다르고 시각이 다른 큰 텍스트 파일 120쌍 → 규칙 기준 내용 비교(해석 + diff)를 모두 거친다.
        var body = string.Join("\n", Enumerable.Range(0, 4000).Select(i => $"line {i} 가나다 some text to make the line longer than usual {i * 7}"));
        Directory.CreateDirectory(Path.Combine(_dir, "l"));
        Directory.CreateDirectory(Path.Combine(_dir, "r"));
        for (var i = 0; i < 120; i++)
        {
            var l = Path.Combine(_dir, "l", $"f{i}.txt");
            var r = Path.Combine(_dir, "r", $"f{i}.txt");
            File.WriteAllText(l, body + "\n");
            File.WriteAllText(r, body.Replace("\n", "\r\n") + "\r\n");
            File.SetLastWriteTimeUtc(l, DateTime.UtcNow.AddMinutes(-30));
        }

        var w = FolderCompareWindow.Open(null, Path.Combine(_dir, "l"), Path.Combine(_dir, "r"));
        var sw = Stopwatch.StartNew();
        var last = sw.Elapsed;
        var worst = TimeSpan.Zero;
        var ticks = 0;
        while (sw.Elapsed < TimeSpan.FromSeconds(5))
        {
            // 입력 우선순위 작업이 얼마나 늦게 실행되는지 본다 (사용자의 클릭·키 입력이 처리되는 우선순위).
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
            var now = sw.Elapsed;
            worst = TimeSpan.FromTicks(Math.Max(worst.Ticks, (now - last).Ticks));
            last = now;
            ticks++;
            await Task.Delay(10);
        }
        w.Close();
        Assert.True(worst < TimeSpan.FromMilliseconds(400), $"UI가 최대 {worst.TotalMilliseconds:F0}ms 멈췄다 (확인 {ticks}번)");
    }
}
