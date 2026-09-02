using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;

namespace ZDD.Net.Tests.Frontier
{
    /// <summary>
    /// 状態の bit-packing（M3-2）が「内部表現だけを変える」ことの確認。
    /// </summary>
    /// <remarks>
    /// 詰め方そのものは <see cref="PackedStateLayoutTests"/>、表の詰め直しは
    /// <see cref="ArrayLevelStateTableTests"/> で見る。ここでは構築結果——ノード ID まで含めて
    /// bit-packing 前と一致すること、スロット幅が切り替わる値域でも族が正しいこと——を見る。
    /// </remarks>
    public class StateBitPackingTests
    {
        /// <summary>
        /// bit-packing 導入前（aecabb6）の構築結果と、DOT 出力の SHA-256 が一致する。
        /// </summary>
        /// <remarks>
        /// DOT はノードを <c>n</c> + ノード ID で書くので、ダイジェストの一致は
        /// 「構築される ZDD のノード ID まで含めて完全一致」を意味する（issue #34 の完了条件）。
        /// </remarks>
        [Theory]
        [InlineData("Path_Grid5x5", 546, "16EACFE1419720443113A42B7922699C")]
        [InlineData("Path_Grid6x6", 2142, "96B86F617193A62DF6637FD968798BAC")]
        [InlineData("Path_Grid4x4_AnyEndpoints", 594, "2420283758929D9A397B4A55606FEFB9")]
        [InlineData("SpanningTree_Complete6", 172, "6008B86FBE2F0112A04400549B296A11")]
        [InlineData("SpanningTree_Grid4x4", 224, "5BDF6F71F444B18CA5CE71BA0ACC6A97")]
        [InlineData("Forest_Grid5x5_TwoComponents", 2052, "BD817A96BB0AC038FFA8C7B8C100D78D")]
        [InlineData("PerfectMatching_Grid6x6", 386, "0EF9C4F7F1342B0A5D3234E7785862DE")]
        public void NodeIdsMatchThePreBitPackingBaseline(string name, long expectedNodeCount, string expectedDigest)
        {
            (Graph graph, Func<Graph, ZddManager, Zdd> build) = BaselineCase(name);

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd zdd = build(graph, manager);

            Assert.Equal(expectedNodeCount, manager.NodeCount);
            Assert.Equal(expectedDigest, Digest(zdd.ToDot()));
        }

        /// <summary>
        /// スロット幅の切り替え境界（255/256、65535/65536）と、4 バイト幅が要る値域。
        /// </summary>
        /// <remarks>
        /// <see cref="SubsetSumSpec"/> の状態は「これまでの和」1 スロットだけなので、
        /// 重みの合計がそのままスロットの値域——つまり必要なスロット幅——になる。
        /// どの幅でも族が総当たりと一致することを見る（幅と値域の対応そのものは
        /// <see cref="PackedStateLayoutTests"/>）。
        /// </remarks>
        [Theory]
        [InlineData(255)]
        [InlineData(256)]
        [InlineData(65535)]
        [InlineData(65536)]
        [InlineData(100_000_000)]
        public void EverySlotWidthBuildsTheSameFamilyAsBruteForce(int total)
        {
            const int ItemCount = 10;

            int[] weights = Weights(ItemCount, total);
            int target = weights.Take(ItemCount / 2).Sum();

            AssertMatchesBruteForce(weights, target, offset: 0);
        }

        /// <summary>値域が負に食い込む状態でも同じ。</summary>
        /// <remarks>
        /// <paramref name="offset"/> だけ値域が下にずれるので、既定の窓（<c>-8..247</c>）に
        /// 収まる場合と、はみ出して構築の途中で窓を広げる場合の両方を通る。どちらでも族が
        /// 総当たりと一致することを見る。なお表に載るのは最後の 1 項目を決める前までの状態なので、
        /// 登録される最大値は <c>offset + total</c> ではなく、そこから最後の重みを引いた値。
        /// </remarks>
        [Theory]
        [InlineData(-8, 200)]      // 既定の窓の内側: 広げない
        [InlineData(-2, 253)]      // 同上（登録される最大値は 215）
        [InlineData(-1000, 60000)] // 下端をはみ出す: 構築の途中で 2 バイトへ広がる
        public void ABiasedWindowBuildsTheSameFamilyAsBruteForce(int offset, int total)
        {
            const int ItemCount = 8;

            int[] weights = Weights(ItemCount, total);
            int target = weights.Take(ItemCount / 2).Sum();

            AssertMatchesBruteForce(weights, target, offset);
        }

        /// <summary>
        /// 同じ族を、幅が切り替わる値域とそうでない値域で構築しても、ノード ID まで一致する。
        /// </summary>
        /// <remarks>
        /// 重みを一律に 1000 倍しても族の形は変わらない（和の比較が相似なだけ）。
        /// 変わるのはスロット幅だけなので、詰め方が結果に漏れていないことがこれで分かる。
        /// </remarks>
        [Fact]
        public void ScalingTheStateValuesChangesTheWidthButNotTheDiagram()
        {
            int[] small = { 3, 1, 4, 1, 5, 9, 2, 6 };
            int[] large = small.Select(w => w * 1000).ToArray();

            using ZddManager narrow = new ZddManager(small.Length);
            using ZddManager wide = new ZddManager(large.Length);

            Zdd narrowZdd = FrontierBuilder.Build<SubsetSumSpec>(narrow, new SubsetSumSpec(small, 15, 0));
            Zdd wideZdd = FrontierBuilder.Build<SubsetSumSpec>(wide, new SubsetSumSpec(large, 15000, 0));

            Assert.Equal(narrow.NodeCount, wide.NodeCount);
            Assert.Equal(narrowZdd.ToDot(), wideZdd.ToDot());
        }

