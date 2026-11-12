using System.Text.Json.Nodes;
using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Domain.Tests;

public class ComparePolicyTests
{
    static ComparePolicy Parse(string json) => ComparePolicy.FromJson((JsonObject)JsonNode.Parse(json)!);

    [Fact]
    public void 내장_정책은_경고_없이_읽힌다()
    {
        var p = ComparePolicy.Builtin;
        Assert.Empty(p.Warnings);
        Assert.False(p.IsFallback);
        Assert.Contains(p.Formats, f => f.Id == "c-family");
        Assert.Contains("c-like", p.Grammars.Keys);
    }

    [Theory]
    [InlineData("main.c", "c-family", ViewKind.Text)]
    [InlineData("Program.CS", "c-family", ViewKind.Text)]
    [InlineData("build.py", "hash-comment", ViewKind.Text)]
    [InlineData("CMakeLists.txt", "hash-comment", ViewKind.Text)]
    [InlineData("App.axaml", "xml", ViewKind.Text)]
    [InlineData("data.csv", "table", ViewKind.Table)]
    [InlineData("logo.png", "images", ViewKind.Image)]
    [InlineData("fw.hex", "firmware-ihex", ViewKind.Binary)]
    [InlineData("app.exe", "binary", ViewKind.Binary)]
    public void 이름으로_파일_형식과_보기를_고른다(string name, string format, ViewKind view)
    {
        var e = ComparePolicy.Builtin.Resolve(name);
        Assert.Equal(format, e.Format?.Id);
        Assert.Equal(view, e.View);
    }

    [Fact]
    public void 모르는_파일은_형식_없이_텍스트다()
    {
        var e = ComparePolicy.Builtin.Resolve("unknown.zzz");
        Assert.Null(e.Format);
        Assert.Equal(ViewKind.Text, e.View);
        Assert.Null(e.Grammar);
    }

    [Fact]
    public void 형식의_규칙이_기본값을_덮고_문법이_따라온다()
    {
        var e = ComparePolicy.Builtin.Resolve("main.c");
        Assert.Equal("c-like", e.Grammar?.Id);
        Assert.Equal(["comment", "whitespace"], e.Text.Unimportant);
        Assert.True(e.Text.IgnoreTrailingWhitespace); // 기본값 층에서 온다
        Assert.Equal("\t", ComparePolicy.Builtin.Resolve("a.tsv").Table.Delimiter);
        Assert.Equal("ihex-to-bin", ComparePolicy.Builtin.Resolve("fw.hex").Converter?.Builtin);
    }

    [Fact]
    public void 겹치는_순서는_내장_사용자_기본값_형식_세션이다()
    {
        var p = Parse("""
        { "schema": 1,
          "defaults": { "text": { "normalize": { "case": true }, "tabWidth": 8 } },
          "formats": [ { "id": "py", "masks": ["*.py"], "text": { "tabWidth": 2 } } ] }
        """);
        var plain = p.Resolve("a.txt");
        Assert.True(plain.Text.IgnoreCase);       // 사용자 defaults
        Assert.True(plain.Text.IgnoreLineBreaks); // 내장 defaults (사용자가 normalize를 일부만 적어도 나머지가 남는다)
        Assert.Equal(8, plain.Text.TabWidth);

        var py = p.Resolve("a.py");
        Assert.Equal(2, py.Text.TabWidth);        // 형식이 defaults를 덮는다
        Assert.True(py.Text.IgnoreCase);

        var session = (JsonObject)JsonNode.Parse("""{ "text": { "tabWidth": 3, "normalize": { "case": false } }, "view": "binary" }""")!;
        var s = p.Resolve("a.py", session: session);
        Assert.Equal(3, s.Text.TabWidth);         // 세션이 마지막
        Assert.False(s.Text.IgnoreCase);
        Assert.Equal(ViewKind.Binary, s.View);
    }

    [Fact]
    public void 위에_있는_형식이_먼저_맞고_제외_마스크는_그_형식에서_뺀다()
    {
        var p = Parse("""
        { "schema": 1, "formats": [
            { "id": "generated", "masks": ["*.g.cs"], "view": "binary" },
            { "id": "cs", "masks": ["*.cs;-*.Designer.cs"], "view": "text" } ] }
        """);
        Assert.Equal("generated", p.Resolve("Icons.g.cs").Format?.Id);
        Assert.Equal("cs", p.Resolve("Main.cs").Format?.Id);
        Assert.Null(p.Resolve("Form1.Designer.cs").Format);
    }

    [Fact]
    public void 사용자가_지운_내장_형식은_되살아나지_않는다()
    {
        var p = Parse("""{ "schema": 1, "formats": [] }""");
        Assert.Null(p.Resolve("main.c").Format);
    }

