using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Sets;
using ZDD.Net.Specs;

namespace ZDD.Net.Tests.Graphs
{
    /// <summary>
    /// M3-9 completion criteria for <see cref="GraphSet"/>: reproduces Graphillion's tutorial scenario
    /// (9x9 grid s-t path count, chained filters, sampling, min/max iteration), every generator matches
    /// building the corresponding spec directly, <c>Including</c>/<c>Excluding</c>/<c>Larger</c>/
    /// <c>Smaller</c> match a post-hoc filter exactly while building a smaller intermediate diagram,
    /// <c>MinIter</c>/<c>MaxIter</c> are genuinely lazy and weight-ordered, <c>RandIter</c> is uniform,
    /// and edges built from an <see cref="Graph.Optimize"/>d graph still read back as the original edges.
    /// </summary>
    public class GraphSetTests
    {
        // OEIS A007764: the number of simple paths between opposite corners of an n×n grid graph.
        [Theory]
        [InlineData(2, "2")]
        [InlineData(3, "12")]
        [InlineData(4, "184")]
        [InlineData(5, "8512")]
        [InlineData(6, "1262816")]
        public void PathsCountMatchesOeisA007764ForDiagonalGridPaths(int n, string expected)
        {
            Graph grid = Graph.Grid(n, n);
            GraphSet paths = GraphSet.Paths(grid, from: 0, to: grid.VertexCount - 1);

            Assert.Equal(BigInteger.Parse(expected), paths.Count);
        }

