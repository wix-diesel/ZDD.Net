using System;
using Xunit;
using ZDD.Net.Graphs;

namespace ZDD.Net.Tests.Graphs
{
    /// <summary>
    /// M2-6 completion criteria for <see cref="Graph"/> itself: edge order preservation, adjacency,
    /// edge index / variable index / level round trips, and rejection of invalid input.
    /// </summary>
    public class GraphTests
    {
        [Fact]
        public void PreservesEdgeOrderExactlyAsConstructed()
        {
            var edges = new[] { new Edge(2, 0), new Edge(0, 1), new Edge(1, 2) };

            var graph = new Graph(3, edges);

            Assert.Equal(edges.Length, graph.EdgeCount);
            for (int i = 0; i < edges.Length; i++)
            {
                // Edge equality is order-independent (see Edge remarks), so U/V are asserted explicitly
                // here to also catch a regression that swaps endpoints.
                Assert.Equal(edges[i].U, graph.Edges[i].U);
                Assert.Equal(edges[i].V, graph.Edges[i].V);
                Assert.Equal(edges[i].U, graph.GetEdge(i).U);
                Assert.Equal(edges[i].V, graph.GetEdge(i).V);
            }
        }

        [Fact]
        public void IncidentEdgesArePerVertexInEdgeOrder()
        {
            // Triangle 0-1-2, edges added out of any "natural" vertex order.
            var graph = new Graph(3, new[] { new Edge(1, 2), new Edge(0, 1), new Edge(0, 2) });

            Assert.Equal(new[] { 1, 2 }, graph.IncidentEdges(0));
            Assert.Equal(new[] { 0, 1 }, graph.IncidentEdges(1));
            Assert.Equal(new[] { 0, 2 }, graph.IncidentEdges(2));

            Assert.Equal(2, graph.Degree(0));
            Assert.Equal(2, graph.Degree(1));
            Assert.Equal(2, graph.Degree(2));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        public void EdgeIndexToVariableIndexIsTheIdentity(int edgeIndex)
        {
            var graph = Graph.Path(6);

            int variableIndex = graph.EdgeIndexToVariableIndex(edgeIndex);

            Assert.Equal(edgeIndex, variableIndex);
            Assert.Equal(edgeIndex, graph.VariableIndexToEdgeIndex(variableIndex));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        public void EdgeIndexToLevelRoundTripsAtTheBoundaries(int edgeIndex)
        {
            var graph = Graph.Path(6); // 5 edges: indices 0 .. 4

            int level = graph.EdgeIndexToLevel(edgeIndex);

            Assert.InRange(level, 1, graph.EdgeCount);
            Assert.Equal(edgeIndex, graph.LevelToEdgeIndex(level));
        }

        [Fact]
        public void EdgeZeroIsTheRootLevelAndTheLastEdgeIsLevelOne()
        {
            var graph = Graph.Path(6); // 5 edges

            Assert.Equal(graph.EdgeCount, graph.EdgeIndexToLevel(0));
            Assert.Equal(1, graph.EdgeIndexToLevel(graph.EdgeCount - 1));
        }

        [Fact]
        public void RejectsNonPositiveVertexCount()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Graph(0, Array.Empty<Edge>()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Graph(-1, Array.Empty<Edge>()));
        }

        [Fact]
        public void RejectsOutOfRangeVertexIndices()
        {
            Assert.Throws<ArgumentException>(() => new Graph(3, new[] { new Edge(0, 3) }));
            Assert.Throws<ArgumentException>(() => new Graph(3, new[] { new Edge(-1, 0) }));
        }

        [Fact]
        public void RejectsSelfLoops()
        {
            Assert.Throws<ArgumentException>(() => new Graph(3, new[] { new Edge(1, 1) }));
        }

        [Fact]
        public void RejectsDuplicateEdgesRegardlessOfDirection()
        {
            Assert.Throws<ArgumentException>(() => new Graph(3, new[] { new Edge(0, 1), new Edge(1, 0) }));
        }

        [Fact]
        public void WithEdgeOrderReordersEdgesAndPreservesTheGraph()
        {
            var graph = Graph.Path(4); // edges: (0,1) (1,2) (2,3)

            var reordered = graph.WithEdgeOrder(new[] { 2, 0, 1 });

            Assert.Equal(new[] { new Edge(2, 3), new Edge(0, 1), new Edge(1, 2) }, reordered.Edges);
            Assert.Equal(graph.VertexCount, reordered.VertexCount);
        }

        [Fact]
        public void WithEdgeOrderRejectsNonPermutations()
        {
            var graph = Graph.Path(4);

            Assert.Throws<ArgumentException>(() => graph.WithEdgeOrder(new[] { 0, 0, 1 }));
            Assert.Throws<ArgumentException>(() => graph.WithEdgeOrder(new[] { 0, 1 }));
        }
    }
}
