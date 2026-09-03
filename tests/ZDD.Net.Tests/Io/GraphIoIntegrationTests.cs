using System.Numerics;
using Xunit;
using ZDD.Net.Graphs;
using ZDD.Net.Io;

namespace ZDD.Net.Tests.Io
{
    /// <summary>
    /// M3-10 completion criterion: a graph loaded through each text format is not just structurally
    /// equal to the original &#8212; it also drives <see cref="GraphSet"/>'s frontier-method operations
    /// correctly, matching what the same operations produce on the in-memory original.
    /// </summary>
    public class GraphIoIntegrationTests
    {
        [Fact]
        public void ADimacsRoundTrippedGraphDrivesGraphSetOperations()
        {
            Graph original = Graph.Grid(3, 3);
            Graph loaded = DimacsGraph.Read(DimacsGraph.Write(original));

            AssertSameOperationResults(original, loaded);
        }

        [Fact]
        public void AnEdgeListRoundTrippedGraphDrivesGraphSetOperations()
        {
            Graph original = Graph.Grid(3, 3);
            Graph loaded = EdgeListGraph.Read(EdgeListGraph.Write(original));

            AssertSameOperationResults(original, loaded);
        }

        [Fact]
        public void ASimpleTextRoundTrippedGraphDrivesGraphSetOperations()
        {
            Graph original = Graph.Grid(3, 3);
            Graph loaded = SimpleTextGraph.Read(SimpleTextGraph.Write(original)).Graph;

            AssertSameOperationResults(original, loaded);
        }

        private static void AssertSameOperationResults(Graph original, Graph loaded)
        {
            BigInteger originalPaths = GraphSet.Paths(original, from: 0, to: original.VertexCount - 1).Count;
            BigInteger loadedPaths = GraphSet.Paths(loaded, from: 0, to: loaded.VertexCount - 1).Count;
            Assert.Equal(originalPaths, loadedPaths);
            Assert.True(originalPaths > 0);

            BigInteger originalTrees = GraphSet.Trees(original).Count;
            BigInteger loadedTrees = GraphSet.Trees(loaded).Count;
            Assert.Equal(originalTrees, loadedTrees);
            Assert.True(originalTrees > 0);

            BigInteger originalMatchings = GraphSet.Matchings(original).Count;
            BigInteger loadedMatchings = GraphSet.Matchings(loaded).Count;
            Assert.Equal(originalMatchings, loadedMatchings);
            Assert.True(originalMatchings > 0);
        }
    }
}
