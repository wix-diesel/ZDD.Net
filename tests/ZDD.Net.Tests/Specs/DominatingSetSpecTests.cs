using System;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Specs
{
    /// <summary>
    /// M3-6 completion criteria for <see cref="DominatingSetSpec"/>: matches an independently written
    /// brute-force enumeration (with every enumerated set verified as an actual dominating set), an
    /// isolated vertex must be selected in every accepted set, and <c>GetChild</c> does not allocate.
    /// </summary>
    public class DominatingSetSpecTests
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

            Zdd built = FrontierBuilder.Build<DominatingSetSpec>(manager, new DominatingSetSpec(graph));

            BruteForceFamily expected = BruteForceVertexSets.Enumerate(graph, IsDominatingSet);

            FamilyAssert.AssertSameFamily(graphName, built, expected);
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        public void EveryEnumeratedSetIsADominatingSet(string graphName)
        {
            Graph graph = SpanningTreeSpecTests.NamedGraph(graphName);
            using ZddManager manager = new ZddManager(graph.VertexCount);

            Zdd built = FrontierBuilder.Build<DominatingSetSpec>(manager, new DominatingSetSpec(graph));

            foreach (int[] vertexSet in built.Sets())
            {
                Assert.True(
                    IsDominatingSet(graph, BruteForceVertexSets.ToMembership(graph, vertexSet)),
                    $"{{{string.Join(",", vertexSet)}}} does not dominate every vertex of {graphName}");
            }
        }

        [Fact]
        public void IsolatedVertexIsSelectedInEveryAcceptedSet()
        {
            // Vertex 2 has no incident edges, so nothing else can ever dominate it.
            var graph = new Graph(3, new[] { new Edge(0, 1) });
            using ZddManager manager = new ZddManager(graph.VertexCount);

            Zdd built = FrontierBuilder.Build<DominatingSetSpec>(manager, new DominatingSetSpec(graph));

            BruteForceFamily expected = BruteForceVertexSets.Enumerate(graph, IsDominatingSet);
            FamilyAssert.AssertSameFamily("graph with an isolated vertex", built, expected);

            foreach (int[] vertexSet in built.Sets())
            {
                Assert.Contains(2, vertexSet);
            }
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new DominatingSetSpec(null!));
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            Graph grid = Graph.Grid(4, 4);
            var spec = new DominatingSetSpec(grid);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneVertexPerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneVertexPerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneVertexPerLevel(DominatingSetSpec spec, Span<int> state, int level)
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

        /// <summary>Checks the definition directly: every vertex is selected or adjacent to a selected vertex.</summary>
        internal static bool IsDominatingSet(Graph graph, bool[] membership)
        {
            for (int v = 0; v < graph.VertexCount; v++)
            {
                if (membership[v])
                {
                    continue;
                }

                bool dominated = false;
                foreach (int edgeIndex in graph.IncidentEdges(v))
                {
                    if (membership[graph.GetEdge(edgeIndex).Other(v)])
                    {
                        dominated = true;
                        break;
                    }
                }

                if (!dominated)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
