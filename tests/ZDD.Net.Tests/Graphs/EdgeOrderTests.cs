using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;

namespace ZDD.Net.Tests.Graphs
{
    /// <summary>
    /// M3-1 completion criteria for edge-order optimization: the peak frontier really shrinks (on a
    /// thousands-of-edges graph, and on grids for the <see cref="EdgeOrderStrategy.Grid"/> strategy), the
    /// reordering leaves the source graph alone, and — the one that matters most — reordering does not
    /// change the family built over it, read back through the edge-index mapping.
    /// </summary>
    public class EdgeOrderTests
    {
        private static readonly EdgeOrderStrategy[] SupportedStrategies =
        {
            EdgeOrderStrategy.AsGiven,
            EdgeOrderStrategy.Bfs,
            EdgeOrderStrategy.Dfs,
            EdgeOrderStrategy.Grid,
        };

        public static TheoryData<EdgeOrderStrategy> Strategies()
        {
            var data = new TheoryData<EdgeOrderStrategy>();
            foreach (EdgeOrderStrategy strategy in SupportedStrategies)
            {
                data.Add(strategy);
            }

            return data;
        }

        [Theory]
        [MemberData(nameof(Strategies))]
        public void OptimizeLeavesTheSourceGraphUntouched(EdgeOrderStrategy strategy)
        {
            Graph graph = Shuffle(Graph.Grid(4, 5), seed: 7);
            Edge[] before = graph.Edges.ToArray();
            EdgeOrderMapping? sourceOrderBefore = graph.SourceOrder;

            Graph optimized = graph.Optimize(strategy);

            Assert.NotSame(graph, optimized);
            Assert.Same(sourceOrderBefore, graph.SourceOrder);
            Assert.Null(Graph.Grid(4, 5).SourceOrder);
            Assert.Equal(before.Length, graph.EdgeCount);
            for (int i = 0; i < before.Length; i++)
            {
                Assert.Equal(before[i].U, graph.Edges[i].U);
                Assert.Equal(before[i].V, graph.Edges[i].V);
            }

            Assert.Equal(graph.VertexCount, optimized.VertexCount);
            Assert.Equal(graph.EdgeCount, optimized.EdgeCount);
        }

        [Theory]
        [MemberData(nameof(Strategies))]
        public void EdgeIndexMappingRoundTripsAndNamesTheRightEdge(EdgeOrderStrategy strategy)
        {
            Graph graph = Shuffle(Graph.Grid(4, 5), seed: 11);

            Graph optimized = graph.Optimize(strategy);
            EdgeOrderMapping mapping = Assert.IsType<EdgeOrderMapping>(optimized.SourceOrder);

            Assert.Same(graph, mapping.Source);
            Assert.Equal(graph.EdgeCount, mapping.Count);

            var sourceIndicesSeen = new HashSet<int>();
            for (int i = 0; i < optimized.EdgeCount; i++)
            {
                int sourceIndex = mapping.ToSourceEdgeIndex(i);

                Assert.True(sourceIndicesSeen.Add(sourceIndex), $"source edge {sourceIndex} was mapped to twice.");
                Assert.Equal(i, mapping.FromSourceEdgeIndex(sourceIndex));
                Assert.Equal(sourceIndex, mapping.ToSourceEdgeIndices[i]);

                // The reordered edge must *be* the source edge, endpoints and all — not merely an edge
                // that happens to compare equal to it.
                Assert.Equal(graph.GetEdge(sourceIndex).U, optimized.GetEdge(i).U);
                Assert.Equal(graph.GetEdge(sourceIndex).V, optimized.GetEdge(i).V);
            }

            Assert.Equal(graph.EdgeCount, sourceIndicesSeen.Count);
        }

        [Theory]
        [MemberData(nameof(Strategies))]
        public void ReorderingBuildsTheSameFamilyReadBackThroughTheMapping(EdgeOrderStrategy strategy)
        {
            // The most important correctness check of M3-1: a different variable order must be the same
            // family, once every set is translated back to the source graph's edge indices.
            Graph graph = Shuffle(Graph.Grid(3, 4), seed: 3);
            HashSet<string> expected = PathFamily(graph, mapping: null);

            Graph optimized = graph.Optimize(strategy);
            HashSet<string> actual = PathFamily(optimized, optimized.SourceOrder);

            Assert.Equal(expected.Count, actual.Count);
            Assert.True(expected.SetEquals(actual), $"{strategy} changed the family it builds.");
        }

