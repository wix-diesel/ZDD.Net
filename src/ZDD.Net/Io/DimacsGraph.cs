using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ZDD.Net.Graphs;
using ZDD.Net.Internal;

namespace ZDD.Net.Io
{
    /// <summary>
    /// Reads and writes graphs in the DIMACS graph format (a <c>p edge</c> problem line, <c>e</c> edge
    /// lines, and <c>c</c> comment lines) &#8212; the de facto standard for graph benchmark data, so a
    /// real-world graph can be loaded without hand-writing <see cref="Graphs.Graph"/> construction code.
    /// </summary>
    /// <remarks>
    /// <b>Vertex numbering.</b> DIMACS numbers vertices from 1; <see cref="Graphs.Graph"/> numbers them
    /// from 0. This class is the one place that conversion happens: <see cref="Read(TextReader)"/>
    /// subtracts 1 from every vertex it reads, <see cref="Write(Graph, TextWriter)"/> adds 1 back.
    /// Nothing above this layer ever sees a 1-based index.
    /// </remarks>
    public static class DimacsGraph
    {
        /// <summary>Reads a graph from its DIMACS text representation.</summary>
        /// <param name="reader">
        /// The source text. Comment lines (<c>c ...</c>), blank lines, extra whitespace, and CRLF/LF
        /// line endings are all tolerated; a missing trailing newline is fine.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
        /// <exception cref="GraphFormatException">
        /// The input has no <c>p edge</c> problem line, a line is malformed, a vertex index is outside
        /// <c>1 .. vertexCount</c> (DIMACS is 1-based), or the number of <c>e</c> lines does not match
        /// the count declared on the problem line.
        /// </exception>
        public static Graph Read(TextReader reader)
        {
            ThrowHelper.ThrowIfNull(reader, nameof(reader));

            int vertexCount = -1;
            int declaredEdgeCount = -1;
            List<Edge>? edges = null;

            int lineNumber = 0;
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                lineNumber++;
                string trimmed = line.Trim();

                if (trimmed.Length == 0 || trimmed[0] == 'c')
                {
                    continue;
                }

                string[] tokens = GraphTextParsing.SplitTokens(trimmed);
                if (tokens.Length == 0)
                {
                    continue;
                }

                if (tokens[0] == "p")
                {
                    if (vertexCount >= 0)
                    {
                        throw new GraphFormatException(lineNumber, "Duplicate 'p' (problem) line.");
                    }

                    if (tokens.Length != 4 || tokens[1] != "edge")
                    {
                        throw new GraphFormatException(lineNumber, "Expected a problem line of the form 'p edge <vertexCount> <edgeCount>'.");
                    }

                    vertexCount = GraphTextParsing.ParseInt(tokens[2], lineNumber, "vertex count");
                    declaredEdgeCount = GraphTextParsing.ParseInt(tokens[3], lineNumber, "edge count");

                    if (vertexCount <= 0)
                    {
                        throw new GraphFormatException(lineNumber, $"Vertex count must be positive, but was {vertexCount}.");
                    }

                    if (declaredEdgeCount < 0)
                    {
                        throw new GraphFormatException(lineNumber, $"Edge count must not be negative, but was {declaredEdgeCount}.");
                    }

                    edges = new List<Edge>(declaredEdgeCount);
                    continue;
                }

                if (tokens[0] == "e")
                {
                    if (edges is null)
                    {
                        throw new GraphFormatException(lineNumber, "An edge line ('e') appeared before the problem line ('p').");
                    }

                    if (tokens.Length != 3)
                    {
                        throw new GraphFormatException(lineNumber, "Expected an edge line of the form 'e <u> <v>'.");
                    }

                    int u = GraphTextParsing.ParseInt(tokens[1], lineNumber, "edge endpoint") - 1;
                    int v = GraphTextParsing.ParseInt(tokens[2], lineNumber, "edge endpoint") - 1;

                    if ((uint)u >= (uint)vertexCount || (uint)v >= (uint)vertexCount)
                    {
                        throw new GraphFormatException(lineNumber, $"Edge endpoint outside 1 .. {vertexCount} (DIMACS vertices are 1-based).");
                    }

                    edges.Add(new Edge(u, v));
                    continue;
                }

                throw new GraphFormatException(lineNumber, $"Unrecognized line type '{tokens[0]}'; expected 'c', 'p', or 'e'.");
            }

            if (edges is null)
            {
                throw new GraphFormatException(lineNumber, "Missing problem line ('p edge <vertexCount> <edgeCount>').");
            }

            if (edges.Count != declaredEdgeCount)
            {
                throw new GraphFormatException(
                    lineNumber,
                    $"The problem line declared {declaredEdgeCount} edges, but {edges.Count} 'e' lines were found.");
            }

            return new Graph(vertexCount, edges);
        }

        /// <summary>Reads a graph from its DIMACS text representation. Convenience wrapper around <see cref="Read(TextReader)"/>.</summary>
        /// <param name="text">The DIMACS text.</param>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
        /// <exception cref="GraphFormatException">See <see cref="Read(TextReader)"/>.</exception>
        public static Graph Read(string text)
        {
            ThrowHelper.ThrowIfNull(text, nameof(text));

            using StringReader reader = new StringReader(text);
            return Read(reader);
        }

        /// <summary>Returns a graph's DIMACS text representation. Convenience wrapper around <see cref="Write(Graph, TextWriter)"/>.</summary>
        /// <param name="graph">The graph to write.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public static string Write(Graph graph)
        {
            using StringWriter writer = new StringWriter(CultureInfo.InvariantCulture);
            Write(graph, writer);
            return writer.ToString();
        }

        /// <summary>Writes a graph in DIMACS text format to <paramref name="writer"/>.</summary>
        /// <param name="graph">The graph to write.</param>
        /// <param name="writer">The destination.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="writer"/> is <see langword="null"/>.</exception>
        public static void Write(Graph graph, TextWriter writer)
        {
            ThrowHelper.ThrowIfNull(graph, nameof(graph));
            ThrowHelper.ThrowIfNull(writer, nameof(writer));

            writer.Write("c Generated by ZDD.Net\n");
            writer.Write("p edge ");
            writer.Write(graph.VertexCount.ToString(CultureInfo.InvariantCulture));
            writer.Write(' ');
            writer.Write(graph.EdgeCount.ToString(CultureInfo.InvariantCulture));
            writer.Write('\n');

            foreach (Edge edge in graph.Edges)
            {
                writer.Write("e ");
                writer.Write((edge.U + 1).ToString(CultureInfo.InvariantCulture));
                writer.Write(' ');
                writer.Write((edge.V + 1).ToString(CultureInfo.InvariantCulture));
                writer.Write('\n');
            }
        }
    }
}