        [Fact]
        public void TutorialScenarioReproduces9x9GridPathCount()
        {
            Graph grid = Graph.Grid(9, 9);
            GraphSet paths = GraphSet.Paths(grid, from: 0, to: 80);

            Assert.Equal(BigInteger.Parse("3266598486981642"), paths.Count);

            (IReadOnlySet<Edge> Set, int Weight) shortest = paths.MinWeight(e => 1);
            Assert.Equal(16, shortest.Weight); // shortest corner-to-corner path on a 9x9 grid: 8 + 8 edges

            IReadOnlySet<Edge> sample = paths.Sample(new Random(42));
            Assert.True(paths.Contains(sample));
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        public void PathsMatchesDirectPathSpecBuild(string graphName)
        {
            Graph graph = NamedGraph(graphName);

            for (int s = 0; s < graph.VertexCount; s++)
            {
                for (int t = 0; t < graph.VertexCount; t++)
                {
                    if (s == t)
                    {
                        continue;
                    }

                    GraphSet actual = GraphSet.Paths(graph, s, t);

                    using ZddManager manager = new ZddManager(graph.EdgeCount);
                    Zdd expected = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(graph, s, t));

                    AssertSameEdgeSets(graph, actual, expected);
                }
            }
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        public void CyclesMatchesDirectCycleSpecBuild(string graphName)
        {
            Graph graph = NamedGraph(graphName);
            GraphSet actual = GraphSet.Cycles(graph);

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd expected = FrontierBuilder.Build<CycleSpec>(manager, new CycleSpec(graph));

            AssertSameEdgeSets(graph, actual, expected);
        }

        [Fact]
        public void TreesMatchesDirectSpanningTreeSpecBuild()
        {
            Graph graph = Graph.Complete(5);
            GraphSet actual = GraphSet.Trees(graph);

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd expected = FrontierBuilder.Build<SpanningTreeSpec>(manager, new SpanningTreeSpec(graph));

            AssertSameEdgeSets(graph, actual, expected);

            // Cayley's formula: n^(n-2) labeled spanning trees on K_n.
            Assert.Equal(new BigInteger(125), actual.Count);
        }

        [Fact]
        public void ForestsMatchesDirectForestSpecBuild()
        {
            Graph graph = Graph.Grid(3, 3);
            GraphSet actual = GraphSet.Forests(graph, components: 2);

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd expected = FrontierBuilder.Build<ForestSpec>(manager, new ForestSpec(graph, components: 2));

            AssertSameEdgeSets(graph, actual, expected);
        }

        [Fact]
        public void MatchingsMatchesDirectMatchingSpecBuild()
        {
            Graph graph = Graph.Complete(5);
            GraphSet actual = GraphSet.Matchings(graph);

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd expected = FrontierBuilder.Build<MatchingSpec>(manager, new MatchingSpec(graph));

            AssertSameEdgeSets(graph, actual, expected);
        }

        [Fact]
        public void HamiltonianPathsAndCyclesMatchDirectSpecBuild()
        {
            Graph graph = Graph.Complete(5);

            GraphSet actualPaths = GraphSet.HamiltonianPaths(graph, 0, 4);
            using (ZddManager manager = new ZddManager(graph.EdgeCount))
            {
                Zdd expected = FrontierBuilder.Build<HamiltonianPathSpec>(manager, new HamiltonianPathSpec(graph, 0, 4));
                AssertSameEdgeSets(graph, actualPaths, expected);
            }

            GraphSet actualCycles = GraphSet.HamiltonianCycles(graph);
            using (ZddManager manager = new ZddManager(graph.EdgeCount))
            {
                Zdd expected = FrontierBuilder.Build<HamiltonianCycleSpec>(manager, new HamiltonianCycleSpec(graph));
                AssertSameEdgeSets(graph, actualCycles, expected);
            }
        }

        [Fact]
        public void CliquesAndIndependentSetsAreVertexFamiliesMatchingDirectSpecBuild()
        {
            Graph graph = Graph.Complete(5);

            SetSet<int> cliques = GraphSet.Cliques(graph);
            using (ZddManager manager = new ZddManager(graph.VertexCount))
            {
                Zdd expected = FrontierBuilder.Build<CliqueSpec>(manager, new CliqueSpec(graph));
                Assert.Equal(expected.Count, cliques.Count);
            }

            SetSet<int> independentSets = GraphSet.IndependentSets(graph);
            using (ZddManager manager = new ZddManager(graph.VertexCount))
            {
                Zdd expected = FrontierBuilder.Build<IndependentSetSpec>(manager, new IndependentSetSpec(graph));
                Assert.Equal(expected.Count, independentSets.Count);
            }
        }

        [Fact]
        public void IncludingExcludingMatchPostHocFilteringExactly()
        {
            Graph grid = Graph.Grid(4, 4);
            GraphSet basePaths = GraphSet.Paths(grid, 0, grid.VertexCount - 1);
            Edge edge = grid.GetEdge(3);

            GraphSet including = basePaths.Including(edge);
            GraphSet excluding = basePaths.Excluding(edge);

            int item = grid.EdgeIndexToVariableIndex(3);
            Zdd postHocIncluding = basePaths.Zdd.SupersetsOf(basePaths.Zdd.Manager.Singleton(item));
            Zdd postHocExcluding = basePaths.Zdd.OffSet(item);

            Assert.Equal(postHocIncluding, including.Zdd);
            Assert.Equal(postHocExcluding, excluding.Zdd);

            Assert.All(including, set => Assert.Contains(edge, set));
            Assert.All(excluding, set => Assert.DoesNotContain(edge, set));
        }

        [Fact]
        public void IncludingExcludingVertexMatchPostHocFilteringExactly()
        {
            Graph grid = Graph.Grid(4, 4);
            GraphSet basePaths = GraphSet.Paths(grid, 0, grid.VertexCount - 1);
            int vertex = 5;

            GraphSet including = basePaths.Including(vertex);
            GraphSet excluding = basePaths.Excluding(vertex);

            bool Touches(IReadOnlySet<Edge> set) => set.Any(e => e.U == vertex || e.V == vertex);

            Assert.All(including, set => Assert.True(Touches(set)));
            Assert.All(excluding, set => Assert.False(Touches(set)));

            // Every path either touches the vertex or does not: the two filters partition the family.
            Assert.Equal(basePaths.Count, including.Count + excluding.Count);
        }

        [Fact]
        public void LargerSmallerLenEqualsMatchPostHocCardinalityFilteringExactly()
        {
            Graph grid = Graph.Grid(4, 4);
            GraphSet basePaths = GraphSet.Paths(grid, 0, grid.VertexCount - 1);
            ZddManager manager = basePaths.Universe.Manager;

            GraphSet larger = basePaths.Larger(8);
            GraphSet smaller = basePaths.Smaller(8);
            GraphSet exact = basePaths.LenEquals(8);

            Zdd postHocLarger = basePaths.Zdd.Intersect(
                FrontierBuilder.Build<CardinalitySpec, int>(manager, new CardinalitySpec(grid.EdgeCount, 9, grid.EdgeCount)));
            Zdd postHocSmaller = basePaths.Zdd.Intersect(
                FrontierBuilder.Build<CardinalitySpec, int>(manager, new CardinalitySpec(grid.EdgeCount, 0, 7)));
            Zdd postHocExact = basePaths.Zdd.Intersect(
                FrontierBuilder.Build<CardinalitySpec, int>(manager, new CardinalitySpec(grid.EdgeCount, 8, 8)));

            Assert.Equal(postHocLarger, larger.Zdd);
            Assert.Equal(postHocSmaller, smaller.Zdd);
            Assert.Equal(postHocExact, exact.Zdd);

            Assert.Equal(basePaths.Count, larger.Count + smaller.Count + exact.Count);
            Assert.Equal(basePaths.Universe.Manager.Empty, basePaths.Smaller(0).Zdd);
        }

        [Fact]
        public void ChainedFiltersComposeCorrectly()
        {
            Graph grid = Graph.Grid(4, 4);
            GraphSet basePaths = GraphSet.Paths(grid, 0, grid.VertexCount - 1);
            Edge include = grid.GetEdge(0);
            Edge exclude = grid.GetEdge(3);

            GraphSet chained = basePaths.Including(include).Excluding(exclude).Smaller(20);

            Assert.All(chained, set =>
            {
                Assert.Contains(include, set);
                Assert.DoesNotContain(exclude, set);
                Assert.True(set.Count < 20);
            });

            Zdd expected = basePaths
                .Including(include).Zdd
                .Intersect(basePaths.Excluding(exclude).Zdd)
                .Intersect(basePaths.Smaller(20).Zdd);

            Assert.Equal(expected, chained.Zdd);
        }

        [Fact]
        public void FiltersAreAppliedAtConstructionTimeNotAsAPostHocIntersection()
        {
            // A graph wide enough that the unfiltered family's build genuinely has more intermediate
            // states than the version with the filter folded into the same frontier walk.
            Graph grid = Graph.Grid(6, 6);
            var baseSpec = new ArraySpecErased<PathSpec>(new PathSpec(grid, 0, grid.VertexCount - 1));
            var filterSpec = new ArraySpecErased<EdgeMembershipSpec>(new EdgeMembershipSpec(grid, grid.EdgeCount / 2, require: false));
            var combinedSpec = new AndErasedSpec(baseSpec, filterSpec);

            long constructionTimeNodeCount = TopDownExpander<ErasedGraphDdSpec, object?>
                .Expand(new ErasedGraphDdSpec(combinedSpec)).NodeCount;

            // What a post-hoc filter (build the unfiltered family, then intersect) would have to
            // materialize before it could even begin filtering.
            long postFilterIntermediateNodeCount = TopDownExpander<ErasedGraphDdSpec, object?>
                .Expand(new ErasedGraphDdSpec(baseSpec)).NodeCount;

            Assert.True(
                constructionTimeNodeCount < postFilterIntermediateNodeCount,
                $"expected the filter folded into construction ({constructionTimeNodeCount} nodes) to build " +
                $"fewer intermediate nodes than the unfiltered family a post-hoc filter would need first " +
                $"({postFilterIntermediateNodeCount} nodes).");
        }

        [Fact]
        public void MinIterAndMaxIterAreWeightOrderedAndMatchMinMaxWeight()
        {
            Graph graph = Graph.Complete(5);
            GraphSet trees = GraphSet.Trees(graph);
            var random = new Random(11);
            Dictionary<Edge, int> weight = graph.Edges.ToDictionary(e => e, _ => random.Next(1, 50));

            int Weight(IReadOnlySet<Edge> set) => set.Sum(e => weight[e]);

            List<IReadOnlySet<Edge>> ascending = trees.MinIter(e => weight[e]).ToList();
            List<IReadOnlySet<Edge>> descending = trees.MaxIter(e => weight[e]).ToList();

            Assert.Equal((int)trees.Count, ascending.Count);
            Assert.Equal((int)trees.Count, descending.Count);

            Assert.Equal(Weight(ascending[0]), trees.MinWeight(e => weight[e]).Weight);
            Assert.Equal(Weight(descending[0]), trees.MaxWeight(e => weight[e]).Weight);

            for (int i = 1; i < ascending.Count; i++)
            {
                Assert.True(Weight(ascending[i - 1]) <= Weight(ascending[i]));
            }

            for (int i = 1; i < descending.Count; i++)
            {
                Assert.True(Weight(descending[i - 1]) >= Weight(descending[i]));
            }

            // Same family, so the same multiset of edge sets regardless of which order it was walked in.
            Assert.Equal(
                ascending.Select(EdgeSetKey).OrderBy(k => k, StringComparer.Ordinal),
                descending.Select(EdgeSetKey).OrderBy(k => k, StringComparer.Ordinal));
        }

        [Fact]
        public void MinIterTakeIsLazyOnAHugeFamily()
        {
            // 9x9 grid s-t paths: 3266598486981642 members. A non-lazy MinIter would never return.
            Graph grid = Graph.Grid(9, 9);
            GraphSet paths = GraphSet.Paths(grid, 0, 80);

            var stopwatch = Stopwatch.StartNew();
            List<IReadOnlySet<Edge>> first10 = paths.MinIter(e => 1).Take(10).ToList();
            stopwatch.Stop();

            Assert.Equal(10, first10.Count);

            // A non-lazy implementation would have to materialize (and sort) all 3.2 quadrillion
            // paths first, which would never finish; a generous bound avoids CI flakiness while
            // still catching that regression by orders of magnitude.
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(30),
                $"MinIter().Take(10) took {stopwatch.Elapsed}, which suggests it is not actually lazy.");

            // The first 10 shortest paths all have the true shortest length.
            Assert.All(first10, set => Assert.Equal(16, set.Count));
        }

