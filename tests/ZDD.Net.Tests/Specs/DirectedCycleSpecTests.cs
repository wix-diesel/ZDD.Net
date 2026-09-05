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
    /// M7-4 completion criteria for <see cref="DirectedCycleSpec"/>: <see cref="DirectedGraph.Bidirected"/>'s
    /// directed simple cycle count is exactly double the undirected count (one per orientation), a one-way
    /// <see cref="DirectedGraph.Cycle"/> has exactly one cycle, the 2-vertex anti-parallel "digon" is never a
    /// cycle, matches brute-force enumeration on small and random directed graphs (vertex count ≤ 8), every
    /// enumerated arc set really is a valid directed cycle family member, <see cref="DirectedCycleSpec.Single"/>
    /// is a subset of the non-<see cref="DirectedCycleSpec.Single"/> family, and <c>GetChild</c> does not allocate.
    /// </summary>
    public class DirectedCycleSpecTests
    {
        // docs/design/m7-directed-graphs.md §3.3's own acceptance test: each undirected simple cycle has
        // exactly two directed orientations, and (per the class remarks on DirectedCycleSpec) the
        // anti-parallel "digon" every Bidirected edge creates is excluded, so the relation is exact — not
        // just an upper bound.
        [Theory]
        [InlineData("complete3")]
        [InlineData("complete4")]
        [InlineData("complete5")]
        [InlineData("complete6")]
        [InlineData("cycle5")]
        [InlineData("cycle6")]
        [InlineData("grid2x3")]
        [InlineData("petersen")]
        public void BidirectedSimpleCycleCountIsExactlyDoubleTheUndirectedCount(string graphName)
        {
            Graph undirected = UndirectedGraphFor(graphName);
            DirectedGraph directed = DirectedGraph.Bidirected(undirected);

            using ZddManager undirectedManager = new ZddManager(undirected.EdgeCount);
            using ZddManager directedManager = new ZddManager(directed.EdgeCount);

            Zdd undirectedCycles = FrontierBuilder.Build<CycleSpec>(undirectedManager, new CycleSpec(undirected, single: true));
            Zdd directedCycles = FrontierBuilder.Build<DirectedCycleSpec>(directedManager, new DirectedCycleSpec(directed, single: true));

            Assert.Equal(undirectedCycles.Count * 2, directedCycles.Count);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(8)]
        public void OneWayCycleGraphHasExactlyOneCycleInEitherMode(int n)
        {
            DirectedGraph graph = DirectedGraph.Cycle(n);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd single = FrontierBuilder.Build<DirectedCycleSpec>(manager, new DirectedCycleSpec(graph, single: true));
            Zdd multi = FrontierBuilder.Build<DirectedCycleSpec>(manager, new DirectedCycleSpec(graph, single: false));

            Assert.Equal(BigInteger.One, single.Count);
            Assert.Equal(BigInteger.One, multi.Count);
            Assert.Equal(single, multi);
        }

        [Fact]
        public void TwoVertexAntiParallelPairIsNeverACycle()
        {
            // DirectedGraph.Cycle(2) is exactly the anti-parallel pair 0->1, 1->0 — a "digon", not a real
            // cycle (see DirectedCycleSpec's remarks on freshness). Both arcs form the family's only
            // candidate closure, and it must be rejected in either mode.
            DirectedGraph graph = DirectedGraph.Cycle(2);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd single = FrontierBuilder.Build<DirectedCycleSpec>(manager, new DirectedCycleSpec(graph, single: true));
            Zdd multi = FrontierBuilder.Build<DirectedCycleSpec>(manager, new DirectedCycleSpec(graph, single: false));

            Assert.Equal(manager.Empty, single);
            Assert.Equal(manager.Empty, multi);
        }

        [Theory]
        [InlineData("bidirectedPath4")]
        [InlineData("bidirectedTriangle")]
        [InlineData("oneWayTriangle")]
        [InlineData("complete4")]
        public void MatchesBruteForceEnumerationOnSmallGraphsForBothModes(string graphName)
        {
            DirectedGraph graph = DirectedGraphFor(graphName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd single = FrontierBuilder.Build<DirectedCycleSpec>(manager, new DirectedCycleSpec(graph, single: true));
            BruteForceFamily expectedSingle = BruteForceDirectedCycles(graph, single: true);
            FamilyAssert.AssertSameFamily($"{graphName} single", single, expectedSingle);

            Zdd multi = FrontierBuilder.Build<DirectedCycleSpec>(manager, new DirectedCycleSpec(graph, single: false));
            BruteForceFamily expectedMulti = BruteForceDirectedCycles(graph, single: false);
            FamilyAssert.AssertSameFamily($"{graphName} multi", multi, expectedMulti);
        }

        // Vertex count kept to <= 8 per the completion criteria; arc counts kept low enough that the
        // 2^EdgeCount brute-force scan stays fast.
        [Theory]
        [InlineData(5, 8, 1)]
        [InlineData(6, 10, 2)]
        [InlineData(8, 14, 3)]
        [InlineData(8, 16, 4)]
        public void MatchesBruteForceEnumerationOnRandomDirectedGraphs(int vertexCount, int arcCount, int seed)
        {
            DirectedGraph graph = RandomDirectedGraph(vertexCount, arcCount, seed);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd single = FrontierBuilder.Build<DirectedCycleSpec>(manager, new DirectedCycleSpec(graph, single: true));
            FamilyAssert.AssertSameFamily(
                $"n={vertexCount} arcs={arcCount} seed={seed} single", single, BruteForceDirectedCycles(graph, single: true));

            Zdd multi = FrontierBuilder.Build<DirectedCycleSpec>(manager, new DirectedCycleSpec(graph, single: false));
            FamilyAssert.AssertSameFamily(
                $"n={vertexCount} arcs={arcCount} seed={seed} multi", multi, BruteForceDirectedCycles(graph, single: false));
        }

        [Theory]
        [InlineData("bidirectedTriangle")]
        [InlineData("complete4")]
        public void EverySingleCycleIsOneDirectedSimpleCycle(string graphName)
        {
            DirectedGraph graph = DirectedGraphFor(graphName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DirectedCycleSpec>(manager, new DirectedCycleSpec(graph, single: true));

            foreach (int[] arcSet in built.Sets())
            {
                Assert.True(
                    IsDirectedCycleFamilyMember(graph, arcSet, single: true),
                    "every enumerated arc set must be exactly one directed simple cycle");
            }
        }

        [Theory]
        [InlineData("bidirectedTriangle")]
        [InlineData("complete4")]
        public void SingleIsASubsetOfMulti(string graphName)
        {
            DirectedGraph graph = DirectedGraphFor(graphName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd single = FrontierBuilder.Build<DirectedCycleSpec>(manager, new DirectedCycleSpec(graph, single: true));
            Zdd multi = FrontierBuilder.Build<DirectedCycleSpec>(manager, new DirectedCycleSpec(graph, single: false));

            Assert.Equal(manager.Empty, single.Difference(multi));
        }

        [Fact]
        public void GraphWithNoEdgesIsEmptyInEitherMode()
        {
            var graph = new DirectedGraph(3, Array.Empty<DirectedEdge>());
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Assert.Equal(manager.Empty, FrontierBuilder.Build<DirectedCycleSpec>(manager, new DirectedCycleSpec(graph, single: true)));
            Assert.Equal(manager.Empty, FrontierBuilder.Build<DirectedCycleSpec>(manager, new DirectedCycleSpec(graph, single: false)));
        }

        [Fact]
        public void OneWayTreeGraphHasNoCyclesInEitherMode()
        {
            DirectedGraph graph = DirectedGraph.Path(5);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Assert.Equal(manager.Empty, FrontierBuilder.Build<DirectedCycleSpec>(manager, new DirectedCycleSpec(graph, single: true)));
            Assert.Equal(manager.Empty, FrontierBuilder.Build<DirectedCycleSpec>(manager, new DirectedCycleSpec(graph, single: false)));
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new DirectedCycleSpec(null!));
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Grid(4, 4));
            var spec = new DirectedCycleSpec(graph, single: false);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneEdgePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(DirectedCycleSpec spec, Span<int> state, int level)
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

        private static Graph UndirectedGraphFor(string graphName) => graphName switch
        {
            "complete3" => Graph.Complete(3),
            "complete4" => Graph.Complete(4),
            "complete5" => Graph.Complete(5),
            "complete6" => Graph.Complete(6),
            "cycle5" => Graph.Cycle(5),
            "cycle6" => Graph.Cycle(6),
            "grid2x3" => Graph.Grid(2, 3),
            "petersen" => PetersenGraph(),
            _ => throw new ArgumentOutOfRangeException(nameof(graphName)),
        };

        private static DirectedGraph DirectedGraphFor(string graphName) => graphName switch
        {
            "bidirectedPath4" => DirectedGraph.Bidirected(Graph.Path(4)),
            "bidirectedTriangle" => DirectedGraph.Bidirected(Graph.Cycle(3)),
            "oneWayTriangle" => DirectedGraph.Cycle(3),
            "complete4" => DirectedGraph.Complete(4),
            _ => throw new ArgumentOutOfRangeException(nameof(graphName)),
        };

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

        /// <summary>Brute-force directed cycle families for a graph small enough to enumerate every arc subset.</summary>
        private static BruteForceFamily BruteForceDirectedCycles(DirectedGraph graph, bool single)
        {
            var accepted = new List<int>();
            int edgeCount = graph.EdgeCount;

            if (edgeCount >= 31)
            {
                throw new ArgumentException(
                    $"BruteForceDirectedCycles enumerates all 2^edgeCount subsets and cannot handle {edgeCount} arcs.",
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

                if (IsDirectedCycleFamilyMember(graph, arcSet, single))
                {
                    accepted.Add(mask);
                }
            }

            return BruteForceFamily.FromMasks(edgeCount, accepted);
        }

        /// <summary>
        /// Checks that <paramref name="arcSet"/> forms one or more vertex-disjoint directed simple cycles
        /// (or, with <paramref name="single"/>, exactly one) — every touched vertex has in-degree = out-degree
        /// = 1, and every weakly-connected touched component spans at least 3 vertices (excluding the
        /// 2-vertex anti-parallel "digon"; see <see cref="DirectedCycleSpec"/>'s remarks).
        /// </summary>
        private static bool IsDirectedCycleFamilyMember(DirectedGraph graph, IReadOnlyList<int> arcSet, bool single)
        {
            var inDegree = new int[graph.VertexCount];
            var outDegree = new int[graph.VertexCount];
            var parent = Enumerable.Range(0, graph.VertexCount).ToArray();
            var componentSize = new int[graph.VertexCount];
            Array.Fill(componentSize, 1);

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }

                return x;
            }

            void Union(int a, int b)
            {
                int ra = Find(a);
                int rb = Find(b);
                if (ra != rb)
                {
                    parent[ra] = rb;
                    componentSize[rb] += componentSize[ra];
                }
            }

            foreach (int edgeIndex in arcSet)
            {
                DirectedEdge arc = graph.GetEdge(edgeIndex);
                outDegree[arc.From]++;
                inDegree[arc.To]++;
                Union(arc.From, arc.To);
            }

            var touchedRoots = new HashSet<int>();
            for (int v = 0; v < graph.VertexCount; v++)
            {
                if (inDegree[v] != outDegree[v] || inDegree[v] > 1)
                {
                    return false; // must be 0 or 1 in each direction, and balanced
                }

                if (inDegree[v] == 1)
                {
                    touchedRoots.Add(Find(v));
                }
            }

            foreach (int root in touchedRoots)
            {
                if (componentSize[root] < 3)
                {
                    return false; // a 2-vertex component is the anti-parallel "digon", not a real cycle
                }
            }

            return !single || touchedRoots.Count == 1;
        }

        /// <summary>
        /// The Petersen graph: outer 5-cycle 0-1-2-3-4-0, spokes i–(i+5), inner pentagram 5-7-9-6-8-5.
        /// </summary>
        private static Graph PetersenGraph()
        {
            var edges = new List<Edge>();
            for (int i = 0; i < 5; i++)
            {
                edges.Add(new Edge(i, (i + 1) % 5));
                edges.Add(new Edge(i, i + 5));
            }

            for (int i = 0; i < 5; i++)
            {
                edges.Add(new Edge(5 + i, 5 + ((i + 2) % 5)));
            }

            return new Graph(10, edges.Distinct().ToList());
        }
    }
}