        /// <summary>合計がちょうど <paramref name="total"/> になる重みを作る。</summary>
        private static int[] Weights(int itemCount, int total)
        {
            int[] weights = new int[itemCount];
            int share = total / itemCount;

            for (int i = 0; i < itemCount; i++)
            {
                weights[i] = share;
            }

            weights[itemCount - 1] += total - (share * itemCount);
            return weights;
        }

        private static void AssertMatchesBruteForce(int[] weights, int target, int offset)
        {
            using ZddManager manager = new ZddManager(weights.Length);
            Zdd zdd = FrontierBuilder.Build<SubsetSumSpec>(manager, new SubsetSumSpec(weights, target, offset));

            HashSet<string> expected = new HashSet<string>(BruteForceSubsetSums(weights, target));
            HashSet<string> actual = new HashSet<string>(zdd.Sets().Select(set => string.Join(",", set.OrderBy(i => i))));

            Assert.Equal(expected.Count, (int)zdd.Count);
            Assert.Equal(expected.OrderBy(s => s), actual.OrderBy(s => s));
        }

        /// <summary>和がちょうど <paramref name="target"/> になる部分集合を総当たりで求める。</summary>
        private static IEnumerable<string> BruteForceSubsetSums(int[] weights, int target)
        {
            for (int mask = 0; mask < 1 << weights.Length; mask++)
            {
                long sum = 0;
                List<int> items = new List<int>();

                for (int i = 0; i < weights.Length; i++)
                {
                    if ((mask & (1 << i)) != 0)
                    {
                        sum += weights[i];
                        items.Add(i);
                    }
                }

                if (sum == target)
                {
                    yield return string.Join(",", items);
                }
            }
        }

        private static string Digest(string dot) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dot)))[..32];

        private static (Graph Graph, Func<Graph, ZddManager, Zdd> Build) BaselineCase(string name) => name switch
        {
            "Path_Grid5x5" => (Graph.Grid(5, 5), (g, m) => FrontierBuilder.Build<PathSpec>(m, new PathSpec(g, 0, g.VertexCount - 1))),
            "Path_Grid6x6" => (Graph.Grid(6, 6), (g, m) => FrontierBuilder.Build<PathSpec>(m, new PathSpec(g, 0, g.VertexCount - 1))),
            "Path_Grid4x4_AnyEndpoints" => (Graph.Grid(4, 4), (g, m) => FrontierBuilder.Build<PathSpec>(m, new PathSpec(g, 0, 0, allowAnyEndpoints: true))),
            "SpanningTree_Complete6" => (Graph.Complete(6), (g, m) => FrontierBuilder.Build<SpanningTreeSpec>(m, new SpanningTreeSpec(g))),
            "SpanningTree_Grid4x4" => (Graph.Grid(4, 4), (g, m) => FrontierBuilder.Build<SpanningTreeSpec>(m, new SpanningTreeSpec(g))),
            "Forest_Grid5x5_TwoComponents" => (Graph.Grid(5, 5), (g, m) => FrontierBuilder.Build<ForestSpec>(m, new ForestSpec(g, 2))),
            "PerfectMatching_Grid6x6" => (Graph.Grid(6, 6), (g, m) => FrontierBuilder.Build<MatchingSpec>(m, new MatchingSpec(g, perfect: true))),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown baseline case."),
        };
    }

    /// <summary>
    /// 重みの和がちょうど <c>target</c> になる部分集合の族。状態は「これまでの和」1 スロットだけ。
    /// </summary>
    /// <remarks>
    /// 枝刈りをしないので、状態の値は 0（<c>offset</c>）から重みの総和までを一通り取る。
    /// スロットの値域を試験側から直接決められるので、幅の切り替え境界を作るのに使う。
    /// </remarks>
    internal readonly struct SubsetSumSpec : IArrayDdSpec
    {
        private readonly int[] _weights;
        private readonly int _target;
        private readonly int _offset;

        /// <summary>重み <paramref name="weights"/>、目標 <paramref name="target"/> のスペック。</summary>
        /// <param name="weights">項目 <c>i</c> の重み。</param>
        /// <param name="target">受理する和。</param>
        /// <param name="offset">全スロットに足す下駄。負にするとバイアスが 0 でなくなる。</param>
        public SubsetSumSpec(int[] weights, int target, int offset)
        {
            _weights = weights;
            _target = target;
            _offset = offset;
        }

        public int ArrayLength => 1;

        public int GetRoot(Span<int> state)
        {
            state[0] = _offset;
            return _weights.Length;
        }

        public int GetChild(Span<int> state, int level, int value)
        {
            if (value == 1)
            {
                state[0] += _weights[_weights.Length - level];
            }

            if (level > 1)
            {
                return level - 1;
            }

            return state[0] - _offset == _target ? DdResult.True : DdResult.False;
        }
    }
}
