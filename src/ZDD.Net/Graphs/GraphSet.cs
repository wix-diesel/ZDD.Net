using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Io;
using ZDD.Net.Sets;
using ZDD.Net.Specs;

namespace ZDD.Net.Graphs
{
    /// <summary>
    /// The family of edge sets of one <see cref="Graphs.Graph"/> &#8212; the API most users of ZDD.Net
    /// meet first. Every lower layer (Core's ZDD engine, the frontier-method framework, the built-in
    /// specs) sits behind this one type, following Graphillion's vocabulary translated to .NET naming
    /// so a user moving from Python pays no relearning cost (docs/PLAN.md &#167;8).
    /// </summary>
    /// <example>
    /// <code>
    /// var g = Graph.Grid(9, 9);
    /// var paths = GraphSet.Paths(g, from: 0, to: 80);
    ///
    /// Console.WriteLine(paths.Count);                     // 3266598486981642
    /// var shortest = paths.MinWeight(e =&gt; 1);
    /// var sample   = paths.Sample(new Random(42));
    ///
    /// var filtered = paths.Including(edge).Excluding(other).Smaller(20);
    /// foreach (var p in filtered.Take(10)) { /* ... */ }
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>
    /// A specialization of <see cref="SetSet{T}"/> for <see cref="Graphs.Edge"/>: internally, a
    /// <see cref="Graphs.Graph"/>'s edge index <i>is</i> its ZDD variable index
    /// (<see cref="Graphs.Graph.EdgeIndexToVariableIndex"/>), so <see cref="SetUniverse{T}"/> and every
    /// existing spec already agree on that numbering &#8212; <see cref="GraphSet"/> only adds the
    /// Graphillion-shaped surface (generators, filters, weight-ordered lazy iteration) on top.
    /// </para>
    /// <para>
    /// <b>Filters are applied during construction, not after it.</b> <see cref="Including(Edge)"/> /
    /// <see cref="Excluding(Edge)"/> / <see cref="Larger"/> / <see cref="Smaller"/> /
    /// <see cref="LenEquals"/> compose a new frontier spec (see <c>GraphSetSpec.cs</c>'s
    /// <c>AndErasedSpec</c>) and re-run <see cref="FrontierBuilder"/> rather than building the
    /// unfiltered family and intersecting: the frontier walk never explores a branch that already
    /// violates the filter, so the diagram built along the way is never larger &#8212; and typically much
    /// smaller &#8212; than the one a post-hoc filter would have to materialize first.
    /// </para>
    /// <para>
    /// <b>Edge identity survives <see cref="Graphs.Graph.Optimize"/></b>: a filter or generator
    /// receives and returns <see cref="Graphs.Edge"/> values (structural identity, not index), and every
    /// result is translated through whichever graph actually built the family &#8212; the original or an
    /// optimized reordering of it &#8212; so a <see cref="GraphSet"/> built from an optimized graph is
    /// still read back in terms of the original endpoints, never a reordered index.
    /// </para>
    /// </remarks>
    public sealed class GraphSet : IEnumerable<IReadOnlySet<Edge>>, IEquatable<GraphSet>
    {
        private readonly SetSet<Edge> _family;
        private readonly IErasedGraphSpec _spec;

        private GraphSet(Graph graph, SetUniverse<Edge> universe, Zdd zdd, IErasedGraphSpec spec)
        {
            Graph = graph;
            _family = new SetSet<Edge>(universe, zdd);
            _spec = spec;
        }

        /// <summary>The graph this family's edge sets are drawn from.</summary>
        public Graph Graph { get; }

        /// <summary>The element &#8596; item-index mapping this family is expressed over (edge index <c>i</c> is item index <c>i</c>).</summary>
        public SetUniverse<Edge> Universe => _family.Universe;

        /// <summary>The underlying ZDD, for callers who want to drop down to the low-level API.</summary>
        public Zdd Zdd => _family.Zdd;

        /// <summary>The exact number of member edge sets, in time proportional to node count. See <see cref="SetSet{T}"/>'s remarks on LINQ's <c>Count()</c>.</summary>
        public BigInteger Count => _family.Count;

        /// <summary>The number of member edge sets, approximated as a <see cref="double"/>. Faster than <see cref="Count"/>.</summary>
        public double CountApprox => _family.CountApprox;

        /// <summary>The exact number of member edge sets, as a <see cref="long"/>.</summary>
        /// <exception cref="OverflowException"><see cref="Count"/> does not fit in a <see cref="long"/>.</exception>
        public long LongCount() => _family.LongCount();

        /// <summary>Whether this family has no member edge sets.</summary>
        public bool IsEmpty => _family.IsEmpty;

        // ==================== Generators ====================

        /// <summary>The family of simple <c>from</c>&#8211;<c>to</c> paths of <paramref name="graph"/> (Knuth's <c>SIMPATH</c>).</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="from">One endpoint. Ignored when <paramref name="allowAnyEndpoints"/> is <see langword="true"/>.</param>
        /// <param name="to">The other endpoint. Ignored when <paramref name="allowAnyEndpoints"/> is <see langword="true"/>.</param>
        /// <param name="allowAnyEndpoints">When <see langword="true"/>, every simple path in the graph, for any pair of endpoints.</param>
        /// <example><code>GraphSet paths = GraphSet.Paths(Graph.Grid(9, 9), from: 0, to: 80);</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="from"/> or <paramref name="to"/> is outside <c>0 .. graph.VertexCount - 1</c>.</exception>
        public static GraphSet Paths(Graph graph, int from, int to, bool allowAnyEndpoints = false)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return Generate(graph, new PathSpec(graph, from, to, allowAnyEndpoints));
        }

