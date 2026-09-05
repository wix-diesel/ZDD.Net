using System;
using System.Collections.Generic;
using Xunit;
using ZDD.Net.Graphs;

namespace ZDD.Net.Tests.Graphs
{
    /// <summary>
    /// M7-1 completion criteria for <see cref="DirectedGraph"/>: construction/validation, adjacency
    /// (<see cref="DirectedGraph.OutgoingEdges"/> / <see cref="DirectedGraph.IncomingEdges"/> /
    /// <see cref="DirectedGraph.IncidentEdges"/>), the <see cref="DirectedGraph.Bidirected"/> /
    /// <see cref="DirectedGraph.ToUndirected"/> round trip, and the generator shortcuts
    /// (<see cref="DirectedGraph.Grid"/> / <see cref="DirectedGraph.Complete"/> /
    /// <see cref="DirectedGraph.Cycle"/> / <see cref="DirectedGraph.Path"/>).
    /// </summary>
    public class DirectedGraphTests
    {
        [Fact]
        public void AllowsAntiParallelArcs()
        {
            var graph = new DirectedGraph(2, new[] { new DirectedEdge(0, 1), new DirectedEdge(1, 0) });

            Assert.Equal(2, graph.EdgeCount);
        }

        [Fact]
        public void RejectsNonPositiveVertexCount()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DirectedGraph(0, Array.Empty<DirectedEdge>()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DirectedGraph(-1, Array.Empty<DirectedEdge>()));
        }

        [Fact]
        public void RejectsOutOfRangeVertexIndices()
        {
            Assert.Throws<ArgumentException>(() => new DirectedGraph(3, new[] { new DirectedEdge(0, 3) }));
            Assert.Throws<ArgumentException>(() => new DirectedGraph(3, new[] { new DirectedEdge(-1, 0) }));
        }

        [Fact]
        public void RejectsSelfLoops()
        {
            Assert.Throws<ArgumentException>(() => new DirectedGraph(3, new[] { new DirectedEdge(1, 1) }));
        }

        [Fact]
        public void RejectsDuplicateArcsInTheSameDirection()
        {
            Assert.Throws<ArgumentException>(() => new DirectedGraph(3, new[] { new DirectedEdge(0, 1), new DirectedEdge(0, 1) }));
        }

        [Fact]
        public void OutgoingIncomingAndIncidentCountAntiParallelArcsCorrectly()
        {
            // 0 -> 1, 1 -> 0, 1 -> 2
            var graph = new DirectedGraph(3, new[]
            {
                new DirectedEdge(0, 1),
                new DirectedEdge(1, 0),
                new DirectedEdge(1, 2),
            });

            Assert.Equal(new[] { 0 }, graph.OutgoingEdges(0));
            Assert.Equal(new[] { 1 }, graph.IncomingEdges(0));
            Assert.Equal(new[] { 0, 1 }, graph.IncidentEdges(0));
            Assert.Equal(1, graph.OutDegree(0));
            Assert.Equal(1, graph.InDegree(0));

            Assert.Equal(new[] { 1, 2 }, graph.OutgoingEdges(1));
            Assert.Equal(new[] { 0 }, graph.IncomingEdges(1));
            Assert.Equal(new[] { 0, 1, 2 }, graph.IncidentEdges(1));
            Assert.Equal(2, graph.OutDegree(1));
            Assert.Equal(1, graph.InDegree(1));

            Assert.Equal(Array.Empty<int>(), graph.OutgoingEdges(2));
            Assert.Equal(new[] { 2 }, graph.IncomingEdges(2));
            Assert.Equal(new[] { 2 }, graph.IncidentEdges(2));
            Assert.Equal(0, graph.OutDegree(2));
            Assert.Equal(1, graph.InDegree(2));
        }

