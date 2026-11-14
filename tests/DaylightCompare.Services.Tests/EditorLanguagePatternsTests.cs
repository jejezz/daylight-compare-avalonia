using DaylightCompare.Services;

namespace DaylightCompare.Services.Tests;

public class EditorLanguagePatternsTests
{
    [Fact]
    public void 마스크에서_강조_확장자를_고른다()
    {
        Assert.Equal(".c", EditorLanguages.ExtensionForPatterns(["*.c", "*.h"]));
        Assert.Equal(".mk", EditorLanguages.ExtensionForPatterns(["Makefile", "*.mk"]));
        Assert.Equal(".py", EditorLanguages.ExtensionForPatterns(["src/**", "*.py"]));
        Assert.Null(EditorLanguages.ExtensionForPatterns(["*.txt", "*.log"])); // 강조 없는 확장자뿐
        Assert.Null(EditorLanguages.ExtensionForPatterns([]));
    }
}