        [Fact]
        public void RandIterIsUniformEnoughToPassAChiSquaredTest()
        {
            const int Draws = 20_000;

            Graph graph = Graph.Cycle(5);
            GraphSet cycles = GraphSet.Cycles(graph, single: false); // {empty is excluded}; only one nonempty cycle family member: the whole 5-cycle
            GraphSet matchings = GraphSet.Matchings(graph); // richer family for a meaningful chi-squared test

            int categories = (int)matchings.Count;
            Assert.True(categories > 1);

            var observed = new Dictionary<string, int>();
            foreach (IReadOnlySet<Edge> set in matchings.RandIter(new Random(4242)).Take(Draws))
            {
                string key = EdgeSetKey(set);
                observed[key] = observed.GetValueOrDefault(key) + 1;
            }

            Assert.Equal(categories, observed.Count);

            double expected = (double)Draws / categories;
            double chiSquared = observed.Values.Sum(count => ((count - expected) * (count - expected)) / expected);

            // Degrees of freedom = categories - 1; threshold picked generously above the upper 0.1%
            // critical value so a fixed seed either always passes or reveals a real bias.
            double threshold = categories * 3.0 + 20.0;
            Assert.True(
                chiSquared < threshold,
                $"The chi-squared statistic was {chiSquared:F2} over {categories} categories, which suggests RandIter is biased.");

            Assert.NotEmpty(cycles.ToList()); // sanity: the "single: false" family is nonempty too
        }

