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
    /// M7-4 completion criteria for <see cref="DirectedHamiltonianPathSpec"/>: <see cref="DirectedGraph.Complete"/>'s
    /// per-endpoint count matches the same <c>(n-2)!</c> formula as the undirected <see cref="HamiltonianPathSpec"/>
    /// (direction is already fixed by construction, so no factor-of-two adjustment applies), matches
    /// brute-force enumeration on small and random directed graphs (vertex count ≤ 8), every enumerated arc
    /// set really visits every vertex exactly once, and <c>GetChild</c> does not allocate.
    /// </summary>
    public class DirectedHamiltonianPathSpecTests
    {
        [Theory]
        [InlineData(3, "1")]
        [InlineData(4, "2")]
        [InlineData(5, "6")]
        [InlineData(6, "24")]
        [InlineData(7, "120")]
        public void CountOnCompleteDirectedGraphMatchesFactorialFormula(int n, string expected)
        {
            DirectedGraph graph = DirectedGraph.Complete(n);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DirectedHamiltonianPathSpec>(manager, new DirectedHamiltonianPathSpec(graph, 0, n - 1));

            Assert.Equal(BigInteger.Parse(expected), built.Count);
        }

        [Theory]
        [InlineData("oneWayPath4")]
        [InlineData("bidirectedCycle5")]
        [InlineData("complete4")]
        [InlineData("gridWithOneWayShortcut")]
        public void MatchesBruteForceEnumerationOnSmallGraphs(string graphName)
        {
            DirectedGraph graph = DirectedGraphFor(graphName);

            for (int s = 0; s < graph.VertexCount; s++)
            {
                for (int t = 0; t < graph.VertexCount; t++)
                {
                    if (s == t)
                    {
                        continue;
                    }

                    using ZddManager manager = new ZddManager(graph.EdgeCount);
                    Zdd built = FrontierBuilder.Build<DirectedHamiltonianPathSpec>(manager, new DirectedHamiltonianPathSpec(graph, s, t));

                    BruteForceFamily expected = BruteForceDirectedHamiltonianPaths(graph, s, t);

                    FamilyAssert.AssertSameFamily($"{graphName} s={s} t={t}", built, expected);
                }
            }
        }

        // Vertex count kept to <= 8 per the completion criteria; arc counts kept low enough that the
        // 2^EdgeCount brute-force scan — run once per ordered endpoint pair, so up to VertexCount *
        // (VertexCount - 1) times per case — stays fast (matches DirectedPathSpecTests' random-graph scale).
        [Theory]
        [InlineData(5, 8, 1)]
        [InlineData(6, 10, 2)]
        [InlineData(8, 14, 3)]
        [InlineData(8, 16, 4)]
        public void MatchesBruteForceEnumerationOnRandomDirectedGraphs(int vertexCount, int arcCount, int seed)
        {
            DirectedGraph graph = RandomDirectedGraph(vertexCount, arcCount, seed);

            for (int s = 0; s < graph.VertexCount; s++)
            {
                for (int t = 0; t < graph.VertexCount; t++)
                {
                    if (s == t)
                    {
                        continue;
                    }

                    using ZddManager manager = new ZddManager(graph.EdgeCount);
                    Zdd built = FrontierBuilder.Build<DirectedHamiltonianPathSpec>(manager, new DirectedHamiltonianPathSpec(graph, s, t));

                    BruteForceFamily expected = BruteForceDirectedHamiltonianPaths(graph, s, t);

                    FamilyAssert.AssertSameFamily(
                        $"n={vertexCount} arcs={arcCount} seed={seed} s={s} t={t}", built, expected);
                }
            }
        }

        [Theory]
        [InlineData(4, "2")]
        [InlineData(5, "6")]
        public void EveryEnumeratedCompleteGraphPathVisitsEveryVertexExactlyOnce(int n, string expected)
        {
            DirectedGraph graph = DirectedGraph.Complete(n);
            int s = 0;
            int t = n - 1;
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DirectedHamiltonianPathSpec>(manager, new DirectedHamiltonianPathSpec(graph, s, t));
            Assert.Equal(BigInteger.Parse(expected), built.Count);

            foreach (int[] arcSet in built.Sets())
            {
                AssertIsDirectedHamiltonianPath(graph, s, t, arcSet);
            }
        }

        [Fact]
        public void SEqualsTIsEmpty()
        {
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Grid(3, 3));
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DirectedHamiltonianPathSpec>(manager, new DirectedHamiltonianPathSpec(graph, 4, 4));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void AVertexThatCanNeverBeVisitedIsEmpty()
        {
            // Vertex 3 has no incoming arc at all: it can never reach (in 1, out 1), so no Hamiltonian path
            // through it as an interior vertex exists.
            var graph = new DirectedGraph(4, new[]
            {
                new DirectedEdge(0, 1), new DirectedEdge(1, 2), new DirectedEdge(1, 3),
            });

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<DirectedHamiltonianPathSpec>(manager, new DirectedHamiltonianPathSpec(graph, 0, 2));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void ConstructorRejectsOutOfRangeEndpoints()
        {
            DirectedGraph graph = DirectedGraph.Path(4);

            Assert.Throws<ArgumentOutOfRangeException>(() => new DirectedHamiltonianPathSpec(graph, -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DirectedHamiltonianPathSpec(graph, 0, 4));
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new DirectedHamiltonianPathSpec(null!, 0, 1));
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            DirectedGraph graph = DirectedGraph.Complete(6);
            var spec = new DirectedHamiltonianPathSpec(graph, 0, graph.VertexCount - 1);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneEdgePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(DirectedHamiltonianPathSpec spec, Span<int> state, int level)
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
            "oneWayPath4" => DirectedGraph.Path(4),
            "bidirectedCycle5" => DirectedGraph.Bidirected(Graph.Cycle(5)),
            "complete4" => DirectedGraph.Complete(4),
            "gridWithOneWayShortcut" => AddShortcut(DirectedGraph.Bidirected(Graph.Grid(2, 3)), 0, 5),
            _ => throw new ArgumentOutOfRangeException(nameof(graphName)),
        };

        private static DirectedGraph AddShortcut(DirectedGraph graph, int from, int to)
        {
            var edges = graph.Edges.ToList();
            edges.Add(new DirectedEdge(from, to));
            return new DirectedGraph(graph.VertexCount, edges);
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

        private static void AssertIsDirectedHamiltonianPath(DirectedGraph graph, int s, int t, int[] arcSet)
        {
            Assert.Equal(graph.VertexCount - 1, arcSet.Length);

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
                if (v == s)
                {
                    Assert.True(outDegree[v] == 1 && inDegree[v] == 0, $"source {v} must be (out 1, in 0)");
                }
                else if (v == t)
                {
                    Assert.True(inDegree[v] == 1 && outDegree[v] == 0, $"sink {v} must be (in 1, out 0)");
                }
                else
                {
                    Assert.True(inDegree[v] == 1 && outDegree[v] == 1, $"interior {v} must be (in 1, out 1)");
                }
            }

            var visited = new HashSet<int> { s };
            int current = s;
            int steps = 0;
            while (current != t)
            {
                int next = outArc[current];
                Assert.True(next >= 0 && visited.Add(next), "the walk from s did not reach t cleanly");
                current = next;
                steps++;
            }

            Assert.Equal(graph.VertexCount - 1, steps);
        }

        private static BruteForceFamily BruteForceDirectedHamiltonianPaths(DirectedGraph graph, int s, int t)
        {
            var accepted = new List<int>();
            int edgeCount = graph.EdgeCount;

            if (edgeCount >= 31)
            {
                throw new ArgumentException(
                    $"BruteForceDirectedHamiltonianPaths enumerates all 2^edgeCount subsets and cannot handle {edgeCount} arcs.",
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

                if (IsDirectedHamiltonianPath(graph, s, t, arcSet))
                {
                    accepted.Add(mask);
                }
            }

            return BruteForceFamily.FromMasks(edgeCount, accepted);
        }

        private static bool IsDirectedHamiltonianPath(DirectedGraph graph, int s, int t, List<int> arcSet)
        {
            if (arcSet.Count != graph.VertexCount - 1)
            {
                return false;
            }

            var inDegree = new int[graph.VertexCount];
            var outDegree = new int[graph.VertexCount];
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
                if (v == s)
                {
                    if (!(outDegree[v] == 1 && inDegree[v] == 0))
                    {
                        return false;
                    }
                }
                else if (v == t)
                {
                    if (!(inDegree[v] == 1 && outDegree[v] == 0))
                    {
                        return false;
                    }
                }
                else if (!(inDegree[v] == 1 && outDegree[v] == 1))
                {
                    return false;
                }
            }

            var visited = new HashSet<int> { s };
            int current = s;
            int steps = 0;
            while (current != t)
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
