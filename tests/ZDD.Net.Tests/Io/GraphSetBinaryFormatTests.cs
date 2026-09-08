using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using ZDD.Net.Graphs;
using ZDD.Net.Io;

namespace ZDD.Net.Tests.Io
{
    public class GraphSetBinaryFormatTests
    {
        [Fact]
        public void RoundTripsEveryMemberOfAnUndirectedFamily()
        {
            Graph graph = Graph.Grid(3, 3);
            GraphSet original = GraphSet.Paths(graph, 0, 8);

            GraphSet restored = RoundTrip(original);

            Assert.Equal(graph.VertexCount, restored.Graph.VertexCount);
            Assert.Equal(graph.Edges, restored.Graph.Edges);
            AssertMembersEqual(original, restored);
        }

        [Fact]
        public void RoundTripsEveryMemberOfADirectedFamily()
        {
            DirectedGraph graph = new DirectedGraph(4, new[]
            {
                new DirectedEdge(0, 1),
                new DirectedEdge(1, 3),
                new DirectedEdge(0, 2),
                new DirectedEdge(2, 3),
                new DirectedEdge(1, 2),
            });
            DirectedGraphSet original = DirectedGraphSet.Paths(graph, 0, 3);

            DirectedGraphSet restored = RoundTrip(original);

            Assert.Equal(graph.VertexCount, restored.Graph.VertexCount);
            Assert.Equal(graph.Edges, restored.Graph.Edges);
            AssertMembersEqual(original, restored);
        }

        [Fact]
        public void RoundTripsOptimizedGraphWithItsOriginalEdgeOrderMapping()
        {
            Graph source = new Graph(5, new[]
            {
                new Edge(3, 4),
                new Edge(2, 3),
                new Edge(1, 2),
                new Edge(0, 1),
                new Edge(0, 4),
            });
            Graph optimized = source.Optimize(EdgeOrderStrategy.Bfs, EdgeOrderOptions.FromVertex(0));
            GraphSet original = GraphSet.Paths(optimized, 0, 3);

            GraphSet restored = RoundTrip(original);

            Assert.NotNull(restored.Graph.SourceOrder);
            Assert.Equal(optimized.Edges, restored.Graph.Edges);
            Assert.Equal(source.Edges, restored.Graph.SourceOrder!.Source.Edges);
            Assert.Equal(optimized.SourceOrder!.ToSourceEdgeIndices, restored.Graph.SourceOrder.ToSourceEdgeIndices);
            AssertMembersEqual(original.ToEdgeOrder(source), restored.ToEdgeOrder(restored.Graph.SourceOrder.Source));
        }

        [Fact]
        public void RoundTripsOptimizedDirectedGraphWithItsOriginalEdgeOrderMapping()
        {
            DirectedGraph source = new DirectedGraph(5, new[]
            {
                new DirectedEdge(3, 4),
                new DirectedEdge(2, 3),
                new DirectedEdge(1, 2),
                new DirectedEdge(0, 1),
                new DirectedEdge(0, 4),
            });
            DirectedGraph optimized = source.Optimize(EdgeOrderStrategy.Bfs, EdgeOrderOptions.FromVertex(0));
            DirectedGraphSet original = DirectedGraphSet.Paths(optimized, 0, 3);

            DirectedGraphSet restored = RoundTrip(original);

            Assert.NotNull(restored.Graph.SourceOrder);
            Assert.Equal(optimized.Edges, restored.Graph.Edges);
            Assert.Equal(source.Edges, restored.Graph.SourceOrder!.Source.Edges);
            Assert.Equal(optimized.SourceOrder!.ToSourceEdgeIndices, restored.Graph.SourceOrder.ToSourceEdgeIndices);
        }

        [Fact]
        public void RejectsBadMagicUnsupportedVersionAndKindMismatch()
        {
            GraphSet family = GraphSet.PowerSet(Graph.Path(3));
            byte[] bytes = Write(family);

            byte[] badMagic = (byte[])bytes.Clone();
            badMagic[0] ^= 0xff;
            Assert.Throws<ZddFormatException>(() => GraphSetBinaryFormat.Read(new MemoryStream(badMagic)));

            byte[] badVersion = (byte[])bytes.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(badVersion.AsSpan(4, 4), 2);
            Assert.Throws<ZddFormatException>(() => GraphSetBinaryFormat.Read(new MemoryStream(badVersion)));

            Assert.Throws<ZddFormatException>(() => GraphSetBinaryFormat.ReadDirected(new MemoryStream(bytes)));
        }

        [Fact]
        public void RejectsTruncatedGraphAndInvalidSourceOrder()
        {
            Graph source = Graph.Path(4);
            GraphSet family = GraphSet.PowerSet(source.WithEdgeOrder(new[] { 2, 0, 1 }));
            byte[] bytes = Write(family);

            Assert.Throws<ZddFormatException>(() => GraphSetBinaryFormat.Read(new MemoryStream(bytes.Take(11).ToArray())));

            byte[] badOrder = (byte[])bytes.Clone();
            int sourceOrderStart = 9 + 1 + 1 + (source.EdgeCount * 2) + 1;
            badOrder[sourceOrderStart + 1] = badOrder[sourceOrderStart];
            Assert.Throws<ZddFormatException>(() => GraphSetBinaryFormat.Read(new MemoryStream(badOrder)));
        }

        [Fact]
        public void ExistingZddBinaryVersionOneStillRoundTrips()
        {
            GraphSet family = GraphSet.Paths(Graph.Grid(3, 3), 0, 8);
            using MemoryStream stream = new MemoryStream();
            ZddBinaryFormat.Write(family.Zdd, stream);

            Assert.Equal(1u, ZddBinaryFormat.FormatVersion);
            stream.Position = 0;
            var restored = ZddBinaryFormat.Read(stream);
            using var manager = restored.Manager;
            Assert.Equal(family.Zdd.Sets().Select(Key).OrderBy(x => x), restored.Sets().Select(Key).OrderBy(x => x));
        }

        private static GraphSet RoundTrip(GraphSet family)
        {
            using MemoryStream stream = new MemoryStream(Write(family));
            return GraphSetBinaryFormat.Read(stream);
        }

        private static DirectedGraphSet RoundTrip(DirectedGraphSet family)
        {
            using MemoryStream stream = new MemoryStream();
            GraphSetBinaryFormat.Write(family, stream);
            stream.Position = 0;
            return GraphSetBinaryFormat.ReadDirected(stream);
        }

        private static byte[] Write(GraphSet family)
        {
            using MemoryStream stream = new MemoryStream();
            GraphSetBinaryFormat.Write(family, stream);
            return stream.ToArray();
        }

        private static void AssertMembersEqual(GraphSet expected, GraphSet actual) =>
            Assert.Equal(expected.Select(Key).OrderBy(x => x), actual.Select(Key).OrderBy(x => x));

        private static void AssertMembersEqual(DirectedGraphSet expected, DirectedGraphSet actual) =>
            Assert.Equal(expected.Select(Key).OrderBy(x => x), actual.Select(Key).OrderBy(x => x));

        private static string Key(IEnumerable<Edge> edges) =>
            string.Join(",", edges.OrderBy(e => e.U).ThenBy(e => e.V).Select(e => $"{e.U}-{e.V}"));

        private static string Key(IEnumerable<DirectedEdge> edges) =>
            string.Join(",", edges.OrderBy(e => e.From).ThenBy(e => e.To).Select(e => $"{e.From}>{e.To}"));

        private static string Key(IEnumerable<int> items) => string.Join(",", items);
    }
}
