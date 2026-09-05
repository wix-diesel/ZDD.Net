using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Io;
using ZDD.Net.Sets;
using ZDD.Net.Tests.Harness;
using ZDD.Net.Tests.Specs;
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

        // ---- コストフィルタ（M6-8）----

        [Fact]
        public void CostAtMostCostAtLeastCostEqualsMatchPostHocWeightFilteringExactly()
        {
            Graph grid = Graph.Grid(4, 4);
            GraphSet basePaths = GraphSet.Paths(grid, 0, grid.VertexCount - 1);
            ZddManager manager = basePaths.Universe.Manager;

            // Alternating positive/negative per-edge costs, so the completion criterion's "negative
            // coefficients" is covered by the same scenario as the three operators.
            long[] costs = new long[grid.EdgeCount];
            var costByEdge = new Dictionary<Edge, long>();
            for (int i = 0; i < costs.Length; i++)
            {
                costs[i] = i % 2 == 0 ? i + 1 : -(i + 1);
                costByEdge[grid.GetEdge(i)] = costs[i];
            }

            const long bound = 3;
            GraphSet atMost = basePaths.CostAtMost(e => costByEdge[e], bound);
            GraphSet atLeast = basePaths.CostAtLeast(e => costByEdge[e], bound);
            GraphSet equals = basePaths.CostEquals(e => costByEdge[e], bound);

            LinearConstraintSpec specAtMost = new LinearConstraintSpec(costs, LinearConstraintOperator.LessOrEqual, bound);
            LinearConstraintSpec specAtLeast = new LinearConstraintSpec(costs, LinearConstraintOperator.GreaterOrEqual, bound);
            LinearConstraintSpec specEquals = new LinearConstraintSpec(costs, LinearConstraintOperator.Equal, bound);

            Zdd postHocAtMost = basePaths.Zdd.Intersect(FrontierBuilder.Build<LinearConstraintSpec, long>(manager, specAtMost));
            Zdd postHocAtLeast = basePaths.Zdd.Intersect(FrontierBuilder.Build<LinearConstraintSpec, long>(manager, specAtLeast));
            Zdd postHocEquals = basePaths.Zdd.Intersect(FrontierBuilder.Build<LinearConstraintSpec, long>(manager, specEquals));

            Assert.Equal(postHocAtMost, atMost.Zdd);
            Assert.Equal(postHocAtLeast, atLeast.Zdd);
            Assert.Equal(postHocEquals, equals.Zdd);

            // Every kept edge set actually satisfies the operator it was filtered by.
            Assert.All(atMost, set => Assert.True(TotalCost(set, costByEdge) <= bound));
            Assert.All(atLeast, set => Assert.True(TotalCost(set, costByEdge) >= bound));
            Assert.All(equals, set => Assert.Equal(bound, TotalCost(set, costByEdge)));
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
        public void CostAtMostComposesWithOtherFiltersInAChain()
        {
            Graph grid = Graph.Grid(4, 4);
            GraphSet basePaths = GraphSet.Paths(grid, 0, grid.VertexCount - 1);
            Edge include = grid.GetEdge(0);

            GraphSet chained = basePaths.Including(include).CostAtMost(e => 1, 8);

            Assert.All(chained, set =>
            {
                Assert.Contains(include, set);
                Assert.True(set.Count <= 8);
            });

            Zdd expected = basePaths.Including(include).Zdd.Intersect(basePaths.CostAtMost(e => 1, 8).Zdd);
            Assert.Equal(expected, chained.Zdd);
        }

        private static long TotalCost(IReadOnlySet<Edge> set, IReadOnlyDictionary<Edge, long> costByEdge)
        {
            long sum = 0;

            foreach (Edge edge in set)
            {
                sum += costByEdge[edge];
            }

            return sum;
        }

        // ---- 1 要素変種（M6-7）----

        [Fact]
        public void SomeItemVariantsMatchTheUnderlyingZddOperations()
        {
            Graph grid = Graph.Grid(4, 4);
            GraphSet basePaths = GraphSet.Paths(grid, 0, grid.VertexCount - 1);
            Edge[] someEdges = [grid.GetEdge(0), grid.GetEdge(3), grid.GetEdge(5)];
            int[] someIndices = [0, 3, 5];

            Assert.Equal(basePaths.Zdd.RemoveSomeItem(), basePaths.RemoveSomeItem().Zdd);
            Assert.Equal(basePaths.Zdd.AddSomeItem(), basePaths.AddSomeItem().Zdd);
            Assert.Equal(basePaths.Zdd.RemoveAddSomeItems(), basePaths.RemoveAddSomeItems().Zdd);

            Assert.Equal(basePaths.Zdd.RemoveSomeItem(someIndices), basePaths.RemoveSomeItem(someEdges).Zdd);
            Assert.Equal(basePaths.Zdd.AddSomeItem(someIndices), basePaths.AddSomeItem(someEdges).Zdd);
            Assert.Equal(basePaths.Zdd.RemoveAddSomeItems(someIndices), basePaths.RemoveAddSomeItems(someEdges).Zdd);

            // The GraphSet's own Universe still decodes the wrapped Zdd's sets back into real edges
            // of this graph (not just some default(Edge) or an index misread as an edge).
            GraphSet removed = basePaths.RemoveSomeItem(someEdges);
            Assert.NotEmpty(removed);
            Assert.All(removed, set => Assert.All(set, edge => Assert.Contains(edge, grid.Edges)));
            Assert.Equal(removed.Count, removed.Count());
        }

        [Fact]
        public void SomeItemVariantsRejectAnEdgeOutsideTheGraph()
        {
            Graph grid = Graph.Grid(4, 4);
            GraphSet basePaths = GraphSet.Paths(grid, 0, grid.VertexCount - 1);
            Edge foreign = new Edge(0, grid.VertexCount - 1);

            Assert.Throws<ArgumentException>(() => basePaths.RemoveSomeItem(foreign));
            Assert.Throws<ArgumentException>(() => basePaths.AddSomeItem(foreign));
            Assert.Throws<ArgumentException>(() => basePaths.RemoveAddSomeItems(foreign));
        }

        [Fact]
        public void SomeItemVariantsComposeCorrectlyWithFurtherFilters()
        {
            // A family built by direct Zdd algebra (not a frontier walk) still has to filter
            // correctly afterward (PrecomputedZddSpec, M6-7): this is the regression test for that
            // bridge, mirroring IncludingExcludingMatchPostHocFilteringExactly's post-hoc comparison.
            Graph grid = Graph.Grid(4, 4);
            GraphSet basePaths = GraphSet.Paths(grid, 0, grid.VertexCount - 1);
            Edge include = grid.GetEdge(0);
            Edge exclude = grid.GetEdge(1);

            GraphSet removed = basePaths.RemoveSomeItem();

            GraphSet including = removed.Including(include);
            GraphSet excluding = removed.Excluding(exclude);
            GraphSet smaller = removed.Smaller(6);

            int includeItem = grid.EdgeIndexToVariableIndex(0);
            int excludeItem = grid.EdgeIndexToVariableIndex(1);

            Zdd postHocIncluding = removed.Zdd.SupersetsOf(removed.Zdd.Manager.Singleton(includeItem));
            Zdd postHocExcluding = removed.Zdd.OffSet(excludeItem);

            Assert.Equal(postHocIncluding, including.Zdd);
            Assert.Equal(postHocExcluding, excluding.Zdd);
            Assert.All(including, set => Assert.Contains(include, set));
            Assert.All(excluding, set => Assert.DoesNotContain(exclude, set));
            Assert.All(smaller, set => Assert.True(set.Count < 6));

            // Chaining further still matches a post-hoc intersection of everything applied so far.
            GraphSet chained = removed.Including(include).Excluding(exclude).Smaller(6);
            Zdd expectedChained = postHocIncluding.OffSet(excludeItem).Intersect(smaller.Zdd);
            Assert.Equal(expectedChained, chained.Zdd);
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

        // ---- 辺の族ジェネレータ拡張（M6-9）----

        [Fact]
        public void ConnectedSubgraphsMatchesDirectSpecBuild()
        {
            Graph graph = Graph.Grid(3, 3);
            int[] terminals = { 0, 4, 8 };
            GraphSet actual = GraphSet.ConnectedSubgraphs(graph, terminals);

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd expected = FrontierBuilder.Build<ConnectedSubgraphSpec>(manager, new ConnectedSubgraphSpec(graph, terminals));

            AssertSameEdgeSets(graph, actual, expected);
        }

        [Fact]
        public void SteinerTreesMatchesDirectSpecBuild()
        {
            Graph graph = Graph.Grid(3, 3);
            int[] terminals = { 0, 4, 8 };
            GraphSet actual = GraphSet.SteinerTrees(graph, terminals);

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd expected = FrontierBuilder.Build<SteinerTreeSpec>(manager, new SteinerTreeSpec(graph, terminals));

            AssertSameEdgeSets(graph, actual, expected);
        }

        [Fact]
        public void SteinerTreesMinWeightMatchesTheKnownM45MinimumSteinerTree()
        {
            // Same graph/weights/terminals as SteinerTreeSpecTests.MinWeightMatchesBruteForceMinimumSteinerTree
            // (M4-5, independently checked there against a brute-force minimum): the completion criterion
            // this reproduces asks that GraphSet.SteinerTrees agree with that already-verified minimum.
            Graph graph = Graph.Grid(2, 3);
            int[] weights = { 4, 1, 2, 5, 3, 2, 1 };
            Assert.Equal(graph.EdgeCount, weights.Length);
            int[] terminals = { 0, 2, 5 };

            var weightByEdge = new Dictionary<Edge, int>();
            for (int i = 0; i < weights.Length; i++)
            {
                weightByEdge[graph.GetEdge(i)] = weights[i];
            }

            GraphSet trees = GraphSet.SteinerTrees(graph, terminals);
            (IReadOnlySet<Edge> Set, int Weight) actual = trees.MinWeight(e => weightByEdge[e]);

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<SteinerTreeSpec>(manager, new SteinerTreeSpec(graph, terminals));
            int expectedWeight = built.MinWeight(weights).Weight;

            Assert.Equal(expectedWeight, actual.Weight);
        }

        [Fact]
        public void CutsMatchesDirectSpecBuild()
        {
            Graph graph = Graph.Grid(3, 3);
            int s = 0;
            int t = graph.VertexCount - 1;

            foreach (bool minimalOnly in new[] { false, true })
            {
                GraphSet actual = GraphSet.Cuts(graph, s, t, minimalOnly);

                using ZddManager manager = new ZddManager(graph.EdgeCount);
                Zdd expected = FrontierBuilder.Build<CutSpec>(manager, new CutSpec(graph, s, t, minimalOnly));

                AssertSameEdgeSets(graph, actual, expected);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void CutsMinWeightMatchesMaxFlowMinCutTheorem(int seed)
        {
            Graph graph = SpanningTreeSpecTests.RandomConnectedGraph(vertexCount: 6, extraEdgeProbability: 0.4, seed);
            var random = new Random(seed * 97 + 1);
            var weightByEdge = new Dictionary<Edge, int>();
            var weights = new int[graph.EdgeCount];
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = random.Next(1, 6);
                weightByEdge[graph.GetEdge(i)] = weights[i];
            }

            int s = 0;
            int t = graph.VertexCount - 1;

            GraphSet cuts = GraphSet.Cuts(graph, s, t);
            int minCutWeight = cuts.MinWeight(e => weightByEdge[e]).Weight;
            int maxFlow = NaiveMaxFlow(graph, s, t, weights);

            Assert.Equal(maxFlow, minCutWeight);
        }

        [Fact]
        public void DegreeConstrainedMatchesDirectSpecBuild()
        {
            Graph graph = Graph.Complete(5);
            int[] lo = { 1, 1, 1, 1, 1 };
            int[] hi = { 2, 2, 2, 2, 2 };

            GraphSet actualArray = GraphSet.DegreeConstrained(graph, lo, hi);
            GraphSet actualUniform = GraphSet.DegreeConstrained(graph, lo: 1, hi: 2);

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd expected = FrontierBuilder.Build<DegreeConstraintSpec>(manager, new DegreeConstraintSpec(graph, lo, hi));

            AssertSameEdgeSets(graph, actualArray, expected);
            AssertSameEdgeSets(graph, actualUniform, expected);
        }

        [Fact]
        public void EdgeCoversMatchesDirectSpecBuild()
        {
            Graph graph = Graph.Grid(3, 3);
            GraphSet actual = GraphSet.EdgeCovers(graph);

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd expected = FrontierBuilder.Build<DegreeConstraintSpec>(manager, new DegreeConstraintSpec(graph, lo: 1, hi: graph.EdgeCount));

            AssertSameEdgeSets(graph, actual, expected);
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        public void EdgeCoversMatchesBruteForceEdgeCoverEnumeration(string graphName)
        {
            Graph graph = SpanningTreeSpecTests.NamedGraph(graphName);
            GraphSet actual = GraphSet.EdgeCovers(graph);

            var expectedKeys = new HashSet<string>();
            int bound = 1 << graph.EdgeCount;
            for (int mask = 0; mask < bound; mask++)
            {
                var touched = new bool[graph.VertexCount];
                var edges = new List<Edge>();
                for (int i = 0; i < graph.EdgeCount; i++)
                {
                    if ((mask & (1 << i)) == 0)
                    {
                        continue;
                    }

                    Edge edge = graph.GetEdge(i);
                    edges.Add(edge);
                    touched[edge.U] = true;
                    touched[edge.V] = true;
                }

                if (Array.TrueForAll(touched, t => t))
                {
                    expectedKeys.Add(EdgeSetKey(edges));
                }
            }

            HashSet<string> actualKeys = actual.Select(EdgeSetKey).ToHashSet();
            Assert.Equal(expectedKeys, actualKeys);
        }

        [Fact]
        public void KnapsacksMatchesDirectSpecBuild()
        {
            Graph graph = Graph.Complete(5);
            int[] weights = { 2, 3, 4, 5, 9, 1, 6, 7, 8, 2 };
            Assert.Equal(graph.EdgeCount, weights.Length);
            const long capacity = 10;

            GraphSet actual = GraphSet.Knapsacks(graph, weights, capacity);

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd expected = FrontierBuilder.Build<KnapsackSpec, long>(manager, new KnapsackSpec(weights, capacity));

            AssertSameEdgeSets(graph, actual, expected);
        }

        [Fact]
        public void KnapsacksRejectsAWeightArrayOfTheWrongLength()
        {
            Graph graph = Graph.Complete(5);
            Assert.Throws<ArgumentException>(() => GraphSet.Knapsacks(graph, new[] { 1, 2, 3 }, capacity: 5));
        }

        [Fact]
        public void EdgeFamilyGeneratorsChainWithIncludingExcludingLargerSmallerAndCostAtMost()
        {
            Graph graph = Graph.Grid(3, 3);
            Edge include = graph.GetEdge(0);
            Edge exclude = graph.GetEdge(1);

            var generators = new (string Name, GraphSet Family)[]
            {
                ("ConnectedSubgraphs", GraphSet.ConnectedSubgraphs(graph, new[] { 0, 4, 8 })),
                ("SteinerTrees", GraphSet.SteinerTrees(graph, new[] { 0, 4, 8 })),
                ("Cuts", GraphSet.Cuts(graph, 0, graph.VertexCount - 1)),
                ("DegreeConstrained", GraphSet.DegreeConstrained(graph, lo: 0, hi: 2)),
                ("EdgeCovers", GraphSet.EdgeCovers(graph)),
                ("Knapsacks", GraphSet.Knapsacks(graph, Enumerable.Repeat(1, graph.EdgeCount).ToArray(), capacity: graph.EdgeCount)),
            };

            foreach ((string name, GraphSet family) in generators)
            {
                GraphSet chained = family.Including(include).Excluding(exclude).Larger(0).Smaller(graph.EdgeCount + 1).CostAtMost(e => 1, graph.EdgeCount);

                Zdd expected = family.Zdd
                    .SupersetsOf(family.Zdd.Manager.Singleton(graph.EdgeIndexToVariableIndex(0)))
                    .OffSet(graph.EdgeIndexToVariableIndex(1));

                Assert.True(
                    chained.Zdd.IsSubsetOf(expected),
                    $"{name}: chained filters kept an edge set that violates Including/Excluding.");
                Assert.All(chained, set =>
                {
                    Assert.Contains(include, set);
                    Assert.DoesNotContain(exclude, set);
                });
            }
        }

        private static int NaiveMaxFlow(Graph graph, int s, int t, int[] capacities)
        {
            int n = graph.VertexCount;
            var capacity = new int[n, n];

            for (int i = 0; i < graph.EdgeCount; i++)
            {
                Edge edge = graph.GetEdge(i);
                capacity[edge.U, edge.V] += capacities[i];
                capacity[edge.V, edge.U] += capacities[i];
            }

            int totalFlow = 0;
            while (true)
            {
                var parent = new int[n];
                Array.Fill(parent, -1);
                parent[s] = s;
                var queue = new Queue<int>();
                queue.Enqueue(s);

                while (queue.Count > 0 && parent[t] == -1)
                {
                    int u = queue.Dequeue();
                    for (int v = 0; v < n; v++)
                    {
                        if (parent[v] == -1 && capacity[u, v] > 0)
                        {
                            parent[v] = u;
                            queue.Enqueue(v);
                        }
                    }
                }

                if (parent[t] == -1)
                {
                    break;
                }

                int bottleneck = int.MaxValue;
                for (int v = t; v != s; v = parent[v])
                {
                    bottleneck = Math.Min(bottleneck, capacity[parent[v], v]);
                }

                for (int v = t; v != s; v = parent[v])
                {
                    capacity[parent[v], v] -= bottleneck;
                    capacity[v, parent[v]] += bottleneck;
                }

                totalFlow += bottleneck;
            }

            return totalFlow;
        }

        // ---- ToDot（M5-4、issue #56）----

        [Fact]
        public void ToDotLabelsEachLevelByItsEdgeByDefault()
        {
            Graph graph = Graph.Grid(2, 2);
            GraphSet paths = GraphSet.Paths(graph, from: 0, to: graph.VertexCount - 1);

            string dot = paths.ToDot();

            Assert.True(paths.Zdd.NodeCount > 0);

            // 既定のアイテム番号ラベル（x0, x1, ...）は使われない。
            Assert.DoesNotContain("label=\"x", dot, StringComparison.Ordinal);

            // 実際に使われている辺のどれかは "(u, v)" の形でラベルに出ている。
            Assert.Contains(graph.Edges, edge => dot.Contains($"label=\"{edge}\"", StringComparison.Ordinal));

            DotSyntax.Validate(dot);
        }

        [Fact]
        public void ToDotHonorsAnExplicitLevelLabelInsteadOfTheDefaultEdgeOne()
        {
            Graph graph = Graph.Grid(2, 2);
            GraphSet paths = GraphSet.Paths(graph, from: 0, to: graph.VertexCount - 1);

            string dot = paths.ToDot(new DotOptions { LevelLabel = item => $"e{item}" });

            Assert.Contains("label=\"e", dot, StringComparison.Ordinal);
            Assert.DoesNotContain(paths.Graph.Edges.Select(e => e.ToString()), edgeText => dot.Contains($"label=\"{edgeText}\"", StringComparison.Ordinal));
        }

        [Fact]
        public void GraphSetWriteDotProducesTheSameTextAsToDot()
        {
            Graph graph = Graph.Grid(2, 2);
            GraphSet paths = GraphSet.Paths(graph, from: 0, to: graph.VertexCount - 1);

            using StringWriter writer = new StringWriter();
            paths.WriteDot(writer);

            Assert.Equal(paths.ToDot(), writer.ToString());
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