        [Theory]
        [MemberData(nameof(Strategies))]
        public void ReorderingKeepsOeisA007764(EdgeOrderStrategy strategy)
        {
            // The flagship M2 number (5x5 grid, corner-to-corner simple paths) must survive every strategy.
            Graph grid = Graph.Grid(5, 5);

            Graph optimized = grid.Optimize(strategy);
            using ZddManager manager = new ZddManager(optimized.EdgeCount);
            Zdd paths = FrontierBuilder.Build<PathSpec>(
                manager, new PathSpec(optimized, 0, optimized.VertexCount - 1));

            Assert.Equal(new BigInteger(8512), paths.Count);
        }

        [Theory]
        [MemberData(nameof(Strategies))]
        public void ReorderingKeepsWhatEveryBuiltInGraphSpecCounts(EdgeOrderStrategy strategy)
        {
            Graph graph = Shuffle(Graph.Grid(4, 4), seed: 9);
            Graph optimized = graph.Optimize(strategy);

            Assert.Equal(Counts(graph), Counts(optimized));

            static BigInteger[] Counts(Graph graph)
            {
                using ZddManager manager = new ZddManager(graph.EdgeCount);
                return new[]
                {
                    FrontierBuilder.Build<PathSpec>(manager, new PathSpec(graph, 0, graph.VertexCount - 1)).Count,
                    FrontierBuilder.Build<SpanningTreeSpec>(manager, new SpanningTreeSpec(graph)).Count,
                    FrontierBuilder.Build<ForestSpec>(manager, new ForestSpec(graph, components: 2)).Count,
                    FrontierBuilder.Build<MatchingSpec>(manager, new MatchingSpec(graph, perfect: true)).Count,
                };
            }
        }

        [Theory]
        [InlineData(3, 9)]
        [InlineData(4, 20)]
        [InlineData(5, 5)]
        [InlineData(7, 7)]
        [InlineData(20, 4)]
        [InlineData(2, 60)]
        public void GridStrategyIsNoWiderThanBfsOnGrids(int rows, int cols)
        {
            Graph grid = Graph.Grid(rows, cols);

            int gridWidth = grid.EstimateMaxFrontierSize(EdgeOrderStrategy.Grid);
            int bfsWidth = grid.EstimateMaxFrontierSize(EdgeOrderStrategy.Bfs);

            Assert.True(gridWidth <= bfsWidth, $"Grid ({gridWidth}) should not be wider than Bfs ({bfsWidth}).");
            Assert.True(gridWidth <= Math.Min(rows, cols) + 1, $"Grid ({gridWidth}) should be about the shorter side.");
        }

        [Fact]
        public void GridStrategyFallsBackToBfsOnAGraphThatIsNotAGrid()
        {
            Graph graph = Graph.Complete(6);

            Graph viaGrid = graph.Optimize(EdgeOrderStrategy.Grid);
            Graph viaBfs = graph.Optimize(EdgeOrderStrategy.Bfs);

            Assert.Equal(viaBfs.SourceOrder!.ToSourceEdgeIndices, viaGrid.SourceOrder!.ToSourceEdgeIndices);
        }

        [Fact]
        public void BfsAndGridNarrowThePeakFrontierOnAThousandsOfEdgesGraph()
        {
            // A 40x40 grid whose edges arrive in an arbitrary order: 3,120 edges, and the order alone
            // decides whether a build over it is feasible at all.
            Graph graph = Shuffle(Graph.Grid(40, 40), seed: 7);

            int asGiven = graph.EstimateMaxFrontierSize();
            int bfs = graph.EstimateMaxFrontierSize(EdgeOrderStrategy.Bfs);
            int grid = graph.EstimateMaxFrontierSize(EdgeOrderStrategy.Grid);

            Assert.Equal(3120, graph.EdgeCount);
            Assert.True(bfs * 10 < asGiven, $"Bfs ({bfs}) should be far narrower than AsGiven ({asGiven}).");
            Assert.True(grid <= bfs, $"Grid ({grid}) should not be wider than Bfs ({bfs}).");

            // The frontier only ever holds vertices, so a strategy claiming less than one grid side would
            // mean the width computation, not the strategy, is wrong.
            Assert.True(grid >= 41, $"A 40x40 grid cannot be narrower than 41 ({grid}).");
        }

        [Theory]
        [MemberData(nameof(Strategies))]
        public void EveryStrategyHandlesDisconnectedGraphsAndIsolatedVertices(EdgeOrderStrategy strategy)
        {
            // Two components (a triangle and an edge) plus three vertices touching no edge at all.
            var graph = new Graph(9, new[]
            {
                new Edge(0, 1), new Edge(1, 2), new Edge(2, 0), new Edge(4, 5),
            });

            Graph optimized = graph.Optimize(strategy);

            AssertIsPermutationOfEdges(graph, optimized);
            Assert.Equal(graph.EstimateMaxFrontierSize(strategy), optimized.EstimateMaxFrontierSize());

            HashSet<string> expected = PathFamily(graph, mapping: null, s: 0, t: 2);
            HashSet<string> actual = PathFamily(optimized, optimized.SourceOrder, s: 0, t: 2);
            Assert.True(expected.SetEquals(actual), $"{strategy} changed the family on a disconnected graph.");
        }

