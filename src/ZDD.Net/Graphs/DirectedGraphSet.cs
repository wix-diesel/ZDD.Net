using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Io;
using ZDD.Net.Sets;
using ZDD.Net.Specs;

namespace ZDD.Net.Graphs
{
    /// <summary>
    /// The family of arc sets of one <see cref="Graphs.DirectedGraph"/> &#8212; the directed counterpart of
    /// <see cref="GraphSet"/> (M7-6, docs/design/m7-directed-graphs.md &#167;3.5), offering the same
    /// generator/filter/enumeration/weight surface over <see cref="DirectedEdge"/> instead of
    /// <see cref="Edge"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// DirectedGraph grid = DirectedGraph.Bidirected(Graph.Grid(9, 9));
    /// DirectedGraphSet paths = DirectedGraphSet.Paths(grid, from: 0, to: 80);
    ///
    /// Console.WriteLine(paths.Count);                     // matches GraphSet.Paths's undirected count
    /// var shortest = paths.MinWeight(e =&gt; 1);
    /// var sample   = paths.Sample(new Random(42));
    ///
    /// var filtered = paths.Including(edge).Excluding(other).Smaller(20);
    /// foreach (var p in filtered.Take(10)) { /* ... */ }
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>
    /// A specialization of <see cref="SetSet{T}"/> for <see cref="DirectedEdge"/>, built the same way
    /// <see cref="GraphSet"/> is built on <see cref="Edge"/>: a <see cref="Graphs.DirectedGraph"/>'s arc
    /// index <i>is</i> its ZDD variable index (<see cref="Graphs.DirectedGraph.EdgeIndexToVariableIndex"/>),
    /// and this type's fluent filters reuse the same type-erased spec chain (<c>IErasedGraphSpec</c> in
    /// <c>GraphSetSpec.cs</c>) <see cref="GraphSet"/> uses &#8212; it is generic over the wrapped spec, not
    /// tied to undirected edges.
    /// </para>
    /// <para>
    /// <b>No common base class with <see cref="GraphSet"/></b> (OPEN-QUESTIONS B22): a shared base would need
    /// a self-referential type parameter (CRTP) so that e.g. <see cref="Including(DirectedEdge)"/> can return
    /// its own concrete type, which costs public-API readability far more than the ~30 lines of thin wrapper
    /// this type duplicates from <see cref="GraphSet"/> &#8212; the actual logic lives once, in
    /// <see cref="SetSet{T}"/> and the shared erased-spec chain.
    /// </para>
    /// <para>
    /// <b>Filters are applied during construction, not after it</b>, exactly as <see cref="GraphSet"/>'s are
    /// &#8212; see its remarks.
    /// </para>
    /// <para>
    /// <see cref="Including(int)"/> / <see cref="Excluding(int)"/> are direction-agnostic: they require (or
    /// forbid) at least one incident arc regardless of whether it points in or out of the vertex. To
    /// distinguish direction, use <see cref="Including(DirectedEdge)"/> / <see cref="Excluding(DirectedEdge)"/>.
    /// </para>
    /// </remarks>
    public sealed class DirectedGraphSet : IEnumerable<IReadOnlySet<DirectedEdge>>, IEquatable<DirectedGraphSet>
    {
        private readonly SetSet<DirectedEdge> _family;
        private readonly IErasedGraphSpec _spec;

        private DirectedGraphSet(DirectedGraph graph, SetUniverse<DirectedEdge> universe, Zdd zdd, IErasedGraphSpec spec)
        {
            Graph = graph;
            _family = new SetSet<DirectedEdge>(universe, zdd);
            _spec = spec;
        }

        /// <summary>The graph this family's arc sets are drawn from.</summary>
        public DirectedGraph Graph { get; }

        /// <summary>The element &#8596; item-index mapping this family is expressed over (arc index <c>i</c> is item index <c>i</c>).</summary>
        public SetUniverse<DirectedEdge> Universe => _family.Universe;

        /// <summary>The underlying ZDD, for callers who want to drop down to the low-level API.</summary>
        public Zdd Zdd => _family.Zdd;

        /// <summary>The exact number of member arc sets, in time proportional to node count. See <see cref="SetSet{T}"/>'s remarks on LINQ's <c>Count()</c>.</summary>
        public BigInteger Count => _family.Count;

        /// <summary>The number of member arc sets, approximated as a <see cref="double"/>. Faster than <see cref="Count"/>.</summary>
        public double CountApprox => _family.CountApprox;

        /// <summary>The exact number of member arc sets, as a <see cref="long"/>.</summary>
        /// <exception cref="OverflowException"><see cref="Count"/> does not fit in a <see cref="long"/>.</exception>
        public long LongCount() => _family.LongCount();

        /// <summary>Whether this family has no member arc sets.</summary>
        public bool IsEmpty => _family.IsEmpty;

        // ==================== Factories (M8-1) ====================

        /// <summary>
        /// The family of exactly <paramref name="edgeSets"/> &#8212; the directed counterpart of
        /// <see cref="GraphSet.FromSets"/>, and the starting point for a family that no generator
        /// produces. Duplicate arcs within one set, and duplicate sets, are collapsed.
        /// </summary>
        /// <param name="graph">The graph whose arcs the sets are drawn from.</param>
        /// <param name="edgeSets">The member arc sets; every arc must be one of <paramref name="graph"/>'s.</param>
        /// <example><code>DirectedGraphSet gs = DirectedGraphSet.FromSets(graph, new[] { new[] { new DirectedEdge(1, 2) } });</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/>, <paramref name="edgeSets"/>, or one of its sets is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">An arc is not part of <paramref name="graph"/> (the message names it).</exception>
        public static DirectedGraphSet FromSets(DirectedGraph graph, IEnumerable<IEnumerable<DirectedEdge>> edgeSets)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(edgeSets);

