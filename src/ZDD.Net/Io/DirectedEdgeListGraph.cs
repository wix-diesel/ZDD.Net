using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ZDD.Net.Graphs;
using ZDD.Net.Internal;

namespace ZDD.Net.Io
{
    /// <summary>
    /// Reads and writes directed graphs as a plain edge list: a vertex-count header line, then one arc
    /// per line as two 0-based vertex indices separated by whitespace or a comma, read as <c>u</c> &#8594;
    /// <c>v</c> (M7-7, issue #158, <c>docs/design/m7-directed-graphs.md</c> &#167;3.6).
    /// </summary>
    /// <remarks>
    /// The directed counterpart of <see cref="EdgeListGraph"/>, kept as a separate type rather than a
    /// runtime flag on the same one (see <see cref="Graphs.DirectedEdge"/>'s own remarks for why
    /// <see cref="Graphs.Edge"/> and <see cref="Graphs.DirectedEdge"/> are split types) &#8212; the plain
    /// edge-list format has no header token that could mark a file as directed the way
    /// <see cref="SimpleTextGraph"/>'s <c>directed</c> header keyword or <see cref="DimacsGraph"/>'s
    /// <c>p arc</c> problem line do, so which of <see cref="EdgeListGraph.Read(TextReader)"/> or
    /// <see cref="Read(TextReader)"/> applies is purely the caller's choice of method, not something the
    /// text itself declares. Everything else &#8212; the header line, tokenizing, comment/whitespace
    /// tolerance &#8212; is identical to <see cref="EdgeListGraph"/>; see its remarks.
    /// </remarks>
    public static class DirectedEdgeListGraph
    {
        /// <summary>Reads a directed graph from its edge-list text representation.</summary>
        /// <param name="reader">
        /// The source text: a line with the vertex count, then one <c>u v</c> (or <c>u,v</c>) arc per
        /// line, read as <c>u</c> &#8594; <c>v</c>. <c>#</c> comment lines, blank lines, extra whitespace,
        /// and CRLF/LF line endings are all tolerated; a missing trailing newline is fine.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
        /// <exception cref="GraphFormatException">
        /// The input has no vertex-count header line, an arc line is malformed, a vertex index is outside
        /// <c>0 .. vertexCount - 1</c>, an arc is a self-loop, or an arc duplicates an earlier arc (the
        /// anti-parallel arc <c>v u</c> of an arc <c>u v</c> is not a duplicate). Always names the
        /// offending 1-based line number.
        /// </exception>
        public static DirectedGraph Read(TextReader reader)
        {
            ThrowHelper.ThrowIfNull(reader, nameof(reader));

            int vertexCount = -1;
            List<DirectedEdge> edges = new List<DirectedEdge>();
            HashSet<DirectedEdge> seen = new HashSet<DirectedEdge>();

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
                    throw new GraphFormatException(lineNumber, "Expected an arc line of the form 'u v' or 'u,v'.");
                }

                int u = GraphTextParsing.ParseInt(tokens[0], lineNumber, "arc endpoint");
                int v = GraphTextParsing.ParseInt(tokens[1], lineNumber, "arc endpoint");

                if ((uint)u >= (uint)vertexCount || (uint)v >= (uint)vertexCount)
                {
                    throw new GraphFormatException(lineNumber, $"Arc endpoint outside 0 .. {vertexCount - 1}.");
                }

                if (u == v)
                {
                    throw new GraphFormatException(lineNumber, $"Arc {u} -> {v} is a self-loop; self-loops are not supported.");
                }

                DirectedEdge edge = new DirectedEdge(u, v);
                if (!seen.Add(edge))
                {
                    throw new GraphFormatException(lineNumber, $"Arc {u} -> {v} duplicates an earlier arc; multi-arcs are not supported.");
                }

                edges.Add(edge);
            }

            if (vertexCount < 0)
            {
                throw new GraphFormatException(lineNumber, "Missing vertex count header line.");
            }

            return new DirectedGraph(vertexCount, edges);
        }

        /// <summary>Reads a directed graph from its edge-list text representation. Convenience wrapper around <see cref="Read(TextReader)"/>.</summary>
        /// <param name="text">The edge-list text.</param>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
        /// <exception cref="GraphFormatException">See <see cref="Read(TextReader)"/>.</exception>
        public static DirectedGraph Read(string text)
        {
            ThrowHelper.ThrowIfNull(text, nameof(text));

            using StringReader reader = new StringReader(text);
            return Read(reader);
        }

        /// <summary>Returns a directed graph's edge-list text representation. Convenience wrapper around <see cref="Write(DirectedGraph, TextWriter)"/>.</summary>
        /// <param name="graph">The graph to write.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public static string Write(DirectedGraph graph)
        {
            using StringWriter writer = new StringWriter(CultureInfo.InvariantCulture);
            Write(graph, writer);
            return writer.ToString();
        }

        /// <summary>
        /// Writes a directed graph as an edge list to <paramref name="writer"/>: a vertex-count header
        /// line, then one <c>u v</c> arc per line (<c>u</c> &#8594; <c>v</c>).
        /// </summary>
        /// <param name="graph">The graph to write.</param>
        /// <param name="writer">The destination.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="writer"/> is <see langword="null"/>.</exception>
        public static void Write(DirectedGraph graph, TextWriter writer)
        {
            ThrowHelper.ThrowIfNull(graph, nameof(graph));
            ThrowHelper.ThrowIfNull(writer, nameof(writer));

            writer.Write(graph.VertexCount.ToString(CultureInfo.InvariantCulture));
            writer.Write('\n');

            foreach (DirectedEdge edge in graph.Edges)
            {
                writer.Write(edge.From.ToString(CultureInfo.InvariantCulture));
                writer.Write(' ');
                writer.Write(edge.To.ToString(CultureInfo.InvariantCulture));
                writer.Write('\n');
            }
        }
    }
}