        [Fact]
        public void EveryStrategyHandlesAGraphWithNoEdges()
        {
            var graph = new Graph(4, Array.Empty<Edge>());

            foreach (EdgeOrderStrategy strategy in SupportedStrategies)
            {
                Graph optimized = graph.Optimize(strategy);

                Assert.Equal(0, optimized.EdgeCount);
                Assert.Equal(0, optimized.EstimateMaxFrontierSize());
            }
        }

        [Theory]
        [MemberData(nameof(Strategies))]
        public void EstimateMaxFrontierSizeAgreesWithFrontierManager(EdgeOrderStrategy strategy)
        {
            Graph graph = Shuffle(Graph.Grid(5, 7), seed: 5);

            Graph optimized = graph.Optimize(strategy);

            Assert.Equal(new FrontierManager(graph).MaxFrontierSize, graph.EstimateMaxFrontierSize());
            Assert.Equal(new FrontierManager(optimized).MaxFrontierSize, optimized.EstimateMaxFrontierSize());
            Assert.Equal(optimized.EstimateMaxFrontierSize(), graph.EstimateMaxFrontierSize(strategy));
        }

        [Fact]
        public void EstimateMaxFrontierSizeReturnsAtOnceOnAThousandsOfEdgesGraph()
        {
            Graph graph = Shuffle(Graph.Grid(40, 40), seed: 7);

            Stopwatch stopwatch = Stopwatch.StartNew();
            int width = graph.EstimateMaxFrontierSize();
            stopwatch.Stop();

            Assert.True(width > 0);

            // The estimate is O(V + E), so it is microseconds' work; the bound is loose enough that only a
            // change in complexity class (a build, a per-edge rescan) can trip it.
            Assert.True(
                stopwatch.Elapsed.TotalSeconds < 1.0,
                $"Estimating 3,120 edges took {stopwatch.Elapsed.TotalMilliseconds:F0} ms; it should be immediate.");
        }

        [Fact]
        public void BestOfCandidatesIsNoWorseThanTheDefaultStartVertex()
        {
            Graph graph = Shuffle(Graph.Grid(3, 12), seed: 13);

            int defaultStart = graph.EstimateMaxFrontierSize(EdgeOrderStrategy.Bfs);
            int best = graph.EstimateMaxFrontierSize(EdgeOrderStrategy.Bfs, EdgeOrderOptions.BestOfCandidates());
            int bestOfFive = graph.EstimateMaxFrontierSize(EdgeOrderStrategy.Bfs, EdgeOrderOptions.BestOfCandidates(5));

            Assert.True(best <= defaultStart, $"BestOfCandidates ({best}) should not be worse than the default ({defaultStart}).");
            Assert.True(best <= bestOfFive, $"Trying every vertex ({best}) should not be worse than trying five ({bestOfFive}).");
        }

        [Fact]
        public void SpecifiedStartVertexIsTheVertexTheTraversalStartsFrom()
        {
            Graph graph = Graph.Path(5);

            // Starting from the far end reverses the path's edge order.
            Graph optimized = graph.Optimize(EdgeOrderStrategy.Bfs, EdgeOrderOptions.FromVertex(4));

            Assert.Equal(new[] { 3, 2, 1, 0 }, optimized.SourceOrder!.ToSourceEdgeIndices);
        }

        [Fact]
        public void SpecifiedStartVertexOutsideTheGraphThrows()
        {
            Graph graph = Graph.Path(5);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => graph.Optimize(EdgeOrderStrategy.Bfs, EdgeOrderOptions.FromVertex(5)));
            Assert.Throws<ArgumentOutOfRangeException>(() => EdgeOrderOptions.FromVertex(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => EdgeOrderOptions.BestOfCandidates(-1));
        }

        [Fact]
        public void BeamSearchPathWidthIsNotImplementedYet()
        {
            Graph graph = Graph.Grid(3, 3);

            Assert.Throws<NotSupportedException>(() => graph.Optimize(EdgeOrderStrategy.BeamSearchPathWidth));
            Assert.Throws<NotSupportedException>(
                () => graph.EstimateMaxFrontierSize(EdgeOrderStrategy.BeamSearchPathWidth));
        }

