using System;
using System.IO;
using System.Linq;
using Xunit;
using ZDD.Net.Graphs;
using ZDD.Net.Io;

namespace ZDD.Net.Tests.Io
{
    /// <summary>
    /// M3-10 completion criteria for <see cref="DimacsGraph"/>: exact round trip (vertex count, edge
    /// count, and edge order), a realistic DIMACS file with comments/whitespace/mixed line endings/no
    /// trailing newline, the 1-based &#8596; 0-based vertex conversion, thousands of edges, and
    /// line-numbered exceptions on malformed input.
    /// </summary>
    public class DimacsGraphTests
    {
        // ---- round trip ----

        [Fact]
        public void WriteThenReadReproducesVertexEdgeCountAndOrder()
        {
            Graph original = Graph.Grid(4, 5);

            using StringWriter writer = new StringWriter();
            DimacsGraph.Write(original, writer);

            Graph roundTripped = DimacsGraph.Read(new StringReader(writer.ToString()));

            AssertSameGraph(original, roundTripped);
        }

        [Fact]
        public void WriteThenReadRoundTripsAGraphWithThousandsOfEdges()
        {
            // 50x50 grid: 50*49 + 49*50 = 4900 edges.
            Graph original = Graph.Grid(50, 50);
            Assert.True(original.EdgeCount > 4000);

            using StringWriter writer = new StringWriter();
            DimacsGraph.Write(original, writer);

            Graph roundTripped = DimacsGraph.Read(new StringReader(writer.ToString()));

            AssertSameGraph(original, roundTripped);
        }

        // ---- 1-based <-> 0-based conversion ----

        [Fact]
        public void ReadConvertsOneBasedVerticesToZeroBased()
        {
            Graph graph = DimacsGraph.Read(new StringReader("p edge 5 1\ne 1 5\n"));

            Assert.Equal(5, graph.VertexCount);
            Assert.Equal(1, graph.EdgeCount);
            Assert.Equal(new Edge(0, 4), graph.GetEdge(0));
        }

        [Fact]
        public void WriteConvertsZeroBasedVerticesToOneBased()
        {
            Graph graph = new Graph(5, new[] { new Edge(0, 4) });

            string text = DimacsGraph.Write(graph);

            Assert.Contains("e 1 5", text, StringComparison.Ordinal);
        }

        // ---- a realistic file ----

        [Fact]
        public void ReadToleratesCommentsWhitespaceMixedLineEndingsAndNoTrailingNewline()
        {
            // Comment lines, ragged internal spacing, CRLF mixed with LF, and no final newline.
            string text =
                "c a small path graph\r\n" +
                "c another comment\n" +
                "p  edge   4   3\r\n" +
                "e 1 2\n" +
                "e  2   3\r\n" +
                "e 3 4";

            Graph graph = DimacsGraph.Read(new StringReader(text));

            Assert.Equal(4, graph.VertexCount);
            Assert.Equal(new[] { new Edge(0, 1), new Edge(1, 2), new Edge(2, 3) }, graph.Edges);
        }

        // ---- malformed input: line-numbered exceptions ----

        [Fact]
        public void MissingProblemLineIsRejected()
        {
            var ex = Assert.Throws<GraphFormatException>(() => DimacsGraph.Read(new StringReader("c only a comment\n")));
            Assert.Equal(1, ex.LineNumber);
        }

        [Fact]
        public void EdgeCountMismatchIsRejectedWithTheOffendingLineNumber()
        {
            var ex = Assert.Throws<GraphFormatException>(
                () => DimacsGraph.Read(new StringReader("p edge 3 2\ne 1 2\n")));

            Assert.Equal(2, ex.LineNumber);
        }

        [Fact]
        public void OutOfRangeVertexIsRejectedWithTheOffendingLineNumber()
        {
            var ex = Assert.Throws<GraphFormatException>(
                () => DimacsGraph.Read(new StringReader("p edge 3 1\ne 1 4\n")));

            Assert.Equal(2, ex.LineNumber);
        }

        [Fact]
        public void ABrokenEdgeLineIsRejectedWithTheOffendingLineNumber()
        {
            var ex = Assert.Throws<GraphFormatException>(
                () => DimacsGraph.Read(new StringReader("p edge 3 1\ne 1\n")));

            Assert.Equal(2, ex.LineNumber);
        }

        [Fact]
        public void ANonNumericTokenIsRejectedWithTheOffendingLineNumber()
        {
            var ex = Assert.Throws<GraphFormatException>(
                () => DimacsGraph.Read(new StringReader("p edge three 1\n")));

            Assert.Equal(1, ex.LineNumber);
        }

        // ---- null arguments ----

        [Fact]
        public void ReadRejectsANullReader()
        {
            Assert.Equal("reader", Assert.Throws<ArgumentNullException>(() => DimacsGraph.Read((TextReader)null!)).ParamName);
        }

        [Fact]
        public void WriteRejectsANullGraphOrWriter()
        {
            Graph graph = Graph.Path(3);

            Assert.Equal("graph", Assert.Throws<ArgumentNullException>(() => DimacsGraph.Write(null!, new StringWriter())).ParamName);
            Assert.Equal("writer", Assert.Throws<ArgumentNullException>(() => DimacsGraph.Write(graph, null!)).ParamName);
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
