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
    /// M2-8 completion criteria for <see cref="PathSpec"/>: matches OEIS A007764 on n×n grids (the
    /// library's flagship number), matches brute-force enumeration on small graphs, every enumerated
    /// set really is a simple path, <see cref="PathSpec.AllowAnyEndpoints"/> agrees with the union over
    /// every terminal pair, boundary cases (<c>s == t</c>, disconnected, isolated endpoint) are empty,
    /// and the mate-array canonicalization actually reduces the diagram (turning it off grows the build).
    /// </summary>
    public class PathSpecTests
    {
        // OEIS A007764: the number of simple paths between opposite corners of an n×n grid graph.
        [Theory]
        [InlineData(2, "2")]
        [InlineData(3, "12")]
        [InlineData(4, "184")]
        [InlineData(5, "8512")]
        [InlineData(6, "1262816")]
        [InlineData(7, "575780564")]
        public void CountMatchesOeisA007764ForDiagonalGridPaths(int n, string expected)
        {
            Graph grid = Graph.Grid(n, n);
            using ZddManager manager = new ZddManager(grid.EdgeCount);

            Zdd built = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(grid, 0, grid.VertexCount - 1));

            Assert.Equal(BigInteger.Parse(expected), built.Count);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void EveryEnumeratedGridPathIsAValidSimplePath(int n)
        {
            Graph grid = Graph.Grid(n, n);
            int s = 0;
            int t = grid.VertexCount - 1;
            using ZddManager manager = new ZddManager(grid.EdgeCount);

            Zdd built = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(grid, s, t));

            foreach (int[] edgeSet in built.Sets())
            {
                AssertIsSimplePath(grid, s, t, edgeSet);
            }
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        public void MatchesBruteForceEnumerationOnSmallGraphs(string graphName)
        {
            Graph graph = graphName switch
            {
                "path4" => Graph.Path(4),
                "cycle5" => Graph.Cycle(5),
                "complete5" => Graph.Complete(5),
                _ => throw new ArgumentOutOfRangeException(nameof(graphName)),
            };

            for (int s = 0; s < graph.VertexCount; s++)
            {
                for (int t = 0; t < graph.VertexCount; t++)
                {
                    if (s == t)
                    {
                        continue;
                    }

                    using ZddManager manager = new ZddManager(graph.EdgeCount);
                    Zdd built = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(graph, s, t));

                    BruteForceFamily expected = BruteForcePaths(graph, s, t);

                    FamilyAssert.AssertSameFamily($"{graphName} s={s} t={t}", built, expected);
                }
            }
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        public void AllowAnyEndpointsMatchesBruteForceEnumeration(string graphName)
        {
            Graph graph = graphName == "path4" ? Graph.Path(4) : Graph.Cycle(5);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(graph, 0, 0, allowAnyEndpoints: true));

            BruteForceFamily expected = BruteForceFamily.Empty(graph.EdgeCount);
            for (int s = 0; s < graph.VertexCount; s++)
            {
                for (int t = s + 1; t < graph.VertexCount; t++)
                {
                    expected = expected.Union(BruteForcePaths(graph, s, t));
                }
            }

            FamilyAssert.AssertSameFamily(graphName, built, expected);
        }

        [Fact]
        public void AllowAnyEndpointsEqualsTheUnionOverEveryTerminalPair()
        {
            Graph graph = Graph.Grid(3, 3);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd anyEndpoints = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(graph, 0, 0, allowAnyEndpoints: true));

            Zdd union = manager.Empty;
            for (int s = 0; s < graph.VertexCount; s++)
            {
                for (int t = s + 1; t < graph.VertexCount; t++)
                {
                    union = union.Union(FrontierBuilder.Build<PathSpec>(manager, new PathSpec(graph, s, t)));
                }
            }

            Assert.Equal(union, anyEndpoints);
        }

        [Fact]
        public void SEqualsTIsEmpty()
        {
            Graph graph = Graph.Grid(3, 3);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(graph, 4, 4));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void DisconnectedEndpointsAreEmpty()
        {
            // Two disjoint triangles: vertices 0-1-2 and 3-4-5, no edge between the halves.
            var graph = new Graph(6, new[]
            {
                new Edge(0, 1), new Edge(1, 2), new Edge(2, 0),
                new Edge(3, 4), new Edge(4, 5), new Edge(5, 3),
            });

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(graph, 0, 3));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void IsolatedEndpointIsEmpty()
        {
            // Vertex 3 has no incident edges at all.
            var graph = new Graph(4, new[] { new Edge(0, 1), new Edge(1, 2) });

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(graph, 0, 3));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void ConstructorRejectsOutOfRangeEndpoints()
        {
            Graph graph = Graph.Path(4);

            Assert.Throws<ArgumentOutOfRangeException>(() => new PathSpec(graph, -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PathSpec(graph, 0, 4));
        }

        [Fact]
        public void CanonicalStateProducesFewerNodesThanLeavingStaleMateCodesBehind()
        {
            Graph grid = Graph.Grid(5, 5);
            var canonical = new PathSpec(grid, 0, grid.VertexCount - 1);
            var uncanonical = new UncanonicalPathSpec(grid, 0, grid.VertexCount - 1);

            long canonicalNodeCount = ArrayTopDownExpander<PathSpec>.Expand(canonical).NodeCount;
            long uncanonicalNodeCount = ArrayTopDownExpander<UncanonicalPathSpec>.Expand(uncanonical).NodeCount;

            Assert.True(
                canonicalNodeCount < uncanonicalNodeCount,
                $"expected clearing forgotten slots to shrink the build, got {canonicalNodeCount} " +
                $"(canonical) vs {uncanonicalNodeCount} (stale slots left behind)");

            using ZddManager manager = new ZddManager(grid.EdgeCount);
            Zdd fromCanonical = FrontierBuilder.Build<PathSpec>(manager, canonical);
            Zdd fromUncanonical = FrontierBuilder.Build<UncanonicalPathSpec>(manager, uncanonical);
            Assert.Equal(fromCanonical, fromUncanonical); // same family regardless — only the build's width differs
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            Graph grid = Graph.Grid(4, 4);
            var spec = new PathSpec(grid, 0, grid.VertexCount - 1);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            // Warm up: first calls may allocate lazily (JIT, etc.) and shouldn't count against the hot path.
            RunOneEdgePerLevel(spec, state, rootLevel);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(PathSpec spec, Span<int> state, int level)
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

        /// <summary>Checks that <paramref name="edgeSet"/> is exactly one simple path from <paramref name="s"/> to <paramref name="t"/>.</summary>
        private static void AssertIsSimplePath(Graph graph, int s, int t, int[] edgeSet)
        {
            var degree = new int[graph.VertexCount];
            var adjacency = new List<int>[graph.VertexCount];
            for (int v = 0; v < graph.VertexCount; v++)
            {
                adjacency[v] = new List<int>();
            }

            foreach (int edgeIndex in edgeSet)
            {
                Edge edge = graph.GetEdge(edgeIndex);
                degree[edge.U]++;
                degree[edge.V]++;
                adjacency[edge.U].Add(edge.V);
                adjacency[edge.V].Add(edge.U);
            }

            Assert.True(edgeSet.Length >= 1, "a path must use at least one edge");

            for (int v = 0; v < graph.VertexCount; v++)
            {
                if (v == s || v == t)
                {
                    Assert.True(degree[v] == 1, $"endpoint {v} must have degree 1, has {degree[v]}");
                }
                else
                {
                    Assert.True(degree[v] is 0 or 2, $"non-endpoint {v} must have degree 0 or 2, has {degree[v]}");
                }
            }

            // Connectivity: walking from s using only degree-2 pass-throughs must reach t after
            // exactly edgeSet.Length steps (no cycle, no second disjoint component).
            int steps = 0;
            int previous = -1;
            int current = s;
            while (current != t)
            {
                int next = adjacency[current].First(candidate => candidate != previous);
                previous = current;
                current = next;
                steps++;
                Assert.True(steps <= edgeSet.Length, "walking from s did not reach t within the edge count");
            }

            Assert.Equal(edgeSet.Length, steps);
        }

        /// <summary>Brute-force <c>s</c>–<c>t</c> paths for a graph small enough to enumerate every edge subset.</summary>
        private static BruteForceFamily BruteForcePaths(Graph graph, int s, int t)
        {
            var accepted = new List<int>();
            int edgeCount = graph.EdgeCount;
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

                if (edgeSet.Count == 0)
                {
                    continue;
                }

                if (IsSimplePath(graph, s, t, edgeSet))
                {
                    accepted.Add(mask);
                }
            }

            return BruteForceFamily.FromMasks(edgeCount, accepted);
        }

        private static bool IsSimplePath(Graph graph, int s, int t, List<int> edgeSet)
        {
            var degree = new int[graph.VertexCount];
            foreach (int edgeIndex in edgeSet)
            {
                Edge edge = graph.GetEdge(edgeIndex);
                degree[edge.U]++;
                degree[edge.V]++;
            }

            for (int v = 0; v < graph.VertexCount; v++)
            {
                int expected = v == s || v == t ? 1 : 0;
                if (v != s && v != t && degree[v] is not (0 or 2))
                {
                    return false;
                }

                if ((v == s || v == t) && degree[v] != 1)
                {
                    return false;
                }

                _ = expected;
            }

            // Union-find over the chosen edges: must form exactly one component containing s and t,
            // with vertex count == edge count + 1 (a tree), which for this degree sequence means a path.
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

            var touched = new HashSet<int>();
            foreach (int edgeIndex in edgeSet)
            {
                Edge edge = graph.GetEdge(edgeIndex);
                touched.Add(edge.U);
                touched.Add(edge.V);

                int ru = Find(edge.U);
                int rv = Find(edge.V);
                if (ru == rv)
                {
                    return false; // cycle
                }

                parent[ru] = rv;
            }

            return touched.Select(Find).Distinct().Count() == 1 && Find(s) == Find(t);
        }

        /// <summary>
        /// A byte-for-byte copy of <see cref="PathSpec"/>'s logic, except a forgotten slot keeps
        /// whatever mate code it last held instead of being reset to <c>SlotIsolated</c> — the
        /// ablation the completion criteria ask for, proving the reset is what keeps otherwise
        /// state-identical builds from splitting apart.
        /// </summary>
        private readonly struct UncanonicalPathSpec : IArrayDdSpec
        {
            private const int SlotIsolated = 0;
            private const int SlotFixed = -1;
            private const int SlotEndpointDone = -2;

            private readonly Graph _graph;
            private readonly FrontierManager _frontierManager;
            private readonly int _s;
            private readonly int _t;

            public UncanonicalPathSpec(Graph graph, int s, int t)
            {
                _graph = graph;
                _s = s;
                _t = t;
                _frontierManager = new FrontierManager(graph);
            }

            public int ArrayLength => _frontierManager.MaxFrontierSize + 1;

            public int GetRoot(Span<int> state)
            {
                if (_graph.EdgeCount == 0 || _s == _t || _graph.Degree(_s) == 0 || _graph.Degree(_t) == 0)
                {
                    return DdResult.False;
                }

                return _graph.EdgeCount;
            }

            public int GetChild(Span<int> state, int level, int value)
            {
                int edgeIndex = _graph.LevelToEdgeIndex(level);
                Edge edge = _graph.GetEdge(edgeIndex);

                foreach (int introduced in _frontierManager.IntroducedVertices(edgeIndex))
                {
                    state[_frontierManager.MateIndex(edgeIndex, introduced)] = SlotIsolated;
                }

                if (value == 1 && !TakeEdge(state, edgeIndex, edge))
                {
                    return DdResult.False;
                }

                foreach (int forgotten in _frontierManager.ForgottenVertices(edgeIndex))
                {
                    if (!Forget(state, edgeIndex, forgotten))
                    {
                        return DdResult.False;
                    }
                }

                int remaining = level - 1;
                return remaining > 0 ? remaining : DdResult.True;
            }

            private bool TakeEdge(Span<int> state, int edgeIndex, Edge edge)
            {
                int su = _frontierManager.MateIndex(edgeIndex, edge.U);
                int sv = _frontierManager.MateIndex(edgeIndex, edge.V);
                int mu = state[su];
                int mv = state[sv];

                if (mu == SlotFixed || mv == SlotFixed)
                {
                    return false;
                }

                if ((mu >= 1 && mu - 1 == sv) || (mv >= 1 && mv - 1 == su))
                {
                    return false;
                }

                if (mu == SlotIsolated && mv == SlotIsolated)
                {
                    state[su] = sv + 1;
                    state[sv] = su + 1;
                }
                else if (mu == SlotIsolated)
                {
                    state[su] = mv;
                    state[sv] = SlotFixed;
                    if (mv >= 1)
                    {
                        state[mv - 1] = su + 1;
                    }
                }
                else if (mv == SlotIsolated)
                {
                    state[sv] = mu;
                    state[su] = SlotFixed;
                    if (mu >= 1)
                    {
                        state[mu - 1] = sv + 1;
                    }
                }
                else
                {
                    state[su] = SlotFixed;
                    state[sv] = SlotFixed;
                    if (mu >= 1)
                    {
                        state[mu - 1] = mv;
                    }

                    if (mv >= 1)
                    {
                        state[mv - 1] = mu;
                    }
                }

                return true;
            }

            private bool Forget(Span<int> state, int edgeIndex, int vertex)
            {
                int slot = _frontierManager.MateIndex(edgeIndex, vertex);
                int mate = state[slot];
                bool isTerminal = vertex == _s || vertex == _t;

                if (isTerminal)
                {
                    if (mate == SlotIsolated || mate == SlotFixed)
                    {
                        return false;
                    }
                }
                else if (mate != SlotIsolated && mate != SlotFixed)
                {
                    return false;
                }

                if (mate >= 1)
                {
                    state[mate - 1] = SlotEndpointDone;
                }

                // Deliberately omitted: PathSpec resets state[slot] to SlotIsolated here. Leaving the
                // stale code behind means a slot's leftover value from this now-forgotten vertex can
                // keep two otherwise-identical states from being recognized as equal.
                return true;
            }
        }
    }
}
