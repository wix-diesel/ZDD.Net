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
    /// M3-6 completion criteria for <see cref="CliqueSpec"/>: matches an independently written brute-force
    /// enumeration (with every enumerated set verified as an actual clique), matches the known closed form
    /// (<c>Complete(n)</c> = <c>2^n</c>, every subset is a clique), agrees with
    /// <see cref="IndependentSetSpec"/> built over an independently-computed complement graph, and
    /// <c>GetChild</c> does not allocate.
    /// </summary>
    public class CliqueSpecTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void CompleteCliqueCountIsTwoToTheN(int n)
        {
            Graph graph = Graph.Complete(n);
            using ZddManager manager = new ZddManager(graph.VertexCount);

            Zdd built = FrontierBuilder.Build<CliqueSpec>(manager, new CliqueSpec(graph));

            Assert.Equal(BigInteger.Pow(2, n), built.Count);
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

            Zdd built = FrontierBuilder.Build<CliqueSpec>(manager, new CliqueSpec(graph));

            BruteForceFamily expected = BruteForceVertexSets.Enumerate(graph, IsClique);

            FamilyAssert.AssertSameFamily(graphName, built, expected);
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        public void EveryEnumeratedSetIsAClique(string graphName)
        {
            Graph graph = SpanningTreeSpecTests.NamedGraph(graphName);
            using ZddManager manager = new ZddManager(graph.VertexCount);

            Zdd built = FrontierBuilder.Build<CliqueSpec>(manager, new CliqueSpec(graph));

            foreach (int[] vertexSet in built.Sets())
            {
                Assert.True(
                    IsClique(graph, BruteForceVertexSets.ToMembership(graph, vertexSet)),
                    $"{{{string.Join(",", vertexSet)}}} is not a clique in {graphName}");
            }
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("grid2x3")]
        public void EqualsIndependentSetOfAnIndependentlyComputedComplementGraph(string graphName)
        {
            Graph graph = SpanningTreeSpecTests.NamedGraph(graphName);
            using ZddManager manager = new ZddManager(graph.VertexCount);

            Zdd cliques = FrontierBuilder.Build<CliqueSpec>(manager, new CliqueSpec(graph));

            Graph complement = ComplementWrittenIndependently(graph);
            Zdd independentSetsOfComplement =
                FrontierBuilder.Build<IndependentSetSpec>(manager, new IndependentSetSpec(complement));

            Assert.Equal(independentSetsOfComplement, cliques);
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new CliqueSpec(null!));
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            Graph grid = Graph.Grid(4, 4);
            var spec = new CliqueSpec(grid);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneVertexPerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneVertexPerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneVertexPerLevel(CliqueSpec spec, Span<int> state, int level)
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

        /// <summary>Checks the definition directly: every pair of selected vertices is adjacent.</summary>
        internal static bool IsClique(Graph graph, bool[] membership)
        {
            var adjacent = new HashSet<Edge>();
            for (int i = 0; i < graph.EdgeCount; i++)
            {
                adjacent.Add(graph.GetEdge(i));
            }

            for (int u = 0; u < graph.VertexCount; u++)
            {
                if (!membership[u])
                {
                    continue;
                }

                for (int v = u + 1; v < graph.VertexCount; v++)
                {
                    if (membership[v] && !adjacent.Contains(new Edge(u, v)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Builds the complement graph from the definition, deliberately without going near
        /// <see cref="CliqueSpec"/>'s own (private) complement code, so this test stays an independent
        /// check rather than a restatement of the implementation.
        /// </summary>
        private static Graph ComplementWrittenIndependently(Graph graph)
        {
            var present = new bool[graph.VertexCount, graph.VertexCount];
            for (int i = 0; i < graph.EdgeCount; i++)
            {
                Edge edge = graph.GetEdge(i);
                present[edge.U, edge.V] = true;
                present[edge.V, edge.U] = true;
            }

            var complementEdges = new List<Edge>();
            for (int u = 0; u < graph.VertexCount; u++)
            {
                for (int v = u + 1; v < graph.VertexCount; v++)
                {
                    if (!present[u, v])
                    {
                        complementEdges.Add(new Edge(u, v));
                    }
                }
            }

            return new Graph(graph.VertexCount, complementEdges);
        }
    }
}