            var universe = new SetUniverse<DirectedEdge>(graph.Edges);
            var materialized = new List<DirectedEdge[]>();

            foreach (IEnumerable<DirectedEdge> edgeSet in edgeSets)
            {
                if (edgeSet is null)
                {
                    throw new ArgumentNullException(nameof(edgeSets), $"'{nameof(edgeSets)}' contains a null set.");
                }

                DirectedEdge[] edges = edgeSet as DirectedEdge[] ?? System.Linq.Enumerable.ToArray(edgeSet);

                foreach (DirectedEdge edge in edges)
                {
                    if (!universe.Contains(edge))
                    {
                        throw new ArgumentException($"Arc {edge} is not part of the given graph.", nameof(edgeSets));
                    }
                }

                materialized.Add(edges);
            }

            return FromPrecomputed(graph, universe, SetSet<DirectedEdge>.FromSets(universe, materialized).Zdd);
        }

        /// <summary>The family with no member arc sets at all &#8212; the identity for <c>Union</c>, and what every filter narrows toward.</summary>
        /// <param name="graph">The graph whose arcs the (absent) sets would be drawn from.</param>
        /// <example><code>DirectedGraphSet none = DirectedGraphSet.Empty(graph);</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public static DirectedGraphSet Empty(DirectedGraph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);

