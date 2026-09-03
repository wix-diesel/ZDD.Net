using System;
using System.IO;
using Xunit;
using ZDD.Net.Graphs;
using ZDD.Net.Io;

namespace ZDD.Net.Tests.Io
{
    /// <summary>
    /// M3-10 completion criteria for <see cref="EdgeListGraph"/>: exact round trip (including a trailing
    /// isolated vertex, which a bare "one pair per line" format cannot represent without the header
    /// line), tolerance for comments/whitespace/mixed line endings, thousands of edges, and
    /// line-numbered exceptions on malformed input.
    /// </summary>
    public class EdgeListGraphTests
    {
        // ---- round trip ----

        [Fact]
        public void WriteThenReadReproducesVertexEdgeCountAndOrder()
        {
            Graph original = Graph.Grid(4, 5);

            using StringWriter writer = new StringWriter();
            EdgeListGraph.Write(original, writer);

            Graph roundTripped = EdgeListGraph.Read(new StringReader(writer.ToString()));

            AssertSameGraph(original, roundTripped);
        }

        [Fact]
        public void WriteThenReadPreservesATrailingIsolatedVertex()
        {
            // Vertex 4 has no incident edge; a plain "u v" list alone could not recover VertexCount == 5.
            Graph original = new Graph(5, new[] { new Edge(0, 1), new Edge(1, 2) });

            string text = EdgeListGraph.Write(original);
            Graph roundTripped = EdgeListGraph.Read(text);

            Assert.Equal(5, roundTripped.VertexCount);
            AssertSameGraph(original, roundTripped);
        }

        [Fact]
        public void WriteThenReadRoundTripsAGraphWithThousandsOfEdges()
        {
            Graph original = Graph.Complete(100); // 100*99/2 = 4950 edges
            Assert.True(original.EdgeCount > 4000);

            string text = EdgeListGraph.Write(original);
            Graph roundTripped = EdgeListGraph.Read(text);

            AssertSameGraph(original, roundTripped);
        }

        // ---- tolerant parsing ----

        [Fact]
        public void ReadToleratesCommentsWhitespaceCommasMixedLineEndingsAndNoTrailingNewline()
        {
            string text =
                "# a small path graph\r\n" +
                "4\n" +
                "0 1\r\n" +
                "1,2\n" +
                "2   3";

            Graph graph = EdgeListGraph.Read(text);

            Assert.Equal(4, graph.VertexCount);
            Assert.Equal(new[] { new Edge(0, 1), new Edge(1, 2), new Edge(2, 3) }, graph.Edges);
        }

        // ---- malformed input: line-numbered exceptions ----

        [Fact]
        public void MissingHeaderLineIsRejected()
        {
            var ex = Assert.Throws<GraphFormatException>(() => EdgeListGraph.Read("# only a comment\n"));
            Assert.Equal(1, ex.LineNumber);
        }

        [Fact]
        public void CompletelyEmptyInputReportsLineOneNotLineZero()
        {
            var ex = Assert.Throws<GraphFormatException>(() => EdgeListGraph.Read(string.Empty));
            Assert.Equal(1, ex.LineNumber);
            Assert.StartsWith("Line 1:", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void OutOfRangeVertexIsRejectedWithTheOffendingLineNumber()
        {
            var ex = Assert.Throws<GraphFormatException>(() => EdgeListGraph.Read("3\n0 1\n1 5\n"));
            Assert.Equal(3, ex.LineNumber);
        }

        [Fact]
        public void ABrokenEdgeLineIsRejectedWithTheOffendingLineNumber()
        {
            var ex = Assert.Throws<GraphFormatException>(() => EdgeListGraph.Read("3\n0 1 2\n"));
            Assert.Equal(2, ex.LineNumber);
        }

        // ---- null arguments ----

        [Fact]
        public void ReadRejectsANullReader()
        {
            Assert.Equal("reader", Assert.Throws<ArgumentNullException>(() => EdgeListGraph.Read((TextReader)null!)).ParamName);
        }

        [Fact]
        public void WriteRejectsANullGraphOrWriter()
        {
            Graph graph = Graph.Path(3);

            Assert.Equal("graph", Assert.Throws<ArgumentNullException>(() => EdgeListGraph.Write(null!, new StringWriter())).ParamName);
            Assert.Equal("writer", Assert.Throws<ArgumentNullException>(() => EdgeListGraph.Write(graph, (TextWriter)null!)).ParamName);
        }

        // ---- helpers ----

        private static void AssertSameGraph(Graph expected, Graph actual)
        {
            Assert.Equal(expected.VertexCount, actual.VertexCount);
            Assert.Equal(expected.EdgeCount, actual.EdgeCount);

            for (int i = 0; i < expected.EdgeCount; i++)
            {
                Assert.Equal(expected.GetEdge(i).U, actual.GetEdge(i).U);
                Assert.Equal(expected.GetEdge(i).V, actual.GetEdge(i).V);
            }
        }
    }
}
