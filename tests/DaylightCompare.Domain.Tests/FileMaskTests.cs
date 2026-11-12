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
