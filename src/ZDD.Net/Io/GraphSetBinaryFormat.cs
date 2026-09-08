using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using ZDD.Net.Core;
using ZDD.Net.Graphs;

namespace ZDD.Net.Io
{
    /// <summary>Reads and writes graph-backed ZDD families, including graph topology and edge order.</summary>
    public static class GraphSetBinaryFormat
    {
        /// <summary>The format version this build writes and reads.</summary>
        public const uint FormatVersion = 1;

        private const int MagicSize = 4;
        private const byte UndirectedKind = 0;
        private const byte DirectedKind = 1;
        private const byte NoSourceOrder = 0;
        private const byte HasSourceOrder = 1;

        private static ReadOnlySpan<byte> Magic => "GZDB"u8;

        /// <summary>Writes an undirected graph family, leaving the destination stream open.</summary>
        public static void Write(GraphSet family, Stream stream)
        {
            ArgumentNullException.ThrowIfNull(family);
            ArgumentNullException.ThrowIfNull(stream);

            WriteHeader(stream, UndirectedKind);
            WriteGraph(stream, family.Graph);
            ZddBinaryFormat.Write(family.Zdd, stream);
        }

        /// <summary>Writes a directed graph family, leaving the destination stream open.</summary>
        public static void Write(DirectedGraphSet family, Stream stream)
        {
            ArgumentNullException.ThrowIfNull(family);
            ArgumentNullException.ThrowIfNull(stream);

            WriteHeader(stream, DirectedKind);
            WriteGraph(stream, family.Graph);
            ZddBinaryFormat.Write(family.Zdd, stream);
        }

        /// <summary>Reads an undirected graph family from its standalone binary representation.</summary>
        public static GraphSet Read(Stream stream, ZddManagerOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ReadHeader(stream, UndirectedKind);
            Graph graph = ReadGraph(stream);
            Zdd zdd = ZddBinaryFormat.Read(stream, options);

            try
            {
                return GraphSet.FromZddWithOptions(graph, zdd, options);
            }
            finally
            {
                zdd.Manager.Dispose();
            }
        }

        /// <summary>Reads a directed graph family from its standalone binary representation.</summary>
        public static DirectedGraphSet ReadDirected(Stream stream, ZddManagerOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ReadHeader(stream, DirectedKind);
            DirectedGraph graph = ReadDirectedGraph(stream);
            Zdd zdd = ZddBinaryFormat.Read(stream, options);

            try
            {
                return DirectedGraphSet.FromZddWithOptions(graph, zdd, options);
            }
            finally
            {
                zdd.Manager.Dispose();
            }
        }

        private static void WriteHeader(Stream stream, byte kind)
        {
            Span<byte> header = stackalloc byte[MagicSize + 4 + 1];
            Magic.CopyTo(header);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(MagicSize, 4), FormatVersion);
            header[MagicSize + 4] = kind;
            stream.Write(header);
        }

        private static void ReadHeader(Stream stream, byte expectedKind)
        {
            Span<byte> header = stackalloc byte[MagicSize + 4 + 1];
            ReadExact(stream, header, "header");
            if (!header.Slice(0, MagicSize).SequenceEqual(Magic))
            {
                throw new ZddFormatException("Not a ZDD.Net graph-set binary file: the magic number does not match 'GZDB'.");
            }

            uint version = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(MagicSize, 4));
            if (version != FormatVersion)
            {
                throw new ZddFormatException(
                    $"Unsupported graph-set format version {version}; this build reads format version {FormatVersion}.");
            }

