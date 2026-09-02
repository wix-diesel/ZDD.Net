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
    /// M3-4 completion criteria for <see cref="HamiltonianCycleSpec"/>: <see cref="Graph.Complete"/>'s
    /// Hamiltonian cycle count matches <c>(n-1)!/2</c>, the Petersen graph — the textbook example of a
    /// non-Hamiltonian graph — has exactly zero, <see cref="Graph.Cycle"/> has exactly one, matches
    /// brute-force enumeration on small graphs, every enumerated set really is a Hamiltonian cycle, and
    /// <c>GetChild</c> does not allocate.
    /// </summary>
    public class HamiltonianCycleSpecTests
    {
        // Fixing vertex 0's two neighbors as an unordered pair and arranging the rest in a line gives
        // (n-1)!/2 distinct Hamiltonian cycles in K_n.
        [Theory]
        [InlineData(3, "1")]
        [InlineData(4, "3")]
        [InlineData(5, "12")]
        [InlineData(6, "60")]
        [InlineData(7, "360")]
        [InlineData(8, "2520")]
        [InlineData(9, "20160")]
        public void CountOnCompleteGraphMatchesKnownFormula(int n, string expected)
        {
            Graph graph = Graph.Complete(n);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<HamiltonianCycleSpec>(manager, new HamiltonianCycleSpec(graph));

            Assert.Equal(BigInteger.Parse(expected), built.Count);
            Assert.Equal(BigInteger.Parse(expected), Factorial(n - 1) / 2);
        }

        [Fact]
        public void PetersenGraphHasNoHamiltonianCycle()
        {
            Graph petersen = PetersenGraph();
            using ZddManager manager = new ZddManager(petersen.EdgeCount);

            Zdd built = FrontierBuilder.Build<HamiltonianCycleSpec>(manager, new HamiltonianCycleSpec(petersen));

            Assert.Equal(manager.Empty, built);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(9)]
        public void CycleGraphHasExactlyOneHamiltonianCycle(int n)
        {
            Graph graph = Graph.Cycle(n);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<HamiltonianCycleSpec>(manager, new HamiltonianCycleSpec(graph));

            Assert.Equal(BigInteger.One, built.Count);
        }

        [Theory]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("gridWithDeadEnd")]
        [InlineData("grid2x3")]
        public void MatchesBruteForceEnumerationOnSmallGraphs(string graphName)
        {
            Graph graph = GraphFor(graphName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<HamiltonianCycleSpec>(manager, new HamiltonianCycleSpec(graph));

            BruteForceFamily expected = BruteForceHamiltonianCycles(graph);

            FamilyAssert.AssertSameFamily(graphName, built, expected);
        }

        [Theory]
        [InlineData(4, "3")]
        [InlineData(5, "12")]
        [InlineData(6, "60")]
        public void EveryEnumeratedCompleteGraphCycleIsAHamiltonianCycle(int n, string expected)
        {
            Graph graph = Graph.Complete(n);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<HamiltonianCycleSpec>(manager, new HamiltonianCycleSpec(graph));
            Assert.Equal(BigInteger.Parse(expected), built.Count);

            foreach (int[] edgeSet in built.Sets())
            {
                AssertIsHamiltonianCycle(graph, edgeSet);
            }
        }

        [Fact]
        public void VertexCountBelowThreeIsEmpty()
        {
            var graph = new Graph(2, new[] { new Edge(0, 1) });
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<HamiltonianCycleSpec>(manager, new HamiltonianCycleSpec(graph));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void AVertexThatCanNeverReachDegreeTwoIsEmpty()
        {
            // Vertex 3 has degree 1 (a pendant): it can never reach degree 2, so no Hamiltonian cycle exists.
            var graph = new Graph(4, new[]
            {
                new Edge(0, 1), new Edge(1, 2), new Edge(2, 0), new Edge(1, 3),
            });

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<HamiltonianCycleSpec>(manager, new HamiltonianCycleSpec(graph));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new HamiltonianCycleSpec(null!));
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            Graph graph = Graph.Complete(6);
            var spec = new HamiltonianCycleSpec(graph);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneEdgePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(HamiltonianCycleSpec spec, Span<int> state, int level)
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

        private static Graph GraphFor(string graphName) => graphName switch
        {
            "cycle5" => Graph.Cycle(5),
            "complete5" => Graph.Complete(5),
            "grid2x3" => Graph.Grid(2, 3),
            // A 2x3 grid with an extra pendant edge: the pendant vertex can never reach degree 2, so this
            // graph has no Hamiltonian cycle at all — an all-rejected case for the brute-force cross-check.
            "gridWithDeadEnd" => AddPendant(Graph.Grid(2, 3), attachTo: 0),
            _ => throw new ArgumentOutOfRangeException(nameof(graphName)),
        };

        private static Graph AddPendant(Graph graph, int attachTo)
        {
            var edges = graph.Edges.ToList();
            edges.Add(new Edge(attachTo, graph.VertexCount));
            return new Graph(graph.VertexCount + 1, edges);
        }

        private static void AssertIsHamiltonianCycle(Graph graph, int[] edgeSet)
        {
            Assert.Equal(graph.VertexCount, edgeSet.Length); // a Hamiltonian cycle has exactly n edges

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

            for (int v = 0; v < graph.VertexCount; v++)
            {
                Assert.True(degree[v] == 2, $"vertex {v} must have degree 2, has {degree[v]}");
            }

            // Walking from vertex 0 using only degree-2 pass-throughs must visit every vertex exactly once
            // and return to 0 after exactly VertexCount steps (one connected cycle, not several).
            int steps = 0;
            int previous = -1;
            int current = 0;
            do
            {
                int next = adjacency[current].First(candidate => candidate != previous);
                previous = current;
                current = next;
                steps++;
                Assert.True(steps <= graph.VertexCount, "the walk did not close within VertexCount steps");
            }
            while (current != 0);

            Assert.Equal(graph.VertexCount, steps);
        }

        private static BruteForceFamily BruteForceHamiltonianCycles(Graph graph)
        {
            var accepted = new List<int>();
            int edgeCount = graph.EdgeCount;

            if (edgeCount >= 31)
            {
                throw new ArgumentException(
                    $"BruteForceHamiltonianCycles enumerates all 2^edgeCount subsets and cannot handle {edgeCount} edges.",
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

                if (edgeSet.Count == 0)
                {
                    continue;
                }

                if (IsHamiltonianCycle(graph, edgeSet))
                {
                    accepted.Add(mask);
                }
            }

            return BruteForceFamily.FromMasks(edgeCount, accepted);
        }

        private static bool IsHamiltonianCycle(Graph graph, List<int> edgeSet)
        {
            if (edgeSet.Count != graph.VertexCount)
            {
                return false;
            }

            var degree = new int[graph.VertexCount];
            foreach (int edgeIndex in edgeSet)
            {
                Edge edge = graph.GetEdge(edgeIndex);
                degree[edge.U]++;
                degree[edge.V]++;
            }

            for (int v = 0; v < graph.VertexCount; v++)
            {
                if (degree[v] != 2)
                {
                    return false;
                }
            }

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

            foreach (int edgeIndex in edgeSet)
            {
                Edge edge = graph.GetEdge(edgeIndex);
                parent[Find(edge.U)] = Find(edge.V);
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
                edges.Add(new Edge(5 + i, 5 + (i + 2) % 5));
            }

            return new Graph(10, edges.Distinct().ToList());
        }
    }
}
