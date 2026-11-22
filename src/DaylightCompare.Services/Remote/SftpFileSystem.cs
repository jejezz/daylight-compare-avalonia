// From daylight-commander-avalonia @ 4686eb7
// 고침(이 앱): 서버 해시(IServerHasher, 4c) — SSH exec로 sha256sum. Commander에도 쓸 만하다(폴더 비교·동기화의 내용 비교).
using System.Net.Sockets;
using DaylightCompare.Domain;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace DaylightCompare.Services;

/// <summary>
/// SFTP 서버 하나(연결 하나)를 <see cref="IFileSystem"/>으로 보이게 한 것. SSH.NET의 동기 호출을 작업 스레드에서 부르고, 한 번에 하나씩만
/// 보낸다 (한 연결을 여러 작업이 나눠 쓰므로). SSH.NET 예외는 여기서 <see cref="FileOperationException"/>으로 바꿔 위로 올린다 —
/// 서비스 위쪽은 SSH.NET을 모른다.
/// <para>
/// 서버 해시(<see cref="IServerHasher"/>): 같은 계정으로 SSH 연결을 하나 더 열어 <c>sha256sum</c>(없으면 <c>shasum -a 256</c>)을 돌린다.
/// 그 연결의 호스트 키는 SFTP 연결이 본 지문(<paramref name="hostKeyFingerprint"/>)과 같아야 한다 — 지문을 모르면 서버 해시를 하지 않는다.
/// </para>
/// </summary>
public sealed class SftpFileSystem(SftpClient client, string authority, string? hostKeyFingerprint = null) : IFileSystem, IServerHasher, IDisposable
{
    readonly SemaphoreSlim _gate = new(1, 1);

    // 서버 해시용 SSH 연결 (처음 쓸 때 연다). 열기·도구 찾기에 실패하면 이 연결 동안은 다시 시도하지 않는다(내려받아 해시).
    readonly SemaphoreSlim _sshGate = new(1, 1);
    SshClient? _ssh;
    bool _sshUnavailable, _probed;
    string? _hashTool;

    public bool IsConnected => client.IsConnected;

    /// <summary>로그인 직후의 작업 폴더 (서버 안의 경로).</summary>
    public string WorkingDirectory => client.WorkingDirectory;

    string ServerPath(string path) => Locations.TryParse(path)?.Path ?? throw new FileOperationException(new FileError.Other(path));

    string UrlFor(string serverPath) =>
        Locations.TryParse(authority + "/") is { } loc ? Locations.Format(loc with { Path = Locations.NormalizeServerPath(serverPath) }) : serverPath;

    /// <summary>한 번에 하나씩, 작업 스레드에서 부른다. SSH.NET 예외를 앱의 오류로 바꾼다.</summary>
    async Task<T> Run<T>(Func<T> work, CancellationToken ct, string? name = null)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            return await Task.Run(() =>
            {
                try
                {
                    return work();
                }
                catch (SftpPathNotFoundException)
                {
                    throw new FileOperationException(new FileError.NotFound(name ?? ""));
                }
                catch (SftpPermissionDeniedException)
                {
                    throw new FileOperationException(new FileError.AccessDenied(name ?? ""));
                }
                catch (Exception e) when (e is SshConnectionException or SshOperationTimeoutException or ObjectDisposedException)
                {
                    throw new FileOperationException(new FileError.RemoteNotConnected(authority));
                }
                catch (InvalidOperationException e) when (e.Message.Contains("not connected", StringComparison.OrdinalIgnoreCase))
                {
                    throw new FileOperationException(new FileError.RemoteNotConnected(authority));
                }
                catch (SshException e)
                {
                    throw new FileOperationException(new FileError.Other(e.Message));
                }
            }, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IReadOnlyList<FileEntry>> ListAsync(string path, CancellationToken ct = default) => Run<IReadOnlyList<FileEntry>>(() =>
    {
        var entries = new List<FileEntry>();
        foreach (var item in client.ListDirectory(ServerPath(path)))
        {
            if (item.Name is "." or "..") continue;
            ct.ThrowIfCancellationRequested();
            entries.Add(ToEntry(item));
        }
        return EntrySorting.Sort(entries, SortField.Name, ascending: true);
    }, ct, Locations.NameOf(path));

    FileEntry ToEntry(Renci.SshNet.Sftp.ISftpFile item) => new(
        Path: UrlFor(item.FullName),
        Name: item.Name,
        IsDirectory: item.IsDirectory,
        SizeBytes: item.IsDirectory ? null : item.Length,
        ModifiedAt: new DateTimeOffset(item.LastWriteTimeUtc, TimeSpan.Zero),
        IsHidden: item.Name.StartsWith('.'));

    public Task<FileEntry?> StatAsync(string path, CancellationToken ct = default) => Run<FileEntry?>(() =>
    {
        var server = ServerPath(path);
        if (!client.Exists(server)) return null;
        return ToEntry(client.Get(server));
    }, ct, Locations.NameOf(path));

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default) =>
        Run<Stream>(() => client.OpenRead(ServerPath(path)), ct, Locations.NameOf(path));

