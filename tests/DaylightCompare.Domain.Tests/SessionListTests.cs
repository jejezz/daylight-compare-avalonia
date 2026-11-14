using DaylightCompare.Domain.Settings;

namespace DaylightCompare.Domain.Tests;

public class SessionListTests
{
    static SavedSession S(string name, params string[] lefts) => new()
    {
        Name = name,
        Window = new SessionWindow { Tabs = [.. lefts.Select(l => new SessionTab { Left = l, Right = l + "2" })] },
    };

    [Fact]
    public void 새_이름은_맨_앞에_같은_이름은_제자리를_바꾼다()
    {
        var list = SessionList.Save([], S("a", "1"));
        list = SessionList.Save(list, S("b", "2"));
        Assert.Equal(["b", "a"], list.Select(s => s.Name));
        list = SessionList.Save(list, S("A ", "9")); // 대소문자·앞뒤 공백을 가리지 않는다
        Assert.Equal(["b", "A"], list.Select(s => s.Name));
        Assert.Equal("9", list[1].Window.Tabs[0].Left);
    }

    [Fact]
    public void 이름이_비었거나_탭이_없으면_저장하지_않는다()
    {
        IReadOnlyList<SavedSession> list = [S("a", "1")];
        Assert.Same(list, SessionList.Save(list, S("  ", "1")));
        Assert.Same(list, SessionList.Save(list, S("b")));
    }

    [Fact]
    public void 지우기와_이름_바꾸기()
    {
        IReadOnlyList<SavedSession> list = [S("a", "1"), S("b", "2")];
        Assert.Equal(["b"], SessionList.Remove(list, "A").Select(s => s.Name));
        Assert.Equal(["a", "c"], SessionList.Rename(list, "b", " c ").Select(s => s.Name));
        Assert.Same(list, SessionList.Rename(list, "b", "a"));   // 다른 세션과 같은 이름
        Assert.Same(list, SessionList.Rename(list, "b", " "));
        Assert.Same(list, SessionList.Rename(list, "없음", "x"));
        Assert.Equal(["A", "b"], SessionList.Rename(list, "a", "A").Select(s => s.Name)); // 자기 자신은 대소문자만 바꿀 수 있다
    }

    [Fact]
    public void 보기_필터_이름은_서로_돌아온다()
    {
        foreach (var f in Enum.GetValues<CompareViewFilter>())
            Assert.Equal(f, SessionTab.ViewFilterOf(SessionTab.ViewFilterName(f)));
        Assert.Equal(CompareViewFilter.All, SessionTab.ViewFilterOf("모르는 값"));
    }
}