            byte kind = header[MagicSize + 4];
            if (kind != expectedKind)
            {
                string expected = expectedKind == DirectedKind ? "directed" : "undirected";
                string actual = kind == DirectedKind ? "directed" : kind == UndirectedKind ? "undirected" : $"unknown ({kind})";
                throw new ZddFormatException($"Graph kind mismatch: expected {expected}, but the file contains {actual} data.");
            }
        }

        private static void WriteGraph(Stream stream, Graph graph)
        {
            WriteGraphPrefix(stream, graph.VertexCount, graph.EdgeCount);
            foreach (Edge edge in graph.Edges)
            {
                VarInt.WriteUInt32(stream, (uint)edge.U);
                VarInt.WriteUInt32(stream, (uint)edge.V);
            }

            WriteSourceOrder(stream, graph.SourceOrder?.ToSourceEdgeIndices);
        }

        private static void WriteGraph(Stream stream, DirectedGraph graph)
        {
            WriteGraphPrefix(stream, graph.VertexCount, graph.EdgeCount);
            foreach (DirectedEdge edge in graph.Edges)
            {
                VarInt.WriteUInt32(stream, (uint)edge.From);
                VarInt.WriteUInt32(stream, (uint)edge.To);
            }

            WriteSourceOrder(stream, graph.SourceOrder?.ToSourceEdgeIndices);
        }

        private static void WriteGraphPrefix(Stream stream, int vertexCount, int edgeCount)
        {
            VarInt.WriteUInt32(stream, (uint)vertexCount);
            VarInt.WriteUInt32(stream, (uint)edgeCount);
        }

        private static void WriteSourceOrder(Stream stream, IReadOnlyList<int>? sourceOrder)
        {
            stream.WriteByte(sourceOrder is null ? NoSourceOrder : HasSourceOrder);
            if (sourceOrder is null)
            {
                return;
            }

            foreach (int sourceIndex in sourceOrder)
            {
                VarInt.WriteUInt32(stream, (uint)sourceIndex);
            }
        }

        private static Graph ReadGraph(Stream stream)
        {
            (int vertexCount, int edgeCount) = ReadGraphPrefix(stream);
            var edges = new Edge[edgeCount];
            for (int i = 0; i < edgeCount; i++)
            {
                int u = ReadVertex(stream, vertexCount, $"edge {i} endpoint U");
                int v = ReadVertex(stream, vertexCount, $"edge {i} endpoint V");
                edges[i] = new Edge(u, v);
            }

            int[]? sourceOrder = ReadSourceOrder(stream, edgeCount);
            try
            {
                Graph graph = new Graph(vertexCount, edges);
                return sourceOrder is null ? graph : RestoreSourceOrder(graph, sourceOrder);
            }
            catch (ArgumentException ex)
            {
                throw new ZddFormatException($"Invalid graph section: {ex.Message}");
            }
        }

        private static DirectedGraph ReadDirectedGraph(Stream stream)
        {
            (int vertexCount, int edgeCount) = ReadGraphPrefix(stream);
            var edges = new DirectedEdge[edgeCount];
            for (int i = 0; i < edgeCount; i++)
            {
                int from = ReadVertex(stream, vertexCount, $"arc {i} endpoint From");
                int to = ReadVertex(stream, vertexCount, $"arc {i} endpoint To");
                edges[i] = new DirectedEdge(from, to);
            }

            int[]? sourceOrder = ReadSourceOrder(stream, edgeCount);
            try
            {
                DirectedGraph graph = new DirectedGraph(vertexCount, edges);
                return sourceOrder is null ? graph : RestoreSourceOrder(graph, sourceOrder);
            }
            catch (ArgumentException ex)
            {
                throw new ZddFormatException($"Invalid directed graph section: {ex.Message}");
            }
        }

        private static (int VertexCount, int EdgeCount) ReadGraphPrefix(Stream stream)
        {
            uint rawVertexCount = VarInt.ReadUInt32(stream, "vertex count");
            uint rawEdgeCount = VarInt.ReadUInt32(stream, "edge count");
            if (rawVertexCount == 0 || rawVertexCount > int.MaxValue)
            {
                throw new ZddFormatException($"Vertex count must be between 1 and {int.MaxValue}, but was {rawVertexCount}.");
            }

            if (rawEdgeCount > int.MaxValue)
            {
                throw new ZddFormatException($"Edge count must be at most {int.MaxValue}, but was {rawEdgeCount}.");
            }

            return ((int)rawVertexCount, (int)rawEdgeCount);
        }

        private static int ReadVertex(Stream stream, int vertexCount, string fieldName)
        {
            uint value = VarInt.ReadUInt32(stream, fieldName);
            if (value >= (uint)vertexCount)
            {
                throw new ZddFormatException($"'{fieldName}' is {value}, outside 0 .. {vertexCount - 1}.");
            }

            return (int)value;
        }

        private static int[]? ReadSourceOrder(Stream stream, int edgeCount)
        {
            int flag = stream.ReadByte();
            if (flag < 0)
            {
                throw new ZddFormatException("Unexpected end of stream while reading the source-order flag.");
            }

            if (flag == NoSourceOrder)
            {
                return null;
            }

            if (flag != HasSourceOrder)
            {
                throw new ZddFormatException($"Invalid source-order flag {flag}; expected 0 or 1.");
            }

            var result = new int[edgeCount];
            var seen = new bool[edgeCount];
            for (int i = 0; i < edgeCount; i++)
            {
                uint source = VarInt.ReadUInt32(stream, $"source-order index {i}");
                if (source >= (uint)edgeCount || seen[(int)source])
                {
                    throw new ZddFormatException("Source order is not a permutation of the graph's edge indices.");
                }

                seen[(int)source] = true;
                result[i] = (int)source;
            }

            return result;
        }

        private static Graph RestoreSourceOrder(Graph graph, int[] sourceOrder)
        {
            var sourceEdges = new Edge[graph.EdgeCount];
            for (int i = 0; i < sourceOrder.Length; i++)
            {
                sourceEdges[sourceOrder[i]] = graph.Edges[i];
            }

            return new Graph(graph.VertexCount, sourceEdges).WithEdgeOrder(sourceOrder);
        }

        private static DirectedGraph RestoreSourceOrder(DirectedGraph graph, int[] sourceOrder)
        {
            var sourceEdges = new DirectedEdge[graph.EdgeCount];
            for (int i = 0; i < sourceOrder.Length; i++)
            {
                sourceEdges[sourceOrder[i]] = graph.Edges[i];
            }

            return new DirectedGraph(graph.VertexCount, sourceEdges).WithEdgeOrder(sourceOrder);
        }

        private static void ReadExact(Stream stream, Span<byte> buffer, string fieldName)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = stream.Read(buffer.Slice(total));
                if (read == 0)
                {
                    throw new ZddFormatException($"Unexpected end of stream while reading the graph-set {fieldName}.");
                }

                total += read;
            }
        }
    }
}
