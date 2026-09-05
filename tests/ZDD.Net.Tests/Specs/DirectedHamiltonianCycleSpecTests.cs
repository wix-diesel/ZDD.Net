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
    /// M7-4 completion criteria for <see cref="DirectedHamiltonianCycleSpec"/>: the complete directed graph
    /// <see cref="DirectedGraph.Complete"/> has exactly <c>(n-1)!</c> directed Hamiltonian cycles (n = 4..8),
    /// <see cref="DirectedGraph.Bidirected"/> of the (non-Hamiltonian) Petersen graph has none, a one-way
    /// <see cref="DirectedGraph.Cycle"/> has exactly one, matches brute-force enumeration on small graphs,
    /// every enumerated arc set really is a directed Hamiltonian cycle, and <c>GetChild</c> does not allocate.
    /// </summary>
    public class DirectedHamiltonianCycleSpecTests
    {
        // A directed Hamiltonian cycle on K_n is a cyclic ordering of all n vertices with the direction
        // fixed by that ordering: (n-1)! distinct orderings up to rotation, each giving a distinct arc set
        // (its reverse ordering is a different arc set, unlike the undirected count's n!/2).
        [Theory]
        [InlineData(4, "6")]
        [InlineData(5, "24")]
        [InlineData(6, "120")]
        [InlineData(7, "720")]
        [InlineData(8, "5040")]
        public void CountOnCompleteDirectedGraphMatchesKnownFormula(int n, string expected)
        {
            DirectedGraph graph = DirectedGraph.Complete(n);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DirectedHamiltonianCycleSpec>(manager, new DirectedHamiltonianCycleSpec(graph));

            Assert.Equal(BigInteger.Parse(expected), built.Count);
            Assert.Equal(BigInteger.Parse(expected), Factorial(n - 1));
        }

        [Fact]
        public void BidirectedPetersenGraphHasNoDirectedHamiltonianCycle()
        {
            DirectedGraph graph = DirectedGraph.Bidirected(PetersenGraph());
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DirectedHamiltonianCycleSpec>(manager, new DirectedHamiltonianCycleSpec(graph));

            Assert.Equal(manager.Empty, built);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(9)]
        public void OneWayCycleGraphHasExactlyOneHamiltonianCycle(int n)
        {
            DirectedGraph graph = DirectedGraph.Cycle(n);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DirectedHamiltonianCycleSpec>(manager, new DirectedHamiltonianCycleSpec(graph));

            Assert.Equal(BigInteger.One, built.Count);
        }

        [Fact]
        public void TwoVertexAntiParallelPairIsNeverAHamiltonianCycle()
        {
            // n = 2: the only candidate closure is the anti-parallel pair itself — a digon, not a real
            // cycle (see DirectedCycleSpec's remarks, which this spec's GetRoot guard also relies on).
            DirectedGraph graph = DirectedGraph.Cycle(2);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DirectedHamiltonianCycleSpec>(manager, new DirectedHamiltonianCycleSpec(graph));

            Assert.Equal(manager.Empty, built);
        }

        [Theory]
        [InlineData("oneWayTriangle")]
        [InlineData("bidirectedCycle5")]
        [InlineData("complete4")]
        [InlineData("bidirectedGridWithOneWayShortcut")]
        public void MatchesBruteForceEnumerationOnSmallGraphs(string graphName)
        {
            DirectedGraph graph = DirectedGraphFor(graphName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DirectedHamiltonianCycleSpec>(manager, new DirectedHamiltonianCycleSpec(graph));

            BruteForceFamily expected = BruteForceDirectedHamiltonianCycles(graph);

            FamilyAssert.AssertSameFamily(graphName, built, expected);
        }

        [Theory]
        [InlineData(4, "6")]
        [InlineData(5, "24")]
        public void EveryEnumeratedCompleteGraphCycleIsADirectedHamiltonianCycle(int n, string expected)
        {
            DirectedGraph graph = DirectedGraph.Complete(n);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DirectedHamiltonianCycleSpec>(manager, new DirectedHamiltonianCycleSpec(graph));
            Assert.Equal(BigInteger.Parse(expected), built.Count);

            foreach (int[] arcSet in built.Sets())
            {
                AssertIsDirectedHamiltonianCycle(graph, arcSet);
            }
        }

        [Fact]
        public void VertexCountBelowThreeIsEmpty()
        {
            var graph = new DirectedGraph(2, new[] { new DirectedEdge(0, 1), new DirectedEdge(1, 0) });
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DirectedHamiltonianCycleSpec>(manager, new DirectedHamiltonianCycleSpec(graph));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void AVertexThatCanNeverCloseIsEmpty()
        {
            // Vertex 3 has no incoming arc at all: it can never reach in-degree 1, so no Hamiltonian cycle exists.
            var graph = new DirectedGraph(4, new[]
            {
                new DirectedEdge(0, 1), new DirectedEdge(1, 2), new DirectedEdge(2, 0), new DirectedEdge(1, 3),
            });

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<DirectedHamiltonianCycleSpec>(manager, new DirectedHamiltonianCycleSpec(graph));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new DirectedHamiltonianCycleSpec(null!));
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            DirectedGraph graph = DirectedGraph.Complete(6);
            var spec = new DirectedHamiltonianCycleSpec(graph);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneEdgePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(DirectedHamiltonianCycleSpec spec, Span<int> state, int level)
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

        private static DirectedGraph DirectedGraphFor(string graphName) => graphName switch
        {
            "oneWayTriangle" => DirectedGraph.Cycle(3),
            "bidirectedCycle5" => DirectedGraph.Bidirected(Graph.Cycle(5)),
            "complete4" => DirectedGraph.Complete(4),
            // A bidirected 2x3 grid plus a one-way shortcut arc: exercises mixed one-way/two-way arcs.
            "bidirectedGridWithOneWayShortcut" => AddShortcut(DirectedGraph.Bidirected(Graph.Grid(2, 3)), 0, 5),
            _ => throw new ArgumentOutOfRangeException(nameof(graphName)),
        };

        private static DirectedGraph AddShortcut(DirectedGraph graph, int from, int to)
        {
            var edges = graph.Edges.ToList();
            edges.Add(new DirectedEdge(from, to));
            return new DirectedGraph(graph.VertexCount, edges);
        }

        private static void AssertIsDirectedHamiltonianCycle(DirectedGraph graph, int[] arcSet)
        {
            Assert.Equal(graph.VertexCount, arcSet.Length); // a Hamiltonian cycle has exactly n arcs

            var outArc = new int[graph.VertexCount];
            Array.Fill(outArc, -1);
            var inDegree = new int[graph.VertexCount];
            var outDegree = new int[graph.VertexCount];

            foreach (int edgeIndex in arcSet)
            {
                DirectedEdge arc = graph.GetEdge(edgeIndex);
                outDegree[arc.From]++;
                inDegree[arc.To]++;
                outArc[arc.From] = arc.To;
            }

            for (int v = 0; v < graph.VertexCount; v++)
            {
                Assert.True(inDegree[v] == 1 && outDegree[v] == 1, $"vertex {v} must have in/out-degree 1, has ({inDegree[v]}, {outDegree[v]})");
            }

            var visited = new HashSet<int> { 0 };
            int current = 0;
            int steps = 0;
            do
            {
                current = outArc[current];
                Assert.True(steps < graph.VertexCount, "the walk did not close within VertexCount steps");
                steps++;
                if (current != 0)
                {
                    Assert.True(visited.Add(current), "the walk revisited a vertex before returning to 0");
                }
            }
            while (current != 0);

            Assert.Equal(graph.VertexCount, steps);
        }

        private static BruteForceFamily BruteForceDirectedHamiltonianCycles(DirectedGraph graph)
        {
            var accepted = new List<int>();
            int edgeCount = graph.EdgeCount;

            if (edgeCount >= 31)
            {
                throw new ArgumentException(
                    $"BruteForceDirectedHamiltonianCycles enumerates all 2^edgeCount subsets and cannot handle {edgeCount} arcs.",
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

                if (IsDirectedHamiltonianCycle(graph, arcSet))
                {
                    accepted.Add(mask);
                }
            }

            return BruteForceFamily.FromMasks(edgeCount, accepted);
        }

        private static bool IsDirectedHamiltonianCycle(DirectedGraph graph, List<int> arcSet)
        {
            if (arcSet.Count != graph.VertexCount)
            {
                return false;
            }

            var inDegree = new int[graph.VertexCount];
            var outDegree = new int[graph.VertexCount];
            var parent = Enumerable.Range(0, graph.VertexCount).ToArray();

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }

                return x;
            }

            foreach (int edgeIndex in arcSet)
            {
                DirectedEdge arc = graph.GetEdge(edgeIndex);
                outDegree[arc.From]++;
                inDegree[arc.To]++;
                parent[Find(arc.From)] = Find(arc.To);
            }

            for (int v = 0; v < graph.VertexCount; v++)
            {
                if (inDegree[v] != 1 || outDegree[v] != 1)
                {
                    return false;
                }
            }

            return Enumerable.Range(0, graph.VertexCount).Select(Find).Distinct().Count() == 1;
        }

        private static BigInteger Factorial(int n)
        {
            BigInteger result = BigInteger.One;
            for (int i = 2; i <= n; i++)
            {
                result *= i;
            }

            return result;
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
