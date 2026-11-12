using System.Diagnostics;

namespace DaylightCompare.Services;

/// <summary>
/// 주소나 파일을 OS 기본 앱으로 연다 (브라우저로 저장소 열기, 릴리스 페이지 열기 등).
/// Daylight Commander의 <c>OpenWithDefaultApp</c>을 줄인 것 — 파일 작업 오류 모델이 아직 없으므로 성공 여부만 돌려준다.
/// </summary>
public static class ShellOpen
{
    /// <summary>열었으면 true. 실패해도 예외를 던지지 않는다(정보 창·업데이트 창은 그대로 둔다).</summary>
    public static bool TryOpen(string target)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                using (Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })) { }
            else if (OperatingSystem.IsMacOS())
                using (Process.Start("open", [target])) { }
            else
                using (Process.Start("xdg-open", [target])) { }
            return true;
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