        [Fact]
        public void UnknownStrategyThrows()
        {
            Graph graph = Graph.Grid(3, 3);

            Assert.Throws<ArgumentOutOfRangeException>(() => graph.Optimize((EdgeOrderStrategy)99));
        }

        [Fact]
        public void AsGivenKeepsTheOrderAndStillRecordsAnIdentityMapping()
        {
            Graph graph = Shuffle(Graph.Grid(3, 4), seed: 2);

            Graph optimized = graph.Optimize(EdgeOrderStrategy.AsGiven);

            Assert.Equal(Enumerable.Range(0, graph.EdgeCount), optimized.SourceOrder!.ToSourceEdgeIndices);
            Assert.Equal(graph.EstimateMaxFrontierSize(), optimized.EstimateMaxFrontierSize());
        }

        [Fact]
        public void WithEdgeOrderRecordsTheMappingItApplied()
        {
            Graph graph = Graph.Path(4);

            Graph reordered = graph.WithEdgeOrder(new[] { 2, 0, 1 });

            Assert.Same(graph, reordered.SourceOrder!.Source);
            Assert.Equal(new[] { 2, 0, 1 }, reordered.SourceOrder.ToSourceEdgeIndices);
            Assert.Equal(new[] { 0, 1, 2 }, reordered.SourceOrder.ToSourceEdgeSet(new[] { 1, 2, 0 }));
        }

        [Fact]
        public void ReorderingTwiceLeavesAChainBackToTheOriginalGraph()
        {
            Graph graph = Shuffle(Graph.Grid(3, 4), seed: 4);

            Graph once = graph.Optimize(EdgeOrderStrategy.Bfs);
            Graph twice = once.Optimize(EdgeOrderStrategy.Dfs);

            Assert.Same(once, twice.SourceOrder!.Source);
            Assert.Same(graph, twice.SourceOrder.Source.SourceOrder!.Source);

            for (int i = 0; i < twice.EdgeCount; i++)
            {
                int original = graph.EdgeCount == 0
                    ? 0
                    : once.SourceOrder!.ToSourceEdgeIndex(twice.SourceOrder.ToSourceEdgeIndex(i));

                Assert.Equal(graph.GetEdge(original).U, twice.GetEdge(i).U);
                Assert.Equal(graph.GetEdge(original).V, twice.GetEdge(i).V);
            }
        }

        private static void AssertIsPermutationOfEdges(Graph source, Graph reordered)
        {
            Assert.Equal(source.EdgeCount, reordered.EdgeCount);

            var seen = new bool[source.EdgeCount];
            EdgeOrderMapping mapping = reordered.SourceOrder!;
            for (int i = 0; i < reordered.EdgeCount; i++)
            {
                int sourceIndex = mapping.ToSourceEdgeIndex(i);
                Assert.False(seen[sourceIndex]);
                seen[sourceIndex] = true;
                Assert.Equal(source.GetEdge(sourceIndex), reordered.GetEdge(i));
            }

            Assert.DoesNotContain(false, seen);
        }

        /// <summary>
        /// The s–t path family of <paramref name="graph"/>, with every set translated through
        /// <paramref name="mapping"/> so families built over different edge orders can be compared.
        /// </summary>
        private static HashSet<string> PathFamily(Graph graph, EdgeOrderMapping? mapping, int s = 0, int t = -1)
        {
            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd paths = FrontierBuilder.Build<PathSpec>(
                manager, new PathSpec(graph, s, t < 0 ? graph.VertexCount - 1 : t));

            var family = new HashSet<string>();
            foreach (int[] edgeSet in paths.Sets())
            {
                int[] translated = mapping is null ? edgeSet : mapping.ToSourceEdgeSet(edgeSet);
                Array.Sort(translated);
                family.Add(string.Join(",", translated));
            }

            return family;
        }

        /// <summary>
        /// Rearranges <paramref name="graph"/>'s edges into an arbitrary order, standing in for a graph
        /// that arrives from a file in whatever order it was written in. Uses a fixed linear congruential
        /// generator rather than <see cref="Random"/> so the order is the same on every runtime.
        /// </summary>
        private static Graph Shuffle(Graph graph, int seed)
        {
            var order = Enumerable.Range(0, graph.EdgeCount).ToArray();
            uint state = (uint)seed + 0x9E3779B9u;

            for (int i = order.Length - 1; i > 0; i--)
            {
                state = (state * 1664525u) + 1013904223u;
                int j = (int)(state % (uint)(i + 1));
                (order[i], order[j]) = (order[j], order[i]);
            }

            return graph.WithEdgeOrder(order);
        }
    }
}