            var universe = new SetUniverse<DirectedEdge>(graph.Edges);
            return FromPrecomputed(graph, universe, universe.Manager.Empty);
        }

        /// <summary>The family of every arc subset of <paramref name="graph"/> (2^E).</summary>
        /// <param name="graph">The graph whose arcs are the universe.</param>
        /// <example><code>DirectedGraphSet all = DirectedGraphSet.PowerSet(graph); // Count == 2^graph.EdgeCount</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public static DirectedGraphSet PowerSet(DirectedGraph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);

            var universe = new SetUniverse<DirectedEdge>(graph.Edges);
            return FromPrecomputed(graph, universe, universe.Manager.PowerSetOf(GraphSetFactory.AllItems(graph.EdgeCount)));
        }

        /// <summary>
        /// Reads a <see cref="Core.Zdd"/> built with the low-level API back as a family of
        /// <paramref name="graph"/>'s arc sets, taking item index <c>i</c> to be arc index <c>i</c>.
        /// </summary>
        /// <param name="graph">The graph to read <paramref name="zdd"/> against.</param>
        /// <param name="zdd">The family, over a manager with at least <paramref name="graph"/>'s arc count of variables.</param>
        /// <example><code>DirectedGraphSet gs = DirectedGraphSet.FromZdd(graph, FrontierBuilder.Build&lt;MySpec&gt;(manager, spec));</code></example>
        /// <remarks>
        /// <b>The caller guarantees the correspondence</b>, exactly as in <see cref="GraphSet.FromZdd"/>:
        /// that <paramref name="zdd"/> was really built over <paramref name="graph"/>'s arc order cannot be
        /// checked, only that its manager has enough variables and that no set uses an item outside
        /// <paramref name="graph"/>'s arcs.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="zdd"/>'s manager has fewer variables than <paramref name="graph"/> has arcs, or
        /// some member set uses an item index that is not an arc index of <paramref name="graph"/>.
        /// </exception>
        public static DirectedGraphSet FromZdd(DirectedGraph graph, Zdd zdd)
        {
            ArgumentNullException.ThrowIfNull(graph);

            int levelOffset = GraphSetFactory.ValidateZddOver(zdd, graph.EdgeCount, nameof(zdd));
            var universe = new SetUniverse<DirectedEdge>(graph.Edges);
            Zdd rebuilt = Build(universe.Manager, new PrecomputedZddSpec(zdd, levelOffset));
            return FromPrecomputed(graph, universe, rebuilt);
        }

        // ==================== Generators ====================

        /// <summary>The family of directed simple <c>from</c>&#8211;<c>to</c> paths of <paramref name="graph"/>. See <see cref="Specs.DirectedPathSpec"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="from">The source endpoint. Ignored when <paramref name="allowAnyEndpoints"/> is <see langword="true"/>.</param>
        /// <param name="to">The sink endpoint. Ignored when <paramref name="allowAnyEndpoints"/> is <see langword="true"/>.</param>
        /// <param name="allowAnyEndpoints">When <see langword="true"/>, every directed simple path in the graph, for any ordered pair of endpoints.</param>
        /// <example><code>DirectedGraphSet paths = DirectedGraphSet.Paths(DirectedGraph.Bidirected(Graph.Grid(9, 9)), from: 0, to: 80);</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="from"/> or <paramref name="to"/> is outside <c>0 .. graph.VertexCount - 1</c>.</exception>
        public static DirectedGraphSet Paths(DirectedGraph graph, int from, int to, bool allowAnyEndpoints = false)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return Generate(graph, new DirectedPathSpec(graph, from, to, allowAnyEndpoints));
        }

        /// <summary>The family of arc sets forming directed simple cycles of <paramref name="graph"/>. See <see cref="Specs.DirectedCycleSpec"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="single">When <see langword="true"/> (default), exactly one directed simple cycle; when <see langword="false"/>, any nonempty union of vertex-disjoint directed simple cycles.</param>
        /// <example><code>DirectedGraphSet cycles = DirectedGraphSet.Cycles(DirectedGraph.Bidirected(Graph.Grid(5, 5)));</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public static DirectedGraphSet Cycles(DirectedGraph graph, bool single = true)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return Generate(graph, new DirectedCycleSpec(graph, single));
        }

        /// <summary>The family of directed Hamiltonian <paramref name="s"/>&#8211;<paramref name="t"/> paths of <paramref name="graph"/> (touching every vertex). See <see cref="Specs.DirectedHamiltonianPathSpec"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="s">The source endpoint.</param>
        /// <param name="t">The sink endpoint.</param>
        /// <example><code>DirectedGraphSet tours = DirectedGraphSet.HamiltonianPaths(DirectedGraph.Complete(6), 0, 5);</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="s"/> or <paramref name="t"/> is outside <c>0 .. graph.VertexCount - 1</c>.</exception>
        public static DirectedGraphSet HamiltonianPaths(DirectedGraph graph, int s, int t)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return Generate(graph, new DirectedHamiltonianPathSpec(graph, s, t));
        }

        /// <summary>The family of directed Hamiltonian cycles of <paramref name="graph"/> (touching every vertex). See <see cref="Specs.DirectedHamiltonianCycleSpec"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <example><code>DirectedGraphSet tours = DirectedGraphSet.HamiltonianCycles(DirectedGraph.Complete(6));</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public static DirectedGraphSet HamiltonianCycles(DirectedGraph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return Generate(graph, new DirectedHamiltonianCycleSpec(graph));
        }

        /// <summary>The family of spanning out-arborescences of <paramref name="graph"/> rooted at <paramref name="root"/>. See <see cref="Specs.ArborescenceSpec"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="root">The arborescence's root: the one vertex required to have in-degree 0.</param>
        /// <example><code>DirectedGraphSet trees = DirectedGraphSet.Arborescences(DirectedGraph.Bidirected(Graph.Complete(6)), root: 0);</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="root"/> is outside <c>0 .. graph.VertexCount - 1</c>.</exception>
        public static DirectedGraphSet Arborescences(DirectedGraph graph, int root)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return Generate(graph, new ArborescenceSpec(graph, root));
        }

        /// <summary>
        /// The family of arc sets in which every vertex <c>v</c>'s in-degree lies in <c>[inLo[v], inHi[v]]</c>
        /// and out-degree in <c>[outLo[v], outHi[v]]</c>. See <see cref="Specs.DirectedDegreeConstraintSpec"/>.
        /// </summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="inLo">The minimum in-degree for each vertex, indexed like <see cref="Graphs.DirectedGraph.VertexCount"/>.</param>
        /// <param name="inHi">The maximum in-degree for each vertex.</param>
        /// <param name="outLo">The minimum out-degree for each vertex.</param>
        /// <param name="outHi">The maximum out-degree for each vertex.</param>
        /// <example><code>DirectedGraphSet oneEach = DirectedGraphSet.DegreeConstrained(DirectedGraph.Complete(5), inLo: new[] { 1, 1, 1, 1, 1 }, inHi: new[] { 1, 1, 1, 1, 1 }, outLo: new[] { 1, 1, 1, 1, 1 }, outHi: new[] { 1, 1, 1, 1, 1 });</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/>, <paramref name="inLo"/>, <paramref name="inHi"/>, <paramref name="outLo"/> or <paramref name="outHi"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// One of the arrays does not have exactly <see cref="Graphs.DirectedGraph.VertexCount"/> entries,
        /// or some <c>inHi[v]</c>/<c>outHi[v]</c> is less than the matching <c>inLo[v]</c>/<c>outLo[v]</c>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">Some <c>inLo[v]</c> or <c>outLo[v]</c> is negative.</exception>
        public static DirectedGraphSet DegreeConstrained(DirectedGraph graph, int[] inLo, int[] inHi, int[] outLo, int[] outHi)
        {
            ArgumentNullException.ThrowIfNull(graph);
            return Generate(graph, new DirectedDegreeConstraintSpec(graph, inLo, inHi, outLo, outHi));
        }

        // ==================== Family algebra (M8-2) ====================

        /// <summary>Union: arc sets belonging to either family.</summary>
        /// <param name="other">The other family; must share this family's <see cref="Universe"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="other"/> does not share this family's <see cref="Universe"/>; the message names <see cref="ToUniverseOf"/>.</exception>
        public DirectedGraphSet Union(DirectedGraphSet other) => Combine(other, static (f, g) => f.Union(g));

        /// <summary>Intersection: arc sets belonging to both families.</summary>
        /// <param name="other">The other family; must share this family's <see cref="Universe"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="other"/> does not share this family's <see cref="Universe"/>; the message names <see cref="ToUniverseOf"/>.</exception>
        public DirectedGraphSet Intersect(DirectedGraphSet other) => Combine(other, static (f, g) => f.Intersect(g));

        /// <summary>Difference: arc sets in this family that are not in <paramref name="other"/>.</summary>
        /// <param name="other">The other family; must share this family's <see cref="Universe"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="other"/> does not share this family's <see cref="Universe"/>; the message names <see cref="ToUniverseOf"/>.</exception>
        public DirectedGraphSet Difference(DirectedGraphSet other) => Combine(other, static (f, g) => f.Difference(g));

        /// <summary>Symmetric difference: arc sets belonging to exactly one of the two families.</summary>
        /// <param name="other">The other family; must share this family's <see cref="Universe"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="other"/> does not share this family's <see cref="Universe"/>; the message names <see cref="ToUniverseOf"/>.</exception>
        public DirectedGraphSet SymmetricDifference(DirectedGraphSet other) => Combine(other, static (f, g) => f.SymmetricDifference(g));

        /// <summary>Union. Same as <see cref="Union"/>.</summary>
        public static DirectedGraphSet operator |(DirectedGraphSet left, DirectedGraphSet right) => left.Union(right);

        /// <summary>Intersection. Same as <see cref="Intersect"/>.</summary>
        public static DirectedGraphSet operator &(DirectedGraphSet left, DirectedGraphSet right) => left.Intersect(right);

        /// <summary>Difference. Same as <see cref="Difference"/>.</summary>
        public static DirectedGraphSet operator -(DirectedGraphSet left, DirectedGraphSet right) => left.Difference(right);

        /// <summary>Symmetric difference. Same as <see cref="SymmetricDifference"/>.</summary>
        public static DirectedGraphSet operator ^(DirectedGraphSet left, DirectedGraphSet right) => left.SymmetricDifference(right);

        /// <summary>Keeps only the arc sets that are maximal under inclusion.</summary>
        public DirectedGraphSet Maximal() => WrapPrecomputed(Zdd.Maximal());

        /// <summary>Keeps only the arc sets that are minimal under inclusion.</summary>
        public DirectedGraphSet Minimal() => WrapPrecomputed(Zdd.Minimal());

        // ==================== 1-item variants (M8-4) ====================

        /// <summary>Removes one contained arc from each arc set, using every arc of <see cref="Graph"/>. See <see cref="Zdd.RemoveSomeItem()"/>.</summary>
        public DirectedGraphSet RemoveSomeItem() => WrapPrecomputed(Zdd.RemoveSomeItem());

        /// <summary>Removes one contained arc, chosen from <paramref name="edges"/>, from each arc set. See <see cref="Zdd.RemoveSomeItem(ReadOnlySpan{int})"/>.</summary>
        /// <exception cref="ArgumentException">An arc of <paramref name="edges"/> is not part of <see cref="Graph"/>.</exception>
        public DirectedGraphSet RemoveSomeItem(params ReadOnlySpan<DirectedEdge> edges) =>
            WrapPrecomputed(Zdd.RemoveSomeItem(ResolveEdgeIndices(edges)));

        /// <summary>Adds one absent arc to each arc set, using every arc of <see cref="Graph"/>. See <see cref="Zdd.AddSomeItem()"/>.</summary>
        public DirectedGraphSet AddSomeItem() => WrapPrecomputed(Zdd.AddSomeItem());

        /// <summary>Adds one absent arc, chosen from <paramref name="edges"/>, to each arc set. See <see cref="Zdd.AddSomeItem(ReadOnlySpan{int})"/>.</summary>
        /// <exception cref="ArgumentException">An arc of <paramref name="edges"/> is not part of <see cref="Graph"/>.</exception>
        public DirectedGraphSet AddSomeItem(params ReadOnlySpan<DirectedEdge> edges) =>
            WrapPrecomputed(Zdd.AddSomeItem(ResolveEdgeIndices(edges)));

        /// <summary>Removes one contained arc and adds a different absent arc to each arc set, using every arc of <see cref="Graph"/>. See <see cref="Zdd.RemoveAddSomeItems()"/>.</summary>
        public DirectedGraphSet RemoveAddSomeItems() => WrapPrecomputed(Zdd.RemoveAddSomeItems());

        /// <summary>Removes one contained arc and adds a different absent arc, both chosen from <paramref name="edges"/>, to each arc set. See <see cref="Zdd.RemoveAddSomeItems(ReadOnlySpan{int})"/>.</summary>
        /// <exception cref="ArgumentException">An arc of <paramref name="edges"/> is not part of <see cref="Graph"/>.</exception>
        public DirectedGraphSet RemoveAddSomeItems(params ReadOnlySpan<DirectedEdge> edges) =>
            WrapPrecomputed(Zdd.RemoveAddSomeItems(ResolveEdgeIndices(edges)));

        // ==================== Universe transfer (M8-2) ====================

        /// <summary>
        /// Moves this family onto <paramref name="other"/>'s <see cref="Universe"/> so the two can be
        /// combined (M8-2, issue #188): every generator builds its own universe and <see cref="ZddManager"/>,
        /// so even two families of the same <see cref="Graphs.DirectedGraph"/> need this before
        /// <see cref="Union"/> and friends will accept them.
        /// </summary>
        /// <param name="other">The family to move onto; its <see cref="Graphs.DirectedGraph"/> must have the same arcs at the same indices.</param>
        /// <returns>The same family of arc sets over <paramref name="other"/>'s <see cref="Universe"/>, or this same instance when the two already share one.</returns>
        /// <remarks>
        /// B18 keeps this promotion explicit instead of hiding a <see cref="Zdd.TransferTo"/> inside every
        /// binary operation, where its memory cost would be invisible. Since the arc orders must already
        /// agree, the transfer is the identity map.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="other"/>'s graph has a different vertex count, arc count, or arc order
        /// (the message names the first differing index).
        /// </exception>
        public DirectedGraphSet ToUniverseOf(DirectedGraphSet other)
        {
            ArgumentNullException.ThrowIfNull(other);

            if (ReferenceEquals(Universe, other.Universe))
            {
                return this;
            }

            EnsureSameEdgeOrder(other.Graph);

            Zdd transferred = Zdd.TransferTo(other.Universe.Manager);
            return new DirectedGraphSet(other.Graph, other.Universe, transferred, new PrecomputedZddSpec(transferred));
        }

        /// <summary>Validates that <paramref name="target"/> has this family's graph's arcs at the very same indices.</summary>
        private void EnsureSameEdgeOrder(DirectedGraph target)
        {
            if (target.VertexCount != Graph.VertexCount || target.EdgeCount != Graph.EdgeCount)
            {
                throw new ArgumentException(
                    $"'other' is a family of a different graph ({target.VertexCount} vertices / {target.EdgeCount} arc(s) " +
                    $"against this family's {Graph.VertexCount} / {Graph.EdgeCount}); {nameof(ToUniverseOf)} only moves a " +
                    $"family between universes built over the very same arcs.",
                    "other");
            }

            for (int i = 0; i < Graph.EdgeCount; i++)
            {
                DirectedEdge mine = Graph.GetEdge(i);
                DirectedEdge theirs = target.GetEdge(i);

                if (mine != theirs)
                {
                    throw new ArgumentException(
                        $"'other' is a family of a different arc order: arc index {i} is {mine} here but {theirs} there. " +
                        $"{nameof(ToUniverseOf)} moves a family between universes, not between arc orders: rebuild one of the " +
                        $"two over the other's arc order (see '{nameof(DirectedGraph)}.{nameof(DirectedGraph.WithEdgeOrder)}') first.",
                        "other");
                }
            }
        }

        // ==================== Filters (applied at construction time) ====================

        /// <summary>Keeps only arc sets that include <paramref name="edge"/>.</summary>
        /// <param name="edge">The arc to require; must be one of <see cref="Graph"/>'s arcs.</param>
        /// <exception cref="ArgumentException"><paramref name="edge"/> is not part of <see cref="Graph"/>.</exception>
        public DirectedGraphSet Including(DirectedEdge edge) => FilterEdge(edge, require: true);

        /// <summary>Keeps only arc sets that exclude <paramref name="edge"/>.</summary>
        /// <param name="edge">The arc to forbid; must be one of <see cref="Graph"/>'s arcs.</param>
        /// <exception cref="ArgumentException"><paramref name="edge"/> is not part of <see cref="Graph"/>.</exception>
        public DirectedGraphSet Excluding(DirectedEdge edge) => FilterEdge(edge, require: false);

        /// <summary>Keeps only arc sets that touch <paramref name="vertex"/> (include at least one incident arc, regardless of direction).</summary>
        /// <param name="vertex">The vertex to require touched; must be in <c>0 .. Graph.VertexCount - 1</c>.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="vertex"/> is out of range.</exception>
        public DirectedGraphSet Including(int vertex) => FilterVertex(vertex, require: true);

        /// <summary>Keeps only arc sets that avoid <paramref name="vertex"/> (include none of its incident arcs, in either direction).</summary>
        /// <param name="vertex">The vertex to require untouched; must be in <c>0 .. Graph.VertexCount - 1</c>.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="vertex"/> is out of range.</exception>
        public DirectedGraphSet Excluding(int vertex) => FilterVertex(vertex, require: false);

        /// <summary>Keeps only arc sets with more than <paramref name="n"/> arcs.</summary>
        /// <param name="n">The size threshold; must be non-negative.</param>
        /// <example><code>DirectedGraphSet big = paths.Larger(20);</code></example>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="n"/> is negative.</exception>
        public DirectedGraphSet Larger(int n)
        {
            ThrowIfNegative(n);

            int min = n + 1;
            int max = Math.Max(min, Graph.EdgeCount);
            return Filter(new StructSpecErased<CardinalitySpec, int>(new CardinalitySpec(Graph.EdgeCount, min, max)));
        }

        /// <summary>Keeps only arc sets with fewer than <paramref name="n"/> arcs.</summary>
        /// <param name="n">The size threshold; must be non-negative. <c>0</c> yields the empty family (no set has fewer than zero arcs).</param>
        /// <example><code>DirectedGraphSet small = paths.Smaller(20);</code></example>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="n"/> is negative.</exception>
        public DirectedGraphSet Smaller(int n)
        {
            ThrowIfNegative(n);

            if (n == 0)
            {
                return FilterAlwaysEmpty();
            }

            return Filter(new StructSpecErased<CardinalitySpec, int>(new CardinalitySpec(Graph.EdgeCount, 0, n - 1)));
        }

        /// <summary>Keeps only arc sets with exactly <paramref name="n"/> arcs.</summary>
        /// <param name="n">The required size; must be non-negative.</param>
        /// <example><code>DirectedGraphSet exact = paths.LenEquals(9);</code></example>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="n"/> is negative.</exception>
        public DirectedGraphSet LenEquals(int n)
        {
            ThrowIfNegative(n);
            return Filter(new StructSpecErased<CardinalitySpec, int>(new CardinalitySpec(Graph.EdgeCount, n, n)));
        }

        /// <summary>Keeps only arc sets whose total cost is at most <paramref name="bound"/> (Graphillion's <c>cost_le</c>).</summary>
        /// <param name="cost">Per-arc cost function; may return negatives.</param>
        /// <param name="bound">The maximum total cost.</param>
        /// <example><code>DirectedGraphSet cheap = paths.CostAtMost(e =&gt; 1, 100);</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="cost"/> is <see langword="null"/>.</exception>
        public DirectedGraphSet CostAtMost(Func<DirectedEdge, long> cost, long bound) =>
            Filter(new StructSpecErased<LinearConstraintSpec, long>(new LinearConstraintSpec(BuildWeights(cost), LinearConstraintOperator.LessOrEqual, bound)));

        /// <summary>Keeps only arc sets whose total cost is at least <paramref name="bound"/>.</summary>
        /// <param name="cost">Per-arc cost function; may return negatives.</param>
        /// <param name="bound">The minimum total cost.</param>
        /// <exception cref="ArgumentNullException"><paramref name="cost"/> is <see langword="null"/>.</exception>
        public DirectedGraphSet CostAtLeast(Func<DirectedEdge, long> cost, long bound) =>
            Filter(new StructSpecErased<LinearConstraintSpec, long>(new LinearConstraintSpec(BuildWeights(cost), LinearConstraintOperator.GreaterOrEqual, bound)));

        /// <summary>Keeps only arc sets whose total cost is exactly <paramref name="value"/>.</summary>
        /// <param name="cost">Per-arc cost function; may return negatives.</param>
        /// <param name="value">The required total cost.</param>
        /// <exception cref="ArgumentNullException"><paramref name="cost"/> is <see langword="null"/>.</exception>
        public DirectedGraphSet CostEquals(Func<DirectedEdge, long> cost, long value) =>
            Filter(new StructSpecErased<LinearConstraintSpec, long>(new LinearConstraintSpec(BuildWeights(cost), LinearConstraintOperator.Equal, value)));

        // ==================== Enumeration ====================

        /// <summary>Enumerates the member arc sets lazily, in <see cref="ZddEnumerationOrder.Default"/> order.</summary>
        public IEnumerator<IReadOnlySet<DirectedEdge>> GetEnumerator() => _family.GetEnumerator();

        /// <inheritdoc cref="GetEnumerator"/>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>Lazily enumerates every member arc set in ascending total weight order.</summary>
        /// <param name="weight">Per-arc weight function.</param>
        /// <remarks>
        /// Genuinely lazy: enumerating the first <c>k</c> sets (e.g. via <c>.Take(k)</c>) costs work
        /// proportional to <c>k</c>, not the family's full size &#8212; see <see cref="LazyWeightEnumeration"/>.
        /// </remarks>
        /// <example><code>foreach (var p in paths.MinIter(e =&gt; 1).Take(10)) { /* 10 shortest paths */ }</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="weight"/> is <see langword="null"/>.</exception>
        public IEnumerable<IReadOnlySet<DirectedEdge>> MinIter(Func<DirectedEdge, int> weight)
        {
            ArgumentNullException.ThrowIfNull(weight);
            return IterCore<int, Int32WeightOps>(weight, maximize: false);
        }

        /// <inheritdoc cref="MinIter(Func{DirectedEdge, int})"/>
        public IEnumerable<IReadOnlySet<DirectedEdge>> MinIter(Func<DirectedEdge, long> weight)
        {
            ArgumentNullException.ThrowIfNull(weight);
            return IterCore<long, Int64WeightOps>(weight, maximize: false);
        }

        /// <inheritdoc cref="MinIter(Func{DirectedEdge, int})"/>
        public IEnumerable<IReadOnlySet<DirectedEdge>> MinIter(Func<DirectedEdge, double> weight)
        {
            ArgumentNullException.ThrowIfNull(weight);
            return IterCore<double, DoubleWeightOps>(weight, maximize: false);
        }

        /// <summary>Lazily enumerates every member arc set in descending total weight order. See <see cref="MinIter(Func{DirectedEdge, int})"/>.</summary>
        /// <param name="weight">Per-arc weight function.</param>
        /// <exception cref="ArgumentNullException"><paramref name="weight"/> is <see langword="null"/>.</exception>
        public IEnumerable<IReadOnlySet<DirectedEdge>> MaxIter(Func<DirectedEdge, int> weight)
        {
            ArgumentNullException.ThrowIfNull(weight);
            return IterCore<int, Int32WeightOps>(weight, maximize: true);
        }

        /// <inheritdoc cref="MaxIter(Func{DirectedEdge, int})"/>
        public IEnumerable<IReadOnlySet<DirectedEdge>> MaxIter(Func<DirectedEdge, long> weight)
        {
            ArgumentNullException.ThrowIfNull(weight);
            return IterCore<long, Int64WeightOps>(weight, maximize: true);
        }

        /// <inheritdoc cref="MaxIter(Func{DirectedEdge, int})"/>
        public IEnumerable<IReadOnlySet<DirectedEdge>> MaxIter(Func<DirectedEdge, double> weight)
        {
            ArgumentNullException.ThrowIfNull(weight);
            return IterCore<double, DoubleWeightOps>(weight, maximize: true);
        }

        /// <summary>Lazily and endlessly enumerates member arc sets, each drawn independently and uniformly at random (with replacement).</summary>
        /// <param name="random">Random source; fix a seed for deterministic output.</param>
        /// <remarks>Never completes on its own &#8212; bound it with <c>.Take(n)</c> or a <c>break</c>.</remarks>
        /// <example><code>foreach (var s in paths.RandIter(new Random(1)).Take(5)) { /* ... */ }</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">This family is empty.</exception>
        public IEnumerable<IReadOnlySet<DirectedEdge>> RandIter(Random random)
        {
            ArgumentNullException.ThrowIfNull(random);
            return RandIterCore(random);
        }

        // ==================== Weight optimization ====================

        /// <summary>Returns the maximum-weight member arc set, together with its weight.</summary>
        /// <param name="weight">Per-arc weight function.</param>
        /// <exception cref="ArgumentNullException"><paramref name="weight"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">This family is empty.</exception>
        public (IReadOnlySet<DirectedEdge> Set, int Weight) MaxWeight(Func<DirectedEdge, int> weight) => Wrap(_family.Zdd.MaxWeight(BuildWeights(weight)));

        /// <inheritdoc cref="MaxWeight(Func{DirectedEdge, int})"/>
        public (IReadOnlySet<DirectedEdge> Set, long Weight) MaxWeight(Func<DirectedEdge, long> weight) => Wrap(_family.Zdd.MaxWeight(BuildWeights(weight)));

        /// <inheritdoc cref="MaxWeight(Func{DirectedEdge, int})"/>
        public (IReadOnlySet<DirectedEdge> Set, double Weight) MaxWeight(Func<DirectedEdge, double> weight) => Wrap(_family.Zdd.MaxWeight(BuildWeights(weight)));

        /// <summary>Returns the minimum-weight member arc set, together with its weight.</summary>
        /// <param name="weight">Per-arc weight function.</param>
        /// <example><code>var shortest = paths.MinWeight(e =&gt; 1);</code></example>
        /// <exception cref="ArgumentNullException"><paramref name="weight"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">This family is empty.</exception>
        public (IReadOnlySet<DirectedEdge> Set, int Weight) MinWeight(Func<DirectedEdge, int> weight) => Wrap(_family.Zdd.MinWeight(BuildWeights(weight)));

        /// <inheritdoc cref="MinWeight(Func{DirectedEdge, int})"/>
        public (IReadOnlySet<DirectedEdge> Set, long Weight) MinWeight(Func<DirectedEdge, long> weight) => Wrap(_family.Zdd.MinWeight(BuildWeights(weight)));

        /// <inheritdoc cref="MinWeight(Func{DirectedEdge, int})"/>
        public (IReadOnlySet<DirectedEdge> Set, double Weight) MinWeight(Func<DirectedEdge, double> weight) => Wrap(_family.Zdd.MinWeight(BuildWeights(weight)));

        /// <summary>Returns the <paramref name="k"/> highest-weight member arc sets, sorted by descending weight.</summary>
        /// <param name="weight">Per-arc weight function.</param>
        /// <param name="k">Number of sets to return; 0 or more.</param>
        /// <exception cref="ArgumentNullException"><paramref name="weight"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="k"/> is negative.</exception>
        public (IReadOnlySet<DirectedEdge> Set, int Weight)[] TopK(Func<DirectedEdge, int> weight, int k) => Wrap(_family.Zdd.TopK(BuildWeights(weight), k));

        /// <inheritdoc cref="TopK(Func{DirectedEdge, int}, int)"/>
        public (IReadOnlySet<DirectedEdge> Set, long Weight)[] TopK(Func<DirectedEdge, long> weight, int k) => Wrap(_family.Zdd.TopK(BuildWeights(weight), k));

        /// <inheritdoc cref="TopK(Func{DirectedEdge, int}, int)"/>
        public (IReadOnlySet<DirectedEdge> Set, double Weight)[] TopK(Func<DirectedEdge, double> weight, int k) => Wrap(_family.Zdd.TopK(BuildWeights(weight), k));

        /// <summary>Returns the probability that a set formed by independently including each arc with its given probability belongs to this family.</summary>
        /// <param name="probability">Per-arc inclusion probability function, each between 0 and 1.</param>
        /// <exception cref="ArgumentNullException"><paramref name="probability"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="probability"/> returns a value below 0, above 1, or <see cref="double.NaN"/> for some arc.</exception>
        public double Probability(Func<DirectedEdge, double> probability) => _family.Zdd.Probability(BuildWeights(probability));

        // ==================== Sampling, membership, ranking ====================

        /// <summary>Picks one member arc set uniformly at random.</summary>
        /// <param name="random">Random source; fix a seed for deterministic output.</param>
        /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">This family is empty.</exception>
        public IReadOnlySet<DirectedEdge> Sample(Random random) => _family.Sample(random);

        /// <summary>Picks <paramref name="count"/> member arc sets, drawn independently and uniformly at random (with replacement).</summary>
        /// <param name="count">Number of sets to draw; 0 or more.</param>
        /// <param name="random">Random source; fix a seed for deterministic output.</param>
        /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
        /// <exception cref="InvalidOperationException">This family is empty.</exception>
        public IReadOnlySet<DirectedEdge>[] Sample(int count, Random random) => _family.Sample(count, random);

        /// <summary>Returns whether <paramref name="edges"/> belongs to this family.</summary>
        /// <param name="edges">The arc set to check.</param>
        /// <exception cref="ArgumentNullException"><paramref name="edges"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">An arc is not part of <see cref="Graph"/>.</exception>
        public bool Contains(IEnumerable<DirectedEdge> edges) => _family.Contains(edges);

        /// <summary>Returns the <paramref name="index"/>-th (0-based) member arc set in <paramref name="order"/> order (unranking).</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or at least <see cref="Count"/>.</exception>
        public IReadOnlySet<DirectedEdge> ElementAt(BigInteger index, ZddEnumerationOrder order = ZddEnumerationOrder.Default) => _family.ElementAt(index, order);

        /// <summary>Returns the rank of <paramref name="edges"/> in <paramref name="order"/> order (ranking), or -1 if it is not a member.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="edges"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">An arc is not part of <see cref="Graph"/>.</exception>
        public BigInteger IndexOf(IEnumerable<DirectedEdge> edges, ZddEnumerationOrder order = ZddEnumerationOrder.Default) => _family.IndexOf(edges, order);

        // ==================== Equality ====================

        /// <summary>Whether two families are the same set of member arc sets over the same <see cref="Universe"/>.</summary>
        public bool Equals(DirectedGraphSet? other) => other is not null && _family.Equals(other._family);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is DirectedGraphSet other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => _family.GetHashCode();

        /// <summary>Whether two families are the same set of member arc sets over the same <see cref="Universe"/>.</summary>
        public static bool operator ==(DirectedGraphSet? left, DirectedGraphSet? right) => left is null ? right is null : left.Equals(right);

        /// <summary>Whether two families differ, or belong to different universes.</summary>
        public static bool operator !=(DirectedGraphSet? left, DirectedGraphSet? right) => !(left == right);

        /// <inheritdoc/>
        public override string ToString() => $"DirectedGraphSet({_family.Zdd})";

        // ==================== I/O ====================

        /// <summary>
        /// Writes this family as Graphviz DOT source (docs/design/m7-directed-graphs.md &#167;3.6, M7-7),
        /// labeling each level by its arc instead of a bare item index, unless <paramref name="options"/>
        /// already sets <see cref="DotOptions.LevelLabel"/> itself.
        /// </summary>
        /// <param name="options">Extra rendering knobs; every default besides <see cref="DotOptions.LevelLabel"/> is <see cref="Zdd.ToDot(DotOptions)"/>'s own.</param>
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

        /// <summary>
        /// Wraps a <see cref="Zdd"/> built without a frontier walk (M8-1's factories) as a family over a
        /// fresh universe, using <see cref="PrecomputedZddSpec"/> so <see cref="Filter"/> still composes.
        /// </summary>
        private static DirectedGraphSet FromPrecomputed(DirectedGraph graph, SetUniverse<DirectedEdge> universe, Zdd zdd) =>
            new DirectedGraphSet(graph, universe, zdd, new PrecomputedZddSpec(zdd));

        /// <summary>
        /// The instance counterpart of <see cref="FromPrecomputed"/>: wraps a <see cref="Zdd"/> built by
        /// direct algebra over this family's own universe, so a later <see cref="Filter"/> call still
        /// composes correctly.
        /// </summary>
        private DirectedGraphSet WrapPrecomputed(Zdd zdd) => new DirectedGraphSet(Graph, Universe, zdd, new PrecomputedZddSpec(zdd));

        /// <summary>
        /// Applies a binary ZDD operation after checking that both families are expressed over the very
        /// same <see cref="SetUniverse{T}"/> instance (B18: no implicit promotion), pointing a caller who
        /// hit the mismatch at <see cref="ToUniverseOf"/>.
        /// </summary>
        private DirectedGraphSet Combine(DirectedGraphSet other, Func<Zdd, Zdd, Zdd> operation)
        {
            ArgumentNullException.ThrowIfNull(other);

            if (!ReferenceEquals(Universe, other.Universe))
            {
                throw new ArgumentException(
                    "The two DirectedGraphSet instances do not share the same SetUniverse<DirectedEdge>; only families built over the same universe can be combined (B18: no implicit promotion). " +
                    "Every generator builds a fresh universe, so even two families of the very same DirectedGraph have separate ones: " +
                    $"move the right operand onto the left one first with '{nameof(ToUniverseOf)}' (e.g. 'left | right.{nameof(ToUniverseOf)}(left)').",
                    nameof(other));
            }

            return WrapPrecomputed(operation(Zdd, other.Zdd));
        }

        private static DirectedGraphSet Generate<TSpec>(DirectedGraph graph, TSpec spec)
            where TSpec : struct, IArrayDdSpec
        {
            var universe = new SetUniverse<DirectedEdge>(graph.Edges);
            IErasedGraphSpec erased = new ArraySpecErased<TSpec>(spec);
            Zdd zdd = Build(universe.Manager, erased);
            return new DirectedGraphSet(graph, universe, zdd, erased);
        }

        private static Zdd Build(ZddManager manager, IErasedGraphSpec erased) =>
            FrontierBuilder.Build<ErasedGraphDdSpec, object?>(manager, new ErasedGraphDdSpec(erased));

        private DirectedGraphSet Filter(IErasedGraphSpec filterSpec)
        {
            IErasedGraphSpec combined = new AndErasedSpec(_spec, filterSpec);
            Zdd zdd = Build(Universe.Manager, combined);
            return new DirectedGraphSet(Graph, Universe, zdd, combined);
        }

        private DirectedGraphSet FilterEdge(DirectedEdge edge, bool require)
        {
            int edgeIndex = ResolveEdgeIndex(edge);
            return Filter(new ArraySpecErased<DirectedEdgeMembershipSpec>(new DirectedEdgeMembershipSpec(Graph, edgeIndex, require)));
        }

        private DirectedGraphSet FilterVertex(int vertex, bool require)
        {
            if ((uint)vertex >= (uint)Graph.VertexCount)
            {
                throw new ArgumentOutOfRangeException(nameof(vertex), vertex, $"Must be in 0 .. {Graph.VertexCount - 1}.");
            }

            return Filter(new ArraySpecErased<DirectedVertexTouchSpec>(new DirectedVertexTouchSpec(Graph, vertex, require)));
        }

        private DirectedGraphSet FilterAlwaysEmpty() => new DirectedGraphSet(Graph, Universe, Universe.Manager.Empty, AlwaysFalseSpec.Instance);

        private int ResolveEdgeIndex(DirectedEdge edge)
        {
            for (int i = 0; i < Graph.EdgeCount; i++)
            {
                if (Graph.GetEdge(i) == edge)
                {
                    return i;
                }
            }

            throw new ArgumentException($"Arc {edge} is not part of this graph set's graph.", nameof(edge));
        }

        private int[] ResolveEdgeIndices(ReadOnlySpan<DirectedEdge> edges)
        {
            var indices = new int[edges.Length];

            for (int i = 0; i < edges.Length; i++)
            {
                indices[i] = ResolveEdgeIndex(edges[i]);
            }

            return indices;
        }

        private IEnumerable<IReadOnlySet<DirectedEdge>> IterCore<TWeight, TOps>(Func<DirectedEdge, TWeight> weight, bool maximize)
            where TOps : struct, IWeightOps<TWeight>
        {
            TWeight[] weights = BuildWeights(weight);
            Zdd zdd = _family.Zdd;

            foreach (WeightedSet<TWeight> item in LazyWeightEnumeration.Enumerate<TWeight, TOps>(zdd.Owner!, zdd.Id, weights, maximize))
            {
                yield return Universe.ToElementSet(item.Items);
            }
        }

        private IEnumerable<IReadOnlySet<DirectedEdge>> RandIterCore(Random random)
        {
            while (true)
            {
                yield return Sample(random);
            }
        }

        private TWeight[] BuildWeights<TWeight>(Func<DirectedEdge, TWeight> weight)
        {
            ArgumentNullException.ThrowIfNull(weight);

            var weights = new TWeight[Graph.EdgeCount];
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = weight(Graph.GetEdge(i));
            }

            return weights;
        }

        private (IReadOnlySet<DirectedEdge> Set, TWeight Weight) Wrap<TWeight>(WeightedSet<TWeight> result) =>
            (Universe.ToElementSet(result.Items), result.Weight);

        private (IReadOnlySet<DirectedEdge> Set, TWeight Weight)[] Wrap<TWeight>(WeightedSet<TWeight>[] results)
        {
            var mapped = new (IReadOnlySet<DirectedEdge> Set, TWeight Weight)[results.Length];
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
