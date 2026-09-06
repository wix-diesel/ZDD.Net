using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Graphs
{
    /// <summary>
    /// M7-6 completion criteria for <see cref="DirectedGraphSet"/>: reproduces the same tutorial-style
    /// scenario <see cref="GraphSetTests"/> covers for <see cref="GraphSet"/> (chained filters, sampling,
    /// min/max iteration), but over a <see cref="DirectedGraph"/>; every generator matches building the
    /// corresponding spec directly; filter chains (<c>Including(edge).Smaller(n).CostAtMost(...)</c>) are
    /// genuinely applied during the frontier walk, not as a post-hoc intersection; <c>MinIter</c>/
    /// <c>MaxIter</c>/<c>RandIter</c>/<c>TopK</c>/<c>Sample</c> behave like <see cref="GraphSet"/>'s; <c>ToDot</c>
    /// renders as a <c>digraph</c>; and <c>Bidirected(grid).Paths(...).Count</c> matches OEIS A007764.
    /// </summary>
    public class DirectedGraphSetTests
    {
        // OEIS A007764: an undirected simple s-t path has exactly one orientation starting at "from", so
        // the directed count over the bidirected grid equals the undirected count (M7-3's own criterion).
        [Theory]
        [InlineData(2, "2")]
        [InlineData(3, "12")]
        [InlineData(4, "184")]
        [InlineData(5, "8512")]
        [InlineData(6, "1262816")]
        public void PathsCountMatchesOeisA007764ForBidirectedDiagonalGridPaths(int n, string expected)
        {
            DirectedGraph grid = DirectedGraph.Bidirected(Graph.Grid(n, n));
            DirectedGraphSet paths = DirectedGraphSet.Paths(grid, from: 0, to: grid.VertexCount - 1);

            Assert.Equal(BigInteger.Parse(expected), paths.Count);
        }

        [Fact]
        public void TutorialScenarioReproducesForABidirectedGrid()
        {
            DirectedGraph grid = DirectedGraph.Bidirected(Graph.Grid(6, 6));
            DirectedGraphSet paths = DirectedGraphSet.Paths(grid, from: 0, to: grid.VertexCount - 1);

            Assert.Equal(BigInteger.Parse("1262816"), paths.Count);

            (IReadOnlySet<DirectedEdge> Set, int Weight) shortest = paths.MinWeight(e => 1);
            Assert.Equal(10, shortest.Weight); // shortest corner-to-corner path on a 6x6 grid: 5 + 5 arcs

            IReadOnlySet<DirectedEdge> sample = paths.Sample(new Random(42));
            Assert.True(paths.Contains(sample));

            DirectedEdge firstArc = grid.GetEdge(0);
            DirectedGraphSet through = paths.Including(firstArc);
            DirectedGraphSet avoiding = paths.Excluding(firstArc);
            Assert.Equal(paths.Count, through.Count + avoiding.Count);
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("bidirectedCycle5")]
        [InlineData("complete4")]
        public void PathsMatchesDirectDirectedPathSpecBuild(string graphName)
        {
            DirectedGraph graph = NamedGraph(graphName);

            for (int s = 0; s < graph.VertexCount; s++)
            {
                for (int t = 0; t < graph.VertexCount; t++)
                {
                    if (s == t)
                    {
                        continue;
                    }

                    DirectedGraphSet actual = DirectedGraphSet.Paths(graph, s, t);

                    using ZddManager manager = new ZddManager(graph.EdgeCount);
                    Zdd expected = FrontierBuilder.Build<DirectedPathSpec>(manager, new DirectedPathSpec(graph, s, t));

                    AssertSameArcSets(graph, actual, expected);
                }
            }
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("bidirectedCycle5")]
        [InlineData("complete4")]
        public void CyclesMatchesDirectDirectedCycleSpecBuild(string graphName)
        {
            DirectedGraph graph = NamedGraph(graphName);
            DirectedGraphSet actual = DirectedGraphSet.Cycles(graph);

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd expected = FrontierBuilder.Build<DirectedCycleSpec>(manager, new DirectedCycleSpec(graph));

            AssertSameArcSets(graph, actual, expected);
        }

        [Fact]
        public void HamiltonianPathsAndCyclesMatchDirectSpecBuild()
        {
            DirectedGraph graph = DirectedGraph.Complete(5);

            DirectedGraphSet actualPaths = DirectedGraphSet.HamiltonianPaths(graph, 0, 4);
            using (ZddManager manager = new ZddManager(graph.EdgeCount))
            {
                Zdd expected = FrontierBuilder.Build<DirectedHamiltonianPathSpec>(manager, new DirectedHamiltonianPathSpec(graph, 0, 4));
                AssertSameArcSets(graph, actualPaths, expected);
            }

            DirectedGraphSet actualCycles = DirectedGraphSet.HamiltonianCycles(graph);
            using (ZddManager manager = new ZddManager(graph.EdgeCount))
            {
                Zdd expected = FrontierBuilder.Build<DirectedHamiltonianCycleSpec>(manager, new DirectedHamiltonianCycleSpec(graph));
                AssertSameArcSets(graph, actualCycles, expected);

                // (n-1)! directed Hamiltonian cycles on K_n, up to the choice of starting vertex.
                Assert.Equal(new BigInteger(24), actualCycles.Count);
            }
        }

        [Fact]
        public void ArborescencesMatchesDirectArborescenceSpecBuildAndTuttesTheorem()
        {
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Complete(5));
            DirectedGraphSet actual = DirectedGraphSet.Arborescences(graph, root: 0);

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd expected = FrontierBuilder.Build<ArborescenceSpec>(manager, new ArborescenceSpec(graph, root: 0));

            AssertSameArcSets(graph, actual, expected);
            Assert.Equal(Kirchhoff.CountArborescences(graph, root: 0), actual.Count);
        }

        [Fact]
        public void DegreeConstrainedMatchesDirectDegreeConstraintSpecBuild()
        {
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Complete(5));
            int[] inLo = Enumerable.Repeat(1, graph.VertexCount).ToArray();
            int[] inHi = Enumerable.Repeat(1, graph.VertexCount).ToArray();
            int[] outLo = Enumerable.Repeat(1, graph.VertexCount).ToArray();
            int[] outHi = Enumerable.Repeat(1, graph.VertexCount).ToArray();

            DirectedGraphSet actual = DirectedGraphSet.DegreeConstrained(graph, inLo, inHi, outLo, outHi);

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd expected = FrontierBuilder.Build<DirectedDegreeConstraintSpec>(
                manager, new DirectedDegreeConstraintSpec(graph, inLo, inHi, outLo, outHi));

            AssertSameArcSets(graph, actual, expected);

            // Every member is a disjoint union of directed cycles (in-degree 1, out-degree 1 everywhere).
            Assert.NotEmpty(actual.ToList());
        }

        [Fact]
        public void IncludingExcludingEdgeMatchPostHocFilteringExactly()
        {
            DirectedGraph grid = DirectedGraph.Bidirected(Graph.Grid(4, 4));
            DirectedGraphSet basePaths = DirectedGraphSet.Paths(grid, 0, grid.VertexCount - 1);
            DirectedEdge arc = grid.GetEdge(3);

            DirectedGraphSet including = basePaths.Including(arc);
            DirectedGraphSet excluding = basePaths.Excluding(arc);

            int item = grid.EdgeIndexToVariableIndex(3);
            Zdd postHocIncluding = basePaths.Zdd.SupersetsOf(basePaths.Zdd.Manager.Singleton(item));
            Zdd postHocExcluding = basePaths.Zdd.OffSet(item);

            Assert.Equal(postHocIncluding, including.Zdd);
            Assert.Equal(postHocExcluding, excluding.Zdd);

            Assert.All(including, set => Assert.Contains(arc, set));
            Assert.All(excluding, set => Assert.DoesNotContain(arc, set));
        }

        [Fact]
        public void IncludingExcludingVertexMatchPostHocFilteringExactly()
        {
            DirectedGraph grid = DirectedGraph.Bidirected(Graph.Grid(4, 4));
            DirectedGraphSet basePaths = DirectedGraphSet.Paths(grid, 0, grid.VertexCount - 1);
            int vertex = 5;

            DirectedGraphSet including = basePaths.Including(vertex);
            DirectedGraphSet excluding = basePaths.Excluding(vertex);

            bool Touches(IReadOnlySet<DirectedEdge> set) => set.Any(e => e.From == vertex || e.To == vertex);

            Assert.All(including, set => Assert.True(Touches(set)));
            Assert.All(excluding, set => Assert.False(Touches(set)));

            // Every path either touches the vertex or does not: the two filters partition the family.
            Assert.Equal(basePaths.Count, including.Count + excluding.Count);
        }

        [Fact]
        public void LargerSmallerLenEqualsMatchPostHocCardinalityFilteringExactly()
        {
            DirectedGraph grid = DirectedGraph.Bidirected(Graph.Grid(4, 4));
            DirectedGraphSet basePaths = DirectedGraphSet.Paths(grid, 0, grid.VertexCount - 1);
            ZddManager manager = basePaths.Universe.Manager;

            DirectedGraphSet larger = basePaths.Larger(8);
            DirectedGraphSet smaller = basePaths.Smaller(8);
            DirectedGraphSet exact = basePaths.LenEquals(8);

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
        public void CostAtMostCostAtLeastCostEqualsMatchPostHocWeightFilteringExactly()
        {
            DirectedGraph grid = DirectedGraph.Bidirected(Graph.Grid(4, 4));
            DirectedGraphSet basePaths = DirectedGraphSet.Paths(grid, 0, grid.VertexCount - 1);
            ZddManager manager = basePaths.Universe.Manager;

            long[] costs = new long[grid.EdgeCount];
            var costByArc = new Dictionary<DirectedEdge, long>();
            for (int i = 0; i < costs.Length; i++)
            {
                costs[i] = i % 2 == 0 ? i + 1 : -(i + 1);
                costByArc[grid.GetEdge(i)] = costs[i];
            }

            const long bound = 3;
            DirectedGraphSet atMost = basePaths.CostAtMost(e => costByArc[e], bound);
            DirectedGraphSet atLeast = basePaths.CostAtLeast(e => costByArc[e], bound);
            DirectedGraphSet equals = basePaths.CostEquals(e => costByArc[e], bound);

            LinearConstraintSpec specAtMost = new LinearConstraintSpec(costs, LinearConstraintOperator.LessOrEqual, bound);
            LinearConstraintSpec specAtLeast = new LinearConstraintSpec(costs, LinearConstraintOperator.GreaterOrEqual, bound);
            LinearConstraintSpec specEquals = new LinearConstraintSpec(costs, LinearConstraintOperator.Equal, bound);

            Zdd postHocAtMost = basePaths.Zdd.Intersect(FrontierBuilder.Build<LinearConstraintSpec, long>(manager, specAtMost));
            Zdd postHocAtLeast = basePaths.Zdd.Intersect(FrontierBuilder.Build<LinearConstraintSpec, long>(manager, specAtLeast));
            Zdd postHocEquals = basePaths.Zdd.Intersect(FrontierBuilder.Build<LinearConstraintSpec, long>(manager, specEquals));

            Assert.Equal(postHocAtMost, atMost.Zdd);
            Assert.Equal(postHocAtLeast, atLeast.Zdd);
            Assert.Equal(postHocEquals, equals.Zdd);

            Assert.All(atMost, set => Assert.True(TotalCost(set, costByArc) <= bound));
            Assert.All(atLeast, set => Assert.True(TotalCost(set, costByArc) >= bound));
            Assert.All(equals, set => Assert.Equal(bound, TotalCost(set, costByArc)));
        }

        [Fact]
        public void ChainedFiltersComposeCorrectly()
        {
            DirectedGraph grid = DirectedGraph.Bidirected(Graph.Grid(4, 4));
            DirectedGraphSet basePaths = DirectedGraphSet.Paths(grid, 0, grid.VertexCount - 1);
            DirectedEdge include = grid.GetEdge(0);
            DirectedEdge exclude = grid.GetEdge(3);

            DirectedGraphSet chained = basePaths.Including(include).Excluding(exclude).Smaller(20);

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
            DirectedGraph grid = DirectedGraph.Bidirected(Graph.Grid(6, 6));
            var baseSpec = new ArraySpecErased<DirectedPathSpec>(new DirectedPathSpec(grid, 0, grid.VertexCount - 1));
            var filterSpec = new ArraySpecErased<DirectedEdgeMembershipSpec>(new DirectedEdgeMembershipSpec(grid, grid.EdgeCount / 2, require: false));
            var combinedSpec = new AndErasedSpec(baseSpec, filterSpec);

            long constructionTimeNodeCount = TopDownExpander<ErasedGraphDdSpec, object?>
                .Expand(new ErasedGraphDdSpec(combinedSpec)).NodeCount;

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
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Complete(5));
            DirectedGraphSet arborescences = DirectedGraphSet.Arborescences(graph, root: 0);
            var random = new Random(11);
            Dictionary<DirectedEdge, int> weight = graph.Edges.ToDictionary(e => e, _ => random.Next(1, 50));

            int Weight(IReadOnlySet<DirectedEdge> set) => set.Sum(e => weight[e]);

            List<IReadOnlySet<DirectedEdge>> ascending = arborescences.MinIter(e => weight[e]).ToList();
            List<IReadOnlySet<DirectedEdge>> descending = arborescences.MaxIter(e => weight[e]).ToList();

            Assert.Equal((int)arborescences.Count, ascending.Count);
            Assert.Equal((int)arborescences.Count, descending.Count);

            Assert.Equal(Weight(ascending[0]), arborescences.MinWeight(e => weight[e]).Weight);
            Assert.Equal(Weight(descending[0]), arborescences.MaxWeight(e => weight[e]).Weight);

            for (int i = 1; i < ascending.Count; i++)
            {
                Assert.True(Weight(ascending[i - 1]) <= Weight(ascending[i]));
            }

            for (int i = 1; i < descending.Count; i++)
            {
                Assert.True(Weight(descending[i - 1]) >= Weight(descending[i]));
            }
        }

        [Fact]
        public void TopKMatchesTheKHighestWeightMembers()
        {
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Complete(5));
            DirectedGraphSet arborescences = DirectedGraphSet.Arborescences(graph, root: 0);
            var random = new Random(7);
            Dictionary<DirectedEdge, int> weight = graph.Edges.ToDictionary(e => e, _ => random.Next(1, 50));

            (IReadOnlySet<DirectedEdge> Set, int Weight)[] top3 = arborescences.TopK(e => weight[e], 3);
            List<IReadOnlySet<DirectedEdge>> descending = arborescences.MaxIter(e => weight[e]).Take(3).ToList();

            Assert.Equal(3, top3.Length);
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(descending[i].Sum(e => weight[e]), top3[i].Weight);
            }
        }

        [Fact]
        public void RandIterIsUniformEnoughToPassAChiSquaredTest()
        {
            const int Draws = 20_000;

            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Cycle(5));
            DirectedGraphSet cycles = DirectedGraphSet.Cycles(graph); // one directed 5-cycle per orientation

            int categories = (int)cycles.Count;
            Assert.True(categories > 1);

            var observed = new Dictionary<string, int>();
            foreach (IReadOnlySet<DirectedEdge> set in cycles.RandIter(new Random(4242)).Take(Draws))
            {
                string key = ArcSetKey(set);
                observed[key] = observed.GetValueOrDefault(key) + 1;
            }

            Assert.Equal(categories, observed.Count);

            double expected = (double)Draws / categories;
            double chiSquared = observed.Values.Sum(count => ((count - expected) * (count - expected)) / expected);

            double threshold = categories * 3.0 + 20.0;
            Assert.True(
                chiSquared < threshold,
                $"The chi-squared statistic was {chiSquared:F2} over {categories} categories, which suggests RandIter is biased.");
        }

        [Fact]
        public void SampleElementAtAndIndexOfRoundTrip()
        {
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Complete(4));
            DirectedGraphSet arborescences = DirectedGraphSet.Arborescences(graph, root: 0);

            IReadOnlySet<DirectedEdge> sample = arborescences.Sample(new Random(1));
            Assert.True(arborescences.Contains(sample));

            BigInteger rank = arborescences.IndexOf(sample);
            Assert.True(rank >= 0 && rank < arborescences.Count);
            Assert.Equal(sample, arborescences.ElementAt(rank));

            IReadOnlySet<DirectedEdge>[] samples = arborescences.Sample(5, new Random(2));
            Assert.Equal(5, samples.Length);
            Assert.All(samples, s => Assert.True(arborescences.Contains(s)));
        }

        [Fact]
        public void ProbabilityMatchesBruteForceOverASmallFamily()
        {
            DirectedGraph graph = DirectedGraph.Path(3); // arcs: 0->1, 1->2
            DirectedGraphSet paths = DirectedGraphSet.Paths(graph, 0, 2, allowAnyEndpoints: true);

            var p = new Dictionary<DirectedEdge, double>
            {
                [graph.GetEdge(0)] = 0.5,
                [graph.GetEdge(1)] = 0.25,
            };

            double actual = paths.Probability(e => p[e]);

            // Brute force over all 4 subsets of {arc0, arc1}, weighting by inclusion probability.
            double expected = 0.0;
            for (int mask = 0; mask < 4; mask++)
            {
                bool has0 = (mask & 1) != 0;
                bool has1 = (mask & 2) != 0;
                var set = new List<DirectedEdge>();
                if (has0)
                {
                    set.Add(graph.GetEdge(0));
                }

                if (has1)
                {
                    set.Add(graph.GetEdge(1));
                }

                if (paths.Contains(set))
                {
                    double weight = (has0 ? p[graph.GetEdge(0)] : 1 - p[graph.GetEdge(0)]) *
                        (has1 ? p[graph.GetEdge(1)] : 1 - p[graph.GetEdge(1)]);
                    expected += weight;
                }
            }

            Assert.Equal(expected, actual, precision: 9);
        }

        [Fact]
        public void EqualityComparesByMemberArcSetsOverTheSameUniverse()
        {
            // GraphSet.Equals (and this type's) requires the exact same Universe instance, so this
            // compares filters chained off one shared base rather than two independent generator calls
            // (which would each build their own universe and never compare equal).
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Complete(4));
            DirectedGraphSet basePaths = DirectedGraphSet.Paths(graph, 0, 3);
            DirectedEdge arc = graph.GetEdge(0);

            DirectedGraphSet a = basePaths.Including(arc);
            DirectedGraphSet b = basePaths.Including(arc);
            DirectedGraphSet c = basePaths.Excluding(arc);

            Assert.True(a.Equals(a));
            Assert.True(a.Equals(b));
            Assert.True(a == b);
            Assert.False(a == c);
            Assert.True(a != c);
        }

        // ---- ToDot ----

        [Fact]
        public void ToDotRendersAsADigraphAndLabelsEachLevelByItsArc()
        {
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Grid(2, 2));
            DirectedGraphSet paths = DirectedGraphSet.Paths(graph, from: 0, to: graph.VertexCount - 1);

            string dot = paths.ToDot();

            Assert.StartsWith("digraph", dot, StringComparison.Ordinal);
            Assert.DoesNotContain("label=\"x", dot, StringComparison.Ordinal);
            Assert.Contains(graph.Edges, arc => dot.Contains($"label=\"{arc}\"", StringComparison.Ordinal));

            DotSyntax.Validate(dot);
        }

        [Fact]
        public void WriteDotProducesTheSameTextAsToDot()
        {
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Grid(2, 2));
            DirectedGraphSet paths = DirectedGraphSet.Paths(graph, from: 0, to: graph.VertexCount - 1);

            using System.IO.StringWriter writer = new System.IO.StringWriter();
            paths.WriteDot(writer);

            Assert.Equal(paths.ToDot(), writer.ToString());
        }

        // ---- Helpers ----

        private static DirectedGraph NamedGraph(string name) => name switch
        {
            "path4" => DirectedGraph.Path(4),
            "bidirectedCycle5" => DirectedGraph.Bidirected(Graph.Cycle(5)),
            "complete4" => DirectedGraph.Complete(4),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

        private static long TotalCost(IReadOnlySet<DirectedEdge> set, IReadOnlyDictionary<DirectedEdge, long> costByArc)
        {
            long sum = 0;

            foreach (DirectedEdge arc in set)
            {
                sum += costByArc[arc];
            }

            return sum;
        }

        private static void AssertSameArcSets(DirectedGraph graph, DirectedGraphSet actual, Zdd expected)
        {
            HashSet<string> actualKeys = actual.Select(ArcSetKey).ToHashSet();

            var expectedKeys = new HashSet<string>();
            foreach (int[] items in expected.Sets())
            {
                DirectedEdge[] arcs = items.Select(graph.GetEdge).ToArray();
                expectedKeys.Add(ArcSetKey(arcs));
            }

            Assert.Equal(expectedKeys, actualKeys);
        }

        private static string ArcSetKey(IEnumerable<DirectedEdge> set) =>
            string.Join(";", set.Select(e => $"{e.From}->{e.To}").OrderBy(s => s, StringComparer.Ordinal));
    }
}