    public Task<Stream> OpenWriteAsync(string path, bool overwrite, CancellationToken ct = default) =>
        Run<Stream>(() => client.Open(ServerPath(path), overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write), ct, Locations.NameOf(path));

    public Task CreateDirectoryAsync(string path, CancellationToken ct = default) =>
        Run(() => { client.CreateDirectory(ServerPath(path)); return 0; }, ct, Locations.NameOf(path));

    public Task DeleteAsync(string path, CancellationToken ct = default) =>
        Run(() => { DeleteRecursive(ServerPath(path), ct); return 0; }, ct, Locations.NameOf(path));

    void DeleteRecursive(string serverPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var item = client.Get(serverPath);
        if (item.IsDirectory && !item.IsSymbolicLink)
        {
            foreach (var child in client.ListDirectory(serverPath))
                if (child.Name is not ("." or "..")) DeleteRecursive(child.FullName, ct);
            client.DeleteDirectory(serverPath);
        }
        else
        {
            client.DeleteFile(serverPath);
        }
    }

    public Task RenameAsync(string from, string to, CancellationToken ct = default) =>
        Run(() => { client.RenameFile(ServerPath(from), ServerPath(to)); return 0; }, ct, Locations.NameOf(from));

    public Task SetModifiedAsync(string path, DateTimeOffset modified, CancellationToken ct = default) => Run(() =>
    {
        try { client.SetLastWriteTimeUtc(ServerPath(path), modified.UtcDateTime); }
        catch (SshException) { } // 서버가 거부해도 복사 자체는 성공이다
        return 0;
    }, ct, Locations.NameOf(path));

    public async Task<IReadOnlyDictionary<string, byte[]>?> HashSha256Async(IReadOnlyList<string> paths, CancellationToken ct = default)
    {
        if (paths.Count == 0) return new Dictionary<string, byte[]>();
        if (hostKeyFingerprint is null) return null;
        await _sshGate.WaitAsync(ct);
        try
        {
            if (_sshUnavailable) return null;
            var ssh = _ssh ??= await Task.Run(OpenSsh, ct);
            if (!_probed)
            {
                _hashTool = ServerHashCommands.ToolCommand(await ExecAsync(ssh, ServerHashCommands.Probe, ct));
                _probed = true;
            }
            if (_hashTool is null)
            {
                _sshUnavailable = true;
                return null;
            }
            // 서버 경로 → 앱 경로 (같은 서버 경로로 오는 앱 경로가 여럿일 수 있다)
            var byServer = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var path in paths)
            {
                var server = ServerPath(path);
                if (!byServer.TryGetValue(server, out var list)) byServer[server] = list = [];
                list.Add(path);
            }
            var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var (command, _) in ServerHashCommands.Batches(_hashTool, [.. byServer.Keys]))
                foreach (var (name, hash) in ServerHashCommands.Parse(await ExecAsync(ssh, command, ct)))
                    if (byServer.TryGetValue(name, out var list))
                        foreach (var path in list) result[path] = hash;
            return result;
        }
        catch (Exception e) when (e is SshException or SocketException or ObjectDisposedException or InvalidOperationException or TimeoutException)
        {
            _sshUnavailable = true; // exec를 막은 서버(SFTP 전용 계정 등)·끊긴 연결: 내려받아 해시한다
            return null;
        }
        finally
        {
            _sshGate.Release();
        }
    }

    SshClient OpenSsh()
    {
        var ssh = new SshClient(client.ConnectionInfo);
        ssh.HostKeyReceived += (_, e) => e.CanTrust = e.FingerPrintSHA256 == hostKeyFingerprint;
        try
        {
            ssh.Connect();
            return ssh;
        }
        catch
        {
            ssh.Dispose();
            throw;
        }
    }

    static async Task<string> ExecAsync(SshClient ssh, string commandText, CancellationToken ct)
    {
        using var command = ssh.CreateCommand(commandText);
        await command.ExecuteAsync(ct);
        return command.Result; // 몇 파일을 못 읽어 종료 코드가 0이 아니어도 읽은 것은 쓴다
    }

    public void Dispose()
    {
        try { if (client.IsConnected) client.Disconnect(); } catch (Exception e) when (e is SshException or ObjectDisposedException) { }
        client.Dispose();
        _gate.Dispose();
        try { _ssh?.Dispose(); } catch (Exception e) when (e is SshException or ObjectDisposedException) { }
    }
}
