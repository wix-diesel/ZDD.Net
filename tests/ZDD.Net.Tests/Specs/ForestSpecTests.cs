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
    /// M2-9 completion criteria for <see cref="ForestSpec"/>: the count matches brute-force enumeration
    /// (with and without a fixed component target), every enumerated set really is a forest with the right
    /// number of trees, an unconstrained forest's count matches "every acyclic edge subset" independently
    /// computed, and the comp-array canonicalization actually reduces the diagram.
    /// </summary>
    public class ForestSpecTests
    {
        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        public void UnconstrainedCountMatchesBruteForceEnumeration(string graphName)
        {
            Graph graph = SpanningTreeSpecTests.NamedGraph(graphName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<ForestSpec>(manager, new ForestSpec(graph));

            BruteForceFamily expected = BruteForceForests(graph, components: null);

            FamilyAssert.AssertSameFamily(graphName, built, expected);
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        public void ConstrainedComponentCountMatchesBruteForceEnumeration(string graphName)
        {
            Graph graph = SpanningTreeSpecTests.NamedGraph(graphName);

            for (int k = 1; k <= graph.VertexCount; k++)
            {
                using ZddManager manager = new ZddManager(graph.EdgeCount);
                Zdd built = FrontierBuilder.Build<ForestSpec>(manager, new ForestSpec(graph, k));

                BruteForceFamily expected = BruteForceForests(graph, k);

                FamilyAssert.AssertSameFamily($"{graphName} k={k}", built, expected);
            }
        }

        [Fact]
        public void OneComponentEqualsSpanningTree()
        {
            Graph graph = Graph.Grid(3, 3);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd forest = FrontierBuilder.Build<ForestSpec>(manager, new ForestSpec(graph, components: 1));
            Zdd tree = FrontierBuilder.Build<SpanningTreeSpec>(manager, new SpanningTreeSpec(graph));

            Assert.Equal(tree, forest);
        }

        [Fact]
        public void VertexCountComponentsIsTheEdgelessFamily()
        {
            Graph graph = Graph.Grid(2, 3);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<ForestSpec>(manager, new ForestSpec(graph, graph.VertexCount));

            Assert.Equal(BigInteger.One, built.Count);
            Assert.Equal(Array.Empty<int>(), Assert.Single(built.Sets()));
        }

        [Theory]
        [InlineData(2, 3)]
        [InlineData(3, 3)]
        public void EveryEnumeratedGridSetIsAValidForest(int rows, int cols)
        {
            Graph graph = Graph.Grid(rows, cols);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<ForestSpec>(manager, new ForestSpec(graph));

            foreach (int[] edgeSet in built.Sets())
            {
                AssertIsForest(graph, edgeSet, components: null);
            }

            for (int k = 1; k <= graph.VertexCount; k++)
            {
                Zdd withK = FrontierBuilder.Build<ForestSpec>(manager, new ForestSpec(graph, k));
                foreach (int[] edgeSet in withK.Sets())
                {
                    AssertIsForest(graph, edgeSet, k);
                }
            }
        }

        [Fact]
        public void ComponentsMustBePositive()
        {
            Graph graph = Graph.Path(4);

            Assert.Throws<ArgumentOutOfRangeException>(() => new ForestSpec(graph, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ForestSpec(graph, -1));
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new ForestSpec(null!));
        }

        [Fact]
        public void TooFewComponentsForIsolatedVerticesIsEmpty()
        {
            // Vertex 3 has no incident edges: it is always its own tree, so 1 component is unreachable.
            var graph = new Graph(4, new[] { new Edge(0, 1), new Edge(1, 2) });

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<ForestSpec>(manager, new ForestSpec(graph, components: 1));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void CanonicalStateProducesFewerNodesThanLeavingRepresentativesUnnormalized()
        {
            Graph grid = Graph.Grid(4, 4);
            var canonical = new ForestSpec(grid);
            var uncanonical = new UncanonicalForestSpec(grid);

            long canonicalNodeCount = ArrayTopDownExpander<ForestSpec>.Expand(canonical).NodeCount;
            long uncanonicalNodeCount = ArrayTopDownExpander<UncanonicalForestSpec>.Expand(uncanonical).NodeCount;

            Assert.True(
                canonicalNodeCount < uncanonicalNodeCount,
                $"expected canonicalizing the representative to shrink the build, got {canonicalNodeCount} " +
                $"(canonical) vs {uncanonicalNodeCount} (non-canonical representative choice)");

            using ZddManager manager = new ZddManager(grid.EdgeCount);
            Zdd fromCanonical = FrontierBuilder.Build<ForestSpec>(manager, canonical);
            Zdd fromUncanonical = FrontierBuilder.Build<UncanonicalForestSpec>(manager, uncanonical);
            Assert.Equal(fromCanonical, fromUncanonical); // same family regardless — only the build's width differs
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            Graph grid = Graph.Grid(4, 4);
            var spec = new ForestSpec(grid);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneEdgePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(ForestSpec spec, Span<int> state, int level)
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

        private static void AssertIsForest(Graph graph, int[] edgeSet, int? components)
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
                int ru = Find(edge.U);
                int rv = Find(edge.V);
                Assert.NotEqual(ru, rv); // no cycle
                parent[ru] = rv;
            }

            if (components is int expected)
            {
                var roots = new HashSet<int>();
                for (int v = 0; v < graph.VertexCount; v++)
                {
                    roots.Add(Find(v));
                }

                Assert.Equal(expected, roots.Count);
            }
        }

        private static BruteForceFamily BruteForceForests(Graph graph, int? components)
        {
            var accepted = new List<int>();
            int edgeCount = graph.EdgeCount;

            if (edgeCount >= 31)
            {
                throw new ArgumentException(
                    $"BruteForceForests enumerates all 2^edgeCount subsets and cannot handle {edgeCount} edges.",
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

                if (IsForest(graph, edgeSet, components))
                {
                    accepted.Add(mask);
                }
            }

            return BruteForceFamily.FromMasks(edgeCount, accepted);
        }

        private static bool IsForest(Graph graph, List<int> edgeSet, int? components)
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
                int ru = Find(edge.U);
                int rv = Find(edge.V);
                if (ru == rv)
                {
                    return false; // cycle
                }

                parent[ru] = rv;
            }

            if (components is int expected)
            {
                var roots = new HashSet<int>();
                for (int v = 0; v < graph.VertexCount; v++)
                {
                    roots.Add(Find(v));
                }

                return roots.Count == expected;
            }

            return true;
        }

        /// <summary>
        /// A byte-for-byte copy of <see cref="ForestSpec"/>'s logic, except merging two components always
        /// keeps <c>edge.U</c>'s representative instead of canonicalizing to the smaller slot number — see
        /// <see cref="SpanningTreeSpecTests.UncanonicalSpanningTreeSpec"/> for why this specific deviation
        /// breaks merging without breaking correctness.
        /// </summary>
        internal readonly struct UncanonicalForestSpec : IArrayDdSpec
        {
            private readonly Graph _graph;
            private readonly FrontierManager _frontierManager;

            public UncanonicalForestSpec(Graph graph)
            {
                _graph = graph;
                _frontierManager = new FrontierManager(graph);
            }

            public int ArrayLength => _frontierManager.MaxFrontierSize + 1;

            public int GetRoot(Span<int> state)
            {
                if (_graph.EdgeCount == 0)
                {
                    return DdResult.True;
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

                IReadOnlyList<int> forgottenVertices = _frontierManager.ForgottenVertices(edgeIndex);
                for (int i = 0; i < forgottenVertices.Count; i++)
                {
                    int slot = _frontierManager.MateIndex(edgeIndex, forgottenVertices[i]);
                    SpanningComponentState.Forget(state, frontierLength, slot);
                }

                int remaining = level - 1;
                return remaining > 0 ? remaining : DdResult.True;
            }
        }
    }
}
