using Avalonia.Platform.Storage;

namespace DaylightCompare.App.Views;

/// <summary>
/// 파일·폴더 고르기 창이 처음 열릴 곳 — 마지막으로 고른 곳 (설정 <c>last_pick_dir</c>). 매번 처음부터 찾아가지 않게 한다.
/// 폴더를 골랐으면 그 폴더의 <b>부모</b>를 기억한다 — 이웃 폴더(다른 버전·브랜치)를 이어서 고르기 쉽다.
/// </summary>
static class PickLocations
{
    public static async Task<IStorageFolder?> StartAsync(IStorageProvider provider)
    {
        if (App.Settings.Value.LastPickDir is not { Length: > 0 } dir || !Directory.Exists(dir)) return null;
        try { return await provider.TryGetFolderFromPathAsync(dir); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    public static void Remember(string pickedPath, bool isFolder)
    {
        var dir = Path.GetDirectoryName(isFolder ? pickedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : pickedPath);
        if (dir is { Length: > 0 } && dir != App.Settings.Value.LastPickDir) App.Settings.Update(s => s with { LastPickDir = dir });
    }
}
