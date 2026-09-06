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
    /// M8-2 completion criteria for <see cref="GraphSet"/>'s and <see cref="DirectedGraphSet"/>'s family
    /// algebra: the four binary operations, their operators, <c>Maximal</c> / <c>Minimal</c> and
    /// <c>ToUniverseOf</c> &#8212; matched against the brute-force family and the <see cref="Zdd"/> layer,
    /// with a universe mismatch rejected by an <see cref="ArgumentException"/> that names <c>ToUniverseOf</c>.
    /// </summary>
    public class GraphSetAlgebraTests
    {
        // ==================== Binary operations, exhaustively ====================

        [Fact]
        public void BinaryOperationsMatchBruteForceForEveryPairOfFamiliesOfAThreeEdgeGraph()
        {
            Graph graph = Graph.Path(4); // 3 edges, so 2^(2^3) = 256 families and 65536 pairs

            BruteForceFamily[] expectedFamilies = FamilyCases.AllFamilies(graph.EdgeCount).ToArray();
            GraphSet[] actualFamilies = expectedFamilies.Select(f => FamilyOf(graph, f)).ToArray();

            for (int i = 0; i < expectedFamilies.Length; i++)
            {
                for (int j = 0; j < expectedFamilies.Length; j++)
                {
                    BruteForceFamily left = expectedFamilies[i];
                    BruteForceFamily right = expectedFamilies[j];

                    GraphSet a = actualFamilies[i];
                    GraphSet b = actualFamilies[j].ToUniverseOf(a);

                    Assert.Same(a.Universe, b.Universe);

                    Assert.Equal(left.Union(right).Masks, Masks(graph, a.Union(b)));
                    Assert.Equal(left.Intersect(right).Masks, Masks(graph, a.Intersect(b)));
                    Assert.Equal(left.Difference(right).Masks, Masks(graph, a.Difference(b)));
                    Assert.Equal(left.SymmetricDifference(right).Masks, Masks(graph, a.SymmetricDifference(b)));
                }
            }
        }

        [Fact]
        public void OperatorsAgreeWithTheirNamedMethodsAndWithTheZddLayer()
        {
            Graph graph = Graph.Complete(4); // 6 edges

            foreach ((BruteForceFamily left, BruteForceFamily right) in PairsOf(graph.EdgeCount, seed: 8_201))
            {
                GraphSet a = FamilyOf(graph, left);
                GraphSet b = FamilyOf(graph, right).ToUniverseOf(a);

                // Both halves of the criterion at once: the operator equals its named method, and the
                // named method equals the same operation done directly on the underlying ZDDs.
                AssertSameFamily(a.Zdd.Union(b.Zdd), a | b, a.Union(b));
                AssertSameFamily(a.Zdd.Intersect(b.Zdd), a & b, a.Intersect(b));
                AssertSameFamily(a.Zdd.Difference(b.Zdd), a - b, a.Difference(b));
                AssertSameFamily(a.Zdd.SymmetricDifference(b.Zdd), a ^ b, a.SymmetricDifference(b));
            }
        }

        [Fact]
        public void MaximalAndMinimalMatchBruteForceForEveryFamilyOfAThreeEdgeGraph()
        {
            Graph graph = Graph.Path(4);

            foreach (BruteForceFamily family in FamilyCases.AllFamilies(graph.EdgeCount))
            {
                GraphSet gs = FamilyOf(graph, family);

                Assert.Equal(family.Maximal().Masks, Masks(graph, gs.Maximal()));
                Assert.Equal(family.Minimal().Masks, Masks(graph, gs.Minimal()));
            }
        }

        [Fact]
        public void UnionOfAFamilyWithEmptyIsTheFamilyItself()
        {
            Graph graph = Graph.Grid(3, 3);
            GraphSet paths = GraphSet.Paths(graph, from: 0, to: 8);
            GraphSet empty = GraphSet.Empty(graph).ToUniverseOf(paths);

            Assert.Equal(paths, paths.Union(empty));
            Assert.True(paths.Intersect(empty).IsEmpty);
            Assert.Equal(paths, paths.Difference(empty));
            Assert.Equal(paths, paths.SymmetricDifference(empty));
        }

        // ==================== Universe mismatch ====================

        [Fact]
        public void BinaryOperationsRejectFamiliesOfSeparateUniversesAndNameToUniverseOf()
        {
            Graph graph = Graph.Grid(3, 3);

            // The whole point of B23: these two come from the same Graph, but each generator built its
            // own SetUniverse<Edge>, so they cannot be combined without an explicit ToUniverseOf.
            GraphSet paths = GraphSet.Paths(graph, from: 0, to: 8);
            GraphSet cycles = GraphSet.Cycles(graph);

            Assert.NotSame(paths.Universe, cycles.Universe);

            foreach (Func<GraphSet> operation in new Func<GraphSet>[]
            {
                () => paths.Union(cycles),
                () => paths.Intersect(cycles),
                () => paths.Difference(cycles),
                () => paths.SymmetricDifference(cycles),
                () => paths | cycles,
                () => paths & cycles,
                () => paths - cycles,
                () => paths ^ cycles,
            })
            {
                ArgumentException ex = Assert.Throws<ArgumentException>(() => operation());

                Assert.Equal("other", ex.ParamName);
                Assert.Contains(nameof(GraphSet.ToUniverseOf), ex.Message, StringComparison.Ordinal);

                // The suggested fix moves the right operand, matching the docs and keeping the result
                // on the left operand's universe (which is what Combine builds it over).
                Assert.Contains($"left | right.{nameof(GraphSet.ToUniverseOf)}(left)", ex.Message, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void BinaryOperationsRejectANullOther()
        {
            GraphSet paths = GraphSet.Paths(Graph.Grid(3, 3), from: 0, to: 8);

            Assert.Throws<ArgumentNullException>(() => paths.Union(null!));
            Assert.Throws<ArgumentNullException>(() => paths.Intersect(null!));
            Assert.Throws<ArgumentNullException>(() => paths.Difference(null!));
            Assert.Throws<ArgumentNullException>(() => paths.SymmetricDifference(null!));
        }

        // ==================== ToUniverseOf ====================

        [Fact]
        public void ToUniverseOfMovesAFamilyOntoAnothersUniverseWithoutChangingItsMembers()
        {
            Graph graph = Graph.Grid(3, 3);
            GraphSet paths = GraphSet.Paths(graph, from: 0, to: 8);
            GraphSet cycles = GraphSet.Cycles(graph);

            GraphSet moved = cycles.ToUniverseOf(paths);

            Assert.Same(paths.Universe, moved.Universe);
            Assert.Same(paths.Graph, moved.Graph);
            Assert.Equal(cycles.Count, moved.Count);
            Assert.Equal(Masks(graph, cycles), Masks(graph, moved));
        }

        [Fact]
        public void ToUniverseOfIsAnIdentityWhenBothFamiliesAlreadyShareAUniverse()
        {
            GraphSet paths = GraphSet.Paths(Graph.Grid(3, 3), from: 0, to: 8);
            GraphSet shorter = paths.Smaller(6);

            Assert.Same(shorter, shorter.ToUniverseOf(paths));
        }

        [Fact]
        public void ToUniverseOfRoundTripsBackToTheOriginalFamily()
        {
            Graph graph = Graph.Grid(3, 3);
            GraphSet paths = GraphSet.Paths(graph, from: 0, to: 8);
            GraphSet cycles = GraphSet.Cycles(graph);

            GraphSet roundTripped = cycles.ToUniverseOf(paths).ToUniverseOf(cycles);

            Assert.Same(cycles.Universe, roundTripped.Universe);
            Assert.Equal(cycles, roundTripped); // GraphSet equality already requires the same universe
        }

        [Fact]
        public void ToUniverseOfRejectsADifferentEdgeOrderAndNamesToEdgeOrder()
        {
            Graph graph = Graph.Grid(3, 3);
            Graph reordered = graph.Optimize();

            GraphSet onOriginal = GraphSet.Paths(graph, from: 0, to: 8);
            GraphSet onReordered = GraphSet.Paths(reordered, from: 0, to: 8);

            Assert.NotEqual(graph.Edges, reordered.Edges); // the premise: same edges, different order

            ArgumentException ex = Assert.Throws<ArgumentException>(() => onReordered.ToUniverseOf(onOriginal));

            Assert.Equal("other", ex.ParamName);
            Assert.Contains(nameof(GraphSet.ToEdgeOrder), ex.Message, StringComparison.Ordinal);

            // The guidance has to name an argument the caller actually holds, not ToEdgeOrder's own
            // parameter name, so that following the message verbatim compiles.
            Assert.Contains($"{nameof(GraphSet.ToEdgeOrder)}(other.{nameof(GraphSet.Graph)})", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ToEdgeOrderThenToUniverseOfCombinesFamiliesBuiltOverDifferentEdgeOrders()
        {
            Graph graph = Graph.Grid(3, 3);
            Graph reordered = graph.Optimize();

            GraphSet onOriginal = GraphSet.Paths(graph, from: 0, to: 8);
            GraphSet onReordered = GraphSet.Cycles(reordered);

            // The guidance the exception above gives, followed through: align the edge order first,
            // then move onto the other family's universe.
            GraphSet combined = onOriginal.Union(onReordered.ToEdgeOrder(graph).ToUniverseOf(onOriginal));

            Assert.Equal(onOriginal.Count + GraphSet.Cycles(graph).Count, combined.Count);
        }

        [Fact]
        public void ToUniverseOfRejectsAFamilyOfADifferentGraph()
        {
            GraphSet paths = GraphSet.Paths(Graph.Grid(3, 3), from: 0, to: 8);
            GraphSet other = GraphSet.Paths(Graph.Grid(2, 3), from: 0, to: 5);

            ArgumentException ex = Assert.Throws<ArgumentException>(() => paths.ToUniverseOf(other));

            Assert.Equal("other", ex.ParamName);
        }

        [Fact]
        public void ToUniverseOfRejectsANullOther()
        {
            GraphSet paths = GraphSet.Paths(Graph.Grid(3, 3), from: 0, to: 8);

            Assert.Throws<ArgumentNullException>(() => paths.ToUniverseOf(null!));
        }

        // ==================== Filters still compose with a result ====================

        [Fact]
        public void FiltersStillApplyToTheResultOfEveryAlgebraicOperation()
        {
            Graph graph = Graph.Path(4);
            Edge first = graph.GetEdge(0);

            foreach ((BruteForceFamily left, BruteForceFamily right) in PairsOf(graph.EdgeCount, seed: 8_202))
            {
                GraphSet a = FamilyOf(graph, left);
                GraphSet b = FamilyOf(graph, right).ToUniverseOf(a);

                AssertFilters(graph, left.Union(right), a.Union(b), first);
                AssertFilters(graph, left.Intersect(right), a.Intersect(b), first);
                AssertFilters(graph, left.Difference(right), a.Difference(b), first);
                AssertFilters(graph, left.SymmetricDifference(right), a.SymmetricDifference(b), first);
                AssertFilters(graph, left.Maximal(), a.Maximal(), first);
                AssertFilters(graph, left.Minimal(), a.Minimal(), first);
            }
        }

        // ==================== The scenario from issue #188 ====================

        [Fact]
        public void PathsUnionCyclesOfTheSameGridReadsAsBothFamiliesTogether()
        {
            Graph g = Graph.Grid(4, 4);

            GraphSet paths = GraphSet.Paths(g, from: 0, to: 15);
            GraphSet cycles = GraphSet.Cycles(g);

            GraphSet either = paths | cycles.ToUniverseOf(paths);

            // Paths run 0 -> 15 and cycles are closed walks, so no edge set is in both families.
            Assert.Equal(paths.Count + cycles.Count, either.Count);
            Assert.True(either.Contains(paths.Sample(new Random(42))));
            Assert.True(either.Contains(cycles.Sample(new Random(42))));

            // And the result is still a GraphSet in every other respect.
            Assert.Equal(
                either.Count - either.Excluding(g.GetEdge(0)).Count,
                either.Including(g.GetEdge(0)).Count);
        }

        // ==================== DirectedGraphSet ====================

        [Fact]
        public void DirectedBinaryOperationsMatchBruteForceForEveryPairOfFamiliesOfAThreeArcGraph()
        {
            DirectedGraph graph = DirectedGraph.Cycle(3); // 3 arcs

            BruteForceFamily[] expectedFamilies = FamilyCases.AllFamilies(graph.EdgeCount).ToArray();
            DirectedGraphSet[] actualFamilies = expectedFamilies.Select(f => FamilyOf(graph, f)).ToArray();

            for (int i = 0; i < expectedFamilies.Length; i++)
            {
                for (int j = 0; j < expectedFamilies.Length; j++)
                {
                    BruteForceFamily left = expectedFamilies[i];
                    BruteForceFamily right = expectedFamilies[j];

                    DirectedGraphSet a = actualFamilies[i];
                    DirectedGraphSet b = actualFamilies[j].ToUniverseOf(a);

                    Assert.Equal(left.Union(right).Masks, Masks(graph, a | b));
                    Assert.Equal(left.Intersect(right).Masks, Masks(graph, a & b));
                    Assert.Equal(left.Difference(right).Masks, Masks(graph, a - b));
                    Assert.Equal(left.SymmetricDifference(right).Masks, Masks(graph, a ^ b));
                    Assert.Equal(left.Maximal().Masks, Masks(graph, a.Maximal()));
                    Assert.Equal(left.Minimal().Masks, Masks(graph, a.Minimal()));
                }
            }
        }

        [Fact]
        public void DirectedBinaryOperationsRejectFamiliesOfSeparateUniversesAndNameToUniverseOf()
        {
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Grid(3, 3));

            DirectedGraphSet paths = DirectedGraphSet.Paths(graph, from: 0, to: 8);
            DirectedGraphSet cycles = DirectedGraphSet.Cycles(graph);

            Assert.NotSame(paths.Universe, cycles.Universe);

            ArgumentException ex = Assert.Throws<ArgumentException>(() => paths.Union(cycles));

            Assert.Equal("other", ex.ParamName);
            Assert.Contains(nameof(DirectedGraphSet.ToUniverseOf), ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void DirectedToUniverseOfRoundTripsAndCombinesPathsWithCycles()
        {
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Grid(3, 3));

            DirectedGraphSet paths = DirectedGraphSet.Paths(graph, from: 0, to: 8);
            DirectedGraphSet cycles = DirectedGraphSet.Cycles(graph);

            DirectedGraphSet moved = cycles.ToUniverseOf(paths);
            Assert.Same(paths.Universe, moved.Universe);
            Assert.Equal(cycles, moved.ToUniverseOf(cycles));

            DirectedGraphSet either = paths | moved;
            Assert.Equal(paths.Count + cycles.Count, either.Count);
            Assert.Equal(either.Count, either.Larger(0).Count); // filters still compose
        }

        [Fact]
        public void DirectedToUniverseOfRejectsADifferentArcOrder()
        {
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Grid(3, 3));
            DirectedGraph reordered = graph.Optimize();

            DirectedGraphSet onOriginal = DirectedGraphSet.Paths(graph, from: 0, to: 8);
            DirectedGraphSet onReordered = DirectedGraphSet.Paths(reordered, from: 0, to: 8);

            ArgumentException ex = Assert.Throws<ArgumentException>(() => onReordered.ToUniverseOf(onOriginal));

            Assert.Equal("other", ex.ParamName);
            Assert.Contains(nameof(DirectedGraph.WithEdgeOrder), ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void DirectedToUniverseOfRejectsNullAndADifferentGraph()
        {
            DirectedGraphSet paths = DirectedGraphSet.Paths(DirectedGraph.Cycle(4), from: 0, to: 2);
            DirectedGraphSet other = DirectedGraphSet.Paths(DirectedGraph.Cycle(5), from: 0, to: 2);

            Assert.Throws<ArgumentNullException>(() => paths.ToUniverseOf(null!));
            Assert.Equal("other", Assert.Throws<ArgumentException>(() => paths.ToUniverseOf(other)).ParamName);
        }

        // ==================== Helpers ====================

        private static IEnumerable<(BruteForceFamily Left, BruteForceFamily Right)> PairsOf(int edgeCount, int seed)
        {
            BruteForceFamily[] families = FamilyCases.RandomFamilies(edgeCount, count: 12, seed).ToArray();

            for (int i = 0; i < families.Length; i++)
            {
                yield return (families[i], families[(i + 1) % families.Length]);
            }
        }

        private static GraphSet FamilyOf(Graph graph, BruteForceFamily family) =>
            GraphSet.FromSets(graph, ToSets(family, graph.EdgeCount, graph.GetEdge));

        private static DirectedGraphSet FamilyOf(DirectedGraph graph, BruteForceFamily family) =>
            DirectedGraphSet.FromSets(graph, ToSets(family, graph.EdgeCount, graph.GetEdge));

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

        private static void AssertSameFamily(Zdd expected, GraphSet actual, GraphSet named)
        {
            Assert.Equal(ZddFamilies.ToBruteForce(expected), ZddFamilies.ToBruteForce(actual.Zdd));
            Assert.Equal(actual, named);
        }

        private static void AssertFilters(Graph graph, BruteForceFamily expected, GraphSet actual, Edge edge)
        {
            int bit = 1 << IndexOf(graph, edge);

            Assert.Equal(expected.Masks, Masks(graph, actual));
            Assert.Equal(Filtered(expected, m => (m & bit) != 0), Masks(graph, actual.Including(edge)));
            Assert.Equal(Filtered(expected, m => (m & bit) == 0), Masks(graph, actual.Excluding(edge)));
            Assert.Equal(Filtered(expected, m => BitOperations.PopCount((uint)m) == 1), Masks(graph, actual.LenEquals(1)));
            Assert.Equal(Filtered(expected, m => BitOperations.PopCount((uint)m) > 1), Masks(graph, actual.Larger(1)));
            Assert.Equal(Filtered(expected, m => BitOperations.PopCount((uint)m) < 2), Masks(graph, actual.Smaller(2)));
        }

        private static SortedSet<int> Filtered(BruteForceFamily family, Func<int, bool> keeps) =>
            new SortedSet<int>(family.Masks.Where(keeps));

        private static int IndexOf(Graph graph, Edge edge)
        {
            for (int i = 0; i < graph.EdgeCount; i++)
            {
                if (graph.GetEdge(i) == edge)
                {
                    return i;
                }
            }

            throw new ArgumentException($"Edge {edge} is not part of the graph.", nameof(edge));
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
    }
}
