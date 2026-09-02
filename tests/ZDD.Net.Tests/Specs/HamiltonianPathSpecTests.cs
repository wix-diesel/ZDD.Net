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
    /// M3-4 completion criteria for <see cref="HamiltonianPathSpec"/>: <see cref="Graph.Complete"/>'s
    /// per-endpoint-pair count matches <c>(n-2)!</c> and the total over every pair matches <c>n!/2</c>, the
    /// Petersen graph (famously non-Hamiltonian for cycles) still has Hamiltonian paths, matches
    /// brute-force enumeration on small graphs, every enumerated set really visits every vertex exactly
    /// once, and <c>GetChild</c> does not allocate.
    /// </summary>
    public class HamiltonianPathSpecTests
    {
        // A Hamiltonian s-t path in K_n is a permutation of the n-2 interior vertices between the two
        // fixed endpoints: (n-2)! of them.
        [Theory]
        [InlineData(3, "1")]
        [InlineData(4, "2")]
        [InlineData(5, "6")]
        [InlineData(6, "24")]
        [InlineData(7, "120")]
        public void CountOnCompleteGraphMatchesFactorialFormula(int n, string expected)
        {
            Graph graph = Graph.Complete(n);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<HamiltonianPathSpec>(manager, new HamiltonianPathSpec(graph, 0, n - 1));

            Assert.Equal(BigInteger.Parse(expected), built.Count);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void TotalOverEveryEndpointPairOnCompleteGraphMatchesNFactorialOverTwo(int n)
        {
            Graph graph = Graph.Complete(n);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            BigInteger total = BigInteger.Zero;
            for (int s = 0; s < n; s++)
            {
                for (int t = s + 1; t < n; t++)
                {
                    Zdd built = FrontierBuilder.Build<HamiltonianPathSpec>(manager, new HamiltonianPathSpec(graph, s, t));
                    total += built.Count;
                }
            }

            Assert.Equal(Factorial(n) / 2, total);
        }

        [Fact]
        public void PetersenGraphHasHamiltonianPathsDespiteHavingNoHamiltonianCycle()
        {
            Graph petersen = PetersenGraph();
            using ZddManager manager = new ZddManager(petersen.EdgeCount);

            bool anyPathExists = false;
            for (int s = 0; s < petersen.VertexCount && !anyPathExists; s++)
            {
                for (int t = s + 1; t < petersen.VertexCount; t++)
                {
                    Zdd built = FrontierBuilder.Build<HamiltonianPathSpec>(manager, new HamiltonianPathSpec(petersen, s, t));
                    if (!built.Equals(manager.Empty))
                    {
                        anyPathExists = true;
                        break;
                    }
                }
            }

            Assert.True(anyPathExists, "the Petersen graph is known to have Hamiltonian paths");
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("gridWithDeadEnd")]
        public void MatchesBruteForceEnumerationOnSmallGraphs(string graphName)
        {
            Graph graph = GraphFor(graphName);

            for (int s = 0; s < graph.VertexCount; s++)
            {
                for (int t = 0; t < graph.VertexCount; t++)
                {
                    if (s == t)
                    {
                        continue;
                    }

                    using ZddManager manager = new ZddManager(graph.EdgeCount);
                    Zdd built = FrontierBuilder.Build<HamiltonianPathSpec>(manager, new HamiltonianPathSpec(graph, s, t));

                    BruteForceFamily expected = BruteForceHamiltonianPaths(graph, s, t);

                    FamilyAssert.AssertSameFamily($"{graphName} s={s} t={t}", built, expected);
                }
            }
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void EveryEnumeratedGridPathVisitsEveryVertexExactlyOnce(int n)
        {
            Graph grid = Graph.Grid(n, n);
            int s = 0;
            int t = grid.VertexCount - 1;
            using ZddManager manager = new ZddManager(grid.EdgeCount);

            Zdd built = FrontierBuilder.Build<HamiltonianPathSpec>(manager, new HamiltonianPathSpec(grid, s, t));

            foreach (int[] edgeSet in built.Sets())
            {
                AssertIsHamiltonianPath(grid, s, t, edgeSet);
            }
        }

        [Fact]
        public void SEqualsTIsEmpty()
        {
            Graph graph = Graph.Grid(3, 3);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<HamiltonianPathSpec>(manager, new HamiltonianPathSpec(graph, 4, 4));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void AVertexThatCanNeverReachDegreeTwoIsEmpty()
        {
            // Vertex 3 has degree 1 (a pendant): it can never reach degree 2, so no Hamiltonian path exists.
            var graph = new Graph(4, new[] { new Edge(0, 1), new Edge(1, 2), new Edge(1, 3) });

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<HamiltonianPathSpec>(manager, new HamiltonianPathSpec(graph, 0, 2));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void ConstructorRejectsOutOfRangeEndpoints()
        {
            Graph graph = Graph.Path(4);

            Assert.Throws<ArgumentOutOfRangeException>(() => new HamiltonianPathSpec(graph, -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new HamiltonianPathSpec(graph, 0, 4));
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            Graph grid = Graph.Grid(4, 4);
            var spec = new HamiltonianPathSpec(grid, 0, grid.VertexCount - 1);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneEdgePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(HamiltonianPathSpec spec, Span<int> state, int level)
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
            "path4" => Graph.Path(4),
            "cycle5" => Graph.Cycle(5),
            "complete5" => Graph.Complete(5),
            // A 2x3 grid with an extra pendant edge hanging off a corner: the pendant vertex can only ever
            // be a path endpoint, never an interior vertex.
            "gridWithDeadEnd" => AddPendant(Graph.Grid(2, 3), attachTo: 0),
            _ => throw new ArgumentOutOfRangeException(nameof(graphName)),
        };

        private static Graph AddPendant(Graph graph, int attachTo)
        {
            var edges = graph.Edges.ToList();
            edges.Add(new Edge(attachTo, graph.VertexCount));
            return new Graph(graph.VertexCount + 1, edges);
        }

        private static void AssertIsHamiltonianPath(Graph graph, int s, int t, int[] edgeSet)
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

            for (int v = 0; v < graph.VertexCount; v++)
            {
                if (v == s || v == t)
                {
                    Assert.True(degree[v] == 1, $"endpoint {v} must have degree 1, has {degree[v]}");
                }
                else
                {
                    Assert.True(degree[v] == 2, $"interior vertex {v} must have degree 2, has {degree[v]}");
                }
            }

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

            Assert.Equal(graph.VertexCount - 1, steps); // a Hamiltonian path visits every vertex exactly once
        }

        private static BruteForceFamily BruteForceHamiltonianPaths(Graph graph, int s, int t)
        {
            var accepted = new List<int>();
            int edgeCount = graph.EdgeCount;

            if (edgeCount >= 31)
            {
                throw new ArgumentException(
                    $"BruteForceHamiltonianPaths enumerates all 2^edgeCount subsets and cannot handle {edgeCount} edges.",
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

                if (IsHamiltonianPath(graph, s, t, edgeSet))
                {
                    accepted.Add(mask);
                }
            }

            return BruteForceFamily.FromMasks(edgeCount, accepted);
        }

        private static bool IsHamiltonianPath(Graph graph, int s, int t, List<int> edgeSet)
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
                int expected = v == s || v == t ? 1 : 2;
                if (degree[v] != expected)
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
                int ru = Find(edge.U);
                int rv = Find(edge.V);
                if (ru == rv)
                {
                    return false; // cycle
                }

                parent[ru] = rv;
            }

            return Find(s) == Find(t) && edgeSet.Count == graph.VertexCount - 1;
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
