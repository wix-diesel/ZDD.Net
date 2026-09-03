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
    /// M4-4 completion criteria for <see cref="ConnectedSubgraphSpec"/>: matches brute-force enumeration on
    /// small graphs across several terminal-set patterns, every enumerated set actually connects its
    /// terminals, the all-vertices-terminal family's spanning (<c>n-1</c>-edge) members are exactly
    /// <see cref="SpanningTreeSpec"/>'s spanning trees, the two-terminal family's <c>Minimal()</c> members
    /// are exactly <see cref="PathSpec"/>'s <c>s</c>&#8211;<c>t</c> paths, disconnected terminals build to
    /// <c>Empty</c>, the comp-array canonicalization actually reduces the diagram, and <c>GetChild</c> does
    /// not allocate.
    /// </summary>
    public class ConnectedSubgraphSpecTests
    {
        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        public void MatchesBruteForceEnumerationForVariousTerminalSets(string graphName)
        {
            Graph graph = SpanningTreeSpecTests.NamedGraph(graphName);

            foreach (int[] terminals in TerminalPatterns(graph.VertexCount))
            {
                using ZddManager manager = new ZddManager(graph.EdgeCount);
                Zdd built = FrontierBuilder.Build<ConnectedSubgraphSpec>(
                    manager, new ConnectedSubgraphSpec(graph, terminals));

                BruteForceFamily expected = BruteForceConnectedSubgraphs(graph, terminals);

                FamilyAssert.AssertSameFamily($"{graphName} terminals=[{string.Join(",", terminals)}]", built, expected);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void MatchesBruteForceEnumerationOnRandomGraphs(int seed)
        {
            Graph graph = SpanningTreeSpecTests.RandomConnectedGraph(vertexCount: 6, extraEdgeProbability: 0.3, seed);

            foreach (int[] terminals in TerminalPatterns(graph.VertexCount))
            {
                using ZddManager manager = new ZddManager(graph.EdgeCount);
                Zdd built = FrontierBuilder.Build<ConnectedSubgraphSpec>(
                    manager, new ConnectedSubgraphSpec(graph, terminals));

                BruteForceFamily expected = BruteForceConnectedSubgraphs(graph, terminals);

                FamilyAssert.AssertSameFamily($"seed={seed} terminals=[{string.Join(",", terminals)}]", built, expected);
            }
        }

        [Theory]
        [InlineData(2, 3)]
        [InlineData(3, 3)]
        public void EveryEnumeratedSetHasAllTerminalsConnected(int rows, int cols)
        {
            Graph graph = Graph.Grid(rows, cols);
            int[] terminals = { 0, graph.VertexCount - 1, graph.VertexCount / 2 };

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<ConnectedSubgraphSpec>(manager, new ConnectedSubgraphSpec(graph, terminals));

            foreach (int[] edgeSet in built.Sets())
            {
                Assert.True(AreConnected(graph, edgeSet, terminals));
            }
        }

        [Fact]
        public void AllVerticesTerminalSpanningMembersMatchSpanningTree()
        {
            Graph graph = Graph.Grid(2, 3);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd connected = FrontierBuilder.Build<ConnectedSubgraphSpec>(
                manager, new ConnectedSubgraphSpec(graph, Enumerable.Range(0, graph.VertexCount)));
            Zdd trees = FrontierBuilder.Build<SpanningTreeSpec>(manager, new SpanningTreeSpec(graph));

            int spanningEdgeCount = graph.VertexCount - 1;
            var spanningMembers = connected.Sets().Where(set => set.Length == spanningEdgeCount);
            BruteForceFamily connectedSpanning = BruteForceFamily.FromMasks(
                graph.EdgeCount, spanningMembers.Select(set => BruteForceFamily.MaskOf(graph.EdgeCount, set)));
            BruteForceFamily treeFamily = BruteForceFamily.FromMasks(
                graph.EdgeCount, trees.Sets().Select(set => BruteForceFamily.MaskOf(graph.EdgeCount, set)));

            Assert.Equal(treeFamily, connectedSpanning);
        }

        [Fact]
        public void TwoTerminalMinimalMembersMatchPath()
        {
            Graph graph = Graph.Grid(3, 3);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            int s = 0;
            int t = graph.VertexCount - 1;

            Zdd connected = FrontierBuilder.Build<ConnectedSubgraphSpec>(
                manager, new ConnectedSubgraphSpec(graph, new[] { s, t }));
            Zdd path = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(graph, s, t));

            Assert.Equal(path, connected.Minimal());
        }

        [Fact]
        public void DisconnectedTerminalsIsEmpty()
        {
            // Two disjoint triangles: vertices 0-1-2 and 3-4-5, no edge between the halves.
            var graph = new Graph(6, new[]
            {
                new Edge(0, 1), new Edge(1, 2), new Edge(2, 0),
                new Edge(3, 4), new Edge(4, 5), new Edge(5, 3),
            });

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<ConnectedSubgraphSpec>(manager, new ConnectedSubgraphSpec(graph, new[] { 0, 3 }));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void IsolatedTerminalIsEmpty()
        {
            // Vertex 3 has no incident edges at all.
            var graph = new Graph(4, new[] { new Edge(0, 1), new Edge(1, 2) });

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<ConnectedSubgraphSpec>(manager, new ConnectedSubgraphSpec(graph, new[] { 0, 3 }));

            Assert.Equal(manager.Empty, built);
        }

        [Theory]
        [InlineData(new int[0])]
        [InlineData(new[] { 2 })]
        public void ZeroOrOneTerminalAcceptsEveryEdgeSubset(int[] terminals)
        {
            Graph graph = Graph.Path(4);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<ConnectedSubgraphSpec>(manager, new ConnectedSubgraphSpec(graph, terminals));

            Assert.Equal(BigInteger.Pow(2, graph.EdgeCount), built.Count);
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new ConnectedSubgraphSpec(null!, new[] { 0 }));
        }

        [Fact]
        public void ConstructorRejectsNullTerminals()
        {
            Graph graph = Graph.Path(4);
            Assert.Throws<ArgumentNullException>(() => new ConnectedSubgraphSpec(graph, null!));
        }

        [Fact]
        public void ConstructorRejectsOutOfRangeTerminal()
        {
            Graph graph = Graph.Path(4);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConnectedSubgraphSpec(graph, new[] { 0, 4 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConnectedSubgraphSpec(graph, new[] { -1 }));
        }

        [Fact]
        public void ConstructorRejectsRepeatedTerminal()
        {
            Graph graph = Graph.Path(4);
            Assert.Throws<ArgumentException>(() => new ConnectedSubgraphSpec(graph, new[] { 0, 1, 0 }));
        }

        [Fact]
        public void CanonicalStateProducesFewerNodesThanLeavingStaleComponentCodesBehind()
        {
            Graph grid = Graph.Grid(4, 4);
            int[] terminals = { 0, grid.VertexCount - 1 };
            var canonical = new ConnectedSubgraphSpec(grid, terminals);
            var uncanonical = new UncanonicalConnectedSubgraphSpec(grid, terminals);

            long canonicalNodeCount = ArrayTopDownExpander<ConnectedSubgraphSpec>.Expand(canonical).NodeCount;
            long uncanonicalNodeCount = ArrayTopDownExpander<UncanonicalConnectedSubgraphSpec>.Expand(uncanonical).NodeCount;

            Assert.True(
                canonicalNodeCount < uncanonicalNodeCount,
                $"expected clearing forgotten slots to shrink the build, got {canonicalNodeCount} " +
                $"(canonical) vs {uncanonicalNodeCount} (stale slots left behind)");

            using ZddManager manager = new ZddManager(grid.EdgeCount);
            Zdd fromCanonical = FrontierBuilder.Build<ConnectedSubgraphSpec>(manager, canonical);
            Zdd fromUncanonical = FrontierBuilder.Build<UncanonicalConnectedSubgraphSpec>(manager, uncanonical);
            Assert.Equal(fromCanonical, fromUncanonical); // same family regardless — only the build's width differs
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            Graph grid = Graph.Grid(4, 4);
            var spec = new ConnectedSubgraphSpec(grid, new[] { 0, grid.VertexCount - 1 });
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneEdgePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(ConnectedSubgraphSpec spec, Span<int> state, int level)
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

        /// <summary>A handful of terminal-set shapes: none, one, two, three, and "every vertex".</summary>
        private static IEnumerable<int[]> TerminalPatterns(int vertexCount)
        {
            yield return Array.Empty<int>();
            yield return new[] { 0 };
            yield return new[] { 0, vertexCount - 1 };

            if (vertexCount >= 3)
            {
                yield return new[] { 0, vertexCount / 2, vertexCount - 1 };
            }

            yield return Enumerable.Range(0, vertexCount).ToArray();
        }

        /// <summary>Whether every vertex in <paramref name="terminals"/> shares a component under <paramref name="edgeSet"/>.</summary>
        private static bool AreConnected(Graph graph, IReadOnlyList<int> edgeSet, IReadOnlyList<int> terminals)
        {
            if (terminals.Count <= 1)
            {
                return true;
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

        private static BruteForceFamily BruteForceConnectedSubgraphs(Graph graph, IReadOnlyList<int> terminals)
        {
            var accepted = new List<int>();
            int edgeCount = graph.EdgeCount;

            if (edgeCount >= 31)
            {
                throw new ArgumentException(
                    $"BruteForceConnectedSubgraphs enumerates all 2^edgeCount subsets and cannot handle {edgeCount} edges.",
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

                if (AreConnected(graph, edgeSet, terminals))
                {
                    accepted.Add(mask);
                }
            }

            return BruteForceFamily.FromMasks(edgeCount, accepted);
        }

        /// <summary>
        /// A byte-for-byte copy of <see cref="ConnectedSubgraphSpec"/>'s logic, except merging two
        /// components always keeps <c>edge.U</c>'s representative instead of canonicalizing to whichever
        /// slot number is smaller — see <see cref="SpanningTreeSpecTests.UncanonicalSpanningTreeSpec"/> for
        /// why this specific deviation breaks merging without breaking correctness.
        /// </summary>
        internal readonly struct UncanonicalConnectedSubgraphSpec : IArrayDdSpec
        {
            private readonly Graph _graph;
            private readonly FrontierManager _frontierManager;
            private readonly bool[] _isTerminal;
            private readonly int _terminalCount;

            public UncanonicalConnectedSubgraphSpec(Graph graph, IEnumerable<int> terminals)
            {
                _graph = graph;
                _frontierManager = new FrontierManager(graph);
                _isTerminal = new bool[graph.VertexCount];
                _terminalCount = 0;

                foreach (int vertex in terminals)
                {
                    if (!_isTerminal[vertex])
                    {
                        _isTerminal[vertex] = true;
                        _terminalCount++;
                    }
                }
            }

            private int TerminalsSeenSlot => _frontierManager.MaxFrontierSize;

            private int OpenTerminalComponentCountSlot => _frontierManager.MaxFrontierSize + 1;

            public int ArrayLength => _frontierManager.MaxFrontierSize + 2;

            public int GetRoot(Span<int> state)
            {
                if (_terminalCount >= 2)
                {
                    for (int v = 0; v < _graph.VertexCount; v++)
                    {
                        if (_isTerminal[v] && _graph.Degree(v) == 0)
                        {
                            return DdResult.False;
                        }
                    }
                }

                if (_graph.EdgeCount == 0)
                {
                    return _terminalCount <= 1 ? DdResult.True : DdResult.False;
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
                    int vertex = introducedVertices[i];
                    int slot = _frontierManager.MateIndex(edgeIndex, vertex);
                    bool isTerminal = _isTerminal[vertex];
                    state[slot] = isTerminal ? -(slot + 1) : slot + 1;

                    if (isTerminal)
                    {
                        state[TerminalsSeenSlot]++;
                        state[OpenTerminalComponentCountSlot]++;
                    }
                }

                if (value == 1)
                {
                    int su = _frontierManager.MateIndex(edgeIndex, edge.U);
                    int sv = _frontierManager.MateIndex(edgeIndex, edge.V);
                    int codeU = state[su];
                    int codeV = state[sv];
                    int repU = (codeU < 0 ? -codeU : codeU) - 1;
                    int repV = (codeV < 0 ? -codeV : codeV) - 1;

                    if (repU != repV)
                    {
                        bool terminalU = codeU < 0;
                        bool terminalV = codeV < 0;
                        bool resultTerminal = terminalU || terminalV;

                        // Not canonicalized: keeps repU regardless of which slot number is smaller.
                        int keepCode = resultTerminal ? -(repU + 1) : repU + 1;
                        for (int slot = 0; slot < frontierLength; slot++)
                        {
                            int code = state[slot];
                            if (code == ConnectedComponentState.SlotEmpty)
                            {
                                continue;
                            }

                            int rep = (code < 0 ? -code : code) - 1;
                            if (rep == repU || rep == repV)
                            {
                                state[slot] = keepCode;
                            }
                        }

                        if (terminalU && terminalV)
                        {
                            state[OpenTerminalComponentCountSlot]--;
                        }
                    }
                }

                IReadOnlyList<int> forgottenVertices = _frontierManager.ForgottenVertices(edgeIndex);
                for (int i = 0; i < forgottenVertices.Count; i++)
                {
                    int slot = _frontierManager.MateIndex(edgeIndex, forgottenVertices[i]);
                    bool closed = ConnectedComponentState.Forget(state, frontierLength, slot, out bool hadTerminal);

                    if (!closed || !hadTerminal)
                    {
                        continue;
                    }

                    if (state[TerminalsSeenSlot] != _terminalCount || state[OpenTerminalComponentCountSlot] != 1)
                    {
                        return DdResult.False;
                    }

                    state[OpenTerminalComponentCountSlot] = 0;
                }

                int remaining = level - 1;
                return remaining > 0 ? remaining : DdResult.True;
            }
        }
    }
}
