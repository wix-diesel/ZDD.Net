using System;
using System.Collections.Generic;
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
    /// M2-10 completion criteria for <see cref="MatchingSpec"/>: the perfect-matching count matches an
    /// independent bitmask-DP permanent computation, known closed forms hold (complete graphs, cycles,
    /// paths), matches brute-force enumeration on small graphs with every enumerated set verified as an
    /// actual matching, <see cref="MatchingSpec.Perfect"/> covers every vertex, an odd vertex count with
    /// <see cref="MatchingSpec.Perfect"/> builds to <c>Empty</c>, and <c>GetChild</c> does not allocate.
    /// </summary>
    public class MatchingSpecTests
    {
        [Theory]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(6)]
        [InlineData(8)]
        public void PerfectMatchingCountMatchesBitmaskDpForCompleteGraphs(int n)
        {
            Graph graph = Graph.Complete(n);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<MatchingSpec>(manager, new MatchingSpec(graph, perfect: true));

            Assert.Equal(BitmaskMatchingCounter.CountPerfectMatchings(graph), built.Count);
            Assert.Equal(DoubleFactorial(n - 1), built.Count); // (n-1)!! closed form for K_n, n even
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void MatchingCountMatchesBitmaskDpForRandomGraphs(int seed)
        {
            Graph graph = SpanningTreeSpecTests.RandomConnectedGraph(vertexCount: 7, extraEdgeProbability: 0.35, seed);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<MatchingSpec>(manager, new MatchingSpec(graph));

            Assert.Equal(BitmaskMatchingCounter.CountMatchings(graph), built.Count);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        public void MatchingCountForCycleIsLucasNumber(int n)
        {
            Graph graph = Graph.Cycle(n);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<MatchingSpec>(manager, new MatchingSpec(graph));

            Assert.Equal(BitmaskMatchingCounter.CountMatchings(graph), built.Count);
            Assert.Equal(Lucas(n), built.Count);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        public void MatchingCountForPathIsFibonacciNumber(int n)
        {
            Graph graph = Graph.Path(n);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<MatchingSpec>(manager, new MatchingSpec(graph));

            Assert.Equal(BitmaskMatchingCounter.CountMatchings(graph), built.Count);
            Assert.Equal(Fibonacci(n + 1), built.Count);
        }

        [Theory]
        [InlineData("path4", false)]
        [InlineData("path4", true)]
        [InlineData("cycle5", false)]
        [InlineData("cycle6", true)]
        [InlineData("complete5", false)]
        [InlineData("complete6", true)]
        [InlineData("grid2x3", false)]
        [InlineData("grid2x3", true)]
        public void MatchesBruteForceEnumerationOnSmallGraphs(string graphName, bool perfect)
        {
            Graph graph = NamedGraph(graphName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<MatchingSpec>(manager, new MatchingSpec(graph, perfect));

            BruteForceFamily expected = BruteForceMatchings(graph, perfect);

            FamilyAssert.AssertSameFamily($"{graphName} perfect={perfect}", built, expected);
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        public void EveryEnumeratedSetIsAValidMatching(string graphName)
        {
            Graph graph = NamedGraph(graphName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<MatchingSpec>(manager, new MatchingSpec(graph));

            foreach (int[] edgeSet in built.Sets())
            {
                AssertIsMatching(graph, edgeSet);
            }
        }

        [Theory]
        [InlineData("complete4")]
        [InlineData("cycle6")]
        [InlineData("grid2x3")]
        public void PerfectMatchingsCoverEveryVertex(string graphName)
        {
            Graph graph = NamedGraph(graphName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<MatchingSpec>(manager, new MatchingSpec(graph, perfect: true));

            Assert.True(built.Count > BigInteger.Zero, "expected at least one perfect matching for this test graph");

            foreach (int[] edgeSet in built.Sets())
            {
                AssertIsMatching(graph, edgeSet);

                var covered = new bool[graph.VertexCount];
                foreach (int edgeIndex in edgeSet)
                {
                    Edge edge = graph.GetEdge(edgeIndex);
                    covered[edge.U] = true;
                    covered[edge.V] = true;
                }

                for (int v = 0; v < graph.VertexCount; v++)
                {
                    Assert.True(covered[v], $"vertex {v} is not covered by {string.Join(",", edgeSet)}");
                }
            }
        }

        [Theory]
        [InlineData(3)]
        [InlineData(5)]
        [InlineData(7)]
        public void OddVertexCountWithPerfectIsEmpty(int n)
        {
            Graph graph = Graph.Complete(n);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<MatchingSpec>(manager, new MatchingSpec(graph, perfect: true));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new MatchingSpec(null!));
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            Graph grid = Graph.Grid(4, 4);
            var spec = new MatchingSpec(grid);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneEdgePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(MatchingSpec spec, Span<int> state, int level)
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

        /// <summary>Checks that <paramref name="edgeSet"/> is a matching: no two edges share a vertex.</summary>
        private static void AssertIsMatching(Graph graph, int[] edgeSet)
        {
            var used = new bool[graph.VertexCount];
            foreach (int edgeIndex in edgeSet)
            {
                Edge edge = graph.GetEdge(edgeIndex);
                Assert.False(used[edge.U], $"vertex {edge.U} is covered twice in {string.Join(",", edgeSet)}");
                Assert.False(used[edge.V], $"vertex {edge.V} is covered twice in {string.Join(",", edgeSet)}");
                used[edge.U] = true;
                used[edge.V] = true;
            }
        }

        private static Graph NamedGraph(string graphName) => graphName switch
        {
            "path4" => Graph.Path(4),
            "cycle5" => Graph.Cycle(5),
            "cycle6" => Graph.Cycle(6),
            "complete4" => Graph.Complete(4),
            "complete5" => Graph.Complete(5),
            "complete6" => Graph.Complete(6),
            "grid2x3" => Graph.Grid(2, 3),
            _ => throw new ArgumentOutOfRangeException(nameof(graphName)),
        };

        private static BruteForceFamily BruteForceMatchings(Graph graph, bool perfect)
        {
            var accepted = new List<int>();
            int edgeCount = graph.EdgeCount;

            if (edgeCount >= 31)
            {
                throw new ArgumentException(
                    $"BruteForceMatchings enumerates all 2^edgeCount subsets and cannot handle {edgeCount} edges.",
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

                if (IsMatching(graph, edgeSet, perfect))
                {
                    accepted.Add(mask);
                }
            }

            return BruteForceFamily.FromMasks(edgeCount, accepted);
        }

        private static bool IsMatching(Graph graph, List<int> edgeSet, bool perfect)
        {
            var used = new bool[graph.VertexCount];
            foreach (int edgeIndex in edgeSet)
            {
                Edge edge = graph.GetEdge(edgeIndex);
                if (used[edge.U] || used[edge.V])
                {
                    return false;
                }

                used[edge.U] = true;
                used[edge.V] = true;
            }

            if (!perfect)
            {
                return true;
            }

            foreach (bool covered in used)
            {
                if (!covered)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The double factorial <c>k!! = k(k-2)(k-4)...</c>, with <c>0!! = (-1)!! = 1</c>.</summary>
        private static BigInteger DoubleFactorial(int k)
        {
            BigInteger result = BigInteger.One;
            for (int i = k; i > 0; i -= 2)
            {
                result *= i;
            }

            return result;
        }

        /// <summary>Fibonacci numbers with <c>F(0) = 0</c>, <c>F(1) = 1</c>.</summary>
        private static BigInteger Fibonacci(int n)
        {
            BigInteger a = 0, b = 1;
            for (int i = 0; i < n; i++)
            {
                (a, b) = (b, a + b);
            }

            return a;
        }

        /// <summary>Lucas numbers with <c>L(0) = 2</c>, <c>L(1) = 1</c>.</summary>
        private static BigInteger Lucas(int n)
        {
            BigInteger a = 2, b = 1;
            for (int i = 0; i < n; i++)
            {
                (a, b) = (b, a + b);
            }

            return a;
        }
    }
}
