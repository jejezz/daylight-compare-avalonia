using System.Text.Json.Nodes;
using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Domain.Tests;

public class FileMaskTests
{
    [Theory]
    [InlineData("*.c;*.h", "main.c", true)]
    [InlineData("*.c;*.h", "MAIN.H", true)]
    [InlineData("*.c;*.h", "main.cpp", false)]
    [InlineData("*.c;-test_*", "test_a.c", false)]
    [InlineData("-*.bak", "a.txt", true)]
    [InlineData("-*.bak", "a.bak", false)]
    [InlineData("", "anything", true)]
    [InlineData("file?.txt", "file1.txt", true)]
    [InlineData("file?.txt", "file10.txt", false)]
    public void 이름_마스크(string mask, string name, bool expected) => Assert.Equal(expected, new FileMask(mask).Matches(name));

    [Theory]
    [InlineData("src/*.c", "src/main.c", true)]
    [InlineData("src/*.c", "src/sub/main.c", false)]
    [InlineData("src/**/*.c", "src/sub/deep/main.c", true)]
    [InlineData("src/**/*.c", "src/main.c", true)]
    [InlineData("**/gen/*", "a/b/gen/x.cs", true)]
    public void 경로_마스크는_상대_경로와_맞춘다(string mask, string path, bool expected) =>
        Assert.Equal(expected, new FileMask(mask).Matches(path[(path.LastIndexOf('/') + 1)..], path));

    [Fact]
    public void 포함_패턴이_없으면_형식_고르기에는_맞지_않는다()
    {
        Assert.False(new FileMask("-*.bak").IncludesExplicitly("a.txt"));
        Assert.True(new FileMask("*.txt").IncludesExplicitly("a.txt"));
    }
}

/// <summary>Beyond Compare 필터 문법 (BC5 도움말 file_masks — BC_COMPAT.md §2).</summary>
public class BcFileMaskTests
{
    [Theory]
    [InlineData("*.*", "README", true)]          // *.* 은 확장자 없는 것도
    [InlineData("*.", "README", true)]           // *. = 확장자 없음
    [InlineData("*.", "a.txt", false)]
    [InlineData("[az]*.c", "a1.c", true)]
    [InlineData("[az]*.c", "b1.c", false)]
    [InlineData("[a-c]?.c", "b1.c", true)]
    [InlineData("[!az]*.c", "b1.c", true)]
    [InlineData("[!az]*.c", "a1.c", false)]
    [InlineData("[[]x].txt", "[x].txt", true)]
    [InlineData("*.pas;*.dfm;-*.dcu", "unit.dcu", false)]
    public void 와일드카드(string mask, string name, bool expected) => Assert.Equal(expected, new FileMask(mask).Matches(name));

    [Fact]
    public void 끝이_역빗금이면_폴더_마스크다()
    {
        var m = new FileMask(@"*.cs;-.git\;-bin\");
        Assert.False(m.MatchesFolder(".git"));
        Assert.False(m.MatchesFolder("BIN"));
        Assert.True(m.MatchesFolder("src"));
        Assert.True(m.Matches("a.cs"));
        Assert.False(m.Matches("bin")); // 파일 이름 bin은 *.cs가 아니라서 — 폴더 마스크는 파일에 쓰지 않는다
        Assert.True(new FileMask(@"-bin\").Matches("bin"));
    }

    [Fact]
    public void 폴더_포함_마스크()
    {
        var m = new FileMask(@"Source\");
        Assert.True(m.HasFolderIncludes);
        Assert.True(m.MatchesFolder("Source"));
        Assert.False(m.MatchesFolder("Docs"));
        Assert.True(m.Matches("anything.txt")); // 파일 포함 마스크는 없으므로 파일은 모두
    }

    [Theory]
    [InlineData(@"p\f.txt", "p/f.txt", true)]
    [InlineData(@"p\f.txt", "q/p/f.txt", false)]
    [InlineData(@".\f.txt", "f.txt", true)]
    [InlineData(@".\f.txt", "sub/f.txt", false)]
    [InlineData(@"...\f.txt", "a/b/f.txt", true)]
    [InlineData(@"...\f.txt", "f.txt", true)]
    [InlineData(@"-...\gen\*.cs", "a/gen/x.cs", false)]
    public void 상대_마스크(string mask, string path, bool expected) =>
        Assert.Equal(expected, new FileMask(mask).Matches(path[(path.LastIndexOf('/') + 1)..], path));

    [Fact]
    public void 정책의_제외_폴더는_폴더에만_쓴다()
    {
        var rules = FolderRules.Parse(new JsonObject
        {
            ["filters"] = new JsonObject { ["excludeFolders"] = new JsonArray("build") },
        }, [], namesIgnoreCase: true);
        Assert.False(rules.Folders.MatchesFolder("build"));
        Assert.True(rules.Folders.Matches("build")); // 이름이 build인 파일은 그대로
    }
}
