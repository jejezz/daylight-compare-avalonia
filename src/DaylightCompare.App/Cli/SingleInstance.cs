// Pattern from jacksbean-avalonia @ df07cf9 (src/JackBean.App/Services/SingleInstance.cs):
// 이름 있는 뮤텍스로 본체를 정한다. 다른 점 — 신호 대신 파이프로 인자를 넘기고(Windows만이 아니라 모든 OS), --wait면 결과를 돌려받는다.
using System.Runtime.InteropServices;
using DaylightCompare.Domain;
using DaylightCompare.Services.Cli;

namespace DaylightCompare.App.Cli;

/// <summary>
/// 앱 본체는 하나다. 이름 있는 뮤텍스(<see cref="AppIdentity.Id"/> — 설치 프로그램의 <c>AppMutex</c>도 같은 이름)를 먼저 잡은 쪽이 본체이고
/// 파이프(<see cref="InstanceChannel"/>)로 다른 실행의 인자를 받는다. 나중에 실행한 쪽은 인자를 넘기고 끝난다.
/// </summary>
internal static partial class SingleInstance
{
    static Mutex? _mutex;
    static readonly CancellationTokenSource Stop = new();

    static string PipeName => InstanceChannel.PipeName(AppIdentity.Id);

    /// <summary>본체가 되는가. 이미 본체가 있으면 false. 뮤텍스를 못 만드는 환경이면 막지 않는다(true).</summary>
    public static bool TryBecomePrimary()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, AppIdentity.Id, out var created);
            if (created) return true;
            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            return true;
        }
    }

    /// <summary>
    /// 떠 있는 본체에 인자를 넘긴다. 본체가 막 뜨는 중일 수 있어 <paramref name="patience"/> 동안 다시 시도한다.
    /// 넘기기 전에 본체가 창을 앞으로 가져올 수 있게 허락한다(Windows는 방금 실행한 프로세스만 앞으로 나올 권리가 있다).
    /// </summary>
    public static int? Forward(IReadOnlyList<string> args, TimeSpan patience)
    {
        if (OperatingSystem.IsWindows()) AllowSetForegroundWindow(AsfwAny);
        var until = DateTime.UtcNow + patience;
        do
        {
            var code = InstanceChannel.SendAsync(PipeName, args, TimeSpan.FromMilliseconds(500)).GetAwaiter().GetResult();
            if (code is not null) return code;
        } while (DateTime.UtcNow < until);
        return null;
    }

    /// <summary>본체가 다른 실행의 인자를 받기 시작한다. <paramref name="handle"/>은 백그라운드 스레드에서 불리므로 UI 일은 넘겨서 한다.</summary>
    public static void Listen(Func<string[], Task<int>> handle) =>
        _ = Task.Run(() => InstanceChannel.ServeAsync(PipeName, handle, Stop.Token));

    public static void Shutdown()
    {
        Stop.Cancel();
        try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex?.Dispose();
    }

    const uint AsfwAny = unchecked((uint)-1);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);
}
