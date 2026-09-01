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
    /// M2-9 completion criteria for <see cref="SpanningTreeSpec"/>: the count matches Kirchhoff's matrix-tree
    /// theorem computed independently of the ZDD (<c>Complete(n)</c>, <c>Grid(r,c)</c>, and random graphs),
    /// matches brute-force enumeration on small graphs with every enumerated set verified as an actual
    /// spanning tree, a disconnected graph builds to <c>Empty</c>, the comp-array canonicalization actually
    /// reduces the diagram, and <c>GetChild</c> does not allocate.
    /// </summary>
    public class SpanningTreeSpecTests
    {
        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void CountMatchesKirchhoffForCompleteGraphs(int n)
        {
            // Cayley's formula, the closed form of the matrix-tree theorem for K_n: n^(n-2) spanning trees.
            Graph graph = Graph.Complete(n);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<SpanningTreeSpec>(manager, new SpanningTreeSpec(graph));

            Assert.Equal(Kirchhoff.CountSpanningTrees(graph), built.Count);
            Assert.Equal(BigInteger.Pow(n, n - 2), built.Count);
        }

        [Theory]
        [InlineData(2, 2)]
        [InlineData(2, 3)]
        [InlineData(3, 3)]
        [InlineData(2, 4)]
        [InlineData(4, 4)]
        public void CountMatchesKirchhoffForGridGraphs(int rows, int cols)
        {
            Graph graph = Graph.Grid(rows, cols);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<SpanningTreeSpec>(manager, new SpanningTreeSpec(graph));

            Assert.Equal(Kirchhoff.CountSpanningTrees(graph), built.Count);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void CountMatchesKirchhoffForRandomConnectedGraphs(int seed)
        {
            Graph graph = RandomConnectedGraph(vertexCount: 7, extraEdgeProbability: 0.35, seed);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<SpanningTreeSpec>(manager, new SpanningTreeSpec(graph));

            Assert.Equal(Kirchhoff.CountSpanningTrees(graph), built.Count);
        }

        [Theory]
        [InlineData(2, 3)]
        [InlineData(3, 3)]
        public void EveryEnumeratedGridSetIsAValidSpanningTree(int rows, int cols)
        {
            Graph graph = Graph.Grid(rows, cols);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<SpanningTreeSpec>(manager, new SpanningTreeSpec(graph));

            foreach (int[] edgeSet in built.Sets())
            {
                AssertIsSpanningTree(graph, edgeSet);
            }
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        public void MatchesBruteForceEnumerationOnSmallGraphs(string graphName)
        {
            Graph graph = NamedGraph(graphName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<SpanningTreeSpec>(manager, new SpanningTreeSpec(graph));

            BruteForceFamily expected = BruteForceSpanningTrees(graph);

            FamilyAssert.AssertSameFamily(graphName, built, expected);
        }

        [Fact]
        public void SingleVertexIsTheBaseFamily()
        {
            var graph = new Graph(1, Array.Empty<Edge>());
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<SpanningTreeSpec>(manager, new SpanningTreeSpec(graph));

            Assert.Equal(BigInteger.One, built.Count);
            Assert.Equal(Array.Empty<int>(), Assert.Single(built.Sets()));
        }

        [Fact]
        public void DisconnectedGraphIsEmpty()
        {
            // Two disjoint triangles: vertices 0-1-2 and 3-4-5, no edge between the halves.
            var graph = new Graph(6, new[]
            {
                new Edge(0, 1), new Edge(1, 2), new Edge(2, 0),
                new Edge(3, 4), new Edge(4, 5), new Edge(5, 3),
            });

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<SpanningTreeSpec>(manager, new SpanningTreeSpec(graph));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void IsolatedVertexIsEmpty()
        {
            // Vertex 3 has no incident edges at all.
            var graph = new Graph(4, new[] { new Edge(0, 1), new Edge(1, 2) });

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<SpanningTreeSpec>(manager, new SpanningTreeSpec(graph));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new SpanningTreeSpec(null!));
        }

        [Fact]
        public void CanonicalStateProducesFewerNodesThanLeavingStaleComponentCodesBehind()
        {
            Graph grid = Graph.Grid(4, 4);
            var canonical = new SpanningTreeSpec(grid);
            var uncanonical = new UncanonicalSpanningTreeSpec(grid);

            long canonicalNodeCount = ArrayTopDownExpander<SpanningTreeSpec>.Expand(canonical).NodeCount;
            long uncanonicalNodeCount = ArrayTopDownExpander<UncanonicalSpanningTreeSpec>.Expand(uncanonical).NodeCount;

            Assert.True(
                canonicalNodeCount < uncanonicalNodeCount,
                $"expected clearing forgotten slots to shrink the build, got {canonicalNodeCount} " +
                $"(canonical) vs {uncanonicalNodeCount} (stale slots left behind)");

            using ZddManager manager = new ZddManager(grid.EdgeCount);
            Zdd fromCanonical = FrontierBuilder.Build<SpanningTreeSpec>(manager, canonical);
            Zdd fromUncanonical = FrontierBuilder.Build<UncanonicalSpanningTreeSpec>(manager, uncanonical);
            Assert.Equal(fromCanonical, fromUncanonical); // same family regardless — only the build's width differs
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            Graph grid = Graph.Grid(4, 4);
            var spec = new SpanningTreeSpec(grid);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneEdgePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(SpanningTreeSpec spec, Span<int> state, int level)
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

        /// <summary>Checks that <paramref name="edgeSet"/> is exactly a spanning tree of <paramref name="graph"/>.</summary>
        internal static void AssertIsSpanningTree(Graph graph, int[] edgeSet)
        {
            Assert.Equal(graph.VertexCount - 1, edgeSet.Length);

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
                int ru = Find(edge.U);
                int rv = Find(edge.V);
                Assert.NotEqual(ru, rv); // no cycle
                parent[ru] = rv;
            }

            int root = Find(0);
            for (int v = 1; v < graph.VertexCount; v++)
            {
                Assert.Equal(root, Find(v)); // fully connected
            }
        }

        internal static Graph NamedGraph(string graphName) => graphName switch
        {
            "path4" => Graph.Path(4),
            "cycle5" => Graph.Cycle(5),
            "complete5" => Graph.Complete(5),
            "grid2x3" => Graph.Grid(2, 3),
            _ => throw new ArgumentOutOfRangeException(nameof(graphName)),
        };

        internal static BruteForceFamily BruteForceSpanningTrees(Graph graph)
        {
            var accepted = new List<int>();
            int edgeCount = graph.EdgeCount;

            if (edgeCount >= 31)
            {
                throw new ArgumentException(
                    $"BruteForceSpanningTrees enumerates all 2^edgeCount subsets and cannot handle {edgeCount} edges.",
                    nameof(graph));
            }

            int bound = 1 << edgeCount;

            for (int mask = 0; mask < bound; mask++)
            {
                var edgeSet = new List<int>();
                for (int i = 0; i < edgeCount; i++)
                {
                    if ((mask & (1 << i)) != 0)
                    {
                        edgeSet.Add(i);
                    }
                }

                if (IsSpanningTree(graph, edgeSet))
                {
                    accepted.Add(mask);
                }
            }

            return BruteForceFamily.FromMasks(edgeCount, accepted);
        }

        private static bool IsSpanningTree(Graph graph, List<int> edgeSet)
        {
            if (edgeSet.Count != graph.VertexCount - 1)
            {
                return false;
            }

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
                int ru = Find(edge.U);
                int rv = Find(edge.V);
                if (ru == rv)
                {
                    return false; // cycle
                }

                parent[ru] = rv;
            }

            int root = Find(0);
            for (int v = 1; v < graph.VertexCount; v++)
            {
                if (Find(v) != root)
                {
                    return false; // disconnected
                }
            }

            return true;
        }

        /// <summary>Builds a connected random graph: a random spanning tree plus extra edges added independently.</summary>
        internal static Graph RandomConnectedGraph(int vertexCount, double extraEdgeProbability, int seed)
        {
            var random = new Random(seed);
            var edges = new List<Edge>();
            var seen = new HashSet<(int, int)>();

            // A random spanning tree via random attachment: vertex i attaches to a uniformly random
            // earlier vertex, guaranteeing connectivity regardless of the extra edges added below.
            var order = Enumerable.Range(0, vertexCount).OrderBy(_ => random.Next()).ToArray();
            for (int i = 1; i < vertexCount; i++)
            {
                int u = order[i];
                int v = order[random.Next(i)];
                AddEdge(edges, seen, u, v);
            }

            for (int u = 0; u < vertexCount; u++)
            {
                for (int v = u + 1; v < vertexCount; v++)
                {
                    if (!seen.Contains((u, v)) && !seen.Contains((v, u)) && random.NextDouble() < extraEdgeProbability)
                    {
                        AddEdge(edges, seen, u, v);
                    }
                }
            }

            return new Graph(vertexCount, edges);

            static void AddEdge(List<Edge> edges, HashSet<(int, int)> seen, int u, int v)
            {
                (int lo, int hi) = u < v ? (u, v) : (v, u);
                if (seen.Add((lo, hi)))
                {
                    edges.Add(new Edge(lo, hi));
                }
            }
        }

        /// <summary>
        /// A byte-for-byte copy of <see cref="SpanningTreeSpec"/>'s logic, except merging two components
        /// always keeps <c>edge.U</c>'s representative instead of canonicalizing to whichever slot number
        /// is smaller. Active/inactive tracking (clearing a slot on forget, reassigning a departing
        /// representative) is untouched and still correct — only the representative <i>value</i> a given
        /// partition of the frontier ends up encoded with is no longer a pure function of that partition,
        /// but also of the history of edge choices that produced it. Two states describing the same
        /// partition can therefore carry different representative numbers and fail to merge — the
        /// ablation the completion criteria ask for ("正準化しないと同じ分割を表す状態が複数生まれ、幅が爆発する").
        /// </summary>
        internal readonly struct UncanonicalSpanningTreeSpec : IArrayDdSpec
        {
            private readonly Graph _graph;
            private readonly FrontierManager _frontierManager;

            public UncanonicalSpanningTreeSpec(Graph graph)
            {
                _graph = graph;
                _frontierManager = new FrontierManager(graph);
            }

            private int ClosedComponentCountSlot => _frontierManager.MaxFrontierSize;

            public int ArrayLength => _frontierManager.MaxFrontierSize + 1;

            public int GetRoot(Span<int> state)
            {
                if (_graph.VertexCount == 1)
                {
                    return DdResult.True;
                }

                if (_graph.EdgeCount == 0)
                {
                    return DdResult.False;
                }

                for (int v = 0; v < _graph.VertexCount; v++)
                {
                    if (_graph.Degree(v) == 0)
                    {
                        return DdResult.False;
                    }
                }

                return _graph.EdgeCount;
            }

            public int GetChild(Span<int> state, int level, int value)
            {
                int edgeIndex = _graph.LevelToEdgeIndex(level);
                Edge edge = _graph.GetEdge(edgeIndex);
                int frontierLength = _frontierManager.MaxFrontierSize;

                IReadOnlyList<int> introducedVertices = _frontierManager.IntroducedVertices(edgeIndex);
                for (int i = 0; i < introducedVertices.Count; i++)
                {
                    SpanningComponentState.Introduce(state, _frontierManager.MateIndex(edgeIndex, introducedVertices[i]));
                }

                if (value == 1)
                {
                    int su = _frontierManager.MateIndex(edgeIndex, edge.U);
                    int sv = _frontierManager.MateIndex(edgeIndex, edge.V);
                    int repU = state[su] - 1;
                    int repV = state[sv] - 1;

                    if (repU == repV)
                    {
                        return DdResult.False;
                    }

                    // Not canonicalized: keeps repU regardless of which slot number is smaller.
                    int keepCode = repU + 1;
                    for (int slot = 0; slot < frontierLength; slot++)
                    {
                        if (state[slot] != SpanningComponentState.SlotEmpty && state[slot] - 1 == repV)
                        {
                            state[slot] = keepCode;
                        }
                    }
                }

                bool isFinalEdge = level == 1;

                IReadOnlyList<int> forgottenVertices = _frontierManager.ForgottenVertices(edgeIndex);
                for (int i = 0; i < forgottenVertices.Count; i++)
                {
                    int slot = _frontierManager.MateIndex(edgeIndex, forgottenVertices[i]);
                    bool closed = SpanningComponentState.Forget(state, frontierLength, slot);
                    if (!closed)
                    {
                        continue;
                    }

                    if (!isFinalEdge)
                    {
                        return DdResult.False;
                    }

                    int closedCount = state[ClosedComponentCountSlot] + 1;
                    if (closedCount > 1)
                    {
                        return DdResult.False;
                    }

                    state[ClosedComponentCountSlot] = closedCount;
                }

                int remaining = level - 1;
                return remaining > 0 ? remaining : DdResult.True;
            }
        }
    }
}
