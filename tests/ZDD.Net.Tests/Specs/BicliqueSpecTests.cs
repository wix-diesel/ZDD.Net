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

namespace ZDD.Net.Tests.Specs
{
    /// <summary>
    /// M6-13 completion criteria for <see cref="BicliqueSpec"/>: matches, for small graphs, the family of
    /// edge sets forming some complete bipartite subgraph (checked against every vertex bipartition, read
    /// literally from the definition); matches known values for <c>K_{3,3}</c>; the size-fixed overload is
    /// exactly the size-(a,b) subfamily of the unconstrained one; the empty edge set's inclusion is
    /// documented and tested; and the size-fixed overload reaches a narrower frontier than the unconstrained
    /// form.
    /// </summary>
    public class BicliqueSpecTests
    {
        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        public void MatchesBruteForceEnumerationOnNamedGraphs(string graphName)
        {
            Graph graph = SpanningTreeSpecTests.NamedGraph(graphName);
            AssertMatchesBruteForce(graphName, graph, new BicliqueSpec(graph), BruteForceBicliques(graph, sizeFixed: null));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void MatchesBruteForceEnumerationOnRandomGraphs(int seed)
        {
            Graph graph = SpanningTreeSpecTests.RandomConnectedGraph(vertexCount: 7, extraEdgeProbability: 0.35, seed);
            AssertMatchesBruteForce($"seed={seed}", graph, new BicliqueSpec(graph), BruteForceBicliques(graph, sizeFixed: null));
        }

        /// <summary>
        /// A wider sweep than <see cref="MatchesBruteForceEnumerationOnRandomGraphs"/>, at a size and density
        /// deliberately chosen to make it likely that some biclique candidate splits into two temporarily
        /// separate groups (each with its own arbitrary relative-parity origin) before a later edge joins
        /// them — exactly the scenario <see cref="Specs.BicliqueVertexState"/>'s remarks describe as needing
        /// the parity union-find and the eager same-side merge on a not-taken edge between two already-grouped
        /// endpoints, rather than a single global side label.
        /// </summary>
        [Theory]
        [InlineData(9, 0.15)]
        [InlineData(9, 0.25)]
        [InlineData(9, 0.4)]
        [InlineData(8, 0.15)]
        public void StressMatchesBruteForceEnumerationAcrossManySeeds(int vertexCount, double extraEdgeProbability)
        {
            for (int seed = 100; seed < 120; seed++)
            {
                Graph graph = SpanningTreeSpecTests.RandomConnectedGraph(vertexCount, extraEdgeProbability, seed);
                AssertMatchesBruteForce($"v={vertexCount} p={extraEdgeProbability} seed={seed}", graph, new BicliqueSpec(graph), BruteForceBicliques(graph, sizeFixed: null));
            }
        }

        [Fact]
        public void MatchesBruteForceEnumerationWithIsolatedVertex()
        {
            // Vertex 3 has no incident edges at all.
            var graph = new Graph(4, new[] { new Edge(0, 1), new Edge(1, 2) });
            AssertMatchesBruteForce("isolated vertex", graph, new BicliqueSpec(graph), BruteForceBicliques(graph, sizeFixed: null));
        }

        [Fact]
        public void MatchesBruteForceEnumerationWithNoEdges()
        {
            var graph = new Graph(3, Array.Empty<Edge>());
            AssertMatchesBruteForce("no edges", graph, new BicliqueSpec(graph), BruteForceBicliques(graph, sizeFixed: null));
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(1, 2)]
        [InlineData(2, 2)]
        [InlineData(2, 3)]
        [InlineData(0, 0)]
        public void MatchesBruteForceEnumerationForFixedSizesOnCompleteGraph(int a, int b)
        {
            Graph graph = Graph.Complete(5);
            AssertMatchesBruteForce(
                $"K5 a={a} b={b}", graph, new BicliqueSpec(graph, a, b), BruteForceBicliques(graph, (a, b)));
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        [InlineData(1, 3)]
        public void MatchesBruteForceEnumerationForFixedSizesOnRandomGraph(int a, int b)
        {
            Graph graph = SpanningTreeSpecTests.RandomConnectedGraph(vertexCount: 6, extraEdgeProbability: 0.4, seed: 7);
            AssertMatchesBruteForce(
                $"random a={a} b={b}", graph, new BicliqueSpec(graph, a, b), BruteForceBicliques(graph, (a, b)));
        }

        [Fact]
        public void EmptyEdgeSetIsAMemberOfTheUnconstrainedFamily()
        {
            Graph graph = Graph.Complete(4);
            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<BicliqueSpec>(manager, new BicliqueSpec(graph));

            Assert.True(built.Contains());
        }

        [Fact]
        public void FixedSizeZeroZeroAcceptsOnlyTheEmptyEdgeSet()
        {
            Graph graph = Graph.Complete(4);
            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<BicliqueSpec>(manager, new BicliqueSpec(graph, 0, 0));

            Assert.Equal(BigInteger.One, built.Count);
            Assert.True(built.Contains());
        }

        [Fact]
        public void FixedSizeExcludesTheEmptyEdgeSetWhenEitherSizeIsPositive()
        {
            Graph graph = Graph.Complete(4);
            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<BicliqueSpec>(manager, new BicliqueSpec(graph, 1, 2));

            Assert.False(built.Contains());
        }

        /// <summary>K_{3,3} has exactly one biclique using every vertex of both sides: the graph itself.</summary>
        [Fact]
        public void K33HasExactlyOneFullSizeBiclique()
        {
            Graph k33 = CompleteBipartite(3, 3);
            using ZddManager manager = new ZddManager(k33.EdgeCount);
            Zdd built = FrontierBuilder.Build<BicliqueSpec>(manager, new BicliqueSpec(k33, 3, 3));

            Assert.Equal(BigInteger.One, built.Count);
        }

        /// <summary>
        /// Every non-empty biclique of K_{3,3} is exactly (non-empty subset of one side) &#215; (non-empty
        /// subset of the other): <c>(2^3 - 1)^2 = 49</c> non-empty ones, plus the empty edge set.
        /// </summary>
        [Fact]
        public void K33HasFortyNineNonEmptyBicliquesPlusTheEmptyOne()
        {
            Graph k33 = CompleteBipartite(3, 3);
            using ZddManager manager = new ZddManager(k33.EdgeCount);
            Zdd built = FrontierBuilder.Build<BicliqueSpec>(manager, new BicliqueSpec(k33));

            Assert.Equal(new BigInteger(50), built.Count);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        [InlineData(1, 2)]
        public void FixedSizeIsExactlyTheSizeSubfamilyOfTheUnconstrainedOne(int a, int b)
        {
            Graph graph = Graph.Complete(5);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd unconstrained = FrontierBuilder.Build<BicliqueSpec>(manager, new BicliqueSpec(graph));
            Zdd fixedSize = FrontierBuilder.Build<BicliqueSpec>(manager, new BicliqueSpec(graph, a, b));

            var expectedMasks = new List<int>();
            foreach (int[] edgeSet in unconstrained.Sets())
            {
                (int countA, int countB) = SideCounts(graph, edgeSet);
                if ((countA == a && countB == b) || (countA == b && countB == a))
                {
                    expectedMasks.Add(BruteForceFamily.MaskOf(graph.EdgeCount, edgeSet));
                }
            }

            FamilyAssert.AssertSameFamily(
                $"a={a} b={b}", fixedSize, BruteForceFamily.FromMasks(graph.EdgeCount, expectedMasks));
        }

        /// <summary>
        /// The fixed-size overload's smaller, capped state should reach a narrower frontier (fewer distinct
        /// reachable states per level) than the unconstrained form's unbounded one, on a graph large enough
        /// for the difference to show.
        /// </summary>
        [Fact]
        public void FixedSizeReachesANarrowerFrontierThanUnconstrained()
        {
            Graph graph = Graph.Grid(3, 3);
            using ZddManager unconstrainedManager = new ZddManager(graph.EdgeCount);
            using ZddManager fixedManager = new ZddManager(graph.EdgeCount);

            int unconstrainedMaxWidth = MaxFrontierWidth(unconstrainedManager, new BicliqueSpec(graph));
            int fixedMaxWidth = MaxFrontierWidth(fixedManager, new BicliqueSpec(graph, 1, 1));

            Assert.True(
                fixedMaxWidth < unconstrainedMaxWidth,
                $"expected the size-fixed build's max width ({fixedMaxWidth}) to be smaller than " +
                $"the unconstrained build's ({unconstrainedMaxWidth})");
        }

        private static int MaxFrontierWidth(ZddManager manager, BicliqueSpec spec)
        {
            var progress = new MaxWidthProgress();
            var options = new BuildOptions { Progress = progress };

            FrontierBuilder.Build<BicliqueSpec>(manager, spec, options);
            return progress.MaxWidth;
        }

        private sealed class MaxWidthProgress : IProgress<BuildProgress>
        {
            public int MaxWidth { get; private set; }

            public void Report(BuildProgress value) => MaxWidth = Math.Max(MaxWidth, value.FrontierSize);
        }

        [Fact]
        public void EveryEnumeratedSetIsActuallyABiclique()
        {
            Graph graph = Graph.Grid(2, 3);
            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<BicliqueSpec>(manager, new BicliqueSpec(graph));

            foreach (int[] edgeSet in built.Sets())
            {
                Assert.True(IsBiclique(graph, edgeSet), $"[{string.Join(",", edgeSet)}] is not a biclique");
            }
        }

        [Fact]
        public void GraphSetExposesBicliques()
        {
            Graph graph = Graph.Grid(2, 3);
            GraphSet bicliques = GraphSet.Bicliques(graph);

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd expected = FrontierBuilder.Build<BicliqueSpec>(manager, new BicliqueSpec(graph));

            Assert.Equal(expected.Count, bicliques.Count);
        }

        [Fact]
        public void GraphSetExposesFixedSizeBicliques()
        {
            Graph graph = Graph.Complete(5);
            GraphSet bicliques = GraphSet.Bicliques(graph, 2, 2);

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd expected = FrontierBuilder.Build<BicliqueSpec>(manager, new BicliqueSpec(graph, 2, 2));

            Assert.Equal(expected.Count, bicliques.Count);
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new BicliqueSpec(null!));
            Assert.Throws<ArgumentNullException>(() => new BicliqueSpec(null!, 1, 1));
        }

        [Fact]
        public void ConstructorRejectsNegativeSizes()
        {
            Graph graph = Graph.Complete(3);
            Assert.Throws<ArgumentOutOfRangeException>(() => new BicliqueSpec(graph, -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BicliqueSpec(graph, 1, -1));
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            Graph grid = Graph.Grid(4, 4);
            var spec = new BicliqueSpec(grid);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneEdgePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(BicliqueSpec spec, Span<int> state, int level)
            {
                while (level > 0)
                {
                    level = spec.GetChild(state, level, 0);
                    if (DdResult.IsTerminal(level))
                    {
                        return;
                    }
                }
            }
        }

        private static void AssertMatchesBruteForce<TSpec>(string context, Graph graph, TSpec spec, BruteForceFamily expected)
            where TSpec : struct, IArrayDdSpec
        {
            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<TSpec>(manager, spec);

            FamilyAssert.AssertSameFamily(context, built, expected);
        }

        /// <summary>
        /// The definition, read literally: for every way to put each vertex on <c>SideA</c>,
        /// <c>SideB</c>, or neither, the assignment is valid exactly when every <c>SideA</c>-<c>SideB</c>
        /// pair is an actual graph edge (so the graph restricted to the two sides is complete bipartite);
        /// its edge set is then exactly those cross edges. Optionally filtered to a fixed <c>(a, b)</c> pair
        /// of side sizes, accepted in either order.
        /// </summary>
        private static BruteForceFamily BruteForceBicliques(Graph graph, (int A, int B)? sizeFixed)
        {
            int vertexCount = graph.VertexCount;
            int edgeCount = graph.EdgeCount;

            if (vertexCount > 9)
            {
                throw new ArgumentException(
                    $"BruteForceBicliques enumerates all 3^{vertexCount} vertex assignments, which is too many for {vertexCount} vertices.",
                    nameof(graph));
            }

            if (edgeCount > BruteForceFamily.MaxVariableCount)
            {
                throw new ArgumentException(
                    $"BruteForceBicliques masks edges into an int (BruteForceFamily.MaxVariableCount = " +
                    $"{BruteForceFamily.MaxVariableCount}) and cannot handle {edgeCount} edges, even with only {vertexCount} vertices.",
                    nameof(graph));
            }

            var masks = new HashSet<int>();
            var side = new int[vertexCount];
            int assignmentCount = 1;
            for (int v = 0; v < vertexCount; v++)
            {
                assignmentCount *= 3;
            }

            for (int assignment = 0; assignment < assignmentCount; assignment++)
            {
                int remaining = assignment;
                int countA = 0;
                int countB = 0;
                for (int v = 0; v < vertexCount; v++)
                {
                    int s = remaining % 3;
                    remaining /= 3;
                    side[v] = s;
                    if (s == 1)
                    {
                        countA++;
                    }
                    else if (s == 2)
                    {
                        countB++;
                    }
                }

                if (sizeFixed is (int a, int b) &&
                    !((countA == a && countB == b) || (countA == b && countB == a)))
                {
                    continue;
                }

                int edgeMask = 0;
                int crossEdgesFound = 0;
                for (int e = 0; e < edgeCount; e++)
                {
                    Edge edge = graph.GetEdge(e);
                    bool cross = (side[edge.U] == 1 && side[edge.V] == 2) || (side[edge.U] == 2 && side[edge.V] == 1);
                    if (cross)
                    {
                        edgeMask |= 1 << e;
                        crossEdgesFound++;
                    }
                }

                // Complete bipartite requires every A-B pair to be an edge, not merely every existing
                // A-B edge to be selected: reject unless the count of cross edges found equals |A|*|B|.
                if (crossEdgesFound != countA * countB)
                {
                    continue;
                }

                masks.Add(edgeMask);
            }

            return BruteForceFamily.FromMasks(edgeCount, masks);
        }

        private static bool IsBiclique(Graph graph, IReadOnlyList<int> edgeSet)
        {
            if (!TryAssignSides(graph, edgeSet, out Dictionary<int, int>? assigned))
            {
                return false;
            }

            var selected = new HashSet<int>(edgeSet);
            for (int e = 0; e < graph.EdgeCount; e++)
            {
                Edge edge = graph.GetEdge(e);
                bool uAssigned = assigned.TryGetValue(edge.U, out int su);
                bool vAssigned = assigned.TryGetValue(edge.V, out int sv);
                bool shouldBeSelected = uAssigned && vAssigned && su != sv;
                bool isSelected = selected.Contains(e);
                if (shouldBeSelected != isSelected)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Derives each touched vertex's side by propagating through <paramref name="edgeSet"/>'s edges, as <see cref="IsBiclique"/> needs.</summary>
        private static (int CountA, int CountB) SideCounts(Graph graph, IReadOnlyList<int> edgeSet)
        {
            bool assignable = TryAssignSides(graph, edgeSet, out Dictionary<int, int> assigned);
            Assert.True(assignable, $"[{string.Join(",", edgeSet)}] is not even a valid 2-coloring, let alone a biclique");
            return (assigned.Values.Count(s => s == 1), assigned.Values.Count(s => s == 2));
        }

        /// <summary>
        /// Assigns each vertex touched by <paramref name="edgeSet"/> to side 1 or 2 by propagating through
        /// its edges (the first edge seeds an arbitrary side for each of its endpoints; every later edge
        /// with one already-assigned endpoint puts the other on the opposite side). Fails if two endpoints
        /// already assigned by earlier edges turn out to need the same side.
        /// </summary>
        private static bool TryAssignSides(Graph graph, IReadOnlyList<int> edgeSet, out Dictionary<int, int> assigned)
        {
            assigned = new Dictionary<int, int>();
            foreach (int e in edgeSet)
            {
                Edge edge = graph.GetEdge(e);
                bool uKnown = assigned.TryGetValue(edge.U, out int su);
                bool vKnown = assigned.TryGetValue(edge.V, out int sv);

                if (!uKnown && !vKnown)
                {
                    assigned[edge.U] = 1;
                    assigned[edge.V] = 2;
                }
                else if (uKnown && !vKnown)
                {
                    assigned[edge.V] = su == 1 ? 2 : 1;
                }
                else if (!uKnown && vKnown)
                {
                    assigned[edge.U] = sv == 1 ? 2 : 1;
                }
                else if (su == sv)
                {
                    return false; // a taken edge within one side
                }
            }

            return true;
        }

        private static Graph CompleteBipartite(int left, int right)
        {
            var edges = new List<Edge>();
            for (int u = 0; u < left; u++)
            {
                for (int v = 0; v < right; v++)
                {
                    edges.Add(new Edge(u, left + v));
                }
            }

            return new Graph(left + right, edges);
        }
    }
}
