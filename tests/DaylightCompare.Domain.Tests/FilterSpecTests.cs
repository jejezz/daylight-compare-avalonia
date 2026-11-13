using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Domain.Tests;

public class FilterSpecTests
{
    const string BackSlash = "\\";

    [Fact]
    public void 포함과_제외_칸을_합쳐_하나의_마스크로_만든다()
    {
        var mask = new FilterSpec("*.cs;*.h", "obj" + BackSlash + ";*.tmp").ToMask();
        Assert.True(mask.Matches("a.cs"));
        Assert.False(mask.Matches("a.txt")); // 포함 목록에 없다
        Assert.False(mask.Matches("a.tmp"));
        Assert.False(mask.MatchesFolder("obj"));
        Assert.True(mask.MatchesFolder("src"));
    }

    [Fact]
    public void 제외_칸은_마이너스가_없어도_제외이고_포함_칸의_마이너스도_제외로_받는다()
    {
        Assert.False(new FilterSpec("", "*.obj;-*.pdb").ToMask().Matches("a.pdb"));
        Assert.False(new FilterSpec("*.cs;-gen.cs", "").ToMask().Matches("gen.cs"));
        Assert.True(new FilterSpec("", "").ToMask().IsEmpty);
    }

    [Fact]
    public void 파일로_내보냈다가_다시_읽으면_같다()
    {
        var spec = new FilterSpec("*.cs;*.h", "bin" + BackSlash + ";*.obj");
        Assert.Equal(spec, FilterSpec.Parse(spec.Format()));
        Assert.Equal(FilterSpec.Empty, FilterSpec.Parse(FilterSpec.Empty.Format()));
    }

    [Fact]
    public void 주석_BOM_대소문자_여러_줄을_받고_알아볼_수_없으면_null()
    {
        var text = "﻿# 설명\r\nInclude: *.cs\r\ninclude: *.h\r\nEXCLUDE: bin" + BackSlash + "\r\n잡음\r\n";
        Assert.Equal(new FilterSpec("*.cs;*.h", "bin" + BackSlash), FilterSpec.Parse(text));
        Assert.Null(FilterSpec.Parse("hello world"));
    }
}