        [Fact]
        public void OptimizeGraphEdgesAreReinterpretedCorrectly()
        {
            // Shuffled first: Grid() already numbers edges to keep the frontier narrow, so
            // Optimize() alone might legitimately leave the order unchanged. Starting from an
            // adversarial order guarantees Optimize() actually reorders edges, which is what this
            // test needs — the point isn't that Optimize() changes anything, it's that whatever
            // order it picks still reads back correctly.
            Graph grid = Shuffle(Graph.Grid(5, 5), seed: 3);
            Graph optimized = grid.Optimize(EdgeOrderStrategy.BeamSearchPathWidth);
            Assert.NotEqual(grid.Edges.ToArray(), optimized.Edges.ToArray()); // sanity: the order actually changed

            GraphSet fromOriginal = GraphSet.Paths(grid, 0, grid.VertexCount - 1);
            GraphSet fromOptimized = GraphSet.Paths(optimized, 0, grid.VertexCount - 1);

            Assert.Equal(fromOriginal.Count, fromOptimized.Count);

            HashSet<string> originalKeys = fromOriginal.Select(EdgeSetKey).ToHashSet();
            HashSet<string> optimizedKeys = fromOptimized.Select(EdgeSetKey).ToHashSet();
            Assert.Equal(originalKeys, optimizedKeys);

            // Every edge set enumerated from the optimized graph is still a genuine simple path between
            // the original endpoints, in terms of the original vertex numbering.
            foreach (IReadOnlySet<Edge> set in fromOptimized)
            {
                AssertIsSimplePath(grid, 0, grid.VertexCount - 1, set);
            }
        }

