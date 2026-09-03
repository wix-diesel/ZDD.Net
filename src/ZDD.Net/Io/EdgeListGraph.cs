using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ZDD.Net.Graphs;
using ZDD.Net.Internal;

namespace ZDD.Net.Io
{
    /// <summary>
    /// Reads and writes graphs as a plain edge list: a vertex-count header line, then one edge per line
    /// as two 0-based vertex indices separated by whitespace or a comma.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="DimacsGraph"/>, there is no offset to this format: vertex indices are exactly
    /// <see cref="Graphs.Graph"/>'s own 0-based numbering, on both read and write. The header line exists
    /// only so the vertex count round-trips even when trailing vertices carry no edge &#8212; a pure
    /// "one pair per line" format has no way to represent that on its own.
    /// </remarks>
    public static class EdgeListGraph
    {
        /// <summary>Reads a graph from its edge-list text representation.</summary>
        /// <param name="reader">
        /// The source text: a line with the vertex count, then one <c>u v</c> (or <c>u,v</c>) edge per
        /// line. <c>#</c> comment lines, blank lines, extra whitespace, and CRLF/LF line endings are all
        /// tolerated; a missing trailing newline is fine.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
        /// <exception cref="GraphFormatException">
        /// The input has no vertex-count header line, an edge line is malformed, or a vertex index is
        /// outside <c>0 .. vertexCount - 1</c>.
        /// </exception>
        public static Graph Read(TextReader reader)
        {
            ThrowHelper.ThrowIfNull(reader, nameof(reader));

            int vertexCount = -1;
            List<Edge> edges = new List<Edge>();

            int lineNumber = 0;
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                lineNumber++;
                string trimmed = line.Trim();

                if (trimmed.Length == 0 || trimmed[0] == '#')
                {
                    continue;
                }

                string[] tokens = GraphTextParsing.SplitTokens(trimmed);
                if (tokens.Length == 0)
                {
                    continue;
                }

                if (vertexCount < 0)
                {
                    if (tokens.Length != 1)
                    {
                        throw new GraphFormatException(lineNumber, "Expected the vertex count on its own line.");
                    }

                    vertexCount = GraphTextParsing.ParseInt(tokens[0], lineNumber, "vertex count");

                    if (vertexCount <= 0)
                    {
                        throw new GraphFormatException(lineNumber, $"Vertex count must be positive, but was {vertexCount}.");
                    }

                    continue;
                }

                if (tokens.Length != 2)
                {
                    throw new GraphFormatException(lineNumber, "Expected an edge line of the form 'u v' or 'u,v'.");
                }

                int u = GraphTextParsing.ParseInt(tokens[0], lineNumber, "edge endpoint");
                int v = GraphTextParsing.ParseInt(tokens[1], lineNumber, "edge endpoint");

                if ((uint)u >= (uint)vertexCount || (uint)v >= (uint)vertexCount)
                {
                    throw new GraphFormatException(lineNumber, $"Edge endpoint outside 0 .. {vertexCount - 1}.");
                }

                edges.Add(new Edge(u, v));
            }

            if (vertexCount < 0)
            {
                throw new GraphFormatException(lineNumber, "Missing vertex count header line.");
            }

            return new Graph(vertexCount, edges);
        }

        /// <summary>Reads a graph from its edge-list text representation. Convenience wrapper around <see cref="Read(TextReader)"/>.</summary>
        /// <param name="text">The edge-list text.</param>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
        /// <exception cref="GraphFormatException">See <see cref="Read(TextReader)"/>.</exception>
        public static Graph Read(string text)
        {
            ThrowHelper.ThrowIfNull(text, nameof(text));

            using StringReader reader = new StringReader(text);
            return Read(reader);
        }

        /// <summary>Returns a graph's edge-list text representation. Convenience wrapper around <see cref="Write(Graph, TextWriter)"/>.</summary>
        /// <param name="graph">The graph to write.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public static string Write(Graph graph)
        {
            using StringWriter writer = new StringWriter(CultureInfo.InvariantCulture);
            Write(graph, writer);
            return writer.ToString();
        }

        /// <summary>
        /// Writes a graph as an edge list to <paramref name="writer"/>: a vertex-count header line, then
        /// one <c>u v</c> edge per line.
        /// </summary>
        /// <param name="graph">The graph to write.</param>
        /// <param name="writer">The destination.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="writer"/> is <see langword="null"/>.</exception>
        public static void Write(Graph graph, TextWriter writer)
        {
            ThrowHelper.ThrowIfNull(graph, nameof(graph));
            ThrowHelper.ThrowIfNull(writer, nameof(writer));

            writer.Write(graph.VertexCount.ToString(CultureInfo.InvariantCulture));
            writer.Write('\n');

            foreach (Edge edge in graph.Edges)
            {
                writer.Write(edge.U.ToString(CultureInfo.InvariantCulture));
                writer.Write(' ');
                writer.Write(edge.V.ToString(CultureInfo.InvariantCulture));
                writer.Write('\n');
            }
        }
    }
}