        /// <summary>The family of edge sets forming simple cycles of <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="single">When <see langword="true"/> (default), exactly one simple cycle; when <see langword="false"/>, any nonempty union of vertex-disjoint simple cycles.</param>
        /// <example><code>GraphSet cycles = GraphSet.Cycles(Graph.Grid(5, 5));</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public static GraphSet Cycles(Graph graph, bool single = true)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return Generate(graph, new CycleSpec(graph, single));
        }

        /// <summary>The family of spanning trees of <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <example><code>GraphSet trees = GraphSet.Trees(Graph.Complete(6));</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public static GraphSet Trees(Graph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return Generate(graph, new SpanningTreeSpec(graph));
        }

        /// <summary>The family of spanning forests of <paramref name="graph"/>, optionally constrained to an exact number of trees.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="components">When given, only forests with exactly this many trees; <see langword="null"/> accepts any number.</param>
        /// <example><code>GraphSet forests = GraphSet.Forests(Graph.Grid(4, 4), components: 3);</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="components"/> is not positive.</exception>
        public static GraphSet Forests(Graph graph, int? components = null)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return Generate(graph, new ForestSpec(graph, components));
        }

        /// <summary>The family of matchings of <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="perfect">When <see langword="true"/>, only matchings that cover every vertex.</param>
        /// <example><code>GraphSet matchings = GraphSet.Matchings(Graph.Complete(6), perfect: true);</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public static GraphSet Matchings(Graph graph, bool perfect = false)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return Generate(graph, new MatchingSpec(graph, perfect));
        }

        /// <summary>The family of Hamiltonian <paramref name="s"/>&#8211;<paramref name="t"/> paths of <paramref name="graph"/> (touching every vertex).</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="s">One endpoint.</param>
        /// <param name="t">The other endpoint.</param>
        /// <example><code>GraphSet tours = GraphSet.HamiltonianPaths(Graph.Complete(6), 0, 5);</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="s"/> or <paramref name="t"/> is outside <c>0 .. graph.VertexCount - 1</c>.</exception>
        public static GraphSet HamiltonianPaths(Graph graph, int s, int t)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return Generate(graph, new HamiltonianPathSpec(graph, s, t));
        }

        /// <summary>The family of Hamiltonian cycles of <paramref name="graph"/> (touching every vertex).</summary>
        /// <param name="graph">The graph to search.</param>
        /// <example><code>GraphSet tours = GraphSet.HamiltonianCycles(Graph.Complete(6));</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public static GraphSet HamiltonianCycles(Graph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return Generate(graph, new HamiltonianCycleSpec(graph));
        }

        /// <summary>
        /// The family of cliques of <paramref name="graph"/>: <b>vertex</b> sets in which every two
        /// vertices are adjacent. Unlike every other generator here, this is a family of vertex sets,
        /// not edge sets, so it is returned as a <see cref="SetSet{T}"/> of <see cref="int"/> (vertex
        /// index) rather than a <see cref="GraphSet"/> &#8212; see <see cref="Specs.CliqueSpec"/>.
        /// </summary>
        /// <param name="graph">The graph to search.</param>
        /// <example><code>SetSet&lt;int&gt; cliques = GraphSet.Cliques(Graph.Complete(6));</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public static SetSet<int> Cliques(Graph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return GenerateVertexFamily(graph, new CliqueSpec(graph));
        }

        /// <summary>
        /// The family of independent sets of <paramref name="graph"/>: <b>vertex</b> sets in which no
        /// two vertices are adjacent. Like <see cref="Cliques"/>, a family of vertex sets, returned as
        /// a <see cref="SetSet{T}"/> of <see cref="int"/> &#8212; see <see cref="Specs.IndependentSetSpec"/>.
        /// </summary>
        /// <param name="graph">The graph to search.</param>
        /// <example><code>SetSet&lt;int&gt; sets = GraphSet.IndependentSets(Graph.Complete(6));</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public static SetSet<int> IndependentSets(Graph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return GenerateVertexFamily(graph, new IndependentSetSpec(graph));
        }

        // ==================== Vertex-family generators (M6-10) ====================

        /// <summary>
        /// The family of vertex covers of <paramref name="graph"/>: <b>vertex</b> sets that include at
        /// least one endpoint of every edge. Like <see cref="Cliques"/>, returned as a
        /// <see cref="SetSet{T}"/> of <see cref="int"/> (vertex index) &#8212; see
        /// <see cref="Specs.VertexCoverSpec"/>.
        /// </summary>
        /// <param name="graph">The graph to search.</param>
        /// <example><code>SetSet&lt;int&gt; covers = GraphSet.VertexCovers(Graph.Complete(6));</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public static SetSet<int> VertexCovers(Graph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return GenerateVertexFamily(graph, new VertexCoverSpec(graph));
        }

        /// <summary>
        /// The family of dominating sets of <paramref name="graph"/>: <b>vertex</b> sets in which every
        /// vertex is either in the set itself or adjacent to a vertex that is. Like <see cref="Cliques"/>,
        /// returned as a <see cref="SetSet{T}"/> of <see cref="int"/> &#8212; see
        /// <see cref="Specs.DominatingSetSpec"/>.
        /// </summary>
        /// <param name="graph">The graph to search.</param>
        /// <example><code>SetSet&lt;int&gt; sets = GraphSet.DominatingSets(Graph.Complete(6));</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public static SetSet<int> DominatingSets(Graph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return GenerateVertexFamily(graph, new DominatingSetSpec(graph));
        }

        /// <summary>
        /// The family of edge sets whose kept edges split <paramref name="graph"/> into exactly
        /// <paramref name="k"/> connected blocks, each sized between <paramref name="minBlockSize"/> and
        /// <paramref name="maxBlockSize"/> vertices &#8212; Graphillion's <c>graph_partitions</c>. See
        /// <see cref="Specs.GraphPartitionSpec"/>.
        /// </summary>
        /// <param name="graph">The graph to partition.</param>
        /// <param name="k">The required number of blocks.</param>
        /// <param name="minBlockSize">The minimum number of vertices a block may have.</param>
        /// <param name="maxBlockSize">The maximum number of vertices a block may have.</param>
        /// <example><code>GraphSet parts = GraphSet.Partitions(Graph.Grid(3, 3), k: 3, minBlockSize: 1, maxBlockSize: 9);</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="k"/> or <paramref name="minBlockSize"/> is not positive, or <paramref name="maxBlockSize"/>
        /// is less than <paramref name="minBlockSize"/>.
        /// </exception>
        public static GraphSet Partitions(Graph graph, int k, int minBlockSize, int maxBlockSize)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return Generate(graph, new GraphPartitionSpec(graph, k, minBlockSize, maxBlockSize));
        }

        /// <summary>
        /// The family of edge sets splitting <paramref name="graph"/> into exactly <paramref name="k"/>
        /// connected blocks of near-equal size &#8212; Graphillion's <c>balanced_partitions</c>, a
        /// convenience over <see cref="Partitions"/> that derives the block-size range from
        /// <paramref name="tolerance"/>: with <c>n</c> = <paramref name="graph"/>'s vertex count,
        /// <c>minBlockSize = floor(n / k * (1 - tolerance))</c> and
        /// <c>maxBlockSize = ceil(n / k * (1 + tolerance))</c>.
        /// </summary>
        /// <param name="graph">The graph to partition.</param>
        /// <param name="k">The required number of blocks.</param>
        /// <param name="tolerance">
        /// The fraction by which a block's size may deviate from the exact average <c>n / k</c>;
        /// <c>0.0</c> (the default) allows only the tightest range that still rounds to a valid partition.
        /// </param>
        /// <example><code>GraphSet parts = GraphSet.BalancedPartitions(Graph.Grid(3, 3), k: 3, tolerance: 0.2);</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="k"/> is not positive, or <paramref name="tolerance"/> is negative, NaN, or infinite.
        /// </exception>
        public static GraphSet BalancedPartitions(Graph graph, int k, double tolerance = 0.0)
        {
            ArgumentNullException.ThrowIfNull(graph);

            if (k <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(k), k, "Must be positive.");
            }

            if (!double.IsFinite(tolerance) || tolerance < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "Must be non-negative and finite.");
            }

            double average = (double)graph.VertexCount / k;
            int minBlockSize = Math.Max(1, (int)Math.Floor(average * (1.0 - tolerance)));
            int maxBlockSize = (int)Math.Ceiling(average * (1.0 + tolerance));

            return Partitions(graph, k, minBlockSize, maxBlockSize);
        }

        /// <summary>
        /// The family of proper <paramref name="k"/>-colorings of <paramref name="graph"/>: assignments of
        /// one color to every vertex such that no edge joins two same-colored vertices. Returned as a
        /// <see cref="SetSet{T}"/> of <c>(int Vertex, int Color)</c> pairs rather than the raw
        /// <c>(vertex, color)</c>-variable encoding <see cref="Specs.ColoringSpec"/> builds internally, so a
        /// coloring can be read directly: <c>foreach (var (v, c) in coloring)</c>.
        /// </summary>
        /// <param name="graph">The graph to color.</param>
        /// <param name="k">The number of available colors; must be positive.</param>
        /// <param name="representativesOnly">
        /// When <see langword="true"/>, keeps only one representative coloring per color-relabeling class
        /// &#8212; see <see cref="Specs.ColoringSpec"/>'s remarks. Defaults to <see langword="false"/>
        /// (every proper coloring), which is what matches the chromatic polynomial.
        /// </param>
        /// <example>
        /// <code>
        /// SetSet&lt;(int Vertex, int Color)&gt; colorings = GraphSet.Colorings(Graph.Complete(4), k: 4);
        /// foreach (var coloring in colorings)
        /// {
        ///     foreach (var (v, c) in coloring) { /* vertex v has color c */ }
        /// }
        /// </code>
        /// </example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="k"/> is not positive.</exception>
        public static SetSet<(int Vertex, int Color)> Colorings(Graph graph, int k, bool representativesOnly = false)
        {
            ArgumentNullException.ThrowIfNull(graph);

            var spec = new ColoringSpec(graph, k, representativesOnly);
            var elements = new (int Vertex, int Color)[spec.VariableCount];
            for (int v = 0; v < graph.VertexCount; v++)
            {
                for (int c = 0; c < k; c++)
                {
                    elements[v * k + c] = (v, c);
                }
            }

            var universe = new SetUniverse<(int Vertex, int Color)>(elements);
            Zdd zdd = FrontierBuilder.Build<ColoringSpec>(universe.Manager, spec);
            return new SetSet<(int Vertex, int Color)>(universe, zdd);
        }

        // ==================== Edge-family generators (M6-9) ====================

        /// <summary>
        /// The family of edge sets in which every one of <paramref name="terminals"/> lies in the same
        /// connected component &#8212; Graphillion's <c>graphs</c>, a generalization of <see cref="Trees"/>'s
        /// "every vertex must be one component" down to "only these vertices must be one component". See
        /// <see cref="Specs.ConnectedSubgraphSpec"/>.
        /// </summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="terminals">The vertices that must end up in the same connected component.</param>
        /// <example><code>GraphSet connected = GraphSet.ConnectedSubgraphs(Graph.Grid(3, 3), new[] { 0, 4, 8 });</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="terminals"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A terminal is outside <c>0 .. graph.VertexCount - 1</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="terminals"/> repeats a vertex.</exception>
        public static GraphSet ConnectedSubgraphs(Graph graph, IEnumerable<int> terminals)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return Generate(graph, new ConnectedSubgraphSpec(graph, terminals));
        }

        /// <summary>
        /// The family of Steiner trees connecting <paramref name="terminals"/>: connected, acyclic edge
        /// sets containing every terminal, in which every leaf is itself a terminal &#8212; Graphillion's
        /// <c>steiner_subgraphs</c> / <c>steiner_trees</c>. <see cref="MinWeight(Func{Edge, int})"/> over
        /// the result gives a minimum Steiner tree. See <see cref="Specs.SteinerTreeSpec"/>.
        /// </summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="terminals">The vertices the tree must connect.</param>
        /// <example><code>GraphSet trees = GraphSet.SteinerTrees(Graph.Grid(3, 3), new[] { 0, 4, 8 });</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="terminals"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A terminal is outside <c>0 .. graph.VertexCount - 1</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="terminals"/> repeats a vertex.</exception>
        public static GraphSet SteinerTrees(Graph graph, IEnumerable<int> terminals)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return Generate(graph, new SteinerTreeSpec(graph, terminals));
        }

        /// <summary>
        /// The family of edge sets whose removal disconnects <paramref name="s"/> from <paramref name="t"/>
        /// &#8212; Graphillion's <c>graphs</c> restricted by <c>cuts</c> / <c>min_cuts</c>. See
        /// <see cref="Specs.CutSpec"/>.
        /// </summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="s">One endpoint.</param>
        /// <param name="t">The other endpoint.</param>
        /// <param name="minimalOnly">When <see langword="true"/>, only inclusion-minimal cuts (no proper subset of a member also disconnects <paramref name="s"/> and <paramref name="t"/>).</param>
        /// <example><code>GraphSet cuts = GraphSet.Cuts(Graph.Grid(3, 3), s: 0, t: 8);</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="s"/> or <paramref name="t"/> is outside <c>0 .. graph.VertexCount - 1</c>.</exception>
        public static GraphSet Cuts(Graph graph, int s, int t, bool minimalOnly = false)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return Generate(graph, new CutSpec(graph, s, t, minimalOnly));
        }

        /// <summary>
        /// The family of edge sets in which every vertex <c>v</c>'s degree lies in <c>[lo[v], hi[v]]</c>
        /// &#8212; a general form covering <see cref="Matchings"/> (<c>[0, 1]</c> everywhere) and
        /// <see cref="EdgeCovers"/> (<c>[1, &#8734;)</c> everywhere) as special cases. See
        /// <see cref="Specs.DegreeConstraintSpec"/>.
        /// </summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="lo">The minimum degree for each vertex, indexed like <see cref="Graphs.Graph.VertexCount"/>.</param>
        /// <param name="hi">The maximum degree for each vertex, indexed like <see cref="Graphs.Graph.VertexCount"/>.</param>
        /// <example><code>GraphSet degreeConstrained = GraphSet.DegreeConstrained(Graph.Complete(5), lo: new[] { 1, 1, 1, 1, 1 }, hi: new[] { 2, 2, 2, 2, 2 });</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/>, <paramref name="lo"/> or <paramref name="hi"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="lo"/> or <paramref name="hi"/> does not have exactly <see cref="Graphs.Graph.VertexCount"/> entries,
        /// or some <c>hi[v]</c> is less than <c>lo[v]</c>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">Some <c>lo[v]</c> is negative.</exception>
        public static GraphSet DegreeConstrained(Graph graph, int[] lo, int[] hi)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return Generate(graph, new DegreeConstraintSpec(graph, lo, hi));
        }

        /// <summary>The family of edge sets in which every vertex's degree lies in <c>[lo, hi]</c>. See <see cref="DegreeConstrained(Graph, int[], int[])"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="lo">The minimum degree, applied to every vertex.</param>
        /// <param name="hi">The maximum degree, applied to every vertex.</param>
        /// <example><code>GraphSet pathsAndCycles = GraphSet.DegreeConstrained(Graph.Grid(3, 3), lo: 0, hi: 2);</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="hi"/> is less than <paramref name="lo"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="lo"/> is negative.</exception>
        public static GraphSet DegreeConstrained(Graph graph, int lo, int hi)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return Generate(graph, new DegreeConstraintSpec(graph, lo, hi));
        }

        /// <summary>
        /// The family of edge covers of <paramref name="graph"/>: edge sets touching every vertex at
        /// least once. An alias for <see cref="DegreeConstrained(Graph, int, int)"/> with <c>lo: 1</c> and
        /// <c>hi</c> effectively unbounded (no vertex's degree can ever <i>exceed</i> <c>graph.EdgeCount</c>,
        /// so that bound never actually constrains any vertex &#8212; it plays the role of &#8734;) &#8212;
        /// an edge cover is simply "every vertex has degree at least one", the specific case of a degree
        /// constraint that needs no upper bound, so no separate spec exists for it.
        /// </summary>
        /// <param name="graph">The graph to search.</param>
        /// <example><code>GraphSet covers = GraphSet.EdgeCovers(Graph.Complete(5));</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public static GraphSet EdgeCovers(Graph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return Generate(graph, new DegreeConstraintSpec(graph, lo: 1, hi: graph.EdgeCount));
        }

        /// <summary>
        /// The family of edge sets whose total weight fits <paramref name="capacity"/>:
        /// <c>&#931; weights[i] x[i] &lt;= capacity</c> &#8212; Graphillion's <c>graphs</c> restricted by a
        /// knapsack constraint. See <see cref="Specs.KnapsackSpec"/>.
        /// </summary>
        /// <param name="graph">The graph whose edges are the items.</param>
        /// <param name="weights">The per-edge weight, indexed like <see cref="Graphs.Graph.Edges"/>; must all be non-negative.</param>
        /// <param name="capacity">The capacity.</param>
        /// <example><code>GraphSet fits = GraphSet.Knapsacks(Graph.Complete(5), weights: new[] { 2, 3, 4, 5, 9, 1, 6, 7, 8, 2 }, capacity: 10);</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="weights"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="weights"/> does not have exactly <see cref="Graphs.Graph.EdgeCount"/> entries.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Some weight is negative.</exception>
        public static GraphSet Knapsacks(Graph graph, int[] weights, long capacity)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(weights);

            if (weights.Length != graph.EdgeCount)
            {
                throw new ArgumentException(
                    $"Expected {graph.EdgeCount} entries (one per edge), got {weights.Length}.", nameof(weights));
            }

            return Generate<KnapsackSpec, long>(graph, new KnapsackSpec(weights, capacity));
        }

        // ==================== 1-item variants (M6-7) ====================

        /// <summary>Removes one contained edge from each edge set, using every edge of <see cref="Graph"/>. See <see cref="Zdd.RemoveSomeItem()"/>.</summary>
        public GraphSet RemoveSomeItem() => WrapPrecomputed(Zdd.RemoveSomeItem());

        /// <summary>Removes one contained edge, chosen from <paramref name="edges"/>, from each edge set. See <see cref="Zdd.RemoveSomeItem(ReadOnlySpan{int})"/>.</summary>
        /// <exception cref="ArgumentException">An edge of <paramref name="edges"/> is not part of <see cref="Graph"/>.</exception>
        public GraphSet RemoveSomeItem(params ReadOnlySpan<Edge> edges) => WrapPrecomputed(Zdd.RemoveSomeItem(ResolveEdgeIndices(edges)));

        /// <summary>Adds one absent edge to each edge set, using every edge of <see cref="Graph"/>. See <see cref="Zdd.AddSomeItem()"/>.</summary>
        public GraphSet AddSomeItem() => WrapPrecomputed(Zdd.AddSomeItem());

        /// <summary>Adds one absent edge, chosen from <paramref name="edges"/>, to each edge set. See <see cref="Zdd.AddSomeItem(ReadOnlySpan{int})"/>.</summary>
        /// <exception cref="ArgumentException">An edge of <paramref name="edges"/> is not part of <see cref="Graph"/>.</exception>
        public GraphSet AddSomeItem(params ReadOnlySpan<Edge> edges) => WrapPrecomputed(Zdd.AddSomeItem(ResolveEdgeIndices(edges)));

        /// <summary>Removes one contained edge and adds a different absent edge to each edge set, using every edge of <see cref="Graph"/>. See <see cref="Zdd.RemoveAddSomeItems()"/>.</summary>
        public GraphSet RemoveAddSomeItems() => WrapPrecomputed(Zdd.RemoveAddSomeItems());

        /// <summary>Removes one contained edge and adds a different absent edge, both chosen from <paramref name="edges"/>, to each edge set. See <see cref="Zdd.RemoveAddSomeItems(ReadOnlySpan{int})"/>.</summary>
        /// <exception cref="ArgumentException">An edge of <paramref name="edges"/> is not part of <see cref="Graph"/>.</exception>
        public GraphSet RemoveAddSomeItems(params ReadOnlySpan<Edge> edges) => WrapPrecomputed(Zdd.RemoveAddSomeItems(ResolveEdgeIndices(edges)));

        // ==================== Filters (applied at construction time) ====================

        /// <summary>Keeps only edge sets that include <paramref name="edge"/>.</summary>
        /// <param name="edge">The edge to require; must be one of <see cref="Graph"/>'s edges.</param>
        /// <exception cref="ArgumentException"><paramref name="edge"/> is not part of <see cref="Graph"/>.</exception>
        public GraphSet Including(Edge edge) => FilterEdge(edge, require: true);

        /// <summary>Keeps only edge sets that exclude <paramref name="edge"/>.</summary>
        /// <param name="edge">The edge to forbid; must be one of <see cref="Graph"/>'s edges.</param>
        /// <exception cref="ArgumentException"><paramref name="edge"/> is not part of <see cref="Graph"/>.</exception>
        public GraphSet Excluding(Edge edge) => FilterEdge(edge, require: false);

        /// <summary>Keeps only edge sets that touch <paramref name="vertex"/> (include at least one incident edge).</summary>
        /// <param name="vertex">The vertex to require touched; must be in <c>0 .. Graph.VertexCount - 1</c>.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="vertex"/> is out of range.</exception>
        public GraphSet Including(int vertex) => FilterVertex(vertex, require: true);

        /// <summary>Keeps only edge sets that avoid <paramref name="vertex"/> (include none of its incident edges).</summary>
        /// <param name="vertex">The vertex to require untouched; must be in <c>0 .. Graph.VertexCount - 1</c>.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="vertex"/> is out of range.</exception>
        public GraphSet Excluding(int vertex) => FilterVertex(vertex, require: false);

        /// <summary>Keeps only edge sets with more than <paramref name="n"/> edges.</summary>
        /// <param name="n">The size threshold; must be non-negative.</param>
        /// <example><code>GraphSet big = paths.Larger(20);</code></example>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="n"/> is negative.</exception>
        public GraphSet Larger(int n)
        {
            ThrowIfNegative(n);

            int min = n + 1;
            int max = Math.Max(min, Graph.EdgeCount);
            return Filter(new StructSpecErased<CardinalitySpec, int>(new CardinalitySpec(Graph.EdgeCount, min, max)));
        }

        /// <summary>Keeps only edge sets with fewer than <paramref name="n"/> edges.</summary>
        /// <param name="n">The size threshold; must be non-negative. <c>0</c> yields the empty family (no set has fewer than zero edges).</param>
        /// <example><code>GraphSet small = paths.Smaller(20);</code></example>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="n"/> is negative.</exception>
        public GraphSet Smaller(int n)
        {
            ThrowIfNegative(n);

            if (n == 0)
            {
                return FilterAlwaysEmpty();
            }

            return Filter(new StructSpecErased<CardinalitySpec, int>(new CardinalitySpec(Graph.EdgeCount, 0, n - 1)));
        }

        /// <summary>Keeps only edge sets with exactly <paramref name="n"/> edges.</summary>
        /// <param name="n">The required size; must be non-negative.</param>
        /// <example><code>GraphSet exact = paths.LenEquals(9);</code></example>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="n"/> is negative.</exception>
        public GraphSet LenEquals(int n)
        {
            ThrowIfNegative(n);
            return Filter(new StructSpecErased<CardinalitySpec, int>(new CardinalitySpec(Graph.EdgeCount, n, n)));
        }

        /// <summary>Keeps only edge sets whose total cost is at most <paramref name="bound"/> (Graphillion's <c>cost_le</c>).</summary>
        /// <param name="cost">Per-edge cost function; may return negatives.</param>
        /// <param name="bound">The maximum total cost.</param>
        /// <example><code>GraphSet cheap = paths.CostAtMost(e =&gt; e.Weight, 100);</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="cost"/> is <see langword="null"/>.</exception>
        public GraphSet CostAtMost(Func<Edge, long> cost, long bound) =>
            Filter(new StructSpecErased<LinearConstraintSpec, long>(new LinearConstraintSpec(BuildWeights(cost), LinearConstraintOperator.LessOrEqual, bound)));

        /// <summary>Keeps only edge sets whose total cost is at least <paramref name="bound"/>.</summary>
        /// <param name="cost">Per-edge cost function; may return negatives.</param>
        /// <param name="bound">The minimum total cost.</param>
        /// <exception cref="ArgumentNullException"><paramref name="cost"/> is <see langword="null"/>.</exception>
        public GraphSet CostAtLeast(Func<Edge, long> cost, long bound) =>
            Filter(new StructSpecErased<LinearConstraintSpec, long>(new LinearConstraintSpec(BuildWeights(cost), LinearConstraintOperator.GreaterOrEqual, bound)));

        /// <summary>Keeps only edge sets whose total cost is exactly <paramref name="value"/>.</summary>
        /// <param name="cost">Per-edge cost function; may return negatives.</param>
        /// <param name="value">The required total cost.</param>
        /// <exception cref="ArgumentNullException"><paramref name="cost"/> is <see langword="null"/>.</exception>
        public GraphSet CostEquals(Func<Edge, long> cost, long value) =>
            Filter(new StructSpecErased<LinearConstraintSpec, long>(new LinearConstraintSpec(BuildWeights(cost), LinearConstraintOperator.Equal, value)));

        // ==================== Universe / edge-order transfer (M6-6) ====================

        /// <summary>
        /// Moves this family onto <paramref name="target"/>, a graph that differs from <see cref="Graph"/>
        /// only in edge order (M6-6, issue #141) &#8212; the common case being "built over
        /// <see cref="Graphs.Graph.Optimize"/>'s reordering, results read back against the graph it
        /// optimized." Built on <see cref="SetSet{T}.ToUniverse"/>, using <see cref="Graph"/>'s
        /// <see cref="Graphs.Graph.SourceOrder"/> (the mapping <see cref="Graphs.Graph.WithEdgeOrder"/> /
        /// <see cref="Graphs.Graph.Optimize"/> leaves behind) to build the item map instead of asking the
        /// caller for one.
        /// </summary>
        /// <param name="target">
        /// The graph to move onto: the same graph <see cref="Graph"/> was reordered from, i.e.
        /// <c><see cref="Graph"/>.SourceOrder.Source</c> (or another graph with the exact same edges at
        /// the indices that mapping expects).
        /// </param>
        /// <returns>The same family of edge sets, expressed over <paramref name="target"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// <see cref="Graph"/> has no <see cref="Graphs.Graph.SourceOrder"/> &#8212; it was not produced by
        /// <see cref="Graphs.Graph.WithEdgeOrder"/> or <see cref="Graphs.Graph.Optimize"/>, so there is no
        /// recorded edge-order mapping for <see cref="ToEdgeOrder"/> to use.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="target"/>'s vertex count, edge count, or edges don't match what
        /// <see cref="Graph"/>'s <see cref="Graphs.Graph.SourceOrder"/> expects (named in the message).
        /// </exception>
        public GraphSet ToEdgeOrder(Graph target)
        {
            ArgumentNullException.ThrowIfNull(target);

            EdgeOrderMapping? sourceOrder = Graph.SourceOrder;
            if (sourceOrder is null)
            {
                throw new InvalidOperationException(
                    $"This family's graph has no {nameof(Graph.SourceOrder)}; it was not produced by " +
                    $"{nameof(Graph.WithEdgeOrder)} or {nameof(Graph.Optimize)}, so there is no recorded " +
                    $"edge-order mapping for {nameof(ToEdgeOrder)} to use.");
            }

            if (target.VertexCount != Graph.VertexCount)
            {
                throw new ArgumentException(
                    $"'{nameof(target)}' has {target.VertexCount} vertices, but this family's graph has " +
                    $"{Graph.VertexCount}; {nameof(ToEdgeOrder)} only moves a family between edge orderings of the same graph.",
                    nameof(target));
            }

            if (target.EdgeCount != Graph.EdgeCount)
            {
                throw new ArgumentException(
                    $"'{nameof(target)}' has {target.EdgeCount} edge(s), but this family's graph has " +
                    $"{Graph.EdgeCount}; {nameof(ToEdgeOrder)} only moves a family between edge orderings of the same graph.",
                    nameof(target));
            }

            var itemMap = new int[Graph.EdgeCount];
            for (int i = 0; i < Graph.EdgeCount; i++)
            {
                int targetIndex = sourceOrder.ToSourceEdgeIndex(i);
                Edge expected = Graph.GetEdge(i);
                Edge actual = target.GetEdge(targetIndex);

                if (actual != expected)
                {
                    throw new ArgumentException(
                        $"'{nameof(target)}' does not match this family's graph's {nameof(Graph.SourceOrder)}: " +
                        $"edge {expected} (index {i}) is expected at index {targetIndex} of '{nameof(target)}', but found {actual}. " +
                        $"{nameof(ToEdgeOrder)} only moves a family between edge orderings of the same graph.",
                        nameof(target));
                }

                itemMap[i] = targetIndex;
            }

            var universe = new SetUniverse<Edge>(target.Edges);
            Zdd mapped = Zdd.MapItemsTo(universe.Manager, itemMap);
            return new GraphSet(target, universe, mapped, new PrecomputedZddSpec(mapped));
        }

        // ==================== Enumeration ====================

        /// <summary>Enumerates the member edge sets lazily, in <see cref="ZddEnumerationOrder.Default"/> order.</summary>
        public IEnumerator<IReadOnlySet<Edge>> GetEnumerator() => _family.GetEnumerator();

        /// <inheritdoc cref="GetEnumerator"/>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>Lazily enumerates every member edge set in ascending total weight order.</summary>
        /// <param name="weight">Per-edge weight function.</param>
        /// <remarks>
        /// Genuinely lazy: enumerating the first <c>k</c> sets (e.g. via <c>.Take(k)</c>) costs work
        /// proportional to <c>k</c>, not the family's full size &#8212; see <see cref="LazyWeightEnumeration"/>.
        /// </remarks>
        /// <example><code>foreach (var p in paths.MinIter(e =&gt; 1).Take(10)) { /* 10 shortest paths */ }</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="weight"/> is <see langword="null"/>.</exception>
        public IEnumerable<IReadOnlySet<Edge>> MinIter(Func<Edge, int> weight)
        {
            ArgumentNullException.ThrowIfNull(weight);
            return IterCore<int, Int32WeightOps>(weight, maximize: false);
        }

        /// <inheritdoc cref="MinIter(Func{Edge, int})"/>
        public IEnumerable<IReadOnlySet<Edge>> MinIter(Func<Edge, long> weight)
        {
            ArgumentNullException.ThrowIfNull(weight);
            return IterCore<long, Int64WeightOps>(weight, maximize: false);
        }

        /// <inheritdoc cref="MinIter(Func{Edge, int})"/>
        public IEnumerable<IReadOnlySet<Edge>> MinIter(Func<Edge, double> weight)
        {
            ArgumentNullException.ThrowIfNull(weight);
            return IterCore<double, DoubleWeightOps>(weight, maximize: false);
        }

        /// <summary>Lazily enumerates every member edge set in descending total weight order. See <see cref="MinIter(Func{Edge, int})"/>.</summary>
        /// <param name="weight">Per-edge weight function.</param>
        /// <exception cref="ArgumentNullException"><paramref name="weight"/> is <see langword="null"/>.</exception>
        public IEnumerable<IReadOnlySet<Edge>> MaxIter(Func<Edge, int> weight)
        {
            ArgumentNullException.ThrowIfNull(weight);
            return IterCore<int, Int32WeightOps>(weight, maximize: true);
        }

        /// <inheritdoc cref="MaxIter(Func{Edge, int})"/>
        public IEnumerable<IReadOnlySet<Edge>> MaxIter(Func<Edge, long> weight)
        {
            ArgumentNullException.ThrowIfNull(weight);
            return IterCore<long, Int64WeightOps>(weight, maximize: true);
        }

        /// <inheritdoc cref="MaxIter(Func{Edge, int})"/>
        public IEnumerable<IReadOnlySet<Edge>> MaxIter(Func<Edge, double> weight)
        {
            ArgumentNullException.ThrowIfNull(weight);
            return IterCore<double, DoubleWeightOps>(weight, maximize: true);
        }

        /// <summary>Lazily and endlessly enumerates member edge sets, each drawn independently and uniformly at random (with replacement).</summary>
        /// <param name="random">Random source; fix a seed for deterministic output.</param>
        /// <remarks>Never completes on its own &#8212; bound it with <c>.Take(n)</c> or a <c>break</c>.</remarks>
        /// <example><code>foreach (var s in paths.RandIter(new Random(1)).Take(5)) { /* ... */ }</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">This family is empty.</exception>
        public IEnumerable<IReadOnlySet<Edge>> RandIter(Random random)
        {
            ArgumentNullException.ThrowIfNull(random);
            return RandIterCore(random);
        }

        // ==================== Weight optimization ====================

        /// <summary>Returns the maximum-weight member edge set, together with its weight.</summary>
        /// <param name="weight">Per-edge weight function.</param>
        /// <exception cref="ArgumentNullException"><paramref name="weight"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">This family is empty.</exception>
        public (IReadOnlySet<Edge> Set, int Weight) MaxWeight(Func<Edge, int> weight) => Wrap(_family.Zdd.MaxWeight(BuildWeights(weight)));

        /// <inheritdoc cref="MaxWeight(Func{Edge, int})"/>
        public (IReadOnlySet<Edge> Set, long Weight) MaxWeight(Func<Edge, long> weight) => Wrap(_family.Zdd.MaxWeight(BuildWeights(weight)));

        /// <inheritdoc cref="MaxWeight(Func{Edge, int})"/>
        public (IReadOnlySet<Edge> Set, double Weight) MaxWeight(Func<Edge, double> weight) => Wrap(_family.Zdd.MaxWeight(BuildWeights(weight)));

        /// <summary>Returns the minimum-weight member edge set, together with its weight.</summary>
        /// <param name="weight">Per-edge weight function.</param>
        /// <example><code>var shortest = paths.MinWeight(e =&gt; 1);</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="weight"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">This family is empty.</exception>
        public (IReadOnlySet<Edge> Set, int Weight) MinWeight(Func<Edge, int> weight) => Wrap(_family.Zdd.MinWeight(BuildWeights(weight)));

        /// <inheritdoc cref="MinWeight(Func{Edge, int})"/>
        public (IReadOnlySet<Edge> Set, long Weight) MinWeight(Func<Edge, long> weight) => Wrap(_family.Zdd.MinWeight(BuildWeights(weight)));

        /// <inheritdoc cref="MinWeight(Func{Edge, int})"/>
        public (IReadOnlySet<Edge> Set, double Weight) MinWeight(Func<Edge, double> weight) => Wrap(_family.Zdd.MinWeight(BuildWeights(weight)));

        /// <summary>Returns the <paramref name="k"/> highest-weight member edge sets, sorted by descending weight.</summary>
        /// <param name="weight">Per-edge weight function.</param>
        /// <param name="k">Number of sets to return; 0 or more.</param>
        /// <exception cref="ArgumentNullException"><paramref name="weight"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="k"/> is negative.</exception>
        public (IReadOnlySet<Edge> Set, int Weight)[] TopK(Func<Edge, int> weight, int k) => Wrap(_family.Zdd.TopK(BuildWeights(weight), k));

        /// <inheritdoc cref="TopK(Func{Edge, int}, int)"/>
        public (IReadOnlySet<Edge> Set, long Weight)[] TopK(Func<Edge, long> weight, int k) => Wrap(_family.Zdd.TopK(BuildWeights(weight), k));

        /// <inheritdoc cref="TopK(Func{Edge, int}, int)"/>
        public (IReadOnlySet<Edge> Set, double Weight)[] TopK(Func<Edge, double> weight, int k) => Wrap(_family.Zdd.TopK(BuildWeights(weight), k));

        /// <summary>Returns the probability that a set formed by independently including each edge with its given probability belongs to this family.</summary>
        /// <param name="probability">Per-edge inclusion probability function, each between 0 and 1.</param>
        /// <exception cref="ArgumentNullException"><paramref name="probability"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="probability"/> returns a value below 0, above 1, or <see cref="double.NaN"/> for some edge.</exception>
        public double Probability(Func<Edge, double> probability) => _family.Zdd.Probability(BuildWeights(probability));

        // ==================== Sampling, membership, ranking ====================

        /// <summary>Picks one member edge set uniformly at random.</summary>
        /// <param name="random">Random source; fix a seed for deterministic output.</param>
        /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">This family is empty.</exception>
        public IReadOnlySet<Edge> Sample(Random random) => _family.Sample(random);

        /// <summary>Picks <paramref name="count"/> member edge sets, drawn independently and uniformly at random (with replacement).</summary>
        /// <param name="count">Number of sets to draw; 0 or more.</param>
        /// <param name="random">Random source; fix a seed for deterministic output.</param>
        /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
        /// <exception cref="InvalidOperationException">This family is empty.</exception>
        public IReadOnlySet<Edge>[] Sample(int count, Random random) => _family.Sample(count, random);

        /// <summary>Returns whether <paramref name="edges"/> belongs to this family.</summary>
        /// <param name="edges">The edge set to check.</param>
        /// <exception cref="ArgumentNullException"><paramref name="edges"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">An edge is not part of <see cref="Graph"/>.</exception>
        public bool Contains(IEnumerable<Edge> edges) => _family.Contains(edges);

        /// <summary>Returns the <paramref name="index"/>-th (0-based) member edge set in <paramref name="order"/> order (unranking).</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or at least <see cref="Count"/>.</exception>
        public IReadOnlySet<Edge> ElementAt(BigInteger index, ZddEnumerationOrder order = ZddEnumerationOrder.Default) => _family.ElementAt(index, order);

        /// <summary>Returns the rank of <paramref name="edges"/> in <paramref name="order"/> order (ranking), or -1 if it is not a member.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="edges"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">An edge is not part of <see cref="Graph"/>.</exception>
        public BigInteger IndexOf(IEnumerable<Edge> edges, ZddEnumerationOrder order = ZddEnumerationOrder.Default) => _family.IndexOf(edges, order);

        // ==================== Equality ====================

        /// <summary>Whether two families are the same set of member edge sets over the same <see cref="Universe"/>.</summary>
        public bool Equals(GraphSet? other) => other is not null && _family.Equals(other._family);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is GraphSet other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => _family.GetHashCode();

        /// <summary>Whether two families are the same set of member edge sets over the same <see cref="Universe"/>.</summary>
        public static bool operator ==(GraphSet? left, GraphSet? right) => left is null ? right is null : left.Equals(right);

        /// <summary>Whether two families differ, or belong to different universes.</summary>
        public static bool operator !=(GraphSet? left, GraphSet? right) => !(left == right);

        /// <inheritdoc/>
        public override string ToString() => $"GraphSet({_family.Zdd})";

        // ==================== I/O ====================

        /// <summary>
        /// Writes this family as Graphviz DOT source, labeling each level by its edge (docs/PLAN.md
        /// §9) instead of a bare item index, unless <paramref name="options"/> already sets
        /// <see cref="DotOptions.LevelLabel"/> itself.
        /// </summary>
        /// <param name="options">
        /// Extra rendering knobs (M5-4, issue #56); every default besides <see cref="DotOptions.LevelLabel"/>
        /// is <see cref="Zdd.ToDot(DotOptions)"/>'s own.
        /// </param>
        public string ToDot(DotOptions? options = null) => Zdd.ToDot(WithEdgeLevelLabel(options));

        /// <summary>Streams this family's DOT representation as <see cref="ToDot"/> does, without buffering it all in memory.</summary>
        /// <param name="writer">The destination writer.</param>
        /// <param name="options">Extra rendering knobs; see <see cref="ToDot"/>.</param>
        public void WriteDot(TextWriter writer, DotOptions? options = null) => Zdd.WriteDot(writer, WithEdgeLevelLabel(options));

        private DotOptions WithEdgeLevelLabel(DotOptions? options)
        {
            if (options?.LevelLabel is not null)
            {
                return options;
            }

            DotOptions effective = options?.Clone() ?? new DotOptions();
            effective.LevelLabel = item => Universe.ElementAt(item).ToString() ?? string.Empty;
            return effective;
        }

        // ==================== Internals ====================

        private static GraphSet Generate<TSpec>(Graph graph, TSpec spec)
            where TSpec : struct, IArrayDdSpec
        {
            var universe = new SetUniverse<Edge>(graph.Edges);
            IErasedGraphSpec erased = new ArraySpecErased<TSpec>(spec);
            Zdd zdd = Build(universe.Manager, erased);
            return new GraphSet(graph, universe, zdd, erased);
        }

        private static GraphSet Generate<TSpec, TState>(Graph graph, TSpec spec)
            where TSpec : struct, IDdSpec<TState>
        {
            var universe = new SetUniverse<Edge>(graph.Edges);
            IErasedGraphSpec erased = new StructSpecErased<TSpec, TState>(spec);
            Zdd zdd = Build(universe.Manager, erased);
            return new GraphSet(graph, universe, zdd, erased);
        }

        private static SetSet<int> GenerateVertexFamily<TSpec>(Graph graph, TSpec spec)
            where TSpec : struct, IArrayDdSpec
        {
            var universe = new SetUniverse<int>(Enumerable.Range(0, graph.VertexCount));
            Zdd zdd = FrontierBuilder.Build<TSpec>(universe.Manager, spec);
            return new SetSet<int>(universe, zdd);
        }

        private static Zdd Build(ZddManager manager, IErasedGraphSpec erased) =>
            FrontierBuilder.Build<ErasedGraphDdSpec, object?>(manager, new ErasedGraphDdSpec(erased));

        private GraphSet Filter(IErasedGraphSpec filterSpec)
        {
            IErasedGraphSpec combined = new AndErasedSpec(_spec, filterSpec);
            Zdd zdd = Build(Universe.Manager, combined);
            return new GraphSet(Graph, Universe, zdd, combined);
        }

        private GraphSet FilterEdge(Edge edge, bool require)
        {
            int edgeIndex = ResolveEdgeIndex(edge);
            return Filter(new ArraySpecErased<EdgeMembershipSpec>(new EdgeMembershipSpec(Graph, edgeIndex, require)));
        }

        private GraphSet FilterVertex(int vertex, bool require)
        {
            if ((uint)vertex >= (uint)Graph.VertexCount)
            {
                throw new ArgumentOutOfRangeException(nameof(vertex), vertex, $"Must be in 0 .. {Graph.VertexCount - 1}.");
            }

            return Filter(new ArraySpecErased<VertexTouchSpec>(new VertexTouchSpec(Graph, vertex, require)));
        }

        private GraphSet FilterAlwaysEmpty() => new GraphSet(Graph, Universe, Universe.Manager.Empty, AlwaysFalseSpec.Instance);

        private int ResolveEdgeIndex(Edge edge)
        {
            for (int i = 0; i < Graph.EdgeCount; i++)
            {
                if (Graph.GetEdge(i) == edge)
                {
                    return i;
                }
            }

            throw new ArgumentException($"Edge {edge} is not part of this graph set's graph.", nameof(edge));
        }

        private int[] ResolveEdgeIndices(ReadOnlySpan<Edge> edges)
        {
            var indices = new int[edges.Length];

            for (int i = 0; i < edges.Length; i++)
            {
                indices[i] = ResolveEdgeIndex(edges[i]);
            }

            return indices;
        }

        /// <summary>
        /// Wraps a <see cref="Zdd"/> built by direct algebra (not a frontier walk) as a
        /// <see cref="GraphSet"/>, using <see cref="PrecomputedZddSpec"/> so a later <see cref="Filter"/>
        /// call (<see cref="Including(Edge)"/>, <see cref="Larger"/>, ...) still composes correctly.
        /// </summary>
        private GraphSet WrapPrecomputed(Zdd zdd) => new GraphSet(Graph, Universe, zdd, new PrecomputedZddSpec(zdd));

        private IEnumerable<IReadOnlySet<Edge>> IterCore<TWeight, TOps>(Func<Edge, TWeight> weight, bool maximize)
            where TOps : struct, IWeightOps<TWeight>
        {
            TWeight[] weights = BuildWeights(weight);
            Zdd zdd = _family.Zdd;

            foreach (WeightedSet<TWeight> item in LazyWeightEnumeration.Enumerate<TWeight, TOps>(zdd.Owner!, zdd.Id, weights, maximize))
            {
                yield return Universe.ToElementSet(item.Items);
            }
        }

        private IEnumerable<IReadOnlySet<Edge>> RandIterCore(Random random)
        {
            while (true)
            {
                yield return Sample(random);
            }
        }

        private TWeight[] BuildWeights<TWeight>(Func<Edge, TWeight> weight)
        {
            ArgumentNullException.ThrowIfNull(weight);

            var weights = new TWeight[Graph.EdgeCount];
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = weight(Graph.GetEdge(i));
            }

            return weights;
        }

        private (IReadOnlySet<Edge> Set, TWeight Weight) Wrap<TWeight>(WeightedSet<TWeight> result) =>
            (Universe.ToElementSet(result.Items), result.Weight);

        private (IReadOnlySet<Edge> Set, TWeight Weight)[] Wrap<TWeight>(WeightedSet<TWeight>[] results)
        {
            var mapped = new (IReadOnlySet<Edge> Set, TWeight Weight)[results.Length];
            for (int i = 0; i < results.Length; i++)
            {
                mapped[i] = Wrap(results[i]);
            }

            return mapped;
        }

        private static void ThrowIfNegative(int n)
        {
            if (n < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(n), n, "Must be non-negative.");
            }
        }

        /// <summary>An erased spec accepting nothing at all, used to keep a filter chain's recipe consistent after <see cref="Smaller"/>(0) collapses it to empty.</summary>
        private sealed class AlwaysFalseSpec : IErasedGraphSpec
        {
            public static readonly AlwaysFalseSpec Instance = new AlwaysFalseSpec();

            public int GetRoot(out object? state)
            {
                state = null;
                return DdResult.False;
            }

            public int GetChild(object? state, int level, int value, out object? nextState)
            {
                nextState = null;
                return DdResult.False;
            }

            public bool StateEquals(object? left, object? right) => true;

            public int StateHashCode(object? state) => 0;
        }
    }
}
