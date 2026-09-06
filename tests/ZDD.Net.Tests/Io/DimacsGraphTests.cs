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
        public void CompletelyEmptyInputReportsLineOneNotLineZero()
        {
            // No line is ever read, so the naive "last line processed" would be 0 — but LineNumber's
            // contract is 1-based, so this must still report line 1.
            var ex = Assert.Throws<GraphFormatException>(() => DimacsGraph.Read(new StringReader(string.Empty)));
            Assert.Equal(1, ex.LineNumber);
            Assert.StartsWith("Line 1:", ex.Message, StringComparison.Ordinal);
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

        // ---- directed (M7-7, issue #158) ----

        [Fact]
        public void WriteDirectedThenReadDirectedReproducesVertexArcCountAndOrder()
        {
            DirectedGraph original = DirectedGraph.Grid(4, 5);

            using StringWriter writer = new StringWriter();
            DimacsGraph.WriteDirected(original, writer);

            DirectedGraph roundTripped = DimacsGraph.ReadDirected(new StringReader(writer.ToString()));

            AssertSameGraph(original, roundTripped);
        }

        [Fact]
        public void WriteDirectedThenReadDirectedPreservesAntiParallelArcs()
        {
            DirectedGraph original = new DirectedGraph(2, new[] { new DirectedEdge(0, 1), new DirectedEdge(1, 0) });

            string text = DimacsGraph.WriteDirected(original);
            DirectedGraph roundTripped = DimacsGraph.ReadDirected(text);

            AssertSameGraph(original, roundTripped);
        }

        [Fact]
        public void ReadDirectedConvertsOneBasedVerticesToZeroBased()
        {
            DirectedGraph graph = DimacsGraph.ReadDirected(new StringReader("p arc 5 1\ne 1 5\n"));

            Assert.Equal(5, graph.VertexCount);
            Assert.Equal(1, graph.EdgeCount);
            Assert.Equal(new DirectedEdge(0, 4), graph.GetEdge(0));
        }

        [Fact]
        public void WriteDirectedConvertsZeroBasedVerticesToOneBased()
        {
            DirectedGraph graph = new DirectedGraph(5, new[] { new DirectedEdge(0, 4) });

            string text = DimacsGraph.WriteDirected(graph);

            Assert.Contains("p arc", text, StringComparison.Ordinal);
            Assert.Contains("e 1 5", text, StringComparison.Ordinal);
        }

        [Fact]
        public void ExistingUndirectedFilesStillReadAsUndirectedThroughRead()
        {
            // Backward compatibility: 'p edge' is exactly the pre-M7-7 format and must keep working
            // through Read, unchanged.
            Graph graph = DimacsGraph.Read(new StringReader("p edge 3 2\ne 1 2\ne 2 3\n"));
            Assert.Equal(new[] { new Edge(0, 1), new Edge(1, 2) }, graph.Edges);
        }

        [Fact]
        public void ReadRejectsAPArcProblemLinePointingAtReadDirected()
        {
            var ex = Assert.Throws<GraphFormatException>(() => DimacsGraph.Read(new StringReader("p arc 2 1\ne 1 2\n")));
            Assert.Equal(1, ex.LineNumber);
            Assert.Contains("ReadDirected", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ReadDirectedRejectsAPEdgeProblemLinePointingAtRead()
        {
            var ex = Assert.Throws<GraphFormatException>(() => DimacsGraph.ReadDirected(new StringReader("p edge 2 1\ne 1 2\n")));
            Assert.Equal(1, ex.LineNumber);
            Assert.Contains("DimacsGraph.Read", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ReadDirectedRejectsASelfLoopWithTheOffendingLineNumber()
        {
            var ex = Assert.Throws<GraphFormatException>(
                () => DimacsGraph.ReadDirected(new StringReader("p arc 2 1\ne 1 1\n")));

            Assert.Equal(2, ex.LineNumber);
            Assert.Contains("self-loop", ex.Message, StringComparison.Ordinal);

            // The message must report the 1-based vertex number as it appears in the input line, not the
            // internal 0-based index — DIMACS is 1-based throughout, including its other error messages.
            Assert.Contains("Arc 1 -> 1", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ReadDirectedRejectsADuplicateArcWithTheOffendingLineNumber()
        {
            var ex = Assert.Throws<GraphFormatException>(
                () => DimacsGraph.ReadDirected(new StringReader("p arc 2 2\ne 1 2\ne 1 2\n")));

            Assert.Equal(3, ex.LineNumber);
            Assert.Contains("duplicates", ex.Message, StringComparison.Ordinal);

            // Same 1-based reporting requirement as the self-loop case above.
            Assert.Contains("Arc 1 -> 2", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ReadDirectedDoesNotRejectAnAntiParallelArcAsADuplicate()
        {
            DirectedGraph graph = DimacsGraph.ReadDirected(new StringReader("p arc 2 2\ne 1 2\ne 2 1\n"));
            Assert.Equal(2, graph.EdgeCount);
        }

        [Fact]
        public void ReadDirectedRejectsANullReader()
        {
            Assert.Equal("reader", Assert.Throws<ArgumentNullException>(() => DimacsGraph.ReadDirected((TextReader)null!)).ParamName);
        }

        [Fact]
        public void WriteDirectedRejectsANullGraphOrWriter()
        {
            DirectedGraph graph = DirectedGraph.Path(3);

            Assert.Equal("graph", Assert.Throws<ArgumentNullException>(() => DimacsGraph.WriteDirected(null!, new StringWriter())).ParamName);
            Assert.Equal("writer", Assert.Throws<ArgumentNullException>(() => DimacsGraph.WriteDirected(graph, null!)).ParamName);
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

        private static void AssertSameGraph(DirectedGraph expected, DirectedGraph actual)
        {
            Assert.Equal(expected.VertexCount, actual.VertexCount);
            Assert.Equal(expected.EdgeCount, actual.EdgeCount);

            for (int i = 0; i < expected.EdgeCount; i++)
            {
                Assert.Equal(expected.GetEdge(i).From, actual.GetEdge(i).From);
                Assert.Equal(expected.GetEdge(i).To, actual.GetEdge(i).To);
            }
        }
    }
}
