using System.Diagnostics;
using DaylightCompare.Domain.Folder;
using Xunit.Abstractions;

namespace DaylightCompare.Domain.Tests;

/// <summary>폴더 비교 창은 비교가 도는 동안 줄 목록을 자주 다시 만든다 — 항목이 아주 많아도 한 번이 짧아야 UI가 굳지 않는다.</summary>
public sealed class FolderViewPerformanceTests(ITestOutputHelper output)
{
    static readonly DateTimeOffset T0 = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    static FolderNode BuildTree(int folders, int filesPerFolder)
    {
        var root = new FolderNode("", "", true, new FileEntry("/l", "l", true), new FileEntry("/r", "r", true), null) { Scanning = false };
        for (var d = 0; d < folders; d++)
        {
            var rel = $"d{d}";
            var dir = new FolderNode(rel, rel, true, new FileEntry("/l/" + rel, rel, true), new FileEntry("/r/" + rel, rel, true), root) { Scanning = false, State = FolderState.Different };
            root.Children.Add(dir);
            for (var f = 0; f < filesPerFolder; f++)
            {
                var name = $"f{f}.txt";
                var path = rel + "/" + name;
                dir.Children.Add(new FolderNode(name, path, false, new FileEntry("/l/" + path, name, false, 10, T0), new FileEntry("/r/" + path, name, false, 11, T0), dir)
                {
                    State = f % 5 == 0 ? FolderState.Different : FolderState.Same,
                });
            }
        }
        return root;
    }

    [Fact]
    public void 십만_개_항목도_줄_목록과_개수를_한_번에_수십_밀리초_안에_만든다()
    {
        var root = BuildTree(folders: 1000, filesPerFolder: 100); // 파일 10만 개
        var open = Enumerable.Range(0, 1000).ToDictionary(i => $"d{i}", _ => true); // 모두 펴진 최악의 경우
        var sw = Stopwatch.StartNew();
        var rows = FolderView.Flatten(root, FolderShow.All, alwaysShowFolders: false, open);
        var flatten = sw.Elapsed;
        sw.Restart();
        var count = FolderView.Count(root);
        var counting = sw.Elapsed;
        output.WriteLine($"Flatten {flatten.TotalMilliseconds:F0}ms ({rows.Count}줄), Count {counting.TotalMilliseconds:F0}ms ({count})");
        Assert.True(flatten < TimeSpan.FromMilliseconds(300), $"줄 목록 만들기가 느리다: {flatten.TotalMilliseconds:F0}ms");
        Assert.True(counting < TimeSpan.FromMilliseconds(300), $"개수 세기가 느리다: {counting.TotalMilliseconds:F0}ms");
    }
}
