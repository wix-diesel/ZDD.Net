using System;
using System.IO;
using System.Linq;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Io;
using ZDD.Net.Specs;

namespace ZDD.Net.Samples.Tutorial
{
    /// <summary>
    /// docs/tutorial.md に載せているコード片をそのまま集めたサンプル。
    /// <c>dotnet run --project samples/Zdd.Tutorial</c> で実際に動く（CI もこれを実行して確かめる。
    /// .github/workflows/ci.yml を参照）。チュートリアル本文を直すときは、対応するメソッドも一緒に直すこと。
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            CountingGridPaths();
            FilteringAndSampling();
            LoadingARealGraph();
            FrontierWidthGuidance();

            Console.Out.WriteLine("all tutorial samples passed");
            return 0;
        }

        /// <summary>「格子グラフの s–t パスを数える」節: GraphSet.Paths でパスを 1 本も展開せずに数える。</summary>
        private static void CountingGridPaths()
        {
            Graph grid = Graph.Grid(5, 5);

            // GraphSet は Graphillion 風の高レベル API。GraphSet.Paths(graph, from, to) だけで
            // s-t 単純パスの族が手に入る（内部では FrontierBuilder.Build<PathSpec> が動いている）。
            GraphSet paths = GraphSet.Paths(grid, from: 0, to: grid.VertexCount - 1);

            // OEIS A007764（n×n 格子の対角単純パス数）: n=5 は 8512。
            Assert(paths.Count == 8512, "GraphSet.Paths: 5x5 grid has 8512 diagonal s-t paths");

            // 低レベル API（FrontierBuilder.Build + PathSpec）でも同じ族が作れる。GraphSet はこの上に
            // 立つ薄いラッパーで、両者は完全に同じ結果になる。
            using ZddManager manager = new ZddManager(grid.EdgeCount);
            Zdd lowLevel = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(grid, s: 0, t: grid.VertexCount - 1));
            Assert(lowLevel.Count == paths.Count, "GraphSet.Paths matches the low-level FrontierBuilder call");
        }

        /// <summary>「フィルタとサンプリング」節: Including/Excluding/Larger/Smaller と Sample/MinIter。</summary>
        private static void FilteringAndSampling()
        {
            Graph grid = Graph.Grid(5, 5);
            GraphSet paths = GraphSet.Paths(grid, from: 0, to: grid.VertexCount - 1);

            // フィルタは構築時に適用される（事後の Intersect ではない）ので、中間結果が
            // 絞り込む前の族より大きくなることはない（docs/frontier-guide.md §8 と同じ考え方）。
            Edge firstStep = grid.GetEdge(0);
            GraphSet through = paths.Including(firstStep);
            GraphSet avoiding = paths.Excluding(firstStep);
            Assert(through.Count + avoiding.Count == paths.Count, "Including + Excluding partitions the family");

            GraphSet shortPaths = paths.Smaller(10);
            GraphSet longPaths = paths.Larger(9);
            Assert(shortPaths.Count + longPaths.Count == paths.Count, "Smaller(10) + Larger(9) partitions the family");

            // MinIter/MaxIter は遅延列挙: 全体を作らず、先頭 k 件だけを見るなら手間も k に比例する。
            int shortestLength = paths.MinIter(_ => 1).First().Count;
            Assert(shortestLength == 8, "the shortest diagonal path in a 5x5 grid has 8 edges");

            // Sample は族に属するどの集合も等しい確率で選ぶ一様ランダム抽出。
            var random = new Random(Seed: 42);
            var sample = paths.Sample(random);
            Assert(paths.Contains(sample), "a sampled set is itself a member of the family");
        }

        /// <summary>
        /// 「実グラフを読み込んで解く」節: DIMACS 形式のテキストを読み込み、辺順序を最適化してから
        /// パスを数える、という一連の流れをエンドツーエンドで動かす。
        /// </summary>
        /// <remarks>
        /// この DIMACS テキストは 3×3 格子と同じグラフだが、辺は行ごとの綺麗な順序ではなく
        /// 「ファイルに書かれていた順」を模した任意の順に並んでいる——ファイルから読んだ実データは
        /// フロンティア法にとって都合の良い順序になっているとは限らない、という現実的な状況。
        /// 数千辺規模の実測（本当に完走するか・何を要するか）は docs/benchmarks.md の M3-11 節を参照。
        /// </remarks>
        private static void LoadingARealGraph()
        {
            const string dimacsText = """
                c 3x3 格子と同じグラフ。辺は「ファイルに書かれていた順」を模して並んでいる
                p edge 9 12
                e 5 6
                e 1 2
                e 4 7
                e 2 3
                e 5 8
                e 1 4
                e 6 9
                e 2 5
                e 7 8
                e 3 6
                e 8 9
                e 4 5
                """;

            // DimacsGraph は TextReader を受ける（実ファイルなら File.OpenText(path) を渡せばよい）。
            // 文字列から直接読みたいだけなら string を受ける簡易オーバーロードもある。
            Graph graph = DimacsGraph.Read(dimacsText);
            Assert(graph.VertexCount == 9 && graph.EdgeCount == 12, "DimacsGraph.Read reproduces the 3x3 grid's shape");

            // ファイルの順のままだと幅が広い。まず見積もってから、必要なら最適化する
            // （次節「辺順序の最適化」の主題そのもの）。
            int asGivenWidth = graph.EstimateMaxFrontierSize();
            Graph optimized = graph.Optimize(EdgeOrderStrategy.Bfs);
            int optimizedWidth = optimized.EstimateMaxFrontierSize();
            Assert(optimizedWidth < asGivenWidth, "Optimize narrows the frontier the file's edge order left wide");

            // 並べ替え後のグラフで構築すれば、GraphSet はそのまま使える——Optimize が返すグラフの
            // 頂点番号は元のグラフと同じままなので(s, t) はそのまま渡せる(変わるのは辺の番号だけ)。
            GraphSet paths = GraphSet.Paths(optimized, from: 0, to: optimized.VertexCount - 1);
            Assert(paths.Count > 0, "the DIMACS-loaded grid has s-t paths");

            // 並べ替え前のグラフで直接数えても同じ族になる(構築の手間が変わるだけ)。
            GraphSet pathsAsGiven = GraphSet.Paths(graph, from: 0, to: graph.VertexCount - 1);
            Assert(pathsAsGiven.Count == paths.Count, "reordering the edges does not change the family, only the cost of building it");

            // DimacsGraph.Write は逆方向(Graph -> DIMACS テキスト)。ラウンドトリップも確認しておく。
            using var writer = new StringWriter();
            DimacsGraph.Write(graph, writer);
            Graph roundTripped = DimacsGraph.Read(writer.ToString());
            Assert(roundTripped.EdgeCount == graph.EdgeCount, "DimacsGraph round-trips a graph's edge count");
        }

        /// <summary>
        /// 「辺順序の最適化と見積り」節: EstimateMaxFrontierSize で構築前に幅を見積り、
        /// 大きすぎるときに BuildOptions の上限で安全に検知する、という実践的な指針。
        /// </summary>
        private static void FrontierWidthGuidance()
        {
            Graph grid = Graph.Grid(6, 6);

            // 構築を始める前に、辺順序だけから幅を見積れる。O(VertexCount + EdgeCount) なので、
            // 数千辺のグラフでも「無謀な計算を始める前に」呼べる。
            int width = grid.EstimateMaxFrontierSize();
            Assert(width > 0, "EstimateMaxFrontierSize runs before any ZDD is built");

            // 見積りが大きすぎるとき、いきなり構築してメモリを使い切って落ちるのではなく、
            // BuildOptions.MaxNodeCount / MaxFrontierSize で上限を切っておけば
            // BuildLimitExceededException として原因の分かる形で止められる。
            var tooTight = new BuildOptions { MaxFrontierSize = width - 1 };
            bool threw = false;
            try
            {
                using ZddManager manager = new ZddManager(grid.EdgeCount);
                FrontierBuilder.Build<PathSpec>(manager, new PathSpec(grid, s: 0, t: grid.VertexCount - 1), tooTight);
            }
            catch (BuildLimitExceededException)
            {
                threw = true;
            }

            Assert(threw, "a MaxFrontierSize below the real width fails fast instead of exhausting memory");

            // 幅が大きすぎて完走できない・したくないときの実践的な選択肢:
            //   1. Graph.Optimize(strategy) で辺順序を変え、幅そのものを狭くする（docs/tutorial.md 前節）。
            //   2. GraphSet の Including/Excluding/Smaller で対象を先に絞ってから数える（前々節）。
            //   3. Count（総数）ではなく MinWeight/TopK など、数え上げより軽い問いに切り替える。
            var shortest = GraphSet.Paths(grid, from: 0, to: grid.VertexCount - 1).MinWeight(_ => 1);
            Assert(shortest.Weight == 10, "a 6x6 grid's shortest corner-to-corner path has 10 edges");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"assertion failed: {message}");
            }
        }
    }
}
