using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ZDD.Net.Graphs;
using ZDD.Net.Internal;

namespace ZDD.Net.Io
{
    /// <summary>
    /// Reads and writes graphs in ZDD.Net's own simple text format: a <c>graph</c> header line, then one
    /// <c>vertex &lt;index&gt; &lt;label&gt;</c> or <c>edge &lt;u&gt; &lt;v&gt;</c> line per vertex label
    /// or edge. The only one of the three formats in this namespace that can carry vertex labels through
    /// a round trip.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Vertex and edge indices are 0-based, matching <see cref="Graphs.Graph"/> directly &#8212; there is
    /// no DIMACS-style offset here. <c>vertex</c> and <c>edge</c> lines may appear in any order relative
    /// to each other (<see cref="Write(Graph, TextWriter, IReadOnlyList{string})"/> happens to write
    /// every <c>vertex</c> line before every <c>edge</c> line, but <see cref="Read(TextReader)"/> does
    /// not require that); edge order is preserved by the order <c>edge</c> lines appear in the input,
    /// regardless of any <c>vertex</c> lines interleaved between them.
    /// </para>
    /// <para>
    /// <c>graph</c> and <c>edge</c> lines tokenize on whitespace/comma like the other two formats. A
    /// <c>vertex</c> line is different: its fields are single-space separated and the label is
    /// <i>everything</i> after the second space, since a label is free text (it may itself contain
    /// commas, tabs, or extra spaces).
    /// </para>
    /// </remarks>
    public static class SimpleTextGraph
    {
        /// <summary>Reads a graph, and the vertex labels it carries, from its simple-text representation.</summary>
        /// <param name="reader">
        /// The source text: a <c>graph &lt;vertexCount&gt; &lt;edgeCount&gt;</c> header line, then
        /// <c>vertex</c> and <c>edge</c> lines. <c>#</c> comment lines, blank lines, extra whitespace,
        /// and CRLF/LF line endings are all tolerated; a missing trailing newline is fine.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
        /// <exception cref="GraphFormatException">
        /// The input has no <c>graph</c> header line, a line is malformed, a vertex index is out of
        /// range, a vertex is labeled more than once, or the number of <c>edge</c> lines does not match
        /// the header's declared count.
        /// </exception>
        public static LabeledGraph Read(TextReader reader)
        {
            ThrowHelper.ThrowIfNull(reader, nameof(reader));

            int vertexCount = -1;
            int declaredEdgeCount = -1;
            string?[]? labels = null;
            List<Edge>? edges = null;

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

                string keyword = FirstToken(trimmed, out string afterKeyword);

                if (keyword == "graph")
                {
                    if (vertexCount >= 0)
                    {
                        throw new GraphFormatException(lineNumber, "Duplicate 'graph' header line.");
                    }

                    string[] tokens = GraphTextParsing.SplitTokens(trimmed);
                    if (tokens.Length != 3)
                    {
                        throw new GraphFormatException(lineNumber, "Expected a header line of the form 'graph <vertexCount> <edgeCount>'.");
                    }

                    vertexCount = GraphTextParsing.ParseInt(tokens[1], lineNumber, "vertex count");
                    declaredEdgeCount = GraphTextParsing.ParseInt(tokens[2], lineNumber, "edge count");

                    if (vertexCount <= 0)
                    {
                        throw new GraphFormatException(lineNumber, $"Vertex count must be positive, but was {vertexCount}.");
                    }

                    if (declaredEdgeCount < 0)
                    {
                        throw new GraphFormatException(lineNumber, $"Edge count must not be negative, but was {declaredEdgeCount}.");
                    }

                    labels = new string?[vertexCount];
                    edges = new List<Edge>(declaredEdgeCount);
                    continue;
                }

                if (edges is null || labels is null)
                {
                    throw new GraphFormatException(lineNumber, "Expected the 'graph' header line first.");
                }

                if (keyword == "vertex")
                {
                    string indexToken = FirstToken(afterKeyword, out string label);

                    int index = GraphTextParsing.ParseInt(indexToken, lineNumber, "vertex index");
                    if ((uint)index >= (uint)vertexCount)
                    {
                        throw new GraphFormatException(lineNumber, $"Vertex index outside 0 .. {vertexCount - 1}.");
                    }

                    if (labels[index] is not null)
                    {
                        throw new GraphFormatException(lineNumber, $"Vertex {index} is labeled more than once.");
                    }

                    labels[index] = label;
                    continue;
                }

                if (keyword == "edge")
                {
                    string[] tokens = GraphTextParsing.SplitTokens(trimmed);
                    if (tokens.Length != 3)
                    {
                        throw new GraphFormatException(lineNumber, "Expected an edge line of the form 'edge <u> <v>'.");
                    }

                    int u = GraphTextParsing.ParseInt(tokens[1], lineNumber, "edge endpoint");
                    int v = GraphTextParsing.ParseInt(tokens[2], lineNumber, "edge endpoint");

                    if ((uint)u >= (uint)vertexCount || (uint)v >= (uint)vertexCount)
                    {
                        throw new GraphFormatException(lineNumber, $"Edge endpoint outside 0 .. {vertexCount - 1}.");
                    }

                    edges.Add(new Edge(u, v));
                    continue;
                }

                throw new GraphFormatException(lineNumber, $"Unrecognized line type '{keyword}'; expected 'graph', 'vertex', or 'edge'.");
            }

