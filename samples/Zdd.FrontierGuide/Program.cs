using System;
using System.Numerics;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;

namespace ZDD.Net.Samples.FrontierGuide
{
    /// <summary>
    /// docs/frontier-guide.md に載せているコード片をそのまま集めたサンプル。
    /// ここに書いたコードは <c>dotnet run --project samples/Zdd.FrontierGuide</c> で実際に動く
    /// （CI もこれを実行して確かめる。.github/workflows/ci.yml を参照）。
    /// ガイドの本文を直すときは、対応するメソッドも一緒に直すこと。
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            WhatIsTheFrontierMethod();
            BuiltInSpecs();
            GraphAndFrontierManager();
            EdgeOrderOptimization();
            BuildOptionsLimits();
            CustomSpecNoThreeConsecutive();

            Console.Out.WriteLine("all frontier-guide samples passed");
            return 0;
        }

        /// <summary>
        /// 「フロンティア法とは何か」節: 5x5 格子の対角 s-t 単純パスを、パスを 1 本も展開せずに数える。
        /// </summary>
        private static void WhatIsTheFrontierMethod()
        {
            Graph grid = Graph.Grid(5, 5);
            using ZddManager manager = new ZddManager(grid.EdgeCount);

            int s = 0;
            int t = grid.VertexCount - 1;
            Zdd paths = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(grid, s, t));

            // OEIS A007764（n×n 格子の対角単純パス数）: n=5 は 8512。
            Assert(paths.Count == 8512, "5x5 grid has 8512 diagonal s-t paths");
        }

        /// <summary>「組み込みスペック」節: PowerSet / Cardinality / LinearConstraint / Knapsack の使い方。</summary>
        private static void BuiltInSpecs()
        {
            const int itemCount = 5;
            using ZddManager manager = new ZddManager(itemCount);

            // PowerSetSpec: n 要素の冪集合そのもの（2^n 個）。
            Zdd powerSet = FrontierBuilder.Build<PowerSetSpec, byte>(manager, new PowerSetSpec(itemCount));
            Assert(powerSet.Count == BigInteger.Pow(2, itemCount), "PowerSetSpec: 2^n subsets");

            // CardinalitySpec: 要素数が [min, max] に収まる部分集合。
            Zdd sizeTwoOrThree = FrontierBuilder.Build<CardinalitySpec, int>(
                manager, new CardinalitySpec(itemCount, min: 2, max: 3));
            Assert(sizeTwoOrThree.Count == 10 + 10, "CardinalitySpec: C(5,2) + C(5,3) = 20");

            // LinearConstraintSpec: Σ a[i] x[i] {<=, ==, >=} b。
            int[] coefficients = { 3, 1, 4, 1, 5 };
            Zdd atMostSeven = FrontierBuilder.Build<LinearConstraintSpec, long>(
                manager, new LinearConstraintSpec(coefficients, LinearConstraintOperator.LessOrEqual, bound: 7));
            Assert(atMostSeven.Count > 0, "LinearConstraintSpec: at least the empty set satisfies <= 7");

            // KnapsackSpec: Σ weights[i] x[i] <= capacity（LinearConstraintSpec の特化版）。
            int[] weights = { 2, 3, 4, 5, 9 };
            Zdd fitsCapacity = FrontierBuilder.Build<KnapsackSpec, long>(
                manager, new KnapsackSpec(weights, capacity: 10));
            Assert(fitsCapacity.Count > 0, "KnapsackSpec: at least the empty set fits capacity 10");

            // グラフ問題（Path / SpanningTree / Forest / Matching）は GraphAndFrontierManager() を参照。
        }

        /// <summary>「Graph の作り方」「FrontierManager による事前見積り」節。</summary>
        private static void GraphAndFrontierManager()
        {
            // Graph.Grid / Complete / Cycle / Path はよく使う形の組み込みショートカット。
            // 辺の順序がフロンティア法の変数順序そのものになる（性能の勘所節を参照）。
            Graph grid = Graph.Grid(3, 3);

            // スペックを書く前、ZDD を構築する前に、辺順序だけからフロンティア幅の見積りができる。
            // 手軽な方（Graph に直接聞く）と、前計算した表ごと欲しい方（FrontierManager）がある。
            Assert(grid.EstimateMaxFrontierSize() > 0, "EstimateMaxFrontierSize is the width the build will need");

            FrontierManager frontierManager = new FrontierManager(grid);
            Assert(
                frontierManager.MaxFrontierSize == grid.EstimateMaxFrontierSize(),
                "FrontierManager.MaxFrontierSize and Graph.EstimateMaxFrontierSize are the same number");

            using ZddManager manager = new ZddManager(grid.EdgeCount);

            // SpanningTreeSpec: 全域木。Kirchhoff の行列木定理と照合済み（tests/.../SpanningTreeSpecTests.cs）。
            Zdd spanningTrees = FrontierBuilder.Build<SpanningTreeSpec>(manager, new SpanningTreeSpec(grid));
            Assert(spanningTrees.Count > 0, "SpanningTreeSpec: a 3x3 grid has spanning trees");

            // ForestSpec: 成分数を指定した森（components: 1 は SpanningTreeSpec と同じ族になる）。
            Zdd forest = FrontierBuilder.Build<ForestSpec>(manager, new ForestSpec(grid, components: 1));
            Assert(forest == spanningTrees, "ForestSpec(components: 1) matches SpanningTreeSpec");

            // MatchingSpec: マッチング（perfect: true で完全マッチングだけに絞れる）。
            Zdd matchings = FrontierBuilder.Build<MatchingSpec>(manager, new MatchingSpec(grid));
            Assert(matchings.Count > 0, "MatchingSpec: a 3x3 grid has matchings (at least the empty one)");
        }

        /// <summary>
        /// 「性能の勘所 - 辺順序でフロンティア幅が変わる」節: <c>Optimize</c> で辺順序を並べ替え、
        /// 辺 index の対応表を通して元のグラフの辺として読み直す。
        /// </summary>
        private static void EdgeOrderOptimization()
        {
            // ファイルから読んだ辺リストのように、辺が任意の順に並んだ 40x40 格子（3,120 辺）。
            Graph large = Shuffle(Graph.Grid(40, 40), seed: 7);

            Graph optimizedLarge = large.Optimize(EdgeOrderStrategy.Bfs);   // 既定は Bfs
            Assert(large.EstimateMaxFrontierSize() == 1408, "an arbitrary edge order is 1408 wide here");
            Assert(optimizedLarge.EstimateMaxFrontierSize() == 42, "Bfs brings it down to 42");

            // 並べ替え後のグラフを作らずに、戦略ごとの幅だけを比べることもできる。
            Assert(
                large.EstimateMaxFrontierSize(EdgeOrderStrategy.Grid) <= large.EstimateMaxFrontierSize(EdgeOrderStrategy.Bfs),
                "on a grid, the Grid strategy is no wider than Bfs");

            // 開始頂点も選べる（既定は次数最小の頂点）。
            Assert(
                large.Optimize(EdgeOrderStrategy.Bfs, EdgeOrderOptions.BestOfCandidates(20)).EstimateMaxFrontierSize() > 0,
                "BestOfCandidates tries several start vertices and keeps the narrowest order");

            // ここからが最も事故りやすい点: 並べ替えたグラフの辺 index は元のグラフのものと違う。
            Graph graph = Shuffle(Graph.Grid(3, 4), seed: 3);
            Graph optimized = graph.Optimize();
            EdgeOrderMapping mapping = optimized.SourceOrder!;

            using ZddManager manager = new ZddManager(optimized.EdgeCount);
            Zdd paths = FrontierBuilder.Build<PathSpec>(
                manager, new PathSpec(optimized, s: 0, t: optimized.VertexCount - 1));

            int pathCount = 0;
            foreach (int[] edgeSet in paths.Sets())
            {
                // 並べ替え後の辺 index → 元のグラフの辺 index（昇順に整列して返る）。
                int[] original = mapping.ToSourceEdgeSet(edgeSet);

                foreach (int edgeIndex in original)
                {
                    Edge edge = graph.GetEdge(edgeIndex);   // 元のグラフの辺として読める
                    Assert(edge.U != edge.V, "a translated index names a real edge of the source graph");
                }

                pathCount++;
            }

            // 辺順序を変えても構築される族は同じ。変わるのは構築にかかる手間だけ。
            using ZddManager sourceManager = new ZddManager(graph.EdgeCount);
            Zdd sourcePaths = FrontierBuilder.Build<PathSpec>(
                sourceManager, new PathSpec(graph, s: 0, t: graph.VertexCount - 1));

            Assert(sourcePaths.Count == paths.Count, "reordering the edges does not change the family");
            Assert(pathCount == (int)paths.Count, "every set came back through the mapping");
        }

        /// <summary>
        /// 辺を任意の順に並べ替える（ファイルから読んだ辺リストの代わり）。固定の線形合同法なので、
        /// 実行するたびに同じ順序になる。
        /// </summary>
        private static Graph Shuffle(Graph graph, int seed)
        {
            var order = new int[graph.EdgeCount];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            uint state = (uint)seed + 0x9E3779B9u;
            for (int i = order.Length - 1; i > 0; i--)
            {
                state = (state * 1664525u) + 1013904223u;
                int j = (int)(state % (uint)(i + 1));
                (order[i], order[j]) = (order[j], order[i]);
            }

            return graph.WithEdgeOrder(order);
        }

        /// <summary>「BuildOptions による上限設定」節: 上限超過で例外になること、進捗が届くことを確かめる。</summary>
        private static void BuildOptionsLimits()
        {
            Graph grid = Graph.Grid(6, 6);
            using ZddManager manager = new ZddManager(grid.EdgeCount);
            var spec = new PathSpec(grid, s: 0, t: grid.VertexCount - 1);

            // 見積りより小さい上限を指定すると BuildLimitExceededException で止まる
            // （メモリを使い切って落ちる代わりに、原因の分かる例外で止める）。
            var frontierManager = new FrontierManager(grid);
            var tightOptions = new BuildOptions { MaxFrontierSize = frontierManager.MaxFrontierSize - 1 };
            bool threw = false;
            try
            {
                FrontierBuilder.Build<PathSpec>(manager, spec, tightOptions);
            }
            catch (BuildLimitExceededException)
            {
                threw = true;
            }

            Assert(threw, "MaxFrontierSize below the real width throws BuildLimitExceededException");

            // IProgress<BuildProgress> には水準ごとに 1 回、フロンティア幅の履歴が届く
            // （bench/ZDD.Net.Benchmarks がピークフロンティア幅を記録するのに使っているのと同じ仕組み）。
            // System.Progress<T> は SynchronizationContext 経由で非同期に届く（コンソールアプリでは
            // スレッドプールにポストされる）ため、Build 呼び出し直後に数を検証すると届く前に読んでしまう
            // ことがある。ここでは同期的に呼ばれる InlineProgress を使い、確定的に検証する。
            int levelsReported = 0;
            var progress = new InlineProgress<BuildProgress>(_ => levelsReported++);
            var progressOptions = new BuildOptions { Progress = progress };
            FrontierBuilder.Build<PathSpec>(manager, spec, progressOptions);

            Assert(levelsReported == grid.EdgeCount, "one BuildProgress report per level");
        }

        /// <summary>
        /// 「独自スペックを書く」節の実例: n 個のアイテムから、連続する 3 要素を同時に選べない部分集合。
        /// 状態は「直近 2 個で何個選んだか」だけでよい（それ以前の選択は以降の判定に影響しない）。
        /// </summary>
        private static void CustomSpecNoThreeConsecutive()
        {
            const int itemCount = 8;
            using ZddManager manager = new ZddManager(itemCount);

            Zdd family = FrontierBuilder.Build<NoThreeConsecutiveSpec, int>(
                manager, new NoThreeConsecutiveSpec(itemCount));

            // ブルートフォースで独立に数え、フロンティア法の結果と一致することを確かめる
            // （完了条件: チュートリアルどおりに書けば独自スペックが作れることの確認）。
            BigInteger expected = CountByBruteForce(itemCount);
            Assert(family.Count == expected, $"NoThreeConsecutiveSpec matches brute force ({expected})");
        }

        private static BigInteger CountByBruteForce(int itemCount)
        {
            BigInteger count = 0;
            for (int mask = 0; mask < (1 << itemCount); mask++)
            {
                int run = 0;
                bool ok = true;
                for (int i = 0; i < itemCount && ok; i++)
                {
                    if ((mask & (1 << i)) != 0)
                    {
                        run++;
                        if (run >= 3)
                        {
                            ok = false;
                        }
                    }
                    else
                    {
                        run = 0;
                    }
                }

                if (ok)
                {
                    count++;
                }
            }

            return count;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"assertion failed: {message}");
            }
        }
    }

    /// <summary>
    /// <see cref="System.Progress{T}"/> と異なり、<see cref="Report"/> の呼び出しスレッド上で
    /// 同期的にハンドラを呼ぶ <see cref="IProgress{T}"/>。<c>System.Progress&lt;T&gt;</c> は
    /// <c>SynchronizationContext</c> 経由で非同期に届く（コンソールアプリではスレッドプールにポスト
    /// される）ため、呼び出し直後に結果を検証するテスト・サンプルでは確定的でない。
    /// </summary>
    internal sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public InlineProgress(Action<T> handler) => _handler = handler;

        public void Report(T value) => _handler(value);
    }

    /// <summary>
    /// 「連続する 3 要素を同時に選べない」制約のスペック。docs/frontier-guide.md の
    /// 「独自スペックを書く」節にそのまま載せている実装。
    /// </summary>
    /// <remarks>
    /// 状態は「直近に連続して選んだ個数（0, 1, または 2）」だけでよい。3 個目を選んだ時点で
    /// アイテムの並び上のどこであろうと不正なので、以降の判定に「これまで何を選んだか」は要らない
    /// —— これが状態を正準に小さく保つということ（frontier-spec-guide.md §4「状態は『以降の遷移に
    /// 影響する情報だけ』を持つ」）。
    /// </remarks>
    public readonly struct NoThreeConsecutiveSpec : IDdSpec<int>
    {
        private readonly int _itemCount;

        public NoThreeConsecutiveSpec(int itemCount)
        {
            _itemCount = itemCount;
        }

        public int GetRoot(ref int run)
        {
            run = 0;
            return _itemCount;
        }

        public int GetChild(ref int run, int level, int value)
        {
            if (value == 0)
            {
                run = 0;
            }
            else
            {
                run++;
                if (run >= 3)
                {
                    return DdResult.False; // 枝刈り: 3 連続に達したら以降は全部不正
                }
            }

            int remaining = level - 1;
            if (remaining == 0)
            {
                return DdResult.True;
            }

            return remaining;
        }

        public bool StateEquals(in int left, in int right) => left == right;

        public int StateHashCode(in int state) => state;
    }
}
