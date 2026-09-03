using System;
using System.Collections.Generic;
using System.IO;
using ZDD.Net.Graphs;
using ZDD.Net.Internal;

namespace ZDD.Net.Io
{
    /// <summary>A graph paired with a label for each vertex, as read by <see cref="SimpleTextGraph.Read(TextReader)"/>.</summary>
    public sealed class LabeledGraph
    {
        /// <summary>The graph.</summary>
        public Graph Graph { get; }

        /// <summary>One label per vertex, indexed the same way as <see cref="Graph"/>'s vertices (<c>0 .. Graph.VertexCount - 1</c>).</summary>
        public IReadOnlyList<string> VertexLabels { get; }

        /// <summary>Pairs a graph with a label for each of its vertices.</summary>
        /// <param name="graph">The graph.</param>
        /// <param name="vertexLabels">One label per vertex, in vertex order.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="vertexLabels"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="vertexLabels"/>'s length does not equal <paramref name="graph"/>'s vertex count.</exception>
        public LabeledGraph(Graph graph, IReadOnlyList<string> vertexLabels)
        {
            ThrowHelper.ThrowIfNull(graph, nameof(graph));
            ThrowHelper.ThrowIfNull(vertexLabels, nameof(vertexLabels));

            if (vertexLabels.Count != graph.VertexCount)
            {
                throw new ArgumentException($"Expected {graph.VertexCount} labels, got {vertexLabels.Count}.", nameof(vertexLabels));
            }

            Graph = graph;
            VertexLabels = vertexLabels;
        }
    }
}
