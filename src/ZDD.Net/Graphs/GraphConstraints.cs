using System.Collections.Generic;
using ZDD.Net.Specs;

namespace ZDD.Net.Graphs
{
    /// <summary>
    /// A bundle of structural constraints on an edge set, composed by <see cref="GraphSet.Graphs(Graph, GraphConstraints)"/>
    /// and <see cref="GraphSet.Where(GraphConstraints)"/> &#8212; Graphillion's single-entry-point
    /// <c>graphs(degree_constraints=, num_edges=, num_comps=, no_loop=, vertex_groups=, graphset=)</c>,
    /// translated to .NET naming (docs/PLAN.md &#167;8). Every field is optional; leaving all of them at their
    /// default (every collection <see langword="null"/>, <see cref="NoLoop"/> <see langword="false"/>) places
    /// no constraint at all, so <see cref="GraphSet.Graphs(Graph, GraphConstraints)"/> returns every subgraph
    /// (the same family <see cref="Specs.PowerSetSpec"/> builds).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>How it's built</b>: each non-default field becomes its own <see cref="IErasedGraphSpec"/> (reusing
    /// an existing spec wherever one already covers the constraint &#8212; <see cref="DegreeConstraintSpec"/>,
    /// <see cref="CardinalitySpec"/>, <see cref="ForestSpec"/>, <see cref="VertexGroupSpec"/>,
    /// <see cref="LinearConstraintSpec"/> &#8212; plus one genuinely new spec, <see cref="ComponentCountSpec"/>,
    /// for <see cref="ComponentCount"/>), and the fields present are folded together with
    /// <c>AndErasedSpec</c> &#8212; the same type-erased conjunction <see cref="GraphSet"/>'s own
    /// <see cref="GraphSet.Including(Edge)"/>/<see cref="GraphSet.Excluding(Edge)"/>/<see cref="GraphSet.Larger"/>/
    /// <see cref="GraphSet.Smaller"/> chain uses. No new composition machinery was needed for this milestone.
    /// Every constraint is therefore applied <i>during</i> the frontier walk, not as a post-hoc filter: the
    /// walk never explores a branch that already violates one of the fields set here.
    /// </para>
    /// <para>
    /// <b>Contradictory constraints</b> (e.g. <c>EdgeCount = (5, 3)</c>, an empty range) throw
    /// <see cref="System.ArgumentException"/> or <see cref="System.ArgumentOutOfRangeException"/> eagerly,
    /// rather than silently building the empty family &#8212; the same choice every individual spec in this
    /// library already makes for an invalid range (see e.g. <see cref="CardinalitySpec"/>,
    /// <see cref="DegreeConstraintSpec"/>). A range that is merely unsatisfiable <i>given the graph</i> (for
    /// instance an <see cref="EdgeCount"/> minimum above <see cref="Graph.EdgeCount"/>) is not an error,
    /// though: that legitimately describes the empty family, exactly as it does for
    /// <see cref="GraphSet.Larger(int)"/>/<see cref="GraphSet.Smaller(int)"/> today.
    /// </para>
    /// </remarks>
    public sealed class GraphConstraints
    {
        /// <summary>
        /// The degree range <c>[Lo, Hi]</c> required of specific vertices, keyed by vertex index. A vertex
        /// absent from the dictionary is unconstrained (any degree from <c>0</c> up to however many edges of
        /// the graph touch it). See <see cref="DegreeConstraintSpec"/>.
        /// </summary>
        public IReadOnlyDictionary<int, (int Lo, int Hi)>? DegreeConstraints { get; set; }

        /// <summary>The total number of edges must fall in <c>[Min, Max]</c>. See <see cref="CardinalitySpec"/>.</summary>
        public (int Min, int Max)? EdgeCount { get; set; }

        /// <summary>
        /// The required number of connected components. <b>Isolated vertices are not counted</b> &#8212; a
        /// vertex left with no selected incident edge never contributes a component of its own, matching
        /// Graphillion's <c>num_comps</c> (see <see cref="ComponentCountSpec"/>'s remarks for the full
        /// rationale and how this differs from <see cref="GraphSet.Forests(Graph, int?)"/>'s component count).
        /// </summary>
        public int? ComponentCount { get; set; }

        /// <summary>Requires the edge set to contain no cycle (be a forest). See <see cref="ForestSpec"/>.</summary>
        public bool NoLoop { get; set; }

        /// <summary>
        /// Vertex groups that must each end up as their own connected component &#8212; vertices in the same
        /// group share a component, vertices from different groups never do, and an ungrouped vertex is free.
        /// See <see cref="VertexGroupSpec"/>.
        /// </summary>
        public IReadOnlyList<IReadOnlyList<int>>? VertexGroups { get; set; }

        /// <summary>
        /// Linear constraints on the chosen edges: each entry requires <c>&#931; Coefficients[i] x[i] {Op} Bound</c>,
        /// where <c>x[i]</c> is 1 if edge <c>i</c> is chosen and 0 otherwise. <c>Coefficients</c> must have
        /// exactly one entry per edge of the graph the constraints are applied to. See <see cref="LinearConstraintSpec"/>.
        /// </summary>
        public IReadOnlyList<(int[] Coefficients, LinearConstraintOperator Op, long Bound)>? LinearConstraints { get; set; }
    }
}
