using System;
using System.Threading;

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
    /// <see cref="EdgeOrderStrategy.Bfs"/> and <see cref="EdgeOrderStrategy.Dfs"/> read the start-vertex
    /// properties — and so does <see cref="EdgeOrderStrategy.Grid"/> when it falls back to BFS. Which
    /// vertex a traversal starts from can change the peak frontier by a factor, so it is worth trying more
    /// than one on a graph that is about to be built over.
    /// <see cref="EdgeOrderStrategy.BeamSearchPathWidth"/> also reads the start-vertex properties — under
    /// <see cref="StartVertexSelection.BestOfCandidates"/>, <see cref="MaxCandidates"/> is how many start
    /// vertices it tries (<c>0</c> for every vertex); under the default <see cref="StartVertexSelection.MinimumDegree"/>
    /// it tries a small fixed number of low-degree vertices, since — unlike BFS/DFS — trying several starts
    /// is part of what the strategy does (PLAN.md §8). It additionally reads <see cref="BeamWidth"/> and
    /// <see cref="CancellationToken"/>.
    /// </remarks>
    public readonly struct EdgeOrderOptions
    {
        private EdgeOrderOptions(
            StartVertexSelection selection, int startVertex, int maxCandidates, int beamWidth, CancellationToken cancellationToken)
        {
            Selection = selection;
            StartVertex = startVertex;
            MaxCandidates = maxCandidates;
            BeamWidth = beamWidth;
            CancellationToken = cancellationToken;
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

        /// <summary>
        /// How many candidate vertices <see cref="EdgeOrderStrategy.BeamSearchPathWidth"/> keeps at each
        /// step of its search; <c>0</c> (the default) picks a built-in default. Wider is slower but tends
        /// not to give a worse order — see <see cref="WithBeamWidth"/>.
        /// </summary>
        public int BeamWidth { get; }

        /// <summary>
        /// Cancels <see cref="EdgeOrderStrategy.BeamSearchPathWidth"/> in progress. Unlike most of .NET,
        /// cancelling this does not throw: it makes the search finish early with the best order it has
        /// found so far, so a caller always gets back a valid, complete edge order.
        /// </summary>
        public CancellationToken CancellationToken { get; }

        /// <summary>Starts the traversal from <paramref name="startVertex"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="startVertex"/> is negative.</exception>
        public static EdgeOrderOptions FromVertex(int startVertex)
        {
            if (startVertex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(startVertex), startVertex, "Must not be negative.");
            }

            return new EdgeOrderOptions(StartVertexSelection.Specified, startVertex, 0, 0, default);
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

            return new EdgeOrderOptions(StartVertexSelection.BestOfCandidates, 0, maxCandidates, 0, default);
        }

        /// <summary>
        /// Returns a copy of this instance with <see cref="EdgeOrderStrategy.BeamSearchPathWidth"/>'s beam
        /// width set to <paramref name="beamWidth"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="beamWidth"/> is not positive.</exception>
        public EdgeOrderOptions WithBeamWidth(int beamWidth)
        {
            if (beamWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(beamWidth), beamWidth, "Must be positive.");
            }

            return new EdgeOrderOptions(Selection, StartVertex, MaxCandidates, beamWidth, CancellationToken);
        }

        /// <summary>Returns a copy of this instance that observes <paramref name="cancellationToken"/>.</summary>
        public EdgeOrderOptions WithCancellationToken(CancellationToken cancellationToken) =>
            new EdgeOrderOptions(Selection, StartVertex, MaxCandidates, BeamWidth, cancellationToken);
    }
}
