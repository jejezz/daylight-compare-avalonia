using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Domain.Tests;

public class FormatOverrideTests
{
    static ComparePolicy Builtin() => ComparePolicy.Builtin;

    [Fact]
    public void 형식_id로_고정하면_이름과_상관없이_그_형식을_쓴다()
    {
        var p = Builtin();
        Assert.Null(p.Resolve("a.dat").Format);
        Assert.Equal("c-family", p.Resolve("a.dat", formatId: "c-family").Format?.Id);
    }

    [Fact]
    public void 빈_글이면_이름이_C여도_형식_없음이다()
    {
        var p = Builtin();
        Assert.Equal("c-family", p.Resolve("a.c").Format?.Id);
        Assert.Null(p.Resolve("a.c", formatId: "").Format);
        Assert.Null(p.Resolve("a.c", formatId: "없는-형식").Format);
    }
}