    [Fact]
    public void 틀린_항목은_건너뛰고_경고를_남긴다()
    {
        var p = Parse("""
        { "schema": 1,
          "defaults": { "nope": {}, "text": { "alignment": "magic", "replacements": [ { "find": "([" } ] } },
          "formats": [
            { "id": "a", "masks": ["*.a"] }, { "id": "a", "masks": ["*.b"] }, { "masks": ["*.c"] },
            { "id": "n", "masks": [] }, { "id": "v", "masks": ["*.v"], "view": "hologram", "grammar": "nope" },
            { "id": "conv", "masks": ["*.x"], "converter": "builtin:teleport" } ],
          "grammars": { "bad": { "comment": ["(unclosed"] } } }
        """);
        Assert.Equal(["a", "v", "conv"], p.Formats.Select(f => f.Id));
        Assert.Contains(p.Warnings, w => w.Contains("defaults.nope"));
        Assert.Contains(p.Warnings, w => w.Contains("id 가 겹쳐"));
        Assert.Contains(p.Warnings, w => w.Contains("id 없는"));
        Assert.Contains(p.Warnings, w => w.Contains("formats.n"));
        Assert.Contains(p.Warnings, w => w.Contains("hologram"));
        Assert.Contains(p.Warnings, w => w.Contains("문법 \"nope\""));
        Assert.Contains(p.Warnings, w => w.Contains("teleport"));
        Assert.Contains(p.Warnings, w => w.Contains("grammars.bad.comment"));
        Assert.Null(p.Formats.Single(f => f.Id == "conv").Converter);

        var e = p.Resolve("x.txt");
        Assert.Equal(AlignmentKind.Histogram, e.Text.Alignment);
        Assert.Contains(e.Warnings, w => w.Contains("magic"));
        Assert.Empty(e.Text.Replacements);
    }

    [Fact]
    public void schema가_다르면_파일_전체를_못_읽은_것이다()
    {
        Assert.Throws<FormatException>(() => Parse("""{ "schema": 2 }"""));
        Assert.Throws<FormatException>(() => Parse("""{ }"""));
        var fb = ComparePolicy.Fallback("깨짐");
        Assert.True(fb.IsFallback);
        Assert.Equal(["깨짐"], fb.Warnings);
        Assert.Equal("c-family", fb.Resolve("a.c").Format?.Id);
    }

    [Fact]
    public void 폴더_규칙_판정_단계_필터_짝짓기()
    {
        var f = ComparePolicy.Builtin.Resolve().Folder;
        Assert.Equal([CriterionKind.Size, CriterionKind.Timestamp, CriterionKind.Content], f.Criteria.Select(c => c.By));
        Assert.True(f.Criteria[1].IgnoreHourOffsets);
        Assert.False(f.Folders.Matches("node_modules"));
        Assert.True(f.Folders.Matches("src"));
        Assert.False(f.Files.Matches("old.bak"));
        Assert.True(f.Files.Matches("main.c"));

        var p = Parse("""
        { "schema": 1, "defaults": { "folder": {
            "criteria": [ { "by": "hash", "algorithm": "crc32" }, { "by": "warp" } ],
            "filters": { "include": ["*.c"], "minSize": 10 },
            "pairing": { "ignoreExtension": true, "renames": [ { "left": "a_(\\d+)\\.txt", "right": "b_$1.txt" } ] } } } }
        """);
        var g = p.Resolve(namesIgnoreCase: false).Folder;
        Assert.Equal([CriterionKind.Hash], g.Criteria.Select(c => c.By));
        Assert.Equal(HashAlgorithmKind.Crc32, g.Criteria[0].Algorithm);
        Assert.False(g.Files.Matches("readme.md"));
        Assert.Equal(10, g.MinSize);
        Assert.False(g.IgnoreCase);
        Assert.True(g.IgnoreExtension);
        Assert.Equal("b_7.txt", g.Renames[0].Left.Replace("a_7.txt", g.Renames[0].Right));
    }

    [Fact]
    public void 바이너리_이미지_표_규칙()
    {
        var p = Parse("""
        { "schema": 1, "defaults": {
            "binary": { "align": "insertions", "ignoreRanges": [ { "start": 16, "length": 4 }, { "start": -1, "length": 2 } ] },
            "image": { "tolerance": 300, "align": "center" },
            "table": { "delimiter": "||", "keyColumns": [0, 2] } } }
        """);
        var e = p.Resolve();
        Assert.Equal(BinaryAlignKind.Insertions, e.Binary.Align);
        Assert.Equal([new ByteRange(16, 4)], e.Binary.IgnoreRanges);
        Assert.Equal(255, e.Image.Tolerance);
        Assert.Equal(ImageAlignKind.Center, e.Image.Align);
        Assert.Equal(",", e.Table.Delimiter);
        Assert.Equal([0, 2], e.Table.KeyColumns);
        Assert.Contains(e.Warnings, w => w.Contains("binary.ignoreRanges"));
        Assert.Contains(e.Warnings, w => w.Contains("table.delimiter"));
    }

    [Fact]
    public void 문법이_토큰을_찾는다()
    {
        var c = ComparePolicy.Builtin.Grammars["c-like"];
        const string line = "int x = 1; // 주석 \"a\"";
        Assert.Contains(c.Tokens["comment"], r => r.Match(line).Value == "// 주석 \"a\"");
        Assert.Contains(c.Tokens["string"], r => r.Match("s = \"a\\\"b\";").Value == "\"a\\\"b\"");
        Assert.Contains(c.Tokens["comment"], r => r.Match("a /* x\ny */ b").Value == "/* x\ny */");
    }
}
