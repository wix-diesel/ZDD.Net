using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Specs
{
    /// <summary>
    /// M4-5 completion criteria for <see cref="SteinerTreeSpec"/>: <c>MinWeight</c> matches a brute-force
    /// minimum Steiner tree on a small weighted graph, brute-force enumeration matches exactly across
    /// several terminal-set patterns, every enumerated set is directly verified to be an actual tree
    /// (acyclic, connected, containing all terminals, with every leaf a terminal), the all-vertices-terminal
    /// family matches <see cref="SpanningTreeSpec"/> exactly and the two-terminal family matches
    /// <see cref="PathSpec"/> exactly, disconnected terminals build to <c>Empty</c>, and <c>GetChild</c>
    /// does not allocate.
    /// </summary>
    public class SteinerTreeSpecTests
    {
        [Fact]
        public void MinWeightMatchesBruteForceMinimumSteinerTree()
        {
            Graph graph = Graph.Grid(2, 3);
            int[] weights = { 4, 1, 2, 5, 3, 2, 1 };
            Assert.Equal(graph.EdgeCount, weights.Length);
            int[] terminals = { 0, 2, 5 };

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<SteinerTreeSpec>(manager, new SteinerTreeSpec(graph, terminals));

            WeightedSet<int> actual = built.MinWeight(weights);
            int expectedWeight = BruteForceMinimumSteinerWeight(graph, terminals, weights);

            Assert.Equal(expectedWeight, actual.Weight);
            Assert.True(IsSteinerTree(graph, actual.Items.ToArray(), terminals));
            Assert.Equal(expectedWeight, actual.Items.Sum(e => weights[e]));
        }

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
                Zdd built = FrontierBuilder.Build<SteinerTreeSpec>(manager, new SteinerTreeSpec(graph, terminals));

                BruteForceFamily expected = BruteForceSteinerTrees(graph, terminals);

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
                Zdd built = FrontierBuilder.Build<SteinerTreeSpec>(manager, new SteinerTreeSpec(graph, terminals));

                BruteForceFamily expected = BruteForceSteinerTrees(graph, terminals);

                FamilyAssert.AssertSameFamily($"seed={seed} terminals=[{string.Join(",", terminals)}]", built, expected);
            }
        }

        [Theory]
        [InlineData(2, 3)]
        [InlineData(3, 3)]
        public void EveryEnumeratedSetIsAnActualSteinerTree(int rows, int cols)
        {
            Graph graph = Graph.Grid(rows, cols);
            int[] terminals = { 0, graph.VertexCount - 1, graph.VertexCount / 2 };

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<SteinerTreeSpec>(manager, new SteinerTreeSpec(graph, terminals));

            Assert.True(built.Count > 0);

            foreach (int[] edgeSet in built.Sets())
            {
                Assert.True(
                    IsSteinerTree(graph, edgeSet, terminals),
                    $"edge set [{string.Join(",", edgeSet)}] is not a valid Steiner tree for terminals [{string.Join(",", terminals)}]");
            }
        }

        [Fact]
        public void AllVerticesTerminalMatchesSpanningTreeExactly()
        {
            Graph graph = Graph.Grid(2, 3);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd steiner = FrontierBuilder.Build<SteinerTreeSpec>(
                manager, new SteinerTreeSpec(graph, Enumerable.Range(0, graph.VertexCount)));
            Zdd trees = FrontierBuilder.Build<SpanningTreeSpec>(manager, new SpanningTreeSpec(graph));

            Assert.Equal(trees, steiner);
        }

        [Fact]
        public void TwoTerminalsMatchesPathExactly()
        {
            Graph graph = Graph.Grid(3, 3);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            int s = 0;
            int t = graph.VertexCount - 1;

            Zdd steiner = FrontierBuilder.Build<SteinerTreeSpec>(manager, new SteinerTreeSpec(graph, new[] { s, t }));
            Zdd path = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(graph, s, t));

            Assert.Equal(path, steiner);
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
            Zdd built = FrontierBuilder.Build<SteinerTreeSpec>(manager, new SteinerTreeSpec(graph, new[] { 0, 3 }));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void IsolatedTerminalIsEmpty()
        {
            // Vertex 3 has no incident edges at all.
            var graph = new Graph(4, new[] { new Edge(0, 1), new Edge(1, 2) });

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<SteinerTreeSpec>(manager, new SteinerTreeSpec(graph, new[] { 0, 3 }));

            Assert.Equal(manager.Empty, built);
        }

        [Theory]
        [InlineData(new int[0])]
        [InlineData(new[] { 2 })]
        public void ZeroOrOneTerminalAcceptsOnlyTheEmptyEdgeSet(int[] terminals)
        {
            Graph graph = Graph.Path(4);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<SteinerTreeSpec>(manager, new SteinerTreeSpec(graph, terminals));

            int[] onlySet = Assert.Single(built.Sets());
            Assert.Empty(onlySet);
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new SteinerTreeSpec(null!, new[] { 0 }));
        }

        [Fact]
        public void ConstructorRejectsNullTerminals()
        {
            Graph graph = Graph.Path(4);
            Assert.Throws<ArgumentNullException>(() => new SteinerTreeSpec(graph, null!));
        }

        [Fact]
        public void ConstructorRejectsOutOfRangeTerminal()
        {
            Graph graph = Graph.Path(4);
            Assert.Throws<ArgumentOutOfRangeException>(() => new SteinerTreeSpec(graph, new[] { 0, 4 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SteinerTreeSpec(graph, new[] { -1 }));
        }

        [Fact]
        public void ConstructorRejectsRepeatedTerminal()
        {
            Graph graph = Graph.Path(4);
            Assert.Throws<ArgumentException>(() => new SteinerTreeSpec(graph, new[] { 0, 1, 0 }));
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            Graph grid = Graph.Grid(4, 4);
            var spec = new SteinerTreeSpec(grid, new[] { 0, grid.VertexCount - 1, grid.VertexCount / 2 });
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneEdgePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(SteinerTreeSpec spec, Span<int> state, int level)
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

        /// <summary>
        /// Whether <paramref name="edgeSet"/> is a Steiner tree for <paramref name="terminals"/>: acyclic,
        /// every terminal touched, all touched vertices in one component, and every touched vertex of
        /// degree 1 is a terminal (no wasted leaf branches).
        /// </summary>
        private static bool IsSteinerTree(Graph graph, IReadOnlyList<int> edgeSet, IReadOnlyList<int> terminals)
        {
            if (edgeSet.Count == 0)
            {
                return terminals.Count <= 1;
            }

            var degree = new Dictionary<int, int>();
            var parent = new Dictionary<int, int>();

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }

                return x;
            }

            void EnsureVertex(int v)
            {
                if (!parent.ContainsKey(v))
                {
                    parent[v] = v;
                    degree[v] = 0;
                }
            }

            foreach (int edgeIndex in edgeSet)
            {
                Edge edge = graph.GetEdge(edgeIndex);
                EnsureVertex(edge.U);
                EnsureVertex(edge.V);
                degree[edge.U]++;
                degree[edge.V]++;

                int ru = Find(edge.U);
                int rv = Find(edge.V);
                if (ru == rv)
                {
                    return false; // cycle
                }

                parent[ru] = rv;
            }

            // A tree on the touched vertices: edges = touched vertices - 1, which (combined with the
            // cycle check above already having ruled out any cycle) implies a single connected component.
            if (edgeSet.Count != parent.Count - 1)
            {
                return false;
            }

            foreach (int terminal in terminals)
            {
                if (!parent.ContainsKey(terminal))
                {
                    return false; // a terminal never touched by any selected edge
                }
            }

            foreach (KeyValuePair<int, int> entry in degree)
            {
                if (entry.Value == 1 && !terminals.Contains(entry.Key))
                {
                    return false; // a non-terminal leaf: a wasted branch
                }
            }

            return true;
        }

        private static BruteForceFamily BruteForceSteinerTrees(Graph graph, IReadOnlyList<int> terminals)
        {
            var accepted = new List<int>();
            int edgeCount = graph.EdgeCount;

            if (edgeCount >= 31)
            {
                throw new ArgumentException(
                    $"BruteForceSteinerTrees enumerates all 2^edgeCount subsets and cannot handle {edgeCount} edges.",
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

                if (IsSteinerTree(graph, edgeSet, terminals))
                {
                    accepted.Add(mask);
                }
            }

            return BruteForceFamily.FromMasks(edgeCount, accepted);
        }

        private static int BruteForceMinimumSteinerWeight(Graph graph, IReadOnlyList<int> terminals, int[] weights)
        {
            int edgeCount = graph.EdgeCount;
            int bound = 1 << edgeCount;
            int best = int.MaxValue;

            for (int mask = 0; mask < bound; mask++)
            {
                var edgeSet = new List<int>();
                int weight = 0;
                for (int i = 0; i < edgeCount; i++)
                {
                    if ((mask & (1 << i)) != 0)
                    {
                        edgeSet.Add(i);
                        weight += weights[i];
                    }
                }

                if (weight < best && IsSteinerTree(graph, edgeSet, terminals))
                {
                    best = weight;
                }
            }

            return best;
        }
    }
}
