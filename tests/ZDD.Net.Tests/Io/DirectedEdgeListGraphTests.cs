using System;
using System.IO;
using Xunit;
using ZDD.Net.Graphs;
using ZDD.Net.Io;

namespace ZDD.Net.Tests.Io
{
    /// <summary>
    /// M7-7 completion criteria for <see cref="DirectedEdgeListGraph"/>: exact round trip (including
    /// anti-parallel arcs and a trailing isolated vertex), tolerance for comments/whitespace/mixed line
    /// endings, thousands of arcs, and line-numbered exceptions on malformed input — including self-loops
    /// and multi-arcs, which the undirected <see cref="EdgeListGraph"/> does not need to detect itself
    /// (it delegates to <see cref="Graph"/>'s constructor) but which this format detects with a clear,
    /// line-numbered <see cref="GraphFormatException"/> per issue #158's completion criteria.
    /// </summary>
    public class DirectedEdgeListGraphTests
    {
        // ---- round trip ----

        [Fact]
        public void WriteThenReadReproducesVertexArcCountAndOrder()
        {
            DirectedGraph original = DirectedGraph.Grid(4, 5);

            using StringWriter writer = new StringWriter();
            DirectedEdgeListGraph.Write(original, writer);

            DirectedGraph roundTripped = DirectedEdgeListGraph.Read(new StringReader(writer.ToString()));

            AssertSameGraph(original, roundTripped);
        }

        [Fact]
        public void WriteThenReadPreservesAntiParallelArcs()
        {
            DirectedGraph original = new DirectedGraph(2, new[] { new DirectedEdge(0, 1), new DirectedEdge(1, 0) });

            string text = DirectedEdgeListGraph.Write(original);
            DirectedGraph roundTripped = DirectedEdgeListGraph.Read(text);

            AssertSameGraph(original, roundTripped);
        }

        [Fact]
        public void WriteThenReadPreservesATrailingIsolatedVertex()
        {
            DirectedGraph original = new DirectedGraph(5, new[] { new DirectedEdge(0, 1), new DirectedEdge(1, 2) });

            string text = DirectedEdgeListGraph.Write(original);
            DirectedGraph roundTripped = DirectedEdgeListGraph.Read(text);

            Assert.Equal(5, roundTripped.VertexCount);
            AssertSameGraph(original, roundTripped);
        }

        [Fact]
        public void WriteThenReadRoundTripsAGraphWithThousandsOfArcs()
        {
            DirectedGraph original = DirectedGraph.Complete(100); // 100*99 = 9900 arcs
            Assert.True(original.EdgeCount > 4000);

            string text = DirectedEdgeListGraph.Write(original);
            DirectedGraph roundTripped = DirectedEdgeListGraph.Read(text);

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

            DirectedGraph graph = DirectedEdgeListGraph.Read(text);

            Assert.Equal(4, graph.VertexCount);
            Assert.Equal(new[] { new DirectedEdge(0, 1), new DirectedEdge(1, 2), new DirectedEdge(2, 3) }, graph.Edges);
        }

        // ---- malformed input: line-numbered exceptions ----

        [Fact]
        public void MissingHeaderLineIsRejected()
        {
            var ex = Assert.Throws<GraphFormatException>(() => DirectedEdgeListGraph.Read("# only a comment\n"));
            Assert.Equal(1, ex.LineNumber);
        }

        [Fact]
        public void CompletelyEmptyInputReportsLineOneNotLineZero()
        {
            var ex = Assert.Throws<GraphFormatException>(() => DirectedEdgeListGraph.Read(string.Empty));
            Assert.Equal(1, ex.LineNumber);
            Assert.StartsWith("Line 1:", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void OutOfRangeVertexIsRejectedWithTheOffendingLineNumber()
        {
            var ex = Assert.Throws<GraphFormatException>(() => DirectedEdgeListGraph.Read("3\n0 1\n1 5\n"));
            Assert.Equal(3, ex.LineNumber);
        }

        [Fact]
        public void ABrokenArcLineIsRejectedWithTheOffendingLineNumber()
        {
            var ex = Assert.Throws<GraphFormatException>(() => DirectedEdgeListGraph.Read("3\n0 1 2\n"));
            Assert.Equal(2, ex.LineNumber);
        }

        [Fact]
        public void ASelfLoopIsRejectedWithTheOffendingLineNumber()
        {
            var ex = Assert.Throws<GraphFormatException>(() => DirectedEdgeListGraph.Read("3\n0 1\n2 2\n"));
            Assert.Equal(3, ex.LineNumber);
            Assert.Contains("self-loop", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ADuplicateArcIsRejectedWithTheOffendingLineNumber()
        {
            var ex = Assert.Throws<GraphFormatException>(() => DirectedEdgeListGraph.Read("3\n0 1\n0 1\n"));
            Assert.Equal(3, ex.LineNumber);
            Assert.Contains("duplicates", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AnAntiParallelArcIsNotRejectedAsADuplicate()
        {
            DirectedGraph graph = DirectedEdgeListGraph.Read("2\n0 1\n1 0\n");
            Assert.Equal(2, graph.EdgeCount);
        }

        // ---- null arguments ----

        [Fact]
        public void ReadRejectsANullReader()
        {
            Assert.Equal("reader", Assert.Throws<ArgumentNullException>(() => DirectedEdgeListGraph.Read((TextReader)null!)).ParamName);
        }

        [Fact]
        public void WriteRejectsANullGraphOrWriter()
        {
            DirectedGraph graph = DirectedGraph.Path(3);

            Assert.Equal("graph", Assert.Throws<ArgumentNullException>(() => DirectedEdgeListGraph.Write(null!, new StringWriter())).ParamName);
            Assert.Equal("writer", Assert.Throws<ArgumentNullException>(() => DirectedEdgeListGraph.Write(graph, (TextWriter)null!)).ParamName);
        }

        // ---- helpers ----

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
