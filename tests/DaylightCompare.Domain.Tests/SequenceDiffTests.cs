using DaylightCompare.Domain.Diff;
using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Domain.Tests;

public class SequenceDiffTests
{
    /// <summary>LCS 길이 (동적 계획법) — Myers가 최소 편집(= 최장 공통 부분 열)을 찾는지 확인하는 기준.</summary>
    static int Lcs(int[] a, int[] b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                dp[i, j] = a[i - 1] == b[j - 1] ? dp[i - 1, j - 1] + 1 : Math.Max(dp[i - 1, j], dp[i, j - 1]);
        return dp[a.Length, b.Length];
    }

    /// <summary>짝들이 올바른 공통 부분 열인가: 값이 같고, 양쪽 모두 순서대로 증가한다.</summary>
    static void AssertValid(int[] a, int[] b, List<(int A, int B)> pairs)
    {
        for (var i = 0; i < pairs.Count; i++)
        {
            Assert.Equal(a[pairs[i].A], b[pairs[i].B]);
            if (i > 0)
            {
                Assert.True(pairs[i].A > pairs[i - 1].A);
                Assert.True(pairs[i].B > pairs[i - 1].B);
            }
        }
    }

    static int[] Random(Random rng, int length, int alphabet) => Enumerable.Range(0, length).Select(_ => rng.Next(alphabet)).ToArray();

    [Fact]
    public void Myers는_무작위_입력에서_최장_공통_부분_열을_찾는다()
    {
        var rng = new Random(1234);
        for (var round = 0; round < 500; round++)
        {
            var a = Random(rng, rng.Next(0, 40), rng.Next(1, 6));
            var b = Random(rng, rng.Next(0, 40), rng.Next(1, 6));
            var pairs = SequenceDiff.Match(a, b, AlignmentKind.Myers);
            AssertValid(a, b, pairs);
            Assert.Equal(Lcs(a, b), pairs.Count);
        }
    }

    [Theory]
    [InlineData(AlignmentKind.Histogram)]
    [InlineData(AlignmentKind.Patience)]
    public void Histogram과_Patience는_올바른_공통_부분_열을_낸다(AlignmentKind algorithm)
    {
        var rng = new Random(99);
        for (var round = 0; round < 500; round++)
        {
            var a = Random(rng, rng.Next(0, 60), rng.Next(1, 20));
            // b는 a를 조금 고친 것 (실제 diff처럼)
            var b = a.ToList();
            for (var e = rng.Next(0, 6); e > 0 && b.Count > 0; e--)
            {
                var at = rng.Next(b.Count);
                switch (rng.Next(3))
                {
                    case 0: b.RemoveAt(at); break;
                    case 1: b.Insert(at, rng.Next(100)); break;
                    default: b[at] = rng.Next(100); break;
                }
            }
            var pairs = SequenceDiff.Match(a, [.. b], algorithm);
            AssertValid(a, [.. b], pairs);
            Assert.True(pairs.Count >= Lcs(a, [.. b]) - 6, "닻 기반이라 최소는 아니어도 크게 벗어나지 않는다");
        }
    }

    [Fact]
    public void 빈_입력과_같은_입력()
    {
        Assert.Empty(SequenceDiff.Match([], []));
        Assert.Empty(SequenceDiff.Match([1, 2], []));
        Assert.Equal([(0, 0), (1, 1)], SequenceDiff.Match([1, 2], [1, 2]));
    }

    [Fact]
    public void Histogram은_흔한_줄보다_드문_줄을_닻으로_삼는다()
    {
        // 함수 두 개의 순서를 바꾼 경우: 닫는 괄호(9)는 흔하고 함수 이름(1, 2)은 드물다.
        int[] a = [1, 10, 9, 2, 20, 9];
        int[] b = [2, 20, 9, 1, 10, 9];
        var pairs = SequenceDiff.Match(a, b, AlignmentKind.Histogram);
        Assert.Equal(3, pairs.Count); // 함수 하나 통째로 (이름·본문·괄호) — 어느 함수를 남길지는 둘 다 맞다
        Assert.True(pairs.Contains((3, 0)) || pairs.Contains((0, 3)), string.Join(",", pairs));
        Assert.DoesNotContain(pairs, p => p.A == 2 && p.B == 2); // 흔한 괄호끼리만 짝짓지 않는다
    }

    [Fact]
    public void 큰_입력도_스택이_넘치지_않고_빠르다()
    {
        var a = Enumerable.Range(0, 200_000).ToArray();
        var b = a.Where(x => x % 1000 != 0).Concat([-1, -2]).ToArray();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var pairs = SequenceDiff.Match(a, b);
        Assert.Equal(a.Length - 200, pairs.Count);
        Assert.True(sw.ElapsedMilliseconds < 3000, $"{sw.ElapsedMilliseconds} ms");

        var c = Enumerable.Repeat(7, 5000).ToArray();     // 전부 같은 줄 (닻 없음 → Myers)
        var d = Enumerable.Repeat(7, 4990).ToArray();
        Assert.Equal(4990, SequenceDiff.Match(c, d).Count);
    }
}
