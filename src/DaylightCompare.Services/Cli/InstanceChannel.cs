using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;

namespace DaylightCompare.Services.Cli;

/// <summary>
/// 떠 있는 앱(본체)과 새로 실행한 프로세스 사이의 통로 — 이름 있는 파이프 (Windows는 명명된 파이프, macOS·Linux는 Unix 소켓).
/// 새 프로세스는 인자를 JSON 배열 한 줄로 보내고, 본체가 돌려주는 숫자 한 줄(종료 코드)을 기다린다.
/// <c>--wait</c>가 아니면 본체가 받자마자 0을 돌려주고, <c>--wait</c>면 그 비교 창을 닫을 때 결과를 돌려준다.
/// 파이프는 같은 사용자만 열 수 있다(<see cref="PipeOptions.CurrentUserOnly"/>).
/// </summary>
public static class InstanceChannel
{
    /// <summary>사용자·로그온 세션마다 다른 이름 (Unix 소켓은 /tmp에 생겨서 사용자끼리 부딪히지 않게).</summary>
    public static string PipeName(string appId) =>
        $"{appId}.{Environment.UserName}.{System.Diagnostics.Process.GetCurrentProcess().SessionId}";

    /// <summary>
    /// 본체에 인자를 넘기고 본체가 돌려준 종료 코드를 받는다. 본체가 없거나 응답이 끊기면 null.
    /// <paramref name="connectTimeout"/> 안에 연결하지 못하면 본체가 없는 것으로 본다.
    /// </summary>
    public static async Task<int?> SendAsync(string pipeName, IReadOnlyList<string> args, TimeSpan connectTimeout, CancellationToken ct = default)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(connectTimeout);
            await pipe.ConnectAsync(timeout.Token);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\n" };
            await writer.WriteLineAsync(new JsonArray([.. args.Select(a => (JsonNode?)JsonValue.Create(a))]).ToJsonString());
            await writer.FlushAsync(ct);
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            var line = await reader.ReadLineAsync(ct);
            return int.TryParse(line, out var code) ? code : null;
        }
        catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 본체 쪽: 연결을 받아 <paramref name="handle"/>에 인자를 넘기고, 그 결과(종료 코드)를 돌려보낸다.
    /// 연결마다 따로 처리하므로 <c>--wait</c>로 오래 기다리는 연결이 있어도 다음 연결을 받는다. <paramref name="ct"/>로 멈춘다.
    /// </summary>
    public static async Task ServeAsync(string pipeName, Func<string[], Task<int>> handle, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct);
            }
            catch (OperationCanceledException) { return; }
            catch (IOException) { await Task.Delay(200, CancellationToken.None); continue; }
            _ = Task.Run(() => AnswerAsync(pipe, handle, ct), CancellationToken.None);
        }
    }

    static async Task AnswerAsync(NamedPipeServerStream pipe, Func<string[], Task<int>> handle, CancellationToken ct)
    {
        await using var _ = pipe;
        try
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            var line = await reader.ReadLineAsync(ct);
            if (line is null) return;
            var args = (JsonNode.Parse(line) as JsonArray)?.Select(n => n?.GetValue<string>() ?? "").ToArray() ?? [];
            var code = await handle(args);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\n" };
            await writer.WriteLineAsync(code.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await writer.FlushAsync(ct);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException or ObjectDisposedException)
        {
            // 상대가 먼저 끝났거나 잘못된 요청 — 무시한다
        }
    }
}