        [Fact]
        public void ToUndirectedCollapsesAntiParallelArcsAndHasNullSourceOrder()
        {
            var graph = new DirectedGraph(3, new[]
            {
                new DirectedEdge(0, 1),
                new DirectedEdge(1, 0),
                new DirectedEdge(1, 2),
            });

            Graph undirected = graph.ToUndirected();

            Assert.Equal(3, undirected.VertexCount);
            Assert.Equal(2, undirected.EdgeCount); // (0,1)+(1,0) collapse to one edge
            Assert.Null(undirected.SourceOrder);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 3)]
        [InlineData(4, 4)]
        public void BidirectedThenToUndirectedMatchesTheOriginalGraph(int rows, int cols)
        {
            Graph original = Graph.Grid(rows, cols);

            Graph roundTripped = DirectedGraph.Bidirected(original).ToUndirected();

            Assert.Equal(original.VertexCount, roundTripped.VertexCount);
            Assert.Equal(new HashSet<Edge>(original.Edges), new HashSet<Edge>(roundTripped.Edges));
            Assert.Null(roundTripped.SourceOrder);
        }

        [Fact]
        public void BidirectedOpensEveryEdgeToBothDirections()
        {
            Graph undirected = Graph.Path(3); // (0,1) (1,2)

            DirectedGraph directed = DirectedGraph.Bidirected(undirected);

            Assert.Equal(4, directed.EdgeCount);
            Assert.Contains(directed.Edges, e => e.From == 0 && e.To == 1);
            Assert.Contains(directed.Edges, e => e.From == 1 && e.To == 0);
            Assert.Contains(directed.Edges, e => e.From == 1 && e.To == 2);
            Assert.Contains(directed.Edges, e => e.From == 2 && e.To == 1);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 3)]
        [InlineData(4, 4)]
        public void GridIsBidirectedUndirectedGrid(int rows, int cols)
        {
            var graph = DirectedGraph.Grid(rows, cols);
            var undirected = Graph.Grid(rows, cols);

            Assert.Equal(rows * cols, graph.VertexCount);
            Assert.Equal(undirected.EdgeCount * 2, graph.EdgeCount);
            AssertSimpleDirectedGraph(graph);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        public void CompleteHasEveryOrderedPairOfDistinctVertices(int n)
        {
            var graph = DirectedGraph.Complete(n);

            Assert.Equal(n, graph.VertexCount);
            Assert.Equal(n * (n - 1), graph.EdgeCount);
            AssertSimpleDirectedGraph(graph);

            for (int v = 0; v < n; v++)
            {
                Assert.Equal(n - 1, graph.OutDegree(v));
                Assert.Equal(n - 1, graph.InDegree(v));
            }
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(10)]
        public void CycleHasNVerticesNArcsAndUnitInOutDegree(int n)
        {
            var graph = DirectedGraph.Cycle(n);

            Assert.Equal(n, graph.VertexCount);
            Assert.Equal(n, graph.EdgeCount);
            AssertSimpleDirectedGraph(graph);

            for (int v = 0; v < n; v++)
            {
                Assert.Equal(1, graph.OutDegree(v));
                Assert.Equal(1, graph.InDegree(v));
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        public void PathHasNVerticesNMinusOneArcsAndEndpointDegrees(int n)
        {
            var graph = DirectedGraph.Path(n);

            Assert.Equal(n, graph.VertexCount);
            Assert.Equal(n - 1, graph.EdgeCount);
            AssertSimpleDirectedGraph(graph);

            if (n == 1)
            {
                Assert.Equal(0, graph.OutDegree(0));
                Assert.Equal(0, graph.InDegree(0));
                return;
            }

            Assert.Equal(1, graph.OutDegree(0));
            Assert.Equal(0, graph.InDegree(0));
            Assert.Equal(0, graph.OutDegree(n - 1));
            Assert.Equal(1, graph.InDegree(n - 1));
            for (int v = 1; v < n - 1; v++)
            {
                Assert.Equal(1, graph.OutDegree(v));
                Assert.Equal(1, graph.InDegree(v));
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void GridRejectsNonPositiveDimensions(int size)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DirectedGraph.Grid(size, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => DirectedGraph.Grid(3, size));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void CompleteRejectsNonPositiveN(int n)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DirectedGraph.Complete(n));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void CycleRejectsFewerThanTwoVertices(int n)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DirectedGraph.Cycle(n));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void PathRejectsNonPositiveN(int n)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DirectedGraph.Path(n));
        }

        private static void AssertSimpleDirectedGraph(DirectedGraph graph)
        {
            var seen = new HashSet<DirectedEdge>();
            foreach (DirectedEdge edge in graph.Edges)
            {
                Assert.NotEqual(edge.From, edge.To);
                Assert.True(seen.Add(edge), $"Duplicate arc {edge}.");
            }
        }
    }
}
