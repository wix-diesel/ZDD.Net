using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ZDD.Net.Graphs;

namespace ZDD.Net.Tests.Graphs
{
    /// <summary>
    /// M2-6 completion criteria for the factories: vertex/edge counts against the theoretical formulas,
    /// degree sequences, absence of self-loops/multi-edges, and rejection of invalid sizes.
    /// </summary>
    public class GraphFactoryTests
    {
        [Theory]
        [InlineData(1, 1)]
        [InlineData(1, 5)]
        [InlineData(5, 1)]
        [InlineData(2, 3)]
        [InlineData(4, 4)]
        [InlineData(3, 7)]
        public void GridHasTheoreticalVertexAndEdgeCounts(int rows, int cols)
        {
            var graph = Graph.Grid(rows, cols);

            Assert.Equal(rows * cols, graph.VertexCount);
            Assert.Equal(rows * (cols - 1) + (rows - 1) * cols, graph.EdgeCount);
            AssertSimpleGraph(graph);
        }

        [Fact]
        public void GridVertexDegreesMatchCornerEdgeCornerPattern()
        {
            var graph = Graph.Grid(3, 4);

            // Corners have degree 2, edge (non-corner boundary) vertices degree 3, interior degree 4.
            int Vertex(int r, int c) => r * 4 + c;

            Assert.Equal(2, graph.Degree(Vertex(0, 0)));
            Assert.Equal(2, graph.Degree(Vertex(0, 3)));
            Assert.Equal(2, graph.Degree(Vertex(2, 0)));
            Assert.Equal(2, graph.Degree(Vertex(2, 3)));
            Assert.Equal(3, graph.Degree(Vertex(0, 1)));
            Assert.Equal(4, graph.Degree(Vertex(1, 1)));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        [InlineData(8)]
        public void CompleteHasTheoreticalEdgeCountAndUniformDegree(int n)
        {
            var graph = Graph.Complete(n);

            Assert.Equal(n, graph.VertexCount);
            Assert.Equal(n * (n - 1) / 2, graph.EdgeCount);
            AssertSimpleGraph(graph);

            for (int v = 0; v < n; v++)
            {
                Assert.Equal(n - 1, graph.Degree(v));
            }
        }

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(10)]
        public void CycleHasNVerticesNEdgesAndDegreeTwo(int n)
        {
            var graph = Graph.Cycle(n);

            Assert.Equal(n, graph.VertexCount);
            Assert.Equal(n, graph.EdgeCount);
            AssertSimpleGraph(graph);

            for (int v = 0; v < n; v++)
            {
                Assert.Equal(2, graph.Degree(v));
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        [InlineData(10)]
        public void PathHasNVerticesNMinusOneEdgesAndEndpointDegreeOne(int n)
        {
            var graph = Graph.Path(n);

            Assert.Equal(n, graph.VertexCount);
            Assert.Equal(n - 1, graph.EdgeCount);
            AssertSimpleGraph(graph);

            if (n == 1)
            {
                Assert.Equal(0, graph.Degree(0));
                return;
            }

            Assert.Equal(1, graph.Degree(0));
            Assert.Equal(1, graph.Degree(n - 1));
            for (int v = 1; v < n - 1; v++)
            {
                Assert.Equal(2, graph.Degree(v));
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void GridRejectsNonPositiveDimensions(int size)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Graph.Grid(size, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => Graph.Grid(3, size));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void CompleteRejectsNonPositiveN(int n)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Graph.Complete(n));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void CycleRejectsFewerThanThreeVertices(int n)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Graph.Cycle(n));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void PathRejectsNonPositiveN(int n)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Graph.Path(n));
        }

        private static void AssertSimpleGraph(Graph graph)
        {
            var seen = new HashSet<Edge>();
            foreach (Edge edge in graph.Edges)
            {
                Assert.NotEqual(edge.U, edge.V);
                Assert.True(seen.Add(edge), $"Duplicate edge {edge}.");
            }
        }
    }
}
