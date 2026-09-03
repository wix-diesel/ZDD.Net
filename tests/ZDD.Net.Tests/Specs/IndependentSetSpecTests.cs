using System;
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
    /// M3-6 completion criteria for <see cref="IndependentSetSpec"/>: matches an independently written
    /// brute-force enumeration (with every enumerated set verified as an actual independent set), matches
    /// the known closed forms (<c>Path(n)</c> = Fibonacci, <c>Cycle(n)</c> = Lucas, <c>Complete(n)</c> =
    /// <c>n + 1</c>), and <c>GetChild</c> does not allocate.
    /// </summary>
    public class IndependentSetSpecTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        public void PathIndependentSetCountIsFibonacciNumber(int n)
        {
            Graph graph = Graph.Path(n);
            using ZddManager manager = new ZddManager(graph.VertexCount);

            Zdd built = FrontierBuilder.Build<IndependentSetSpec>(manager, new IndependentSetSpec(graph));

            Assert.Equal(Fibonacci(n + 2), built.Count);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        public void CycleIndependentSetCountIsLucasNumber(int n)
        {
            Graph graph = Graph.Cycle(n);
            using ZddManager manager = new ZddManager(graph.VertexCount);

            Zdd built = FrontierBuilder.Build<IndependentSetSpec>(manager, new IndependentSetSpec(graph));

            Assert.Equal(Lucas(n), built.Count);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void CompleteIndependentSetCountIsVertexCountPlusOne(int n)
        {
            Graph graph = Graph.Complete(n);
            using ZddManager manager = new ZddManager(graph.VertexCount);

            Zdd built = FrontierBuilder.Build<IndependentSetSpec>(manager, new IndependentSetSpec(graph));

            Assert.Equal(n + 1, built.Count);
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        public void MatchesBruteForceEnumerationOnSmallGraphs(string graphName)
        {
            Graph graph = SpanningTreeSpecTests.NamedGraph(graphName);
            using ZddManager manager = new ZddManager(graph.VertexCount);

            Zdd built = FrontierBuilder.Build<IndependentSetSpec>(manager, new IndependentSetSpec(graph));

            BruteForceFamily expected = BruteForceVertexSets.Enumerate(graph, IsIndependentSet);

            FamilyAssert.AssertSameFamily(graphName, built, expected);
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        public void EveryEnumeratedSetIsIndependent(string graphName)
        {
            Graph graph = SpanningTreeSpecTests.NamedGraph(graphName);
            using ZddManager manager = new ZddManager(graph.VertexCount);

            Zdd built = FrontierBuilder.Build<IndependentSetSpec>(manager, new IndependentSetSpec(graph));

            foreach (int[] vertexSet in built.Sets())
            {
                Assert.True(
                    IsIndependentSet(graph, BruteForceVertexSets.ToMembership(graph, vertexSet)),
                    $"{{{string.Join(",", vertexSet)}}} is not independent in {graphName}");
            }
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new IndependentSetSpec(null!));
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            Graph grid = Graph.Grid(4, 4);
            var spec = new IndependentSetSpec(grid);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneVertexPerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneVertexPerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneVertexPerLevel(IndependentSetSpec spec, Span<int> state, int level)
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

        /// <summary>Checks the definition directly: no edge has both endpoints selected.</summary>
        internal static bool IsIndependentSet(Graph graph, bool[] membership)
        {
            for (int i = 0; i < graph.EdgeCount; i++)
            {
                Edge edge = graph.GetEdge(i);
                if (membership[edge.U] && membership[edge.V])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Fibonacci numbers with <c>F(0) = 0</c>, <c>F(1) = 1</c>.</summary>
        internal static BigInteger Fibonacci(int n)
        {
            BigInteger a = 0, b = 1;
            for (int i = 0; i < n; i++)
            {
                (a, b) = (b, a + b);
            }

            return a;
        }

        /// <summary>Lucas numbers with <c>L(0) = 2</c>, <c>L(1) = 1</c>.</summary>
        internal static BigInteger Lucas(int n)
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
