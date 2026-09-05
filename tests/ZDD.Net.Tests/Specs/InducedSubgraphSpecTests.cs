using System;
using System.Collections.Generic;
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
    /// M6-12 completion criteria for <see cref="InducedSubgraphSpec"/>: matches, for graphs with up to 8
    /// vertices, the family of edge sets induced by every vertex subset; the delayed Unknown/In/Out
    /// determination is correct even when a not-taken edge's two ends resolve to <c>In</c> at different,
    /// non-adjacent points in the edge order; isolated vertices and edgeless graphs are boundary cases; and
    /// intersecting with <see cref="ConnectedSubgraphSpec"/> matches a post-hoc connectivity filter.
    /// </summary>
    public class InducedSubgraphSpecTests
    {
        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        public void MatchesBruteForceEnumerationOnNamedGraphs(string graphName)
        {
            Graph graph = SpanningTreeSpecTests.NamedGraph(graphName);
            AssertMatchesBruteForce(graphName, graph);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void MatchesBruteForceEnumerationOnRandomGraphs(int seed)
        {
            Graph graph = SpanningTreeSpecTests.RandomConnectedGraph(vertexCount: 6, extraEdgeProbability: 0.3, seed);
            AssertMatchesBruteForce($"seed={seed}", graph);
        }

        [Fact]
        public void MatchesBruteForceEnumerationWithIsolatedVertex()
        {
            // Vertex 3 has no incident edges at all.
            var graph = new Graph(4, new[] { new Edge(0, 1), new Edge(1, 2) });
            AssertMatchesBruteForce("isolated vertex", graph);
        }

        [Fact]
        public void MatchesBruteForceEnumerationWithNoEdges()
        {
            var graph = new Graph(3, Array.Empty<Edge>());
            AssertMatchesBruteForce("no edges", graph);
        }

        [Fact]
        public void EmptyGraphAcceptsOnlyTheEmptyEdgeSet()
        {
            var graph = new Graph(3, Array.Empty<Edge>());
            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<InducedSubgraphSpec>(manager, new InducedSubgraphSpec(graph));

            Assert.Equal(BigInteger.One, built.Count);
            Assert.True(built.Contains());
        }

        /// <summary>
        /// The exact scenario the design calls out: skipping edge (0,1) while both ends are still
        /// <c>Unknown</c> must not let them independently become <c>In</c> later through unrelated edges.
        /// Graph is the path 2-1-0-3 relabeled so edge (0,1) is decided (and skipped) before vertex 1 is
        /// forgotten, vertex 1 then becomes <c>In</c> and is forgotten (discarded) via edge (1,2), and only
        /// afterwards does vertex 0 attempt to become <c>In</c> via edge (0,3) &#8212; by which point vertex
        /// 1's own slot is long gone, so only marking vertex 0 <c>Out</c> at vertex 1's transition (not
        /// re-checking from vertex 0's side) can catch the conflict.
        /// </summary>
        [Fact]
        public void DelayedDeterminationRejectsBothEndsBecomingInLater()
        {
            var graph = new Graph(4, new[] { new Edge(0, 1), new Edge(1, 2), new Edge(0, 3) });
            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<InducedSubgraphSpec>(manager, new InducedSubgraphSpec(graph));

            // Edge (0,1) skipped, edges (1,2) and (0,3) taken: touches vertices {0,1,2,3}, so S must be all
            // four vertices, forcing edge (0,1) in too — contradiction. Must not be a member.
            Assert.False(built.Contains(1, 2));

            AssertMatchesBruteForce("delayed determination", graph);
        }

        [Fact]
        public void EveryEnumeratedSetIsActuallyInduced()
        {
            Graph graph = Graph.Grid(2, 3);
            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<InducedSubgraphSpec>(manager, new InducedSubgraphSpec(graph));

            foreach (int[] edgeSet in built.Sets())
            {
                Assert.True(IsInduced(graph, edgeSet));
            }
        }

        [Fact]
        public void IntersectingWithConnectedSubgraphsMatchesPostHocFilter()
        {
            Graph graph = Graph.Grid(2, 3);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd induced = FrontierBuilder.Build<InducedSubgraphSpec>(manager, new InducedSubgraphSpec(graph));
            Zdd connected = FrontierBuilder.Build<ConnectedSubgraphSpec>(
                manager, new ConnectedSubgraphSpec(graph, new[] { 0, graph.VertexCount - 1 }));

            Zdd combined = induced.Intersect(connected);

            var expectedMasks = new List<int>();
            foreach (int[] edgeSet in induced.Sets())
            {
                if (AreConnected(graph, edgeSet, new[] { 0, graph.VertexCount - 1 }))
                {
                    expectedMasks.Add(BruteForceFamily.MaskOf(graph.EdgeCount, edgeSet));
                }
            }

            FamilyAssert.AssertSameFamily("induced ∩ connected", combined, BruteForceFamily.FromMasks(graph.EdgeCount, expectedMasks));
        }

        [Fact]
        public void GraphSetExposesInducedSubgraphs()
        {
            Graph graph = Graph.Grid(2, 3);
            GraphSet induced = GraphSet.InducedSubgraphs(graph);

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd expected = FrontierBuilder.Build<InducedSubgraphSpec>(manager, new InducedSubgraphSpec(graph));

            Assert.Equal(expected.Count, induced.Count);
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new InducedSubgraphSpec(null!));
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            Graph grid = Graph.Grid(4, 4);
            var spec = new InducedSubgraphSpec(grid);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneEdgePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(InducedSubgraphSpec spec, Span<int> state, int level)
            {
                while (level > 0)
                {
                    level = spec.GetChild(state, level, 1);
                    if (DdResult.IsTerminal(level))
                    {
                        return;
                    }
                }
            }
        }

        private static void AssertMatchesBruteForce(string context, Graph graph)
        {
            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<InducedSubgraphSpec>(manager, new InducedSubgraphSpec(graph));

            BruteForceFamily expected = BruteForceInducedSubgraphs(graph);

            FamilyAssert.AssertSameFamily(context, built, expected);
        }

        /// <summary>The definition, read literally: for every vertex subset S, the edges with both ends in S.</summary>
        private static BruteForceFamily BruteForceInducedSubgraphs(Graph graph)
        {
            int edgeCount = graph.EdgeCount;
            int vertexCount = graph.VertexCount;

            if (vertexCount > 20)
            {
                throw new ArgumentException(
                    $"BruteForceInducedSubgraphs enumerates all 2^vertexCount subsets and cannot handle {vertexCount} vertices.",
                    nameof(graph));
            }

            var masks = new HashSet<int>();
            int vertexBound = 1 << vertexCount;

            for (int sMask = 0; sMask < vertexBound; sMask++)
            {
                int edgeMask = 0;
                for (int e = 0; e < edgeCount; e++)
                {
                    Edge edge = graph.GetEdge(e);
                    bool uIn = (sMask & (1 << edge.U)) != 0;
                    bool vIn = (sMask & (1 << edge.V)) != 0;
                    if (uIn && vIn)
                    {
                        edgeMask |= 1 << e;
                    }
                }

                masks.Add(edgeMask);
            }

            return BruteForceFamily.FromMasks(edgeCount, masks);
        }

        private static bool IsInduced(Graph graph, IReadOnlyList<int> edgeSet)
        {
            var touched = new HashSet<int>();
            foreach (int edgeIndex in edgeSet)
            {
                Edge edge = graph.GetEdge(edgeIndex);
                touched.Add(edge.U);
                touched.Add(edge.V);
            }

            for (int e = 0; e < graph.EdgeCount; e++)
            {
                Edge edge = graph.GetEdge(e);
                bool shouldBeSelected = touched.Contains(edge.U) && touched.Contains(edge.V);
                bool isSelected = false;
                foreach (int selected in edgeSet)
                {
                    if (selected == e)
                    {
                        isSelected = true;
                        break;
                    }
                }

                if (shouldBeSelected != isSelected)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether every vertex in <paramref name="terminals"/> shares a component under <paramref name="edgeSet"/>.</summary>
        private static bool AreConnected(Graph graph, IReadOnlyList<int> edgeSet, IReadOnlyList<int> terminals)
        {
            var parent = new int[graph.VertexCount];
            for (int v = 0; v < graph.VertexCount; v++)
            {
                parent[v] = v;
            }

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }

                return x;
            }

            foreach (int edgeIndex in edgeSet)
            {
                Edge edge = graph.GetEdge(edgeIndex);
                parent[Find(edge.U)] = Find(edge.V);
            }

            int root = Find(terminals[0]);
            for (int i = 1; i < terminals.Count; i++)
            {
                if (Find(terminals[i]) != root)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
