namespace ZDD.Net.Graphs
{
    /// <summary>
    /// How <see cref="Graph.Optimize(EdgeOrderStrategy, EdgeOrderOptions)"/> picks an edge order, which is
    /// the frontier method's variable order and therefore what its cost sits on the exponent of (PLAN.md §8).
    /// </summary>
    public enum EdgeOrderStrategy
    {
        /// <summary>Keep the edge order as it is. Useful as the baseline the other strategies are measured against.</summary>
        AsGiven = 0,

        /// <summary>
        /// Visit vertices breadth-first and emit each edge once both of its endpoints have been visited.
        /// The default: it keeps the frontier to roughly one BFS layer, and is what Graphillion uses.
        /// </summary>
        Bfs = 1,

        /// <summary>
        /// The depth-first counterpart of <see cref="Bfs"/>. Narrower on graphs that branch into long
        /// chains, where a BFS layer advances every branch at once but a DFS walk finishes one at a time.
        /// </summary>
        Dfs = 2,

        /// <summary>Approximate path-width minimization by beam search. Not implemented yet (M3-3).</summary>
        BeamSearchPathWidth = 3,

        /// <summary>
        /// The serpentine order for grid graphs: sweep along the longer side, snaking back and forth along
        /// the shorter one, which keeps the frontier at about the shorter side. Falls back to
        /// <see cref="Bfs"/> on a graph that is not a recognized grid (see <see cref="Graph.Optimize(EdgeOrderStrategy, EdgeOrderOptions)"/>).
        /// </summary>
        Grid = 4,
    }
}
