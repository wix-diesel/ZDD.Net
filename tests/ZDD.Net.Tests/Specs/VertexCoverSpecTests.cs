using System;
using System.Linq;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Specs
{
    /// <summary>
    /// M3-6 completion criteria for <see cref="VertexCoverSpec"/>: matches an independently written
    /// brute-force enumeration (with every enumerated set verified as an actual vertex cover), its
    /// complement equals <see cref="IndependentSetSpec"/> on the same graph, and <c>GetChild</c> does not
    /// allocate.
    /// </summary>
    public class VertexCoverSpecTests
    {
        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        public void MatchesBruteForceEnumerationOnSmallGraphs(string graphName)
        {
            Graph graph = SpanningTreeSpecTests.NamedGraph(graphName);
            using ZddManager manager = new ZddManager(graph.VertexCount);

            Zdd built = FrontierBuilder.Build<VertexCoverSpec>(manager, new VertexCoverSpec(graph));

            BruteForceFamily expected = BruteForceVertexSets.Enumerate(graph, IsVertexCover);

            FamilyAssert.AssertSameFamily(graphName, built, expected);
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        public void EveryEnumeratedSetIsAVertexCover(string graphName)
        {
            Graph graph = SpanningTreeSpecTests.NamedGraph(graphName);
            using ZddManager manager = new ZddManager(graph.VertexCount);

            Zdd built = FrontierBuilder.Build<VertexCoverSpec>(manager, new VertexCoverSpec(graph));

            foreach (int[] vertexSet in built.Sets())
            {
                Assert.True(
                    IsVertexCover(graph, BruteForceVertexSets.ToMembership(graph, vertexSet)),
                    $"{{{string.Join(",", vertexSet)}}} does not cover every edge of {graphName}");
            }
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        public void ComplementOfEveryVertexCoverIsAnIndependentSet(string graphName)
        {
            Graph graph = SpanningTreeSpecTests.NamedGraph(graphName);
            using ZddManager manager = new ZddManager(graph.VertexCount);

            Zdd vertexCovers = FrontierBuilder.Build<VertexCoverSpec>(manager, new VertexCoverSpec(graph));
            Zdd independentSets = FrontierBuilder.Build<IndependentSetSpec>(manager, new IndependentSetSpec(graph));

            BruteForceFamily coverFamily = ZddFamilies.ToBruteForce(vertexCovers);
            int universe = coverFamily.UniverseMask;
            BruteForceFamily complementOfCovers =
                BruteForceFamily.FromMasks(graph.VertexCount, coverFamily.Masks.Select(mask => mask ^ universe));

            FamilyAssert.AssertSameFamily($"complement of vertex covers, {graphName}", independentSets, complementOfCovers);
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new VertexCoverSpec(null!));
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            Graph grid = Graph.Grid(4, 4);
            var spec = new VertexCoverSpec(grid);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneVertexPerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneVertexPerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneVertexPerLevel(VertexCoverSpec spec, Span<int> state, int level)
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

        /// <summary>Checks the definition directly: every edge has at least one selected endpoint.</summary>
        internal static bool IsVertexCover(Graph graph, bool[] membership)
        {
            for (int i = 0; i < graph.EdgeCount; i++)
            {
                Edge edge = graph.GetEdge(i);
                if (!membership[edge.U] && !membership[edge.V])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
