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
    /// M4-6 completion criteria for <see cref="GraphPartitionSpec"/>: matches brute-force enumeration on
    /// small graphs across several <c>(k, balance)</c> patterns, every enumerated set actually has exactly
    /// <c>k</c> components each within the size range, <c>k == 1</c> matches
    /// <see cref="ConnectedSubgraphSpec"/> over every vertex, an unsatisfiable balance builds to
    /// <c>Empty</c>, and <c>GetChild</c> does not allocate.
    /// </summary>
    public class GraphPartitionSpecTests
    {
        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        public void MatchesBruteForceEnumerationForVariousBalancePatterns(string graphName)
        {
            Graph graph = SpanningTreeSpecTests.NamedGraph(graphName);

            foreach ((int k, int min, int max) in BalancePatterns(graph.VertexCount))
            {
                using ZddManager manager = new ZddManager(graph.EdgeCount);
                Zdd built = FrontierBuilder.Build<GraphPartitionSpec>(
                    manager, new GraphPartitionSpec(graph, k, min, max));

                BruteForceFamily expected = BruteForcePartitions(graph, k, min, max);

                FamilyAssert.AssertSameFamily($"{graphName} k={k} [{min},{max}]", built, expected);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void MatchesBruteForceEnumerationOnRandomGraphs(int seed)
        {
            Graph graph = SpanningTreeSpecTests.RandomConnectedGraph(vertexCount: 6, extraEdgeProbability: 0.3, seed);

            foreach ((int k, int min, int max) in BalancePatterns(graph.VertexCount))
            {
                using ZddManager manager = new ZddManager(graph.EdgeCount);
                Zdd built = FrontierBuilder.Build<GraphPartitionSpec>(
                    manager, new GraphPartitionSpec(graph, k, min, max));

                BruteForceFamily expected = BruteForcePartitions(graph, k, min, max);

                FamilyAssert.AssertSameFamily($"seed={seed} k={k} [{min},{max}]", built, expected);
            }
        }

        [Theory]
        [InlineData(2, 3, 2, 1, 6)]
        [InlineData(3, 3, 3, 1, 9)]
        public void EveryEnumeratedSetIsAValidPartition(int rows, int cols, int k, int min, int max)
        {
            Graph graph = Graph.Grid(rows, cols);

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<GraphPartitionSpec>(manager, new GraphPartitionSpec(graph, k, min, max));

            foreach (int[] edgeSet in built.Sets())
            {
                List<int> sizes = ComponentSizes(graph, edgeSet);
                Assert.Equal(k, sizes.Count);
                Assert.All(sizes, size => Assert.InRange(size, min, max));
            }
        }

        [Fact]
        public void KEqualsOneMatchesConnectedSubgraphOverAllVertices()
        {
            Graph graph = Graph.Grid(2, 3);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd partition = FrontierBuilder.Build<GraphPartitionSpec>(
                manager, new GraphPartitionSpec(graph, 1, 1, graph.VertexCount));
            Zdd connected = FrontierBuilder.Build<ConnectedSubgraphSpec>(
                manager, new ConnectedSubgraphSpec(graph, Enumerable.Range(0, graph.VertexCount)));

            Assert.Equal(connected, partition);
        }

        [Fact]
        public void UnsatisfiableBalanceIsEmpty()
        {
            // A triangle can only ever end up as 1 or 3 components (2 is impossible: any kept edge merges
            // two vertices into a size-2 piece, leaving a lone size-1 piece — never two same-sized pieces).
            Graph graph = Graph.Cycle(3);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<GraphPartitionSpec>(manager, new GraphPartitionSpec(graph, 2, 2, 2));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void IsolatedVertexOutsideBalanceRangeIsEmpty()
        {
            // Vertex 2 has no incident edges, so it is always its own size-1 block.
            var graph = new Graph(3, new[] { new Edge(0, 1) });
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<GraphPartitionSpec>(manager, new GraphPartitionSpec(graph, 2, 2, 3));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void IsolatedVerticesCountTowardK()
        {
            // Vertex 2 is isolated: it is always its own block, so k must include it.
            var graph = new Graph(3, new[] { new Edge(0, 1) });
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<GraphPartitionSpec>(manager, new GraphPartitionSpec(graph, 2, 1, 2));

            BruteForceFamily expected = BruteForcePartitions(graph, 2, 1, 2);
            FamilyAssert.AssertSameFamily(null, built, expected);
            Assert.True(built.Count > BigInteger.Zero);
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new GraphPartitionSpec(null!, 1, 1, 1));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void ConstructorRejectsNonPositiveK(int k)
        {
            Graph graph = Graph.Path(4);
            Assert.Throws<ArgumentOutOfRangeException>(() => new GraphPartitionSpec(graph, k, 1, 4));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void ConstructorRejectsNonPositiveMinBlockSize(int min)
        {
            Graph graph = Graph.Path(4);
            Assert.Throws<ArgumentOutOfRangeException>(() => new GraphPartitionSpec(graph, 1, min, 4));
        }

        [Fact]
        public void ConstructorRejectsMaxBelowMin()
        {
            Graph graph = Graph.Path(4);
            Assert.Throws<ArgumentOutOfRangeException>(() => new GraphPartitionSpec(graph, 1, 3, 2));
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            Graph grid = Graph.Grid(4, 4);
            var spec = new GraphPartitionSpec(grid, 2, 1, grid.VertexCount);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneEdgePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(GraphPartitionSpec spec, Span<int> state, int level)
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

        /// <summary>A handful of <c>(k, min, max)</c> patterns spanning tight and loose balance ranges.</summary>
        private static IEnumerable<(int K, int Min, int Max)> BalancePatterns(int vertexCount)
        {
            yield return (1, 1, vertexCount);
            yield return (vertexCount, 1, 1);

            if (vertexCount >= 2)
            {
                yield return (2, 1, vertexCount);
                yield return (2, vertexCount / 2, vertexCount);
            }

            if (vertexCount >= 3)
            {
                yield return (3, 1, vertexCount);
            }
        }

        /// <summary>The vertex counts of each connected component of <paramref name="graph"/> under <paramref name="edgeSet"/> (kept edges).</summary>
        private static List<int> ComponentSizes(Graph graph, IReadOnlyList<int> edgeSet)
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
                parent[Find(edge.U)] = Find(edge.V);
            }

            var sizes = new Dictionary<int, int>();
            for (int v = 0; v < graph.VertexCount; v++)
            {
                int root = Find(v);
                sizes[root] = sizes.GetValueOrDefault(root) + 1;
            }

            return sizes.Values.ToList();
        }

        private static BruteForceFamily BruteForcePartitions(Graph graph, int k, int min, int max)
        {
            var accepted = new List<int>();
            int edgeCount = graph.EdgeCount;

            if (edgeCount >= 31)
            {
                throw new ArgumentException(
                    $"BruteForcePartitions enumerates all 2^edgeCount subsets and cannot handle {edgeCount} edges.",
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

                List<int> sizes = ComponentSizes(graph, edgeSet);
                if (sizes.Count == k && sizes.All(size => size >= min && size <= max))
                {
                    accepted.Add(mask);
                }
            }

            return BruteForceFamily.FromMasks(edgeCount, accepted);
        }
    }
}
