using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Graphs;
using ZDD.Net.Sets;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Graphs
{
    /// <summary>
    /// M8-1 completion criteria for <see cref="GraphSet"/>'s and <see cref="DirectedGraphSet"/>'s
    /// factories: <c>FromSets</c> matches <see cref="SetSet{T}.FromSets(SetUniverse{T}, IEnumerable{IEnumerable{T}})"/>
    /// over every family of a small graph, <c>Empty</c> / <c>PowerSet</c> / <c>FromZdd</c> build what
    /// their names say, every filter still composes with a factory-built family, and an edge or item
    /// index that is not the graph's is rejected by name.
    /// </summary>
    public class GraphSetFactoriesTests
    {
        // ==================== FromSets ====================

        [Fact]
        public void FromSetsMatchesSetSetFromSetsForEveryFamilyOfAThreeEdgeGraph()
        {
            Graph graph = Graph.Path(4); // 3 edges, so 2^(2^3) = 256 families

            foreach (BruteForceFamily family in FamilyCases.AllFamilies(graph.EdgeCount))
            {
                List<Edge[]> edgeSets = ToEdgeSets(graph, family);

                GraphSet actual = GraphSet.FromSets(graph, edgeSets);
                SetSet<Edge> expected = SetSet<Edge>.FromSets(new SetUniverse<Edge>(graph.Edges), edgeSets);

                Assert.Equal(expected.Count, actual.Count);
                Assert.Equal(family.Masks, Masks(graph, actual));
                Assert.Equal(
                    ZddFamilies.ToBruteForce(expected.Zdd),
                    ZddFamilies.ToBruteForce(actual.Zdd));
            }
        }

        [Fact]
        public void FromSetsMatchesSetSetFromSetsOnRandomFamiliesOfALargerGraph()
        {
            Graph graph = Graph.Complete(4); // 6 edges

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(graph.EdgeCount, count: 20, seed: 8_001))
            {
                List<Edge[]> edgeSets = ToEdgeSets(graph, family);

                GraphSet actual = GraphSet.FromSets(graph, edgeSets);
                SetSet<Edge> expected = SetSet<Edge>.FromSets(new SetUniverse<Edge>(graph.Edges), edgeSets);

                Assert.Equal(expected.Count, actual.Count);
                Assert.Equal(family.Masks, Masks(graph, actual));
            }
        }

        [Fact]
        public void FromSetsReadsBackTheGraphillionStyleExample()
        {
            Graph graph = Graph.Path(4); // (0,1), (1,2), (2,3)
            GraphSet gs = GraphSet.FromSets(graph, new[]
            {
                new[] { new Edge(1, 2), new Edge(2, 3) },
                new[] { new Edge(0, 1) },
            });

            Assert.Equal(new BigInteger(2), gs.Count);
            Assert.True(gs.Contains(new[] { new Edge(2, 3), new Edge(1, 2) })); // order-independent
            Assert.True(gs.Contains(new[] { new Edge(0, 1) }));
            Assert.False(gs.Contains(new[] { new Edge(1, 2) }));
            Assert.Same(graph, gs.Graph);
        }

        [Fact]
        public void FromSetsCollapsesDuplicateEdgesAndDuplicateSets()
        {
            Graph graph = Graph.Path(4);
            GraphSet gs = GraphSet.FromSets(graph, new[]
            {
                new[] { new Edge(0, 1), new Edge(0, 1) },
                new[] { new Edge(1, 0) },
                new[] { new Edge(0, 1) },
            });

            Assert.Equal(BigInteger.One, gs.Count);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void FromSetsBuildsTheEmptyFamilyAndTheEmptySetFamily(int setCount)
        {
            Graph graph = Graph.Path(4);
            IEnumerable<Edge>[] edgeSets = Enumerable.Repeat(Array.Empty<Edge>(), setCount).ToArray();

            GraphSet gs = GraphSet.FromSets(graph, edgeSets);

            Assert.Equal(new BigInteger(setCount), gs.Count); // {} vs {∅}
        }

        [Fact]
        public void FromSetsRejectsAnEdgeOutsideTheGraphNamingIt()
        {
            Graph graph = Graph.Path(4);
            var outside = new Edge(0, 3);

            ArgumentException ex = Assert.Throws<ArgumentException>(() => GraphSet.FromSets(graph, new[]
            {
                new[] { new Edge(0, 1) },
                new[] { outside },
            }));

            Assert.Contains(outside.ToString(), ex.Message);
            Assert.Equal("edgeSets", ex.ParamName);
        }

        [Fact]
        public void FromSetsRejectsNullArguments()
        {
            Graph graph = Graph.Path(4);

            Assert.Throws<ArgumentNullException>(() => GraphSet.FromSets(null!, Array.Empty<Edge[]>()));
            Assert.Throws<ArgumentNullException>(() => GraphSet.FromSets(graph, null!));
            Assert.Throws<ArgumentNullException>(() => GraphSet.FromSets(graph, new IEnumerable<Edge>[] { null! }));
        }

        // ==================== Empty / PowerSet ====================

        [Fact]
        public void EmptyHasNoMembers()
        {
            Graph graph = Graph.Grid(3, 3);
            GraphSet gs = GraphSet.Empty(graph);

            Assert.True(gs.IsEmpty);
            Assert.Equal(BigInteger.Zero, gs.Count);
            Assert.Empty(gs);
            Assert.Same(graph, gs.Graph);
            Assert.Equal(graph.EdgeCount, gs.Universe.Count);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(4)]
        [InlineData(10)]
        public void PowerSetCountsEverySubsetOfTheEdges(int edgeCount)
        {
            Graph graph = GraphWithEdgeCount(edgeCount);
            GraphSet gs = GraphSet.PowerSet(graph);

            Assert.Equal(BigInteger.Pow(2, edgeCount), gs.Count);
            Assert.False(gs.IsEmpty);
        }

        [Fact]
        public void PowerSetEnumeratesExactlyTheSubsetsOfTheEdges()
        {
            Graph graph = Graph.Path(5); // 4 edges

            Assert.Equal(
                BruteForceFamily.PowerSet(graph.EdgeCount).Masks,
                Masks(graph, GraphSet.PowerSet(graph)));
        }

        // ==================== Filters over factory-built families ====================

        [Fact]
        public void FiltersComposeWithFactoryBuiltFamilies()
        {
            Graph graph = Graph.Path(5); // 4 edges
            Edge first = graph.GetEdge(0);
            Edge last = graph.GetEdge(graph.EdgeCount - 1);

            var families = new List<BruteForceFamily> { BruteForceFamily.PowerSet(graph.EdgeCount) };
            families.AddRange(FamilyCases.RandomFamilies(graph.EdgeCount, count: 8, seed: 8_002));

            foreach (BruteForceFamily family in families)
            {
                GraphSet gs = GraphSet.FromSets(graph, ToEdgeSets(graph, family));

                AssertFilter(graph, family, gs.Including(first), mask => Has(mask, graph, first));
                AssertFilter(graph, family, gs.Excluding(first), mask => !Has(mask, graph, first));
                AssertFilter(graph, family, gs.Including(1), mask => TouchesVertex(mask, graph, 1));
                AssertFilter(graph, family, gs.Excluding(1), mask => !TouchesVertex(mask, graph, 1));
                AssertFilter(graph, family, gs.Larger(1), mask => BitOperations.PopCount((uint)mask) > 1);
                AssertFilter(graph, family, gs.Smaller(3), mask => BitOperations.PopCount((uint)mask) < 3);
                AssertFilter(graph, family, gs.LenEquals(2), mask => BitOperations.PopCount((uint)mask) == 2);

                AssertFilter(
                    graph,
                    family,
                    gs.Including(first).Excluding(last).Smaller(3),
                    mask => Has(mask, graph, first) && !Has(mask, graph, last) && BitOperations.PopCount((uint)mask) < 3);
            }
        }

        [Fact]
        public void FiltersComposeWithAnEmptyFactoryBuiltFamily()
        {
            Graph graph = Graph.Path(5);

            Assert.True(GraphSet.Empty(graph).Including(graph.GetEdge(0)).LenEquals(1).IsEmpty);
        }

        // ==================== FromZdd ====================

        [Fact]
        public void FromZddReadsBackEveryFamilyBuiltOverAMatchingManager()
        {
            Graph graph = Graph.Path(4); // 3 edges
            var universe = new SetUniverse<Edge>(graph.Edges);

            foreach (BruteForceFamily family in FamilyCases.AllFamilies(graph.EdgeCount))
            {
                GraphSet gs = GraphSet.FromZdd(graph, ZddFamilies.Build(universe.Manager, family));

                Assert.Equal(new BigInteger(family.Count), gs.Count);
                Assert.Equal(family.Masks, Masks(graph, gs));
            }
        }

        [Fact]
        public void FromZddReadsBackAFamilyBuiltOverAManagerWithSpareVariables()
        {
            Graph graph = Graph.Path(5); // 4 edges
            using var manager = new ZddManager(graph.EdgeCount + 3);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(graph.EdgeCount, count: 10, seed: 8_003))
            {
                // The same masks, declared over the wider manager: every item still is an edge index.
                BruteForceFamily widened = BruteForceFamily.FromMasks(manager.VariableCount, family.Masks);
                GraphSet gs = GraphSet.FromZdd(graph, ZddFamilies.Build(manager, widened));

                Assert.Equal(new BigInteger(family.Count), gs.Count);
                Assert.Equal(family.Masks, Masks(graph, gs));
            }
        }

        [Fact]
        public void FromZddRoundTripsAGeneratorBuiltFamilyAndKeepsFiltersWorking()
        {
            Graph grid = Graph.Grid(3, 3);
            GraphSet paths = GraphSet.Paths(grid, from: 0, to: 8);
            Edge edge = grid.GetEdge(0);

            GraphSet reread = GraphSet.FromZdd(grid, paths.Zdd);

            Assert.Equal(paths.Count, reread.Count);
            Assert.Equal(Masks(grid, paths), Masks(grid, reread));
            Assert.Equal(paths.Including(edge).Count, reread.Including(edge).Count);
            Assert.Equal(paths.LenEquals(4).Count, reread.LenEquals(4).Count);
        }

        [Fact]
        public void FromZddRejectsAManagerWithTooFewVariables()
        {
            Graph graph = Graph.Path(5); // 4 edges
            using var manager = new ZddManager(graph.EdgeCount - 1);

            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => GraphSet.FromZdd(graph, manager.Singleton(0)));

            Assert.Equal("zdd", ex.ParamName);
            Assert.Contains(graph.EdgeCount.ToString(), ex.Message);
        }

        [Fact]
        public void FromZddRejectsASetUsingAnItemThatIsNotAnEdgeIndex()
        {
            Graph graph = Graph.Path(5); // 4 edges
            using var manager = new ZddManager(graph.EdgeCount + 1);

            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => GraphSet.FromZdd(graph, manager.Singleton(graph.EdgeCount)));

            Assert.Equal("zdd", ex.ParamName);
            Assert.Contains($"item index {graph.EdgeCount}", ex.Message);
        }

        [Fact]
        public void FromZddRejectsANullGraph()
        {
            using var manager = new ZddManager(2);
            Assert.Throws<ArgumentNullException>(() => GraphSet.FromZdd(null!, manager.Empty));
        }

        // ==================== DirectedGraphSet ====================

        [Fact]
        public void DirectedFromSetsMatchesSetSetFromSetsForEveryFamilyOfAThreeArcGraph()
        {
            DirectedGraph graph = DirectedGraph.Cycle(3); // 0->1, 1->2, 2->0

            foreach (BruteForceFamily family in FamilyCases.AllFamilies(graph.EdgeCount))
            {
                List<DirectedEdge[]> edgeSets = ToEdgeSets(graph, family);

                DirectedGraphSet actual = DirectedGraphSet.FromSets(graph, edgeSets);
                SetSet<DirectedEdge> expected =
                    SetSet<DirectedEdge>.FromSets(new SetUniverse<DirectedEdge>(graph.Edges), edgeSets);

                Assert.Equal(expected.Count, actual.Count);
                Assert.Equal(family.Masks, Masks(graph, actual));
            }
        }

        [Fact]
        public void DirectedFromSetsDistinguishesArcDirection()
        {
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Path(3)); // 0->1, 1->0, 1->2, 2->1
            DirectedGraphSet gs = DirectedGraphSet.FromSets(graph, new[] { new[] { new DirectedEdge(0, 1) } });

            Assert.True(gs.Contains(new[] { new DirectedEdge(0, 1) }));
            Assert.False(gs.Contains(new[] { new DirectedEdge(1, 0) }));
        }

        [Fact]
        public void DirectedFromSetsRejectsAnArcOutsideTheGraphNamingIt()
        {
            DirectedGraph graph = DirectedGraph.Cycle(3);
            var outside = new DirectedEdge(1, 0); // the cycle only runs one way

            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => DirectedGraphSet.FromSets(graph, new[] { new[] { outside } }));

            Assert.Contains(outside.ToString(), ex.Message);
            Assert.Equal("edgeSets", ex.ParamName);
        }

        [Fact]
        public void DirectedEmptyAndPowerSetBuildWhatTheirNamesSay()
        {
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Path(3)); // 4 arcs

            DirectedGraphSet empty = DirectedGraphSet.Empty(graph);
            Assert.True(empty.IsEmpty);
            Assert.Equal(BigInteger.Zero, empty.Count);

            DirectedGraphSet all = DirectedGraphSet.PowerSet(graph);
            Assert.Equal(BigInteger.Pow(2, graph.EdgeCount), all.Count);
            Assert.Equal(BruteForceFamily.PowerSet(graph.EdgeCount).Masks, Masks(graph, all));
        }

        [Fact]
        public void DirectedFiltersComposeWithFactoryBuiltFamilies()
        {
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Path(3)); // 4 arcs
            DirectedEdge first = graph.GetEdge(0);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(graph.EdgeCount, count: 8, seed: 8_004))
            {
                DirectedGraphSet gs = DirectedGraphSet.FromSets(graph, ToEdgeSets(graph, family));

                AssertFilter(graph, family, gs.Including(first), mask => (mask & 1) != 0);
                AssertFilter(graph, family, gs.Excluding(first), mask => (mask & 1) == 0);
                AssertFilter(graph, family, gs.LenEquals(2), mask => BitOperations.PopCount((uint)mask) == 2);
                AssertFilter(
                    graph,
                    family,
                    gs.Including(first).Smaller(3),
                    mask => (mask & 1) != 0 && BitOperations.PopCount((uint)mask) < 3);
            }
        }

        [Fact]
        public void DirectedFromZddRoundTripsAGeneratorBuiltFamily()
        {
            DirectedGraph grid = DirectedGraph.Grid(3, 3);
            DirectedGraphSet paths = DirectedGraphSet.Paths(grid, from: 0, to: 8);
            DirectedEdge edge = grid.GetEdge(0);

            DirectedGraphSet reread = DirectedGraphSet.FromZdd(grid, paths.Zdd);

            Assert.Equal(paths.Count, reread.Count);
            Assert.Equal(paths.Including(edge).Count, reread.Including(edge).Count);
        }

        [Fact]
        public void DirectedFromZddReadsBackAFamilyBuiltOverAManagerWithSpareVariables()
        {
            DirectedGraph graph = DirectedGraph.Cycle(3); // 3 arcs
            using var manager = new ZddManager(graph.EdgeCount + 2);

            foreach (BruteForceFamily family in FamilyCases.AllFamilies(graph.EdgeCount))
            {
                BruteForceFamily widened = BruteForceFamily.FromMasks(manager.VariableCount, family.Masks);
                DirectedGraphSet gs = DirectedGraphSet.FromZdd(graph, ZddFamilies.Build(manager, widened));

                Assert.Equal(new BigInteger(family.Count), gs.Count);
                Assert.Equal(family.Masks, Masks(graph, gs));
            }
        }

        [Fact]
        public void DirectedFromZddRejectsAManagerWithTooFewVariables()
        {
            DirectedGraph graph = DirectedGraph.Cycle(3);
            using var manager = new ZddManager(graph.EdgeCount - 1);

            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => DirectedGraphSet.FromZdd(graph, manager.Singleton(0)));

            Assert.Equal("zdd", ex.ParamName);
        }

        // ==================== Helpers ====================

        private static Graph GraphWithEdgeCount(int edgeCount) =>
            new Graph(edgeCount + 1, Enumerable.Range(0, edgeCount).Select(i => new Edge(i, i + 1)));

        private static List<Edge[]> ToEdgeSets(Graph graph, BruteForceFamily family) =>
            ToSets(family, graph.EdgeCount, graph.GetEdge);

        private static List<DirectedEdge[]> ToEdgeSets(DirectedGraph graph, BruteForceFamily family) =>
            ToSets(family, graph.EdgeCount, graph.GetEdge);

        private static List<T[]> ToSets<T>(BruteForceFamily family, int edgeCount, Func<int, T> edgeAt)
        {
            var sets = new List<T[]>();

            foreach (int mask in family.Masks)
            {
                var edges = new List<T>();

                for (int i = 0; i < edgeCount; i++)
                {
                    if ((mask & (1 << i)) != 0)
                    {
                        edges.Add(edgeAt(i));
                    }
                }

                sets.Add(edges.ToArray());
            }

            return sets;
        }

        private static SortedSet<int> Masks(Graph graph, IEnumerable<IReadOnlySet<Edge>> family) =>
            Masks(family, IndexOf(graph.EdgeCount, graph.GetEdge));

        private static SortedSet<int> Masks(DirectedGraph graph, IEnumerable<IReadOnlySet<DirectedEdge>> family) =>
            Masks(family, IndexOf(graph.EdgeCount, graph.GetEdge));

        private static Dictionary<T, int> IndexOf<T>(int edgeCount, Func<int, T> edgeAt)
            where T : notnull
        {
            var index = new Dictionary<T, int>();

            for (int i = 0; i < edgeCount; i++)
            {
                index[edgeAt(i)] = i;
            }

            return index;
        }

        private static SortedSet<int> Masks<T>(IEnumerable<IReadOnlySet<T>> family, Dictionary<T, int> index)
            where T : notnull
        {
            var masks = new SortedSet<int>();

            foreach (IReadOnlySet<T> set in family)
            {
                int mask = 0;

                foreach (T edge in set)
                {
                    mask |= 1 << index[edge];
                }

                Assert.True(masks.Add(mask), "A family enumerated the same set twice.");
            }

            return masks;
        }

        private static bool Has(int mask, Graph graph, Edge edge)
        {
            for (int i = 0; i < graph.EdgeCount; i++)
            {
                if (graph.GetEdge(i) == edge)
                {
                    return (mask & (1 << i)) != 0;
                }
            }

            return false;
        }

        private static bool TouchesVertex(int mask, Graph graph, int vertex)
        {
            foreach (int edgeIndex in graph.IncidentEdges(vertex))
            {
                if ((mask & (1 << edgeIndex)) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertFilter(Graph graph, BruteForceFamily family, GraphSet filtered, Func<int, bool> keeps) =>
            Assert.Equal(Expected(family, keeps), Masks(graph, filtered));

        private static void AssertFilter(DirectedGraph graph, BruteForceFamily family, DirectedGraphSet filtered, Func<int, bool> keeps) =>
            Assert.Equal(Expected(family, keeps), Masks(graph, filtered));

        private static SortedSet<int> Expected(BruteForceFamily family, Func<int, bool> keeps) =>
            new SortedSet<int>(family.Masks.Where(keeps));
    }
}
