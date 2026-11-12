using DaylightCompare.Domain.Policy;

namespace DaylightCompare.Domain.Diff;

/// <summary>
/// 두 정수 열(줄·단어를 같은 값끼리 같은 번호로 바꾼 것)의 공통 부분 열 — 짝지어진 위치 목록. diff의 바닥 알고리즘.
/// <list type="bullet">
/// <item><b>Myers</b>: 최소 편집(가장 짧은 diff). 선형 메모리의 "가운데 뱀(middle snake)" 분할 정복.</item>
/// <item><b>Histogram</b>(기본, git의 histogram과 같은 계열): 왼쪽에서 드물게 나오는 줄을 닻으로 삼아 나누고, 닻이 없는 구간만 Myers.
/// <c>}</c>·빈 줄 같은 흔한 줄에 끌려가지 않아 사람이 읽기 좋은 diff가 나온다.</item>
/// <item><b>Patience</b>: 양쪽에 한 번씩만 나오는 줄만 닻으로 쓰는 Histogram.</item>
/// </list>
/// 재귀 대신 작업 스택을 써서 큰 파일에서도 스택이 넘치지 않는다. 결과는 왼쪽 위치 순으로 정렬된 (왼쪽, 오른쪽) 쌍이다.
/// </summary>
public static class SequenceDiff
{
    /// <summary>닻 후보가 이보다 자주 나오면 닻으로 쓰지 않는다 (git의 MAX_CHAIN_LENGTH와 같은 값).</summary>
    const int MaxOccurrences = 64;

    public static List<(int A, int B)> Match(int[] a, int[] b, AlignmentKind algorithm = AlignmentKind.Histogram)
    {
        var pairs = new List<(int A, int B)>();
        var work = new Stack<(int AS, int AE, int BS, int BE)>();
        work.Push((0, a.Length, 0, b.Length));
        while (work.Count > 0)
        {
            var (aS, aE, bS, bE) = work.Pop();
            // 앞뒤의 같은 부분은 바로 짝짓는다.
            while (aS < aE && bS < bE && a[aS] == b[bS]) pairs.Add((aS++, bS++));
            while (aS < aE && bS < bE && a[aE - 1] == b[bE - 1]) pairs.Add((--aE, --bE));
            if (aS == aE || bS == bE) continue;

            if (algorithm == AlignmentKind.Myers || FindAnchor(a, aS, aE, b, bS, bE, algorithm == AlignmentKind.Patience) is not { } anchor)
            {
                Myers(a, aS, aE, b, bS, bE, pairs);
                continue;
            }
            for (var i = 0; i < anchor.Length; i++) pairs.Add((anchor.A + i, anchor.B + i));
            work.Push((anchor.A + anchor.Length, aE, anchor.B + anchor.Length, bE));
            work.Push((aS, anchor.A, bS, anchor.B));
        }
        pairs.Sort((x, y) => x.A.CompareTo(y.A));
        return pairs;
    }

    /// <summary>
    /// 닻 찾기 (JGit HistogramDiff를 단순화): 오른쪽의 각 원소가 왼쪽에 나오는 위치마다 같은 원소가 이어지는 구간을 넓혀 보고,
    /// 구간 안 원소들의 왼쪽 출현 횟수 중 최솟값이 가장 작은 것(같으면 더 긴 것)을 고른다.
    /// </summary>
    static (int A, int B, int Length)? FindAnchor(int[] a, int aS, int aE, int[] b, int bS, int bE, bool uniqueOnly)
    {
        var positions = new Dictionary<int, List<int>>();
        for (var i = aS; i < aE; i++)
        {
            if (!positions.TryGetValue(a[i], out var list)) positions[a[i]] = list = [];
            list.Add(i);
        }
        Dictionary<int, int>? bCounts = null;
        if (uniqueOnly)
        {
            bCounts = [];
            for (var j = bS; j < bE; j++) bCounts[b[j]] = bCounts.GetValueOrDefault(b[j]) + 1;
        }

        (int A, int B, int Length)? best = null;
        var bestCount = int.MaxValue;
        var mid = (aS + aE) / 2;
        for (var j = bS; j < bE;)
        {
            if (!positions.TryGetValue(b[j], out var occ) || occ.Count > (uniqueOnly ? 1 : MaxOccurrences)
                || (uniqueOnly && bCounts![b[j]] != 1))
            {
                j++;
                continue;
            }
            var furthest = j + 1;
            foreach (var i0 in occ)
            {
                int i = i0, k = j;
                while (i > aS && k > bS && a[i - 1] == b[k - 1]) { i--; k--; }
                int ie = i0 + 1, ke = j + 1;
                while (ie < aE && ke < bE && a[ie] == b[ke]) { ie++; ke++; }
                var min = int.MaxValue;
                for (var t = i; t < ie; t++) min = Math.Min(min, positions[a[t]].Count);
                if (uniqueOnly && min != 1) continue;
                var length = ie - i;
                // 같은 점수면 가운데에 가까운 닻: 나눈 양쪽이 고르게 작아져 큰 파일에서도 O(n log n)에 가깝다.
                var centered = best is { } bb && Math.Abs(i + length / 2 - mid) < Math.Abs(bb.A + bb.Length / 2 - mid);
                if (min < bestCount || (min == bestCount && (length > best!.Value.Length || (length == best.Value.Length && centered))))
                {
                    best = (i, k, length);
                    bestCount = min;
                }
                furthest = Math.Max(furthest, ke);
            }
            j = furthest; // 이미 본 구간 안의 원소는 다시 보지 않는다
        }
        return best;
    }

