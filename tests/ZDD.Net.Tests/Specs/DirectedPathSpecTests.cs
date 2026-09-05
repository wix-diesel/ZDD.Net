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
    /// M7-3 completion criteria for <see cref="DirectedPathSpec"/>: matches OEIS A007764 on
    /// <see cref="DirectedGraph.Bidirected"/> n×n grids (an undirected simple path has a unique
    /// orientation toward its own two endpoints, so the count is unchanged), matches brute-force
    /// enumeration on small and random directed graphs (vertex count ≤ 8), anti-parallel arcs are never
    /// both usable in one path, a one-way-only graph with an unreachable target is empty,
    /// <see cref="DirectedPathSpec.AllowAnyEndpoints"/> agrees with the union over every ordered
    /// endpoint pair, boundary cases are empty, and the spec composes with <c>.And</c>.
    /// </summary>
    public class DirectedPathSpecTests
    {
        // OEIS A007764: the number of simple paths between opposite corners of an n×n grid graph.
        // DirectedGraph.Bidirected opens every grid edge to both directions; an undirected simple path
        // between two fixed endpoints has exactly one orientation that starts at "from", so the directed
        // count over the bidirected grid must equal the undirected count exactly (docs/design/m7-directed-graphs.md §3.2).
        [Theory]
        [InlineData(2, "2")]
        [InlineData(3, "12")]
        [InlineData(4, "184")]
        [InlineData(5, "8512")]
        [InlineData(6, "1262816")]
        [InlineData(7, "575780564")]
        public void CountMatchesOeisA007764ForBidirectedDiagonalGridPaths(int n, string expected)
        {
            DirectedGraph grid = DirectedGraph.Bidirected(Graph.Grid(n, n));
            using ZddManager manager = new ZddManager(grid.EdgeCount);

            Zdd built = FrontierBuilder.Build<DirectedPathSpec>(manager, new DirectedPathSpec(grid, 0, grid.VertexCount - 1));

            Assert.Equal(BigInteger.Parse(expected), built.Count);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void EveryEnumeratedGridPathIsAValidDirectedSimplePath(int n)
        {
            DirectedGraph grid = DirectedGraph.Bidirected(Graph.Grid(n, n));
            int from = 0;
            int to = grid.VertexCount - 1;
            using ZddManager manager = new ZddManager(grid.EdgeCount);

            Zdd built = FrontierBuilder.Build<DirectedPathSpec>(manager, new DirectedPathSpec(grid, from, to));

            foreach (int[] arcSet in built.Sets())
            {
                Assert.True(IsDirectedSimplePath(grid, from, to, arcSet.ToList()), "every enumerated arc set must be a directed simple path");
            }
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("bidirectedCycle5")]
        [InlineData("complete4")]
        public void MatchesBruteForceEnumerationOnSmallGraphs(string graphName)
        {
            DirectedGraph graph = graphName switch
            {
                "path4" => DirectedGraph.Path(4),
                "bidirectedCycle5" => DirectedGraph.Bidirected(Graph.Cycle(5)),
                "complete4" => DirectedGraph.Complete(4),
                _ => throw new ArgumentOutOfRangeException(nameof(graphName)),
            };

            for (int from = 0; from < graph.VertexCount; from++)
            {
                for (int to = 0; to < graph.VertexCount; to++)
                {
                    if (from == to)
                    {
                        continue;
                    }

                    using ZddManager manager = new ZddManager(graph.EdgeCount);
                    Zdd built = FrontierBuilder.Build<DirectedPathSpec>(manager, new DirectedPathSpec(graph, from, to));

                    BruteForceFamily expected = BruteForceDirectedPaths(graph, from, to);

                    FamilyAssert.AssertSameFamily($"{graphName} from={from} to={to}", built, expected);
                }
            }
        }

        // Vertex count kept to <= 8 per the completion criteria; arc counts kept low enough that the
        // 2^EdgeCount brute-force scan (run once per ordered endpoint pair) stays fast.
        [Theory]
        [InlineData(5, 8, 1)]
        [InlineData(6, 10, 2)]
        [InlineData(8, 14, 3)]
        [InlineData(8, 16, 4)]
        public void MatchesBruteForceEnumerationOnRandomDirectedGraphs(int vertexCount, int arcCount, int seed)
        {
            DirectedGraph graph = RandomDirectedGraph(vertexCount, arcCount, seed);

            for (int from = 0; from < graph.VertexCount; from++)
            {
                for (int to = 0; to < graph.VertexCount; to++)
                {
                    if (from == to)
                    {
                        continue;
                    }

                    using ZddManager manager = new ZddManager(graph.EdgeCount);
                    Zdd built = FrontierBuilder.Build<DirectedPathSpec>(manager, new DirectedPathSpec(graph, from, to));

                    BruteForceFamily expected = BruteForceDirectedPaths(graph, from, to);

                    FamilyAssert.AssertSameFamily(
                        $"n={vertexCount} arcs={arcCount} seed={seed} from={from} to={to}", built, expected);
                }
            }
        }

        [Fact]
        public void AntiParallelArcsAreNeverBothUsedInTheSamePath()
        {
            // 0 <-> 1 <-> 2, every edge open to both directions.
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Path(3));
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DirectedPathSpec>(manager, new DirectedPathSpec(graph, 0, 2));

            // The only directed simple path 0 -> 2 is 0 -> 1 -> 2.
            Assert.Equal(BigInteger.One, built.Count);

            foreach (int[] arcSet in built.Sets())
            {
                bool hasZeroOne = arcSet.Any(i => graph.GetEdge(i).From == 0 && graph.GetEdge(i).To == 1);
                bool hasOneZero = arcSet.Any(i => graph.GetEdge(i).From == 1 && graph.GetEdge(i).To == 0);
                bool hasOneTwo = arcSet.Any(i => graph.GetEdge(i).From == 1 && graph.GetEdge(i).To == 2);
                bool hasTwoOne = arcSet.Any(i => graph.GetEdge(i).From == 2 && graph.GetEdge(i).To == 1);

                Assert.False(hasZeroOne && hasOneZero, "an anti-parallel pair can never both be used in one path");
                Assert.False(hasOneTwo && hasTwoOne, "an anti-parallel pair can never both be used in one path");
            }
        }

        [Fact]
        public void OneWayOnlyGraphWithUnreachableTargetIsEmpty()
        {
            DirectedGraph oneWay = DirectedGraph.Path(4); // 0 -> 1 -> 2 -> 3 only, no arcs the other way
            using ZddManager manager = new ZddManager(oneWay.EdgeCount);

            Zdd forward = FrontierBuilder.Build<DirectedPathSpec>(manager, new DirectedPathSpec(oneWay, 0, 3));
            Zdd backward = FrontierBuilder.Build<DirectedPathSpec>(manager, new DirectedPathSpec(oneWay, 3, 0));

            Assert.Equal(BigInteger.One, forward.Count); // the one forward path
            Assert.Equal(manager.Empty, backward); // no arc runs backward, so 3 -> 0 is unreachable
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("bidirectedCycle5")]
        public void AllowAnyEndpointsMatchesBruteForceEnumeration(string graphName)
        {
            DirectedGraph graph = graphName == "path4" ? DirectedGraph.Path(4) : DirectedGraph.Bidirected(Graph.Cycle(5));
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DirectedPathSpec>(manager, new DirectedPathSpec(graph, 0, 0, allowAnyEndpoints: true));

            BruteForceFamily expected = BruteForceFamily.Empty(graph.EdgeCount);
            for (int from = 0; from < graph.VertexCount; from++)
            {
                for (int to = 0; to < graph.VertexCount; to++)
                {
                    if (from == to)
                    {
                        continue;
                    }

                    expected = expected.Union(BruteForceDirectedPaths(graph, from, to));
                }
            }

            FamilyAssert.AssertSameFamily(graphName, built, expected);
        }

        [Fact]
        public void AllowAnyEndpointsEqualsTheUnionOverEveryOrderedEndpointPair()
        {
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Grid(3, 3));
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd anyEndpoints = FrontierBuilder.Build<DirectedPathSpec>(manager, new DirectedPathSpec(graph, 0, 0, allowAnyEndpoints: true));

            Zdd union = manager.Empty;
            for (int from = 0; from < graph.VertexCount; from++)
            {
                for (int to = 0; to < graph.VertexCount; to++)
                {
                    if (from == to)
                    {
                        continue;
                    }

                    union = union.Union(FrontierBuilder.Build<DirectedPathSpec>(manager, new DirectedPathSpec(graph, from, to)));
                }
            }

            Assert.Equal(union, anyEndpoints);
        }

        [Fact]
        public void FromEqualsToIsEmpty()
        {
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Grid(3, 3));
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DirectedPathSpec>(manager, new DirectedPathSpec(graph, 4, 4));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void DisconnectedEndpointsAreEmpty()
        {
            // Two disjoint directed triangles: 0->1->2->0 and 3->4->5->3, no arc between the halves.
            var graph = new DirectedGraph(6, new[]
            {
                new DirectedEdge(0, 1), new DirectedEdge(1, 2), new DirectedEdge(2, 0),
                new DirectedEdge(3, 4), new DirectedEdge(4, 5), new DirectedEdge(5, 3),
            });

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<DirectedPathSpec>(manager, new DirectedPathSpec(graph, 0, 3));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void IsolatedEndpointIsEmpty()
        {
            // Vertex 3 has no incident arcs at all.
            var graph = new DirectedGraph(4, new[] { new DirectedEdge(0, 1), new DirectedEdge(1, 2) });

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<DirectedPathSpec>(manager, new DirectedPathSpec(graph, 0, 3));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void ConstructorRejectsOutOfRangeEndpoints()
        {
            DirectedGraph graph = DirectedGraph.Path(4);

            Assert.Throws<ArgumentOutOfRangeException>(() => new DirectedPathSpec(graph, -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DirectedPathSpec(graph, 0, 4));
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new DirectedPathSpec(null!, 0, 1));
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            DirectedGraph grid = DirectedGraph.Bidirected(Graph.Grid(4, 4));
            var spec = new DirectedPathSpec(grid, 0, grid.VertexCount - 1);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            // Warm up: first calls may allocate lazily (JIT, etc.) and shouldn't count against the hot path.
            RunOneEdgePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(DirectedPathSpec spec, Span<int> state, int level)
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

        /// <summary>
        /// The directed counterpart of the issue's own example ("a simple s-t path AND at most k
        /// edges"): the spec composes with <c>.And</c> without going through an intermediate,
        /// potentially much larger, unfiltered path family.
        /// </summary>
        [Theory]
        [InlineData(4, 6)]
        [InlineData(4, 8)]
        [InlineData(5, 10)]
        public void DirectedPathAndArcCountMatchesPostFilterIntersection(int gridSize, int maxArcs)
        {
            DirectedGraph grid = DirectedGraph.Bidirected(Graph.Grid(gridSize, gridSize));
            int from = 0;
            int to = grid.VertexCount - 1;
            using ZddManager manager = new ZddManager(grid.EdgeCount);

            var pathSpec = new DirectedPathSpec(grid, from, to);
            var atMostKArcs = new CardinalitySpec(grid.EdgeCount, 0, maxArcs);

            ArrayDdSpecAdapter<DirectedPathSpec> pathAsSpec = pathSpec.AsDdSpec();

            Zdd direct = FrontierBuilder.Build<
                AndSpec<ArrayDdSpecAdapter<DirectedPathSpec>, int[], CardinalitySpec, int>,
                AndState<int[], int>>(
                manager,
                pathAsSpec.And<ArrayDdSpecAdapter<DirectedPathSpec>, int[], CardinalitySpec, int>(atMostKArcs));

            Zdd postFiltered = FrontierBuilder.Build<DirectedPathSpec>(manager, pathSpec)
                .Intersect(FrontierBuilder.Build<CardinalitySpec, int>(manager, atMostKArcs));

            Assert.Equal(postFiltered, direct);
        }

        /// <summary>Builds a random directed graph over <paramref name="arcCount"/> distinct ordered pairs of distinct vertices.</summary>
        private static DirectedGraph RandomDirectedGraph(int vertexCount, int arcCount, int seed)
        {
            var random = new Random(seed);
            var candidates = new List<DirectedEdge>();
            for (int u = 0; u < vertexCount; u++)
            {
                for (int v = 0; v < vertexCount; v++)
                {
                    if (u != v)
                    {
                        candidates.Add(new DirectedEdge(u, v));
                    }
                }
            }

            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            return new DirectedGraph(vertexCount, candidates.Take(Math.Min(arcCount, candidates.Count)));
        }

        /// <summary>Brute-force directed <c>from</c>–<c>to</c> paths for a graph small enough to enumerate every arc subset.</summary>
        private static BruteForceFamily BruteForceDirectedPaths(DirectedGraph graph, int from, int to)
        {
            var accepted = new List<int>();
            int edgeCount = graph.EdgeCount;

            // 1 << edgeCount overflows (and would silently under-enumerate) at 31+ arcs; this helper is
            // only ever meant for graphs small enough for a full 2^edgeCount scan to be affordable.
            if (edgeCount >= 31)
            {
                throw new ArgumentException(
                    $"BruteForceDirectedPaths enumerates all 2^edgeCount subsets and cannot handle {edgeCount} arcs.",
                    nameof(graph));
            }

            int bound = 1 << edgeCount;

            for (int mask = 0; mask < bound; mask++)
            {
                var arcSet = new List<int>();
                for (int i = 0; i < edgeCount; i++)
                {
                    if ((mask & (1 << i)) != 0)
                    {
                        arcSet.Add(i);
                    }
                }

                if (arcSet.Count == 0)
                {
                    continue;
                }

                if (IsDirectedSimplePath(graph, from, to, arcSet))
                {
                    accepted.Add(mask);
                }
            }

            return BruteForceFamily.FromMasks(edgeCount, accepted);
        }

        /// <summary>Checks that <paramref name="arcSet"/> is exactly one directed simple path from <paramref name="from"/> to <paramref name="to"/>.</summary>
        private static bool IsDirectedSimplePath(DirectedGraph graph, int from, int to, List<int> arcSet)
        {
            var outDegree = new int[graph.VertexCount];
            var inDegree = new int[graph.VertexCount];
            var outArc = new int[graph.VertexCount];
            Array.Fill(outArc, -1);

            foreach (int edgeIndex in arcSet)
            {
                DirectedEdge arc = graph.GetEdge(edgeIndex);
                outDegree[arc.From]++;
                inDegree[arc.To]++;
                outArc[arc.From] = arc.To;
            }

            for (int v = 0; v < graph.VertexCount; v++)
            {
                if (v == from)
                {
                    if (!(outDegree[v] == 1 && inDegree[v] == 0))
                    {
                        return false;
                    }
                }
                else if (v == to)
                {
                    if (!(inDegree[v] == 1 && outDegree[v] == 0))
                    {
                        return false;
                    }
                }
                else if (!((outDegree[v] == 0 && inDegree[v] == 0) || (outDegree[v] == 1 && inDegree[v] == 1)))
                {
                    return false; // an undirected degree-1 dead end is never a valid path interior
                }
            }

            // Walk from `from` following each vertex's unique chosen outgoing arc (every vertex on the
            // walk has out-degree exactly 1 among the chosen arcs by the checks above). Reaching `to`
            // after exactly arcSet.Count steps, with no vertex repeated, rules out both a premature dead
            // end and a separate disjoint cycle sitting among the chosen arcs.
            var visited = new HashSet<int> { from };
            int current = from;
            int steps = 0;
            while (current != to)
            {
                int next = outArc[current];
                if (next < 0 || !visited.Add(next))
                {
                    return false;
                }

                current = next;
                steps++;
            }

            return steps == arcSet.Count;
        }
    }
}
