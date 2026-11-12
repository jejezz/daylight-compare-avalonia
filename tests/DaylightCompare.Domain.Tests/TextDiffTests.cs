using System.Text.Json.Nodes;
using DaylightCompare.Domain.Diff;
using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Domain.Tests;

public class TextDiffTests
{
    static TextDiffResult Diff(string left, string right, string fileName = "a.txt", string? session = null) =>
        TextDiff.Compare(left, right, ComparePolicy.Builtin.Resolve(fileName, session: session is null ? null : (JsonObject)JsonNode.Parse(session)!));

    static string Kinds(TextDiffResult r) => string.Join(" ", r.Blocks.Select(b => $"{b.Kind}:{b.LeftStart}+{b.LeftCount}/{b.RightStart}+{b.RightCount}"));

    [Fact]
    public void 같은_글은_Equal_하나다()
    {
        var r = Diff("a\nb\nc\n", "a\nb\nc\n");
        Assert.True(r.IsIdentical);
        Assert.Equal("Equal:0+3/0+3", Kinds(r));
        Assert.True(Diff("", "").IsIdentical);
    }

    [Fact]
    public void 넣기_빼기_바꾸기()
    {
        Assert.Equal("Equal:0+1/0+1 RightOnly:1+0/1+1 Equal:1+1/2+1", Kinds(Diff("a\nc\n", "a\nb\nc\n")));
        Assert.Equal("Equal:0+1/0+1 LeftOnly:1+1/1+0 Equal:2+1/1+1", Kinds(Diff("a\nb\nc\n", "a\nc\n")));
        var changed = Diff("a\nb\nc\n", "a\nB2\nc\n");
        Assert.Equal("Equal:0+1/0+1 Changed:1+1/1+1 Equal:2+1/2+1", Kinds(changed));
        Assert.Equal(1, changed.DifferenceCount);
        Assert.False(changed.IsEquivalent);
    }

    [Fact]
    public void 마지막_줄바꿈이_없어도_같은_줄_수다()
    {
        var r = Diff("a\nb", "a\nb\n");
        Assert.Equal(2, r.Left.LineCount);
        Assert.Equal(2, r.Right.LineCount);
        Assert.True(r.IsIdentical);
    }

    [Fact]
    public void 뒤_공백은_기본으로_중요하지_않다()
    {
        var r = Diff("a\nb  \n", "a\nb\n");
        Assert.Equal("Equal:0+1/0+1 Unimportant:1+1/1+1", Kinds(r));
        Assert.True(r.IsEquivalent);
        Assert.False(r.IsIdentical);
    }

    [Fact]
    public void C_계열에서_주석과_공백만_다르면_중요하지_않다()
    {
        const string left = "int x = 1; // old note\nint  y = 2;\n";
        const string right = "int x = 1; /* new note */\nint y=2;\n";
        var r = Diff(left, right, "main.c");
        Assert.True(r.IsEquivalent, Kinds(r));
        Assert.Equal(DiffBlockKind.Unimportant, r.Blocks.Single().Kind);
    }

    [Fact]
    public void 문자열_안의_주석_기호는_주석이_아니다()
    {
        var r = Diff("s = \"http://a\";\n", "s = \"http://b\";\n", "main.c");
        Assert.Equal(DiffBlockKind.Changed, r.Blocks.Single().Kind);
    }

    [Fact]
    public void 여러_줄_주석과_주석만_있는_줄이_늘어난_것은_중요하지_않다()
    {
        const string left = "a();\nb();\n";
        const string right = "a();\n/* 설명\n   여러 줄 */\nb();\n";
        var r = Diff(left, right, "main.c");
        Assert.True(r.IsEquivalent, Kinds(r));
        Assert.Contains(r.Blocks, b => b is { Kind: DiffBlockKind.Unimportant, RightCount: 2, LeftCount: 0 });
    }

    [Fact]
    public void 텍스트_파일에서는_주석도_중요하다()
    {
        Assert.False(Diff("x // a\n", "x // b\n", "notes.txt").IsEquivalent);
    }

    [Fact]
    public void 세션_정책으로_대소문자와_치환을_무시한다()
    {
        Assert.False(Diff("Hello\n", "hello\n").IsEquivalent);
        Assert.True(Diff("Hello\n", "hello\n", session: """{ "text": { "normalize": { "case": true } } }""").IsEquivalent);
        const string replace = """{ "text": { "replacements": [ { "find": "v\\d+\\.\\d+", "with": "vX" } ] } }""";
        Assert.True(Diff("version v1.2\n", "version v1.3\n", session: replace).IsEquivalent);
        Assert.False(Diff("version v1.2\n", "release v1.3\n", session: replace).IsEquivalent);
    }

    [Fact]
    public void 줄_안에서_바뀐_단어와_글자만_강조한다()
    {
        var r = Diff("int count = 10;\n", "int counts = 20;\n");
        var b = r.Blocks.Single();
        var intra = r.Intraline(b.LeftStart, b.RightStart);
        // count → counts: 글자 s만, 10 → 20: 1 → 2만
        Assert.Equal([(9, 1), (13, 1)], intra.Right.Select(s => (s.Start, s.Length)));
        Assert.Equal([(12, 1)], intra.Left.Select(s => (s.Start, s.Length)));
        Assert.All(intra.Right, s => Assert.False(s.Unimportant));
    }

    [Fact]
    public void 줄_안의_주석_차이는_중요하지_않은_강조다()
    {
        var r = Diff("call(); // a\nx = 1;\n", "call(); // b\nx = 2;\n", "main.c");
        var block = r.Blocks.First(b => b.Kind == DiffBlockKind.Changed);
        Assert.Equal(1, block.LeftStart); // 첫 줄은 주석만 달라 Unimportant 구간이다
        var first = r.Intraline(0, 0);
        Assert.All(first.Left, s => Assert.True(s.Unimportant));
    }

    [Fact]
    public void 한글_줄_안_차이()
    {
        var intra = IntralineDiff.Compute("안녕하세요 세계", "안녕하세요 우주");
        Assert.Equal([(6, 2)], intra.Left.Select(s => (s.Start, s.Length)));
        Assert.Equal([(6, 2)], intra.Right.Select(s => (s.Start, s.Length)));
    }

    [Fact]
    public void 큰_파일도_빠르다()
    {
        var lines = Enumerable.Range(0, 50_000).Select(i => $"line {i} // comment {i % 7}").ToList();
        var left = string.Join('\n', lines);
        lines[100] = "changed";
        lines.RemoveAt(30_000);
        lines.Insert(40_000, "inserted");
        var right = string.Join('\n', lines);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = Diff(left, right, "big.c");
        Assert.True(sw.ElapsedMilliseconds < 3000, $"{sw.ElapsedMilliseconds} ms");
        Assert.Equal(3, r.DifferenceCount);
    }
}