    /// <summary>Myers 선형 메모리판: 가운데 뱀으로 나누고 양쪽을 다시 나눈다 (작업 스택).</summary>
    static void Myers(int[] a, int aS0, int aE0, int[] b, int bS0, int bE0, List<(int A, int B)> pairs)
    {
        var work = new Stack<(int AS, int AE, int BS, int BE)>();
        work.Push((aS0, aE0, bS0, bE0));
        int[]? vf = null, vb = null;
        while (work.Count > 0)
        {
            var (aS, aE, bS, bE) = work.Pop();
            while (aS < aE && bS < bE && a[aS] == b[bS]) pairs.Add((aS++, bS++));
            while (aS < aE && bS < bE && a[aE - 1] == b[bE - 1]) pairs.Add((--aE, --bE));
            int n = aE - aS, m = bE - bS;
            if (n == 0 || m == 0) continue;

            var size = 2 * (n + m) + 3;
            if (vf is null || vf.Length < size) { vf = new int[size]; vb = new int[size]; }
            var (x, y, u, v) = MiddleSnake(a, aS, n, b, bS, m, vf, vb!);
            for (int i = x, k = y; i < u; i++, k++) pairs.Add((aS + i, bS + k));
            // 뱀이 아무것도 줄이지 못하면(같은 원소가 없음) 더 나눌 것이 없다.
            if ((x == 0 && y == 0 && u == n && v == m) || (u == x && ((x == 0 && y == 0) || (x == n && y == m)))) continue;
            work.Push((aS + u, aE, bS + v, bE));
            work.Push((aS, aS + x, bS, bS + y));
        }
    }

    /// <summary>
    /// 편집 그래프의 가운데 뱀 (Myers 1986, 4장). 앞쪽·뒤쪽 탐색이 만나는 대각선 구간 (x,y)→(u,v)를 돌려준다 (구간 안 좌표).
    /// </summary>
    static (int X, int Y, int U, int V) MiddleSnake(int[] a, int aS, int n, int[] b, int bS, int m, int[] vf, int[] vb)
    {
        var max = n + m;
        var off = max + 1;
        var delta = n - m;
        var odd = (delta & 1) != 0;
        Array.Clear(vf, 0, 2 * max + 3);
        Array.Clear(vb, 0, 2 * max + 3);
        for (var d = 0; d <= (max + 1) / 2; d++)
        {
            for (var k = -d; k <= d; k += 2)
            {
                var x = k == -d || (k != d && vf[off + k - 1] < vf[off + k + 1]) ? vf[off + k + 1] : vf[off + k - 1] + 1;
                var y = x - k;
                int sx = x, sy = y;
                while (x < n && y < m && a[aS + x] == b[bS + y]) { x++; y++; }
                vf[off + k] = x;
                var kb = delta - k; // 뒤쪽 탐색에서 같은 대각선
                if (odd && kb >= -(d - 1) && kb <= d - 1 && vf[off + k] + vb[off + kb] >= n)
                    return (sx, sy, x, y);
            }
            for (var k = -d; k <= d; k += 2)
            {
                var x = k == -d || (k != d && vb[off + k - 1] < vb[off + k + 1]) ? vb[off + k + 1] : vb[off + k - 1] + 1;
                var y = x - k;
                int sx = x, sy = y;
                while (x < n && y < m && a[aS + n - 1 - x] == b[bS + m - 1 - y]) { x++; y++; }
                vb[off + k] = x;
                var kf = delta - k;
                if (!odd && kf >= -d && kf <= d && vb[off + k] + vf[off + kf] >= n)
                    return (n - x, m - y, n - sx, m - sy);
            }
        }
        return (0, 0, 0, 0); // 도달하지 않는다
    }
}
