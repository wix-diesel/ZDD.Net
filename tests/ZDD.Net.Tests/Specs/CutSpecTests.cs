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
    /// M4-6 completion criteria for <see cref="CutSpec"/>: matches brute-force enumeration (both "every
    /// cut" and "minimal cuts only") on small graphs across several <c>s</c>&#8211;<c>t</c> pairs, every
    /// enumerated set actually disconnects <c>s</c> from <c>t</c>, the minimal-cuts family matches the
    /// general family's <c>Minimal()</c>, the minimum-weight cut's weight matches an independently computed
    /// max flow (max-flow min-cut theorem), and <c>GetChild</c> does not allocate.
    /// </summary>
    public class CutSpecTests
    {
        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        public void MatchesBruteForceEnumerationForVariousTerminalPairs(string graphName)
        {
            Graph graph = SpanningTreeSpecTests.NamedGraph(graphName);

            foreach ((int s, int t) in TerminalPairs(graph.VertexCount))
            {
                foreach (bool minimalOnly in new[] { false, true })
                {
                    using ZddManager manager = new ZddManager(graph.EdgeCount);
                    Zdd built = FrontierBuilder.Build<CutSpec>(manager, new CutSpec(graph, s, t, minimalOnly));

                    BruteForceFamily expected = BruteForceCuts(graph, s, t, minimalOnly);

                    FamilyAssert.AssertSameFamily($"{graphName} s={s} t={t} minimalOnly={minimalOnly}", built, expected);
                }
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void MatchesBruteForceEnumerationOnRandomGraphs(int seed)
        {
            Graph graph = SpanningTreeSpecTests.RandomConnectedGraph(vertexCount: 6, extraEdgeProbability: 0.3, seed);

            foreach ((int s, int t) in TerminalPairs(graph.VertexCount))
            {
                foreach (bool minimalOnly in new[] { false, true })
                {
                    using ZddManager manager = new ZddManager(graph.EdgeCount);
                    Zdd built = FrontierBuilder.Build<CutSpec>(manager, new CutSpec(graph, s, t, minimalOnly));

                    BruteForceFamily expected = BruteForceCuts(graph, s, t, minimalOnly);

                    FamilyAssert.AssertSameFamily($"seed={seed} s={s} t={t} minimalOnly={minimalOnly}", built, expected);
                }
            }
        }

        [Fact]
        public void EveryEnumeratedSetDisconnectsTerminals()
        {
            Graph graph = Graph.Grid(3, 3);
            int s = 0;
            int t = graph.VertexCount - 1;

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<CutSpec>(manager, new CutSpec(graph, s, t));

            foreach (int[] cut in built.Sets())
            {
                Assert.True(Separates(graph, s, t, cut));
            }
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("grid2x3")]
        public void MinimalOnlyFamilyMatchesAllCutsMinimal(string graphName)
        {
            Graph graph = SpanningTreeSpecTests.NamedGraph(graphName);
            int s = 0;
            int t = graph.VertexCount - 1;

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd allCuts = FrontierBuilder.Build<CutSpec>(manager, new CutSpec(graph, s, t, minimalOnly: false));
            Zdd minimalCuts = FrontierBuilder.Build<CutSpec>(manager, new CutSpec(graph, s, t, minimalOnly: true));

            FamilyAssert.AssertSameFamily(graphName, minimalCuts, ZddFamilies.ToBruteForce(allCuts.Minimal()));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void MinCutWeightMatchesMaxFlowMinCutTheorem(int seed)
        {
            Graph graph = SpanningTreeSpecTests.RandomConnectedGraph(vertexCount: 6, extraEdgeProbability: 0.4, seed);
            var random = new Random(seed * 97 + 1);
            var weights = new int[graph.EdgeCount];
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = random.Next(1, 6);
            }

            int s = 0;
            int t = graph.VertexCount - 1;

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd allCuts = FrontierBuilder.Build<CutSpec>(manager, new CutSpec(graph, s, t));

            int minCutWeight = allCuts.MinWeight(weights).Weight;
            int maxFlow = NaiveMaxFlow(graph, s, t, weights);

            Assert.Equal(maxFlow, minCutWeight);
        }

        [Fact]
        public void SameTerminalIsEmpty()
        {
            Graph graph = Graph.Path(4);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<CutSpec>(manager, new CutSpec(graph, 1, 1));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void AlreadyDisconnectedTerminalsAcceptEveryEdgeSubsetForAllCuts()
        {
            // Two disjoint triangles: vertices 0-1-2 and 3-4-5, no edge between the halves.
            var graph = new Graph(6, new[]
            {
                new Edge(0, 1), new Edge(1, 2), new Edge(2, 0),
                new Edge(3, 4), new Edge(4, 5), new Edge(5, 3),
            });

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<CutSpec>(manager, new CutSpec(graph, 0, 3));

            Assert.Equal(BigInteger.Pow(2, graph.EdgeCount), built.Count);
        }

        [Fact]
        public void AlreadyDisconnectedTerminalsHaveOnlyTheEmptySetAsMinimalCut()
        {
            var graph = new Graph(6, new[]
            {
                new Edge(0, 1), new Edge(1, 2), new Edge(2, 0),
                new Edge(3, 4), new Edge(4, 5), new Edge(5, 3),
            });

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<CutSpec>(manager, new CutSpec(graph, 0, 3, minimalOnly: true));

            Assert.Equal(BigInteger.One, built.Count);
            Assert.Equal(Array.Empty<int>(), Assert.Single(built.Sets()));
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new CutSpec(null!, 0, 1));
        }

        [Fact]
        public void ConstructorRejectsOutOfRangeTerminal()
        {
            Graph graph = Graph.Path(4);
            Assert.Throws<ArgumentOutOfRangeException>(() => new CutSpec(graph, -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CutSpec(graph, 0, 4));
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            Graph grid = Graph.Grid(4, 4);
            var spec = new CutSpec(grid, 0, grid.VertexCount - 1, minimalOnly: true);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneEdgePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(CutSpec spec, Span<int> state, int level)
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

        /// <summary>A handful of distinct <c>(s, t)</c> pairs, including adjacent and far-apart vertices.</summary>
        private static IEnumerable<(int S, int T)> TerminalPairs(int vertexCount)
        {
            yield return (0, vertexCount - 1);
            yield return (0, 1);

            if (vertexCount >= 3)
            {
                yield return (0, vertexCount / 2);
            }
        }

        /// <summary>Whether removing <paramref name="cutEdges"/> leaves <paramref name="s"/>, <paramref name="t"/> disconnected.</summary>
        private static bool Separates(Graph graph, int s, int t, IReadOnlyList<int> cutEdges)
        {
            var cut = new bool[graph.EdgeCount];
            foreach (int i in cutEdges)
            {
                cut[i] = true;
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

            for (int i = 0; i < graph.EdgeCount; i++)
            {
                if (cut[i])
                {
                    continue; // removed
                }

                Edge edge = graph.GetEdge(i);
                parent[Find(edge.U)] = Find(edge.V);
            }

            return Find(s) != Find(t);
        }

        private static BruteForceFamily BruteForceCuts(Graph graph, int s, int t, bool minimalOnly)
        {
            int edgeCount = graph.EdgeCount;

            if (edgeCount >= 20)
            {
                throw new ArgumentException(
                    $"BruteForceCuts enumerates all 2^edgeCount subsets (and subsets of those) and cannot handle {edgeCount} edges.",
                    nameof(graph));
            }

            int bound = 1 << edgeCount;
            var isCut = new bool[bound];
            for (int mask = 0; mask < bound; mask++)
            {
                isCut[mask] = Separates(graph, s, t, MaskToEdgeList(mask, edgeCount));
            }

            var accepted = new List<int>();
            for (int mask = 0; mask < bound; mask++)
            {
                if (!isCut[mask])
                {
                    continue;
                }

                if (minimalOnly && HasProperCutSubset(isCut, mask))
                {
                    continue;
                }

                accepted.Add(mask);
            }

            return BruteForceFamily.FromMasks(edgeCount, accepted);
        }

        /// <summary>Whether some proper subset of <paramref name="mask"/> is also flagged as a cut in <paramref name="isCut"/>.</summary>
        private static bool HasProperCutSubset(bool[] isCut, int mask)
        {
            for (int sub = mask; ; sub = (sub - 1) & mask)
            {
                if (sub != mask && isCut[sub])
                {
                    return true;
                }

                if (sub == 0)
                {
                    return false;
                }
            }
        }

        private static List<int> MaskToEdgeList(int mask, int edgeCount)
        {
            var edges = new List<int>();
            for (int i = 0; i < edgeCount; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    edges.Add(i);
                }
            }

            return edges;
        }

        /// <summary>
        /// A textbook Edmonds-Karp max flow (BFS augmenting paths) over the graph with each undirected edge
        /// modeled as two opposite directed arcs of capacity <paramref name="capacities"/>[i] — the standard
        /// reduction under which the max flow equals the minimum-weight <c>s</c>&#8211;<c>t</c> edge cut.
        /// Written independently of <see cref="CutSpec"/>, purely from the max-flow min-cut theorem.
        /// </summary>
        private static int NaiveMaxFlow(Graph graph, int s, int t, int[] capacities)
        {
            int n = graph.VertexCount;
            var capacity = new int[n, n];

            for (int i = 0; i < graph.EdgeCount; i++)
            {
                Edge edge = graph.GetEdge(i);
                capacity[edge.U, edge.V] += capacities[i];
                capacity[edge.V, edge.U] += capacities[i];
            }

            int totalFlow = 0;
            while (true)
            {
                var parent = new int[n];
                Array.Fill(parent, -1);
                parent[s] = s;
                var queue = new Queue<int>();
                queue.Enqueue(s);

                while (queue.Count > 0 && parent[t] == -1)
                {
                    int u = queue.Dequeue();
                    for (int v = 0; v < n; v++)
                    {
                        if (parent[v] == -1 && capacity[u, v] > 0)
                        {
                            parent[v] = u;
                            queue.Enqueue(v);
                        }
                    }
                }

                if (parent[t] == -1)
                {
                    break; // no augmenting path left
                }

                int bottleneck = int.MaxValue;
                for (int v = t; v != s; v = parent[v])
                {
                    bottleneck = Math.Min(bottleneck, capacity[parent[v], v]);
                }

                for (int v = t; v != s; v = parent[v])
                {
                    capacity[parent[v], v] -= bottleneck;
                    capacity[v, parent[v]] += bottleneck;
                }

                totalFlow += bottleneck;
            }

            return totalFlow;
        }
    }
}
