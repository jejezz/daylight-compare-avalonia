using DaylightCompare.Domain;

namespace DaylightCompare.Domain.Tests;

public class AppIdentityTests
{
    [Fact]
    public void 표시_이름과_파일_이름은_규약대로다()
    {
        Assert.Equal("Daylight Compare", AppIdentity.DisplayName);
        Assert.Equal("DaylightCompare", AppIdentity.FileName);
        Assert.DoesNotContain(' ', AppIdentity.FileName);
    }

    [Fact]
    public void 식별자는_규약_형식이고_Daylight_Commander와_다르다()
    {
        // identity.md §3: <접두어>.<패키지명 소문자>. 처음부터 Avalonia 앱이라 .avalonia 접미사는 붙이지 않는다.
        Assert.Equal("art.zoomon.daylightcompare", AppIdentity.Id);
        Assert.NotEqual("art.zoomon.daylightcommander.avalonia", AppIdentity.Id);
    }

    [Fact]
    public void 저작권_문구는_첫_릴리스_연도를_쓴다()
    {
        Assert.Equal(2026, AppIdentity.FirstReleaseYear);
        Assert.Equal("Copyright © 2026 Jongyun Ahn", AppIdentity.Copyright);
    }
}