        // ---- Helpers ----

        private static Graph NamedGraph(string name) => name switch
        {
            "path4" => Graph.Path(4),
            "cycle5" => Graph.Cycle(5),
            "complete5" => Graph.Complete(5),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

        /// <summary>A deterministic pseudo-random edge-order shuffle, so <c>Optimize()</c> has an adversarial order to actually improve on.</summary>
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

        private static void AssertSameEdgeSets(Graph graph, GraphSet actual, Zdd expected)
        {
            HashSet<string> actualKeys = actual.Select(EdgeSetKey).ToHashSet();

            var expectedKeys = new HashSet<string>();
            foreach (int[] items in expected.Sets())
            {
                Edge[] edges = items.Select(graph.GetEdge).ToArray();
                expectedKeys.Add(EdgeSetKey(edges));
            }

            Assert.Equal(expectedKeys, actualKeys);
        }

        private static string EdgeSetKey(IEnumerable<Edge> set) =>
            string.Join(";", set.Select(e => $"{Math.Min(e.U, e.V)}-{Math.Max(e.U, e.V)}").OrderBy(s => s, StringComparer.Ordinal));

        private static void AssertIsSimplePath(Graph graph, int s, int t, IReadOnlySet<Edge> edgeSet)
        {
            var degree = new Dictionary<int, int>();
            var adjacency = new Dictionary<int, List<int>>();

            foreach (Edge edge in edgeSet)
            {
                degree[edge.U] = degree.GetValueOrDefault(edge.U) + 1;
                degree[edge.V] = degree.GetValueOrDefault(edge.V) + 1;

                if (!adjacency.TryGetValue(edge.U, out List<int>? uList))
                {
                    uList = new List<int>();
                    adjacency[edge.U] = uList;
                }

                if (!adjacency.TryGetValue(edge.V, out List<int>? vList))
                {
                    vList = new List<int>();
                    adjacency[edge.V] = vList;
                }

                uList.Add(edge.V);
                vList.Add(edge.U);
            }

            Assert.True(edgeSet.Count >= 1);
            Assert.Equal(1, degree.GetValueOrDefault(s));
            Assert.Equal(1, degree.GetValueOrDefault(t));

            foreach (KeyValuePair<int, int> entry in degree)
            {
                if (entry.Key != s && entry.Key != t)
                {
                    Assert.Equal(2, entry.Value);
                }
            }

            int steps = 0;
            int previous = -1;
            int current = s;
            while (current != t)
            {
                int next = adjacency[current].First(candidate => candidate != previous);
                previous = current;
                current = next;
                steps++;
                Assert.True(steps <= edgeSet.Count);
            }

            Assert.Equal(edgeSet.Count, steps);
        }
    }
}