            if (edges is null || labels is null)
            {
                throw new GraphFormatException(lineNumber, "Missing 'graph' header line.");
            }

            if (edges.Count != declaredEdgeCount)
            {
                throw new GraphFormatException(
                    lineNumber,
                    $"The header declared {declaredEdgeCount} edges, but {edges.Count} 'edge' lines were found.");
            }

            string[] resolvedLabels = new string[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                resolvedLabels[i] = labels[i] ?? i.ToString(CultureInfo.InvariantCulture);
            }

            return new LabeledGraph(new Graph(vertexCount, edges), resolvedLabels);
        }

        /// <summary>Reads a graph, and its vertex labels, from its simple-text representation. Convenience wrapper around <see cref="Read(TextReader)"/>.</summary>
        /// <param name="text">The simple-text representation.</param>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
        /// <exception cref="GraphFormatException">See <see cref="Read(TextReader)"/>.</exception>
        public static LabeledGraph Read(string text)
        {
            ThrowHelper.ThrowIfNull(text, nameof(text));

            using StringReader reader = new StringReader(text);
            return Read(reader);
        }

        /// <summary>Returns a graph's simple-text representation. Convenience wrapper around <see cref="Write(Graph, TextWriter, IReadOnlyList{string})"/>.</summary>
        /// <param name="graph">The graph to write.</param>
        /// <param name="vertexLabels">A label per vertex, or <see langword="null"/> to label each vertex with its own index.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="vertexLabels"/> is non-null and its length does not equal <see cref="Graphs.Graph.VertexCount"/>.</exception>
        public static string Write(Graph graph, IReadOnlyList<string>? vertexLabels = null)
        {
            using StringWriter writer = new StringWriter(CultureInfo.InvariantCulture);
            Write(graph, writer, vertexLabels);
            return writer.ToString();
        }

        /// <summary>
        /// Writes a graph in simple-text format to <paramref name="writer"/>: a <c>graph</c> header
        /// line, then every vertex's label, then every edge.
        /// </summary>
        /// <param name="graph">The graph to write.</param>
        /// <param name="writer">The destination.</param>
        /// <param name="vertexLabels">
        /// A label per vertex, or <see langword="null"/> to label each vertex with its own index (which
        /// <see cref="Read(TextReader)"/> reproduces exactly, since that is its default too).
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="writer"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="vertexLabels"/> is non-null and its length does not equal <see cref="Graphs.Graph.VertexCount"/>.</exception>
        public static void Write(Graph graph, TextWriter writer, IReadOnlyList<string>? vertexLabels = null)
        {
            ThrowHelper.ThrowIfNull(graph, nameof(graph));
            ThrowHelper.ThrowIfNull(writer, nameof(writer));

            if (vertexLabels is not null && vertexLabels.Count != graph.VertexCount)
            {
                throw new ArgumentException($"Expected {graph.VertexCount} labels, got {vertexLabels.Count}.", nameof(vertexLabels));
            }

            writer.Write("graph ");
            writer.Write(graph.VertexCount.ToString(CultureInfo.InvariantCulture));
            writer.Write(' ');
            writer.Write(graph.EdgeCount.ToString(CultureInfo.InvariantCulture));
            writer.Write('\n');

            for (int i = 0; i < graph.VertexCount; i++)
            {
                writer.Write("vertex ");
                writer.Write(i.ToString(CultureInfo.InvariantCulture));
                writer.Write(' ');
                writer.Write(vertexLabels is null ? i.ToString(CultureInfo.InvariantCulture) : vertexLabels[i]);
                writer.Write('\n');
            }

            foreach (Edge edge in graph.Edges)
            {
                writer.Write("edge ");
                writer.Write(edge.U.ToString(CultureInfo.InvariantCulture));
                writer.Write(' ');
                writer.Write(edge.V.ToString(CultureInfo.InvariantCulture));
                writer.Write('\n');
            }
        }

        /// <summary>
        /// Splits <paramref name="trimmed"/> at its first run of spaces/tabs, returning the token before
        /// it and, via <paramref name="remainder"/>, everything after that whitespace run (which may
        /// itself contain further whitespace &#8212; used for free-text vertex labels).
        /// </summary>
        private static string FirstToken(string trimmed, out string remainder)
        {
            int i = 0;
            while (i < trimmed.Length && trimmed[i] != ' ' && trimmed[i] != '\t')
            {
                i++;
            }

            string token = trimmed[..i];

            int j = i;
            while (j < trimmed.Length && (trimmed[j] == ' ' || trimmed[j] == '\t'))
            {
                j++;
            }

            remainder = trimmed[j..];
            return token;
        }
    }
}
