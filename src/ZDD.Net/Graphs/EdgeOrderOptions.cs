using System;

namespace ZDD.Net.Graphs
{
    /// <summary>How <see cref="EdgeOrderOptions"/> picks the vertex a traversal-based strategy starts from.</summary>
    public enum StartVertexSelection
    {
        /// <summary>
        /// Start from a vertex of minimum positive degree (lowest index wins ties). A cheap heuristic:
        /// starting at a corner rather than in the middle keeps the first frontier small.
        /// </summary>
        MinimumDegree = 0,

        /// <summary>Start from <see cref="EdgeOrderOptions.StartVertex"/>.</summary>
        Specified = 1,

        /// <summary>
        /// Try several start vertices and keep the order with the smallest peak frontier. Costs one
        /// traversal and one width evaluation per candidate, so bound it with
        /// <see cref="EdgeOrderOptions.MaxCandidates"/> on a large graph.
        /// </summary>
        BestOfCandidates = 2,
    }

    /// <summary>
    /// Tuning for <see cref="Graph.Optimize(EdgeOrderStrategy, EdgeOrderOptions)"/>: which vertex the
    /// traversal starts from. <see langword="default"/> is <see cref="StartVertexSelection.MinimumDegree"/>.
    /// </summary>
    /// <remarks>
    /// Only <see cref="EdgeOrderStrategy.Bfs"/> and <see cref="EdgeOrderStrategy.Dfs"/> read these — and
    /// <see cref="EdgeOrderStrategy.Grid"/> when it falls back to BFS. Which vertex a traversal starts from
    /// can change the peak frontier by a factor, so it is worth trying more than one on a graph that is
    /// about to be built over.
    /// </remarks>
    public readonly struct EdgeOrderOptions
    {
        private EdgeOrderOptions(StartVertexSelection selection, int startVertex, int maxCandidates)
        {
            Selection = selection;
            StartVertex = startVertex;
            MaxCandidates = maxCandidates;
        }

        /// <summary>The default: start from a vertex of minimum positive degree.</summary>
        public static EdgeOrderOptions Default => default;

        /// <summary>How the start vertex is chosen.</summary>
        public StartVertexSelection Selection { get; }

        /// <summary>
        /// The start vertex, read only when <see cref="Selection"/> is
        /// <see cref="StartVertexSelection.Specified"/>.
        /// </summary>
        public int StartVertex { get; }

        /// <summary>
        /// How many candidate start vertices <see cref="StartVertexSelection.BestOfCandidates"/> tries
        /// (lowest degree first); <c>0</c> means every vertex.
        /// </summary>
        public int MaxCandidates { get; }

        /// <summary>Starts the traversal from <paramref name="startVertex"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="startVertex"/> is negative.</exception>
        public static EdgeOrderOptions FromVertex(int startVertex)
        {
            if (startVertex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(startVertex), startVertex, "Must not be negative.");
            }

            return new EdgeOrderOptions(StartVertexSelection.Specified, startVertex, 0);
        }

        /// <summary>
        /// Tries up to <paramref name="maxCandidates"/> start vertices (lowest degree first, <c>0</c> for
        /// every vertex) and keeps the narrowest resulting order.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxCandidates"/> is negative.</exception>
        public static EdgeOrderOptions BestOfCandidates(int maxCandidates = 0)
        {
            if (maxCandidates < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCandidates), maxCandidates, "Must not be negative.");
            }

            return new EdgeOrderOptions(StartVertexSelection.BestOfCandidates, 0, maxCandidates);
        }
    }
}
