using System;
using System.IO;
using Xunit;
using ZDD.Net.Graphs;
using ZDD.Net.Io;

namespace ZDD.Net.Tests.Io
{
    /// <summary>
    /// M3-10 completion criteria for <see cref="SimpleTextGraph"/>: exact round trip of vertex/edge
    /// counts, edge order, <i>and</i> vertex labels (the one format that carries labels), tolerance for
    /// interleaved/commented/malformed input, and line-numbered exceptions.
    /// </summary>
    public class SimpleTextGraphTests
    {
        // ---- round trip, with labels ----

        [Fact]
        public void WriteThenReadReproducesGraphAndLabels()
        {
            Graph original = new Graph(4, new[] { new Edge(0, 1), new Edge(1, 2), new Edge(2, 3), new Edge(0, 3) });
            string[] labels = { "Alice", "Bob", "Carol", "Dave" };

            string text = SimpleTextGraph.Write(original, labels);
            LabeledGraph roundTripped = SimpleTextGraph.Read(text);

            AssertSameGraph(original, roundTripped.Graph);
            Assert.Equal(labels, roundTripped.VertexLabels);
        }

        [Fact]
        public void WriteWithoutLabelsDefaultsEachVertexToItsOwnIndex()
        {
            Graph original = Graph.Path(4);

            string text = SimpleTextGraph.Write(original);
            LabeledGraph roundTripped = SimpleTextGraph.Read(text);

            Assert.Equal(new[] { "0", "1", "2", "3" }, roundTripped.VertexLabels);
        }

        [Fact]
        public void WriteThenReadRoundTripsAGraphWithThousandsOfEdges()
        {
            Graph original = Graph.Grid(50, 50);
            Assert.True(original.EdgeCount > 4000);

            string text = SimpleTextGraph.Write(original);
            LabeledGraph roundTripped = SimpleTextGraph.Read(text);

            AssertSameGraph(original, roundTripped.Graph);
        }

        // ---- tolerant / order-independent parsing ----

        [Fact]
        public void EdgeOrderIsPreservedEvenWhenVertexLinesAreInterleaved()
        {
            string text =
                "# header first\r\n" +
                "graph 3 2\n" +
                "vertex 0 A\r\n" +
                "edge 0 1\n" +
                "vertex 1 B\n" +
                "vertex 2 C\n" +
                "edge 1 2";

            LabeledGraph result = SimpleTextGraph.Read(text);

            Assert.Equal(3, result.Graph.VertexCount);
            Assert.Equal(new[] { new Edge(0, 1), new Edge(1, 2) }, result.Graph.Edges);
            Assert.Equal(new[] { "A", "B", "C" }, result.VertexLabels);
        }

        [Fact]
        public void UnlabeledVerticesDefaultToTheirIndex()
        {
            LabeledGraph result = SimpleTextGraph.Read("graph 3 1\nvertex 1 middle\nedge 0 2\n");

            Assert.Equal(new[] { "0", "middle", "2" }, result.VertexLabels);
        }

        // ---- malformed input: line-numbered exceptions ----

        [Fact]
        public void MissingHeaderLineIsRejected()
        {
            var ex = Assert.Throws<GraphFormatException>(() => SimpleTextGraph.Read("# only a comment\n"));
            Assert.Equal(1, ex.LineNumber);
        }

        [Fact]
        public void CompletelyEmptyInputReportsLineOneNotLineZero()
        {
            var ex = Assert.Throws<GraphFormatException>(() => SimpleTextGraph.Read(string.Empty));
            Assert.Equal(1, ex.LineNumber);
            Assert.StartsWith("Line 1:", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void EdgeCountMismatchIsRejectedWithTheOffendingLineNumber()
        {
            var ex = Assert.Throws<GraphFormatException>(() => SimpleTextGraph.Read("graph 3 2\nedge 0 1\n"));
            Assert.Equal(2, ex.LineNumber);
        }

        [Fact]
        public void OutOfRangeEdgeEndpointIsRejectedWithTheOffendingLineNumber()
        {
            var ex = Assert.Throws<GraphFormatException>(() => SimpleTextGraph.Read("graph 3 1\nedge 0 5\n"));
            Assert.Equal(2, ex.LineNumber);
        }

        [Fact]
        public void OutOfRangeVertexIndexIsRejectedWithTheOffendingLineNumber()
        {
            var ex = Assert.Throws<GraphFormatException>(() => SimpleTextGraph.Read("graph 3 0\nvertex 5 x\n"));
            Assert.Equal(2, ex.LineNumber);
        }

        [Fact]
        public void ADuplicateVertexLabelIsRejectedWithTheOffendingLineNumber()
        {
            var ex = Assert.Throws<GraphFormatException>(() => SimpleTextGraph.Read("graph 2 0\nvertex 0 a\nvertex 0 b\n"));
            Assert.Equal(3, ex.LineNumber);
        }

        [Fact]
        public void AnUnrecognizedLineTypeIsRejectedWithTheOffendingLineNumber()
        {
            var ex = Assert.Throws<GraphFormatException>(() => SimpleTextGraph.Read("graph 2 0\nbogus 1 2\n"));
            Assert.Equal(2, ex.LineNumber);
        }

        // ---- null / argument validation ----

        [Fact]
        public void ReadRejectsANullReader()
        {
            Assert.Equal("reader", Assert.Throws<ArgumentNullException>(() => SimpleTextGraph.Read((TextReader)null!)).ParamName);
        }

        [Fact]
        public void WriteRejectsANullGraphOrWriter()
        {
            Graph graph = Graph.Path(3);

            Assert.Equal("graph", Assert.Throws<ArgumentNullException>(() => SimpleTextGraph.Write(null!, new StringWriter())).ParamName);
            Assert.Equal("writer", Assert.Throws<ArgumentNullException>(() => SimpleTextGraph.Write(graph, (TextWriter)null!)).ParamName);
        }

        [Fact]
        public void WriteRejectsAMismatchedLabelCount()
        {
            Graph graph = Graph.Path(3);

            Assert.Equal(
                "vertexLabels",
                Assert.Throws<ArgumentException>(() => SimpleTextGraph.Write(graph, new StringWriter(), new[] { "only-one" })).ParamName);
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
