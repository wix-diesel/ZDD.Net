using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using ZDD.Net.Internal;
using ZDD.Net.Io;

namespace ZDD.Net.Core
{
    /// <summary>
    /// A value-type handle representing a family of sets. Holds a reference to the owning
    /// <see cref="ZddManager"/>, a node ID, and the manager generation it was stamped with
    /// (16 bytes total); the family itself lives in the manager's node table.
    /// </summary>
    /// <example>
    /// <code>
    /// using ZddManager manager = new ZddManager(variableCount: 3);
    ///
    /// // 2^{0,1,2} = {&#8709;, {0}, {1}, {2}, {0,1}, {0,2}, {1,2}, {0,1,2}}
    /// Zdd powerSet = manager.Empty.Complement();
    /// Console.WriteLine(powerSet.Count); // 8
    ///
    /// // Sets that contain item 0.
    /// Zdd containingItem0 = powerSet.OnSet(0);
    /// Console.WriteLine(containingItem0.Count); // 4
    /// </code>
    /// </example>
    /// <remarks>
    /// Since ZDDs are canonical, two families are equal iff their node IDs are equal (within the
    /// same manager) — no traversal needed. <c>default(Zdd)</c> is an invalid handle belonging to
    /// no manager (<see cref="IsDefault"/> is <see langword="true"/>); only equality and
    /// <see cref="GetHashCode"/> work on it without throwing.
    /// <para>
    /// <see cref="ZddManager.Collect()"/> renumbers surviving nodes, which invalidates every
    /// handle not registered in <see cref="ZddManager.RootSet"/> at the time (docs/PLAN.md &#167;4.4).
    /// The stamped generation is what lets a stale handle be detected and rejected with
    /// <see cref="ZddCollectedException"/> instead of silently returning a wrong (or nonexistent)
    /// family after ids have moved.
    /// </para>
    /// </remarks>
    public readonly struct Zdd : IEquatable<Zdd>, IEnumerable<int[]>
    {
        private readonly ZddManager? _manager;
        private readonly int _id;

        /// <summary>
        /// The manager's <see cref="ZddManager.Generation"/> at the time this handle was created.
        /// Compared against the manager's current generation to detect a handle that predates a
        /// <see cref="ZddManager.Collect()"/> call (see <see cref="ZddCollectedException"/>).
        /// Meaningless (and unchecked) for terminal ids, which never move.
        /// </summary>
        private readonly int _generation;

        internal Zdd(ZddManager manager, int id)
        {
            _manager = manager;
            _id = id;
            _generation = manager.Generation;
        }

        /// <summary>The manager that owns this family.</summary>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ZddCollectedException">
        /// This handle predates the manager's last <see cref="ZddManager.Collect()"/> call and was
        /// not kept alive via <see cref="ZddManager.RootSet"/> at that time.
        /// </exception>
        public ZddManager Manager
        {
            get
            {
                EnsureNotDefault();
                _manager!.EnsureOwns(this, nameof(Manager));
                return _manager!;
            }
        }

        /// <summary>Whether this is <c>default(Zdd)</c>, an invalid handle belonging to no manager.</summary>
        public bool IsDefault => _manager is null;

        /// <summary>Whether this family is the empty family &#8709;.</summary>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        public bool IsEmpty
        {
            get
            {
                EnsureNotDefault();
                return _id == NodeTable.Bottom;
            }
        }

        /// <summary>Whether this family is <c>{&#8709;}</c> (contains only the empty set).</summary>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        public bool IsBase
        {
            get
            {
                EnsureNotDefault();
                return _id == NodeTable.Top;
            }
        }

        /// <summary>
        /// The number of non-terminal nodes reachable from this family's root. Terminals are not
        /// counted, so <see cref="ZddManager.Empty"/> and <see cref="ZddManager.Base"/> are both 0.
        /// </summary>
        /// <remarks>Re-traverses the family on every call (unlike <see cref="ZddManager.NodeCount"/>, not cached).</remarks>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public long NodeCount => Manager.CountReachableNodes(_id);

        /// <summary>Returns the items (variables) actually used by this family, in ascending order.</summary>
        /// <returns>Ascending array of item indices; a fresh array each call. Empty for &#8709; and <c>{&#8709;}</c>.</returns>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public int[] Support() => Manager.CollectSupport(_id);

        /// <summary>The exact number of sets in this family (its cardinality).</summary>
        /// <remarks>
        /// Returns <see cref="BigInteger"/> since cardinality can grow exponentially (2^n for n
        /// variables); counting itself only takes one addition per node. Re-traverses on every
        /// call. Use <see cref="CountApprox"/> for a faster approximate value.
        /// </remarks>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public BigInteger Count => this.Evaluate<CardinalityEval, BigInteger>(default);

        /// <summary>The number of sets in this family, approximated as a <see cref="double"/>. Faster than <see cref="Count"/>.</summary>
        /// <remarks>
        /// Exact up to 2^53; beyond that low-order digits round off, and it saturates to
        /// <see cref="double.PositiveInfinity"/> past <see cref="double.MaxValue"/> (never throws).
        /// See <see cref="ApproximateCardinalityEval"/>.
        /// </remarks>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public double CountApprox => this.Evaluate<ApproximateCardinalityEval, double>(default);

        /// <summary>Counts the sets in this family, grouped by set size (number of elements).</summary>
        /// <returns>
        /// Array where index <c>k</c> holds the count of sets of size <c>k</c>; length is the
        /// largest set size in the family plus one (0 for &#8709;, <c>[1]</c> for <c>{&#8709;}</c>).
        /// Sums to <see cref="Count"/>. A fresh array each call.
        /// </returns>
        /// <remarks>Costs <c>O(node count &#215; max size)</c>; use <see cref="Count"/> if only the total is needed.</remarks>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public BigInteger[] CountBySize() => this.Evaluate<SizeDistributionEval, BigInteger[]>(default);

        /// <summary>Union <c>F &#8746; G</c>: sets belonging to either family.</summary>
        /// <param name="g">The other family; must belong to the same manager.</param>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="g"/> belongs to a different manager, or is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public Zdd Union(Zdd g) => Manager.Union(this, g);

        /// <summary>Intersection <c>F &#8745; G</c>: sets belonging to both families.</summary>
        /// <param name="g">The other family; must belong to the same manager.</param>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="g"/> belongs to a different manager, or is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public Zdd Intersect(Zdd g) => Manager.Intersect(this, g);

        /// <summary>Difference <c>F &#8726; G</c>: sets in this family that are not in <paramref name="g"/>.</summary>
        /// <param name="g">The other family; must belong to the same manager.</param>
        /// <remarks>This is a difference of families, not a per-set difference.</remarks>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="g"/> belongs to a different manager, or is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public Zdd Difference(Zdd g) => Manager.Difference(this, g);

        /// <summary>Symmetric difference <c>F &#9651; G</c>: sets belonging to exactly one family.</summary>
        /// <param name="g">The other family; must belong to the same manager.</param>
        /// <remarks>Equivalent to <c>(F &#8746; G) &#8726; (F &#8745; G)</c>, computed in a single pass.</remarks>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="g"/> belongs to a different manager, or is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public Zdd SymmetricDifference(Zdd g) => Manager.SymmetricDifference(this, g);

        /// <summary>
        /// Product <c>F * G</c>: the join <c>{ a &#8746; b : a &#8712; F, b &#8712; G }</c> —
        /// one set from each family, unioned together.
        /// </summary>
        /// <param name="g">The other family; must belong to the same manager.</param>
        /// <returns>At most <c>|F| &#215; |G|</c> sets, since equal unions collapse.</returns>
        /// <remarks>
        /// <c>F * {&#8709;} == F</c> and <c>F * &#8709; == &#8709;</c>. Commutative, associative,
        /// and distributes over <see cref="Union"/>.
        /// </remarks>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="g"/> belongs to a different manager, or is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public Zdd Product(Zdd g) => Manager.Product(this, g);

        /// <summary>
        /// Quotient <c>F / G</c>: sets <c>a</c> such that for every <c>b &#8712; G</c>,
        /// <c>a &#8745; b = &#8709;</c> and <c>a &#8746; b &#8712; F</c>.
        /// </summary>
        /// <param name="g">The family to divide by; must belong to the same manager.</param>
        /// <returns>What remains of <c>F</c> after factoring out <c>G</c>; <c>F / G * G</c> is a subfamily of <c>F</c>.</returns>
        /// <remarks>
        /// <c>F / {&#8709;} == F</c>. <c>F / &#8709;</c> is the full power set 2^U (vacuous
        /// universal quantifier), which keeps <c>F == F / G * G + F % G</c> true in that case too.
        /// <c>&#8709; / G == &#8709;</c>; <c>F / F == {&#8709;}</c> for non-empty <c>F</c>.
        /// </remarks>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="g"/> belongs to a different manager, or is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public Zdd Quotient(Zdd g) => Manager.Quotient(this, g);

        /// <summary>Remainder <c>F % G</c>: <c>F &#8726; (G * (F / G))</c> — sets that could not be factored out by <c>G</c>.</summary>
        /// <param name="g">The family to divide by; must belong to the same manager.</param>
        /// <returns>The family satisfying <c>F == F / G * G + F % G</c>.</returns>
        /// <remarks><c>F % {&#8709;} == &#8709;</c>; <c>F % &#8709; == F</c>.</remarks>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="g"/> belongs to a different manager, or is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public Zdd Remainder(Zdd g) => Manager.Remainder(this, g);

        /// <summary>
        /// Meet <c>F &#8851; G</c>: <c>{ a &#8745; b : a &#8712; F, b &#8712; G }</c> —
        /// one set from each family, intersected together.
        /// </summary>
        /// <param name="g">The other family; must belong to the same manager.</param>
        /// <remarks>
        /// Like <see cref="Product"/> but collecting intersections instead of unions. Commutative,
        /// associative, distributes over <see cref="Union"/>. <c>F &#8851; &#8709; == &#8709;</c>;
        /// <c>F &#8851; {&#8709;} == {&#8709;}</c>; <c>F &#8851; F</c> is not generally <c>F</c>.
        /// </remarks>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="g"/> belongs to a different manager, or is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public Zdd Meet(Zdd g) => Manager.Meet(this, g);

        /// <summary>Keeps only sets that contain (are a superset of) some set in <paramref name="g"/>.</summary>
        /// <param name="g">The family giving the condition; must belong to the same manager.</param>
        /// <remarks>
        /// Same operation as <see cref="Restrict"/> (SAPPOROBDD naming); both names are provided.
        /// <c>F.SupersetsOf(Base) == F</c>; <c>F.SupersetsOf(&#8709;) == &#8709;</c>.
        /// </remarks>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="g"/> belongs to a different manager, or is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public Zdd SupersetsOf(Zdd g) => Manager.SupersetsOf(this, g);

        /// <summary>Alias for <see cref="SupersetsOf"/> (SAPPOROBDD naming). Same operation.</summary>
        /// <param name="g">The family giving the condition; must belong to the same manager.</param>
        public Zdd Restrict(Zdd g) => Manager.SupersetsOf(this, g);

        /// <summary>Keeps only sets that are contained in (are a subset of) some set in <paramref name="g"/>.</summary>
        /// <param name="g">The family giving the condition; must belong to the same manager.</param>
        /// <remarks>
        /// Same operation as <see cref="Permit"/> (SAPPOROBDD naming); both names are provided.
        /// <c>F.SubsetsOf(&#8709;) == &#8709;</c>; <c>F.SubsetsOf(Base)</c> is <c>{&#8709;}</c> if
        /// <c>F</c> contains &#8709;, else &#8709;.
        /// </remarks>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="g"/> belongs to a different manager, or is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public Zdd SubsetsOf(Zdd g) => Manager.SubsetsOf(this, g);

        /// <summary>Alias for <see cref="SubsetsOf"/> (SAPPOROBDD naming). Same operation.</summary>
        /// <param name="g">The family giving the condition; must belong to the same manager.</param>
        public Zdd Permit(Zdd g) => Manager.SubsetsOf(this, g);

        /// <summary>Keeps only sets that are not a subset of any set in <paramref name="g"/>.</summary>
        /// <param name="g">The family giving the condition; must belong to the same manager.</param>
        /// <returns>The negation of <see cref="SubsetsOf"/>: <c>F.NonSubsetsOf(G) == F - F.SubsetsOf(G)</c>, computed in one pass.</returns>
        /// <remarks><c>F.NonSubsetsOf(&#8709;) == F</c>; <c>F.NonSubsetsOf(F) == &#8709;</c>.</remarks>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="g"/> belongs to a different manager, or is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public Zdd NonSubsetsOf(Zdd g) => Manager.NonSubsetsOf(this, g);

        /// <summary>Keeps only sets that are not a superset of any set in <paramref name="g"/>.</summary>
        /// <param name="g">The family giving the condition; must belong to the same manager.</param>
        /// <returns>The negation of <see cref="SupersetsOf"/>: <c>F.NonSupersetsOf(G) == F - F.SupersetsOf(G)</c>.</returns>
        /// <remarks><c>F.NonSupersetsOf(&#8709;) == F</c>; <c>F.NonSupersetsOf(Base) == &#8709;</c>.</remarks>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="g"/> belongs to a different manager, or is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public Zdd NonSupersetsOf(Zdd g) => Manager.NonSupersetsOf(this, g);

        /// <summary>Toggles membership of <paramref name="item"/> in every set of this family.</summary>
        /// <param name="item">Item index, between 0 and <see cref="ZddManager.VariableCount"/> (exclusive).</param>
        /// <returns>
        /// <c>{ s &#9651; {item} : s &#8712; this }</c>. The set count is unchanged, and applying
        /// <see cref="Change"/> twice with the same item restores the original family.
        /// </returns>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="item"/> is out of range.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public Zdd Change(int item) => Manager.Change(this, item);

        /// <summary>Keeps only sets containing <paramref name="item"/>, then removes it from each (Minato's <c>Subset1</c>).</summary>
        /// <param name="item">Item index, between 0 and <see cref="ZddManager.VariableCount"/> (exclusive).</param>
        /// <returns>
        /// <c>{ s &#8726; {item} : s &#8712; this, item &#8712; s }</c>. Paired with <see cref="OffSet"/>:
        /// <c>OffSet(i)</c> and <c>OnSet(i).Change(i)</c> partition the original family.
        /// </returns>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="item"/> is out of range.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public Zdd OnSet(int item) => Manager.OnSet(this, item);

        /// <summary>Alias for <see cref="OnSet"/> (Minato's naming).</summary>
        /// <param name="item">Item index, between 0 and <see cref="ZddManager.VariableCount"/> (exclusive).</param>
        public Zdd Subset1(int item) => Manager.OnSet(this, item);

        /// <summary>Keeps only sets that do not contain <paramref name="item"/> (Minato's <c>Subset0</c>).</summary>
        /// <param name="item">Item index, between 0 and <see cref="ZddManager.VariableCount"/> (exclusive).</param>
        /// <returns><c>{ s : s &#8712; this, item &#8713; s }</c>.</returns>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="item"/> is out of range.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public Zdd OffSet(int item) => Manager.OffSet(this, item);

        /// <summary>Alias for <see cref="OffSet"/> (Minato's naming).</summary>
        /// <param name="item">Item index, between 0 and <see cref="ZddManager.VariableCount"/> (exclusive).</param>
        public Zdd Subset0(int item) => Manager.OffSet(this, item);

        /// <summary>Toggles membership of each item in <paramref name="items"/> across every set (a batched <see cref="Change"/>).</summary>
        /// <param name="items">Item indices to toggle, each between 0 and <see cref="ZddManager.VariableCount"/> (exclusive). Empty leaves the family unchanged.</param>
        /// <returns>
        /// <c>{ s &#9651; items : s &#8712; this }</c>. Set count is unchanged; an item listed
        /// twice cancels out and stays as-is.
        /// </returns>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="items"/> contains an out-of-range item (nothing is toggled).</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public Zdd Flip(params ReadOnlySpan<int> items) => Manager.Flip(this, items);

        /// <summary>Keeps only the sets that are maximal under inclusion (<c>{ a &#8712; F : no b &#8712; F has a &#8842; b }</c>).</summary>
        /// <returns>A subfamily of <c>F</c> that is always an antichain, so <c>F.Maximal().Maximal() == F.Maximal()</c>.</returns>
        /// <remarks><c>&#8709;.Maximal() == &#8709;</c>; <c>{&#8709;}.Maximal() == {&#8709;}</c>.</remarks>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public Zdd Maximal() => Manager.Maximal(this);

        /// <summary>Keeps only the sets that are minimal under inclusion (<c>{ a &#8712; F : no b &#8712; F has b &#8842; a }</c>).</summary>
        /// <returns>A subfamily of <c>F</c> that is always an antichain, so <c>F.Minimal().Minimal() == F.Minimal()</c>.</returns>
        /// <remarks>
        /// Common way to drop redundant solutions (e.g. minimal cuts, minimal vertex covers).
        /// <c>&#8709;.Minimal() == &#8709;</c>; <c>{&#8709;}.Minimal() == {&#8709;}</c>; if <c>F</c>
        /// contains &#8709;, <c>F.Minimal() == {&#8709;}</c>.
        /// </remarks>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public Zdd Minimal() => Manager.Minimal(this);

        /// <summary>
        /// Returns the hitting-set family (blocking sets / transversal hypergraph):
        /// all sets that intersect every set in this family (<c>{ a &#8838; U : &#8704; b &#8712; F, a &#8745; b &#8800; &#8709; }</c>).
        /// </summary>
        /// <returns>The universe <c>U</c> is all of the manager's variables (<see cref="ZddManager.VariableCount"/>), not just <see cref="Support"/>.</returns>
        /// <remarks>
        /// Includes every superset of a valid hitting set, so it's upward-closed; use
        /// <c>HittingSets().Minimal()</c> for minimal ones. The result can be exponentially larger
        /// than the input. <c>&#8709;.HittingSets() == 2^U</c> (vacuously true); any family
        /// containing &#8709; produces <c>&#8709;.HittingSets() == &#8709;</c>.
        /// </remarks>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public Zdd HittingSets() => Manager.HittingSets(this);

        /// <summary>Alias for <see cref="HittingSets"/> (blocking sets). Same operation.</summary>
        public Zdd Blocking() => Manager.HittingSets(this);

        /// <summary>Complement <c>2^U &#8726; F</c>: subsets of the universe <c>U</c> not in this family.</summary>
        /// <returns>The universe <c>U</c> is all of the manager's variables (<see cref="ZddManager.VariableCount"/>), not just <see cref="Support"/>.</returns>
        /// <remarks>
        /// A complement of the family, not per-set (each set is not replaced by <c>U &#8726; s</c>).
        /// <c>~~F == F</c>; <c>~&#8709; == 2^U</c>; <c>~2^U == &#8709;</c>; De Morgan's laws hold with
        /// <see cref="Union"/>/<see cref="Intersect"/>.
        /// </remarks>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public Zdd Complement() => Manager.Complement(this);

        /// <summary>Starts a lazy enumeration of this family's sets in <see cref="ZddEnumerationOrder.Default"/> order.</summary>
        /// <returns>Enumerator yielding each set as an ascending <c>int[]</c> of item indices, a fresh array per set.</returns>
        /// <remarks>
        /// Unlike <see cref="Count"/> (proportional to node count), enumeration cost is
        /// proportional to the number of sets returned — hence lazy, so <c>break</c> or
        /// <c>Take(n)</c> bounds the work regardless of family size.
        /// <see cref="System.Collections.Generic.ICollection{T}"/> is intentionally not implemented,
        /// since set counts don't fit in <c>int</c>; use <see cref="Count"/> for that.
        /// </remarks>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public IEnumerator<int[]> GetEnumerator() => Sets().GetEnumerator();

        /// <inheritdoc cref="GetEnumerator"/>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>Creates a lazy enumeration of this family's sets in the given order.</summary>
        /// <param name="order">Order to yield sets in. Defaults to <see cref="ZddEnumerationOrder.Default"/>.</param>
        /// <returns>A lazy enumeration yielding sets in <paramref name="order"/>.</returns>
        /// <remarks>
        /// Each yielded array is freshly allocated, so collecting results (e.g. via <c>ToList()</c>)
        /// is safe. Nothing is traversed until enumeration proceeds; re-enumerating re-traverses
        /// and yields the same order (the family is immutable). Cost per set is proportional to
        /// its size plus the 0-branches walked.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="order"/> is not a defined value.</exception>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public IEnumerable<int[]> Sets(ZddEnumerationOrder order = ZddEnumerationOrder.Default) =>
            SetEnumeration.Enumerate(Manager, _id, order);

        /// <summary>Returns whether the set represented by <paramref name="set"/> belongs to this family.</summary>
        /// <param name="set">Item indices of the set to check, any order, duplicates ignored. Empty asks whether the family contains the empty set.</param>
        /// <remarks>
        /// Walks a single root-to-terminal path without building any family, so O(variable count)
        /// (plus O(k log k) to sort <paramref name="set"/>). Consistent with <see cref="Sets"/>:
        /// any set it yields returns <see langword="true"/> here.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="set"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="set"/> contains a value outside 0 to <see cref="ZddManager.VariableCount"/> (exclusive).</exception>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public bool Contains(IEnumerable<int> set)
        {
            ThrowHelper.ThrowIfNull(set, nameof(set));

            int[] items = set as int[] ?? new List<int>(set).ToArray();
            return Manager.Contains(this, items);
        }

        /// <inheritdoc cref="Contains(IEnumerable{int})"/>
        /// <param name="items">Item indices of the set to check, any order, duplicates ignored. Empty asks whether the family contains the empty set.</param>
        public bool Contains(params ReadOnlySpan<int> items) => Manager.Contains(this, items);

        /// <summary>Returns the <paramref name="index"/>-th (0-based) set in this family (unranking).</summary>
        /// <param name="index">Rank of the set to retrieve, between 0 and <see cref="Count"/> (exclusive); a <see cref="BigInteger"/> since some families exceed <c>long</c>.</param>
        /// <param name="order">Ranking order. Defaults to <see cref="ZddEnumerationOrder.Default"/>.</param>
        /// <returns>Ascending array of item indices; a fresh array each call.</returns>
        /// <remarks>
        /// Precomputes per-node subfamily counts, then walks a single root-to-terminal path — no
        /// need to enumerate the first k sets. Cost is one cardinality pass (like <see cref="Count"/>)
        /// plus O(variable count). For repeated lookups, prefer <see cref="Sample(int, Random)"/>
        /// or similar batched APIs that build the count table once. Order matches <see cref="Sets"/>
        /// for the same <paramref name="order"/>.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or at least <see cref="Count"/>; or <paramref name="order"/> is not a defined value.</exception>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public int[] ElementAt(BigInteger index, ZddEnumerationOrder order = ZddEnumerationOrder.Default) =>
            Manager.ElementAt(this, index, order);

        /// <summary>Returns the rank of the set represented by <paramref name="set"/> (ranking), or <c>-1</c> if it is not in the family.</summary>
        /// <param name="set">Item indices of the set to check, any order, duplicates ignored. Empty asks for the rank of the empty set.</param>
        /// <param name="order">Ranking order. Defaults to <see cref="ZddEnumerationOrder.Default"/>.</param>
        /// <returns>
        /// Rank between 0 and <see cref="Count"/> (exclusive); <c>-1</c> if the set is not in the
        /// family (never throws, following <see cref="System.Collections.IList.IndexOf"/> convention).
        /// </returns>
        /// <remarks>
        /// The inverse of <see cref="ElementAt"/>: <c>IndexOf(ElementAt(k)) == k</c> for all valid
        /// <c>k</c>, and vice versa for member sets given the same <paramref name="order"/>. Same
        /// cost as <see cref="ElementAt"/>.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="set"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="set"/> contains an out-of-range value, or <paramref name="order"/> is not a defined value.</exception>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public BigInteger IndexOf(IEnumerable<int> set, ZddEnumerationOrder order = ZddEnumerationOrder.Default)
        {
            ThrowHelper.ThrowIfNull(set, nameof(set));

            int[] items = set as int[] ?? new List<int>(set).ToArray();
            return Manager.IndexOf(this, items, order);
        }

        /// <inheritdoc cref="IndexOf(IEnumerable{int}, ZddEnumerationOrder)"/>
        /// <param name="items">Item indices of the set to check, any order, duplicates ignored. Empty asks for the rank of the empty set.</param>
        /// <remarks>
        /// Order is fixed to <see cref="ZddEnumerationOrder.Default"/> since <c>params</c> must be
        /// the last parameter; use <see cref="IndexOf(IEnumerable{int}, ZddEnumerationOrder)"/> to choose an order.
        /// </remarks>
        public BigInteger IndexOf(params ReadOnlySpan<int> items) =>
            Manager.IndexOf(this, items, ZddEnumerationOrder.Default);

        /// <summary>Picks one set from this family uniformly at random.</summary>
        /// <param name="random">Random source; fix a seed for deterministic output.</param>
        /// <returns>Ascending array of item indices, with every set in the family equally likely.</returns>
        /// <remarks>
        /// Implemented by feeding a uniform random rank to <see cref="ElementAt"/>, without ever
        /// enumerating the family. True uniformity comes from rejection sampling over the
        /// <see cref="BigInteger"/> rank range (a naive modulo would bias unless the range divides
        /// the RNG's period).
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>, or this family is empty.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public int[] Sample(Random random) => Manager.Sample(this, random);

        /// <summary>Picks <paramref name="count"/> sets from this family uniformly at random.</summary>
        /// <param name="count">Number of sets to draw; 0 or more.</param>
        /// <param name="random">Random source; fix a seed for deterministic output.</param>
        /// <returns><paramref name="count"/> sets, drawn independently with replacement (duplicates possible).</returns>
        /// <remarks>
        /// Same distribution as calling <see cref="Sample(Random)"/> <paramref name="count"/>
        /// times, but faster since the cardinality table is built once and reused.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>, or this family is empty (even when <paramref name="count"/> is 0).</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public int[][] Sample(int count, Random random) => Manager.Sample(this, count, random);

        /// <summary>Returns the maximum-weight set in this family, together with its weight.</summary>
        /// <typeparam name="TWeight">The weight type.</typeparam>
        /// <typeparam name="TOps">Weight operations (<see cref="IWeightOps{TWeight}"/> implementation); must be a <c>struct</c>.</typeparam>
        /// <param name="weights">Per-item weights; length must equal <see cref="ZddManager.VariableCount"/>.</param>
        /// <remarks>
        /// Computed as a longest-path bottom-up DP over nodes rather than by enumerating sets, so
        /// cost is proportional to node count regardless of family size. Negative weights are
        /// fine (ZDDs are acyclic). Ties break toward the set that comes first under
        /// <see cref="ZddEnumerationOrder.Default"/>. Built-in weight types:
        /// <see cref="Int32WeightOps"/>, <see cref="Int64WeightOps"/>, <see cref="DoubleWeightOps"/>,
        /// <see cref="BigIntegerWeightOps"/>; <c>int</c>/<c>long</c>/<c>double</c> have shorthand overloads.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="weights"/>'s length differs from <see cref="ZddManager.VariableCount"/>.</exception>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>, or this family is empty.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public WeightedSet<TWeight> MaxWeight<TWeight, TOps>(params ReadOnlySpan<TWeight> weights)
            where TOps : struct, IWeightOps<TWeight> =>
            Manager.MaxWeight<TWeight, TOps>(this, weights);

        /// <inheritdoc cref="MaxWeight{TWeight, TOps}(ReadOnlySpan{TWeight})"/>
        public WeightedSet<int> MaxWeight(params ReadOnlySpan<int> weights) =>
            Manager.MaxWeight<int, Int32WeightOps>(this, weights);

        /// <inheritdoc cref="MaxWeight{TWeight, TOps}(ReadOnlySpan{TWeight})"/>
        public WeightedSet<long> MaxWeight(params ReadOnlySpan<long> weights) =>
            Manager.MaxWeight<long, Int64WeightOps>(this, weights);

        /// <inheritdoc cref="MaxWeight{TWeight, TOps}(ReadOnlySpan{TWeight})"/>
        public WeightedSet<double> MaxWeight(params ReadOnlySpan<double> weights) =>
            Manager.MaxWeight<double, DoubleWeightOps>(this, weights);

        /// <summary>Returns the minimum-weight set in this family, together with its weight.</summary>
        /// <typeparam name="TWeight">The weight type.</typeparam>
        /// <typeparam name="TOps">Weight operations (<see cref="IWeightOps{TWeight}"/> implementation); must be a <c>struct</c>.</typeparam>
        /// <param name="weights">Per-item weights; length must equal <see cref="ZddManager.VariableCount"/>.</param>
        /// <remarks>
        /// The shortest-path counterpart of <see cref="MaxWeight{TWeight, TOps}(ReadOnlySpan{TWeight})"/>,
        /// same cost. Works even for weight types without a sign (not just negated max-weight).
        /// Ties break toward the set that comes first under the default enumeration order.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="weights"/>'s length differs from <see cref="ZddManager.VariableCount"/>.</exception>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>, or this family is empty.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public WeightedSet<TWeight> MinWeight<TWeight, TOps>(params ReadOnlySpan<TWeight> weights)
            where TOps : struct, IWeightOps<TWeight> =>
            Manager.MinWeight<TWeight, TOps>(this, weights);

        /// <inheritdoc cref="MinWeight{TWeight, TOps}(ReadOnlySpan{TWeight})"/>
        public WeightedSet<int> MinWeight(params ReadOnlySpan<int> weights) =>
            Manager.MinWeight<int, Int32WeightOps>(this, weights);

        /// <inheritdoc cref="MinWeight{TWeight, TOps}(ReadOnlySpan{TWeight})"/>
        public WeightedSet<long> MinWeight(params ReadOnlySpan<long> weights) =>
            Manager.MinWeight<long, Int64WeightOps>(this, weights);

        /// <inheritdoc cref="MinWeight{TWeight, TOps}(ReadOnlySpan{TWeight})"/>
        public WeightedSet<double> MinWeight(params ReadOnlySpan<double> weights) =>
            Manager.MinWeight<double, DoubleWeightOps>(this, weights);

        /// <summary>Returns the <paramref name="k"/> highest-weight sets in this family, sorted by descending weight.</summary>
        /// <typeparam name="TWeight">The weight type.</typeparam>
        /// <typeparam name="TOps">Weight operations (<see cref="IWeightOps{TWeight}"/> implementation); must be a <c>struct</c>.</typeparam>
        /// <param name="weights">Per-item weights; length must equal <see cref="ZddManager.VariableCount"/>.</param>
        /// <param name="k">Number of sets to return; 0 or more.</param>
        /// <returns>Up to <paramref name="k"/> sets in descending weight order (fewer if the family has fewer than <paramref name="k"/> sets).</returns>
        /// <remarks>
        /// Cost grows with <paramref name="k"/>: time <c>O(m &#183; k + k &#183; n)</c>, memory
        /// <c>O(m &#183; k)</c> for <c>m</c> reachable nodes and <c>n</c> variables, since each node
        /// keeps a top-k table. Best suited for small <paramref name="k"/>. When weights tie, which
        /// set lands at which rank is unspecified, but the returned weights always match a full
        /// sort's top <paramref name="k"/>.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="weights"/>'s length differs from <see cref="ZddManager.VariableCount"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="k"/> is negative.</exception>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public WeightedSet<TWeight>[] TopK<TWeight, TOps>(ReadOnlySpan<TWeight> weights, int k)
            where TOps : struct, IWeightOps<TWeight> =>
            Manager.TopK<TWeight, TOps>(this, weights, k);

        /// <inheritdoc cref="TopK{TWeight, TOps}(ReadOnlySpan{TWeight}, int)"/>
        public WeightedSet<int>[] TopK(ReadOnlySpan<int> weights, int k) =>
            Manager.TopK<int, Int32WeightOps>(this, weights, k);

        /// <inheritdoc cref="TopK{TWeight, TOps}(ReadOnlySpan{TWeight}, int)"/>
        public WeightedSet<long>[] TopK(ReadOnlySpan<long> weights, int k) =>
            Manager.TopK<long, Int64WeightOps>(this, weights, k);

        /// <inheritdoc cref="TopK{TWeight, TOps}(ReadOnlySpan{TWeight}, int)"/>
        public WeightedSet<double>[] TopK(ReadOnlySpan<double> weights, int k) =>
            Manager.TopK<double, DoubleWeightOps>(this, weights, k);

        /// <summary>
        /// Returns the probability that a set formed by independently including each item with
        /// probability <paramref name="probabilities"/> belongs to this family.
        /// </summary>
        /// <param name="probabilities">Per-item probabilities; length must equal <see cref="ZddManager.VariableCount"/>, each between 0 and 1.</param>
        /// <returns><c>&#931;<sub>A&#8712;F</sub> &#928;<sub>i&#8712;A</sub> p[i] &#183; &#928;<sub>i&#8713;A</sub> (1 - p[i])</c>, between 0 and 1.</returns>
        /// <remarks>
        /// Directly expresses network reliability (e.g. the probability that a random edge subset
        /// keeps s and t connected), avoiding a 2^n enumeration. The universe is the manager's
        /// full variable set, not just <see cref="Support"/>, so items unused by the family still
        /// contribute their <c>(1 - p[i])</c> factor. Boundary cases: &#8709; gives 0, <c>{&#8709;}</c>
        /// gives <c>&#928;(1 - p[i])</c>, and 2^U gives 1.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="probabilities"/>'s length differs from <see cref="ZddManager.VariableCount"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="probabilities"/> contains a value below 0, above 1, or <see cref="double.NaN"/>.</exception>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public double Probability(params ReadOnlySpan<double> probabilities) =>
            Manager.Probability(this, probabilities);

        /// <summary>Returns the expected weight of a set drawn uniformly at random from this family.</summary>
        /// <param name="weights">Per-item weights; length must equal <see cref="ZddManager.VariableCount"/>.</param>
        /// <returns><c>(&#931;<sub>A&#8712;F</sub> &#931;<sub>i&#8712;A</sub> w[i]) / |F|</c>.</returns>
        /// <remarks>
        /// Distribution is uniform over the family (as in <see cref="Sample(Random)"/>), distinct
        /// from <see cref="Probability"/>'s independent-item model — values do not coincide. By
        /// linearity of expectation this equals <c>&#931;<sub>i</sub> w[i] &#183; ItemFrequency()[i]</c>,
        /// which is how it's computed; cost matches <see cref="ItemFrequency"/>.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="weights"/>'s length differs from <see cref="ZddManager.VariableCount"/>.</exception>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>, or this family is empty.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public double ExpectedValue(params ReadOnlySpan<double> weights) =>
            Manager.ExpectedValue(this, weights);

        /// <summary>Returns, for each item, the probability it appears in a set drawn uniformly at random from this family.</summary>
        /// <returns>
        /// Array of length <see cref="ZddManager.VariableCount"/>; index <c>i</c> is the count of
        /// sets containing item <c>i</c> divided by the family's cardinality. A fresh array each call.
        /// </returns>
        /// <remarks>
        /// Returns the exact probability (via a <see cref="BigInteger"/> count internally, then
        /// one division) in time proportional to node count, rather than approximating via repeated
        /// <see cref="Sample(int, Random)"/> calls. Items outside <see cref="Support"/> get probability 0.
        /// </remarks>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>, or this family is empty.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public double[] ItemFrequency() => Manager.ItemFrequency(this);

        /// <summary>Returns whether every set in this family also belongs to <paramref name="g"/> (family inclusion <c>F &#8838; G</c>).</summary>
        /// <param name="g">The other family; must belong to this manager.</param>
        /// <remarks>
        /// Same answer as <c>(F - G).IsEmpty</c> but without building the difference family —
        /// stops at the first counterexample. &#8709; is a subset of any family; <c>F.IsSubsetOf(F)</c> is always true.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="g"/> belongs to a different manager, or is <c>default(Zdd)</c>.</exception>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public bool IsSubsetOf(Zdd g) => Manager.IsSubsetOf(this, g);

        /// <summary>Returns whether this family and <paramref name="g"/> share any common set.</summary>
        /// <param name="g">The other family; must belong to this manager.</param>
        /// <remarks>
        /// Same answer as <c>(F &amp; G) != Empty</c> but without building the intersection family —
        /// stops at the first common set found. False whenever either family is empty.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="g"/> belongs to a different manager, or is <c>default(Zdd)</c>.</exception>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public bool Overlaps(Zdd g) => Manager.Overlaps(this, g);

        /// <summary>Whether two handles refer to the same family in the same manager.</summary>
        /// <param name="other">The handle to compare against.</param>
        /// <remarks>
        /// Also compares <see cref="Generation"/> (terminals excepted, since they never move): a
        /// collection can reassign a stale handle's old id to an unrelated family, so two handles
        /// with equal ids from different generations are not the same family and must not compare
        /// equal.
        /// </remarks>
        public bool Equals(Zdd other) =>
            ReferenceEquals(_manager, other._manager)
            && _id == other._id
            && (NodeTable.IsTerminal(_id) || _generation == other._generation);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Zdd other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            HashCode.Combine(
                _manager is null ? 0 : RuntimeHelpers.GetHashCode(_manager),
                _id,
                NodeTable.IsTerminal(_id) ? 0 : _generation);

        /// <summary>Whether two handles refer to the same family in the same manager.</summary>
        /// <param name="left">Left-hand operand.</param>
        /// <param name="right">Right-hand operand.</param>
        public static bool operator ==(Zdd left, Zdd right) => left.Equals(right);

        /// <summary>Whether two handles refer to different families (or different managers).</summary>
        /// <param name="left">Left-hand operand.</param>
        /// <param name="right">Right-hand operand.</param>
        public static bool operator !=(Zdd left, Zdd right) => !left.Equals(right);

        /// <summary>Union <c>F &#8746; G</c>. Same as <see cref="Union"/>.</summary>
        /// <param name="left">Left-hand operand.</param>
        /// <param name="right">Right-hand operand.</param>
        public static Zdd operator |(Zdd left, Zdd right) => left.Manager.Union(left, right);

        /// <summary>Intersection <c>F &#8745; G</c>. Same as <see cref="Intersect"/>.</summary>
        /// <param name="left">Left-hand operand.</param>
        /// <param name="right">Right-hand operand.</param>
        public static Zdd operator &(Zdd left, Zdd right) => left.Manager.Intersect(left, right);

        /// <summary>Difference <c>F &#8726; G</c>. Same as <see cref="Difference"/>.</summary>
        /// <param name="left">Left-hand operand.</param>
        /// <param name="right">Right-hand operand.</param>
        public static Zdd operator -(Zdd left, Zdd right) => left.Manager.Difference(left, right);

        /// <summary>Symmetric difference <c>F &#9651; G</c>. Same as <see cref="SymmetricDifference"/>.</summary>
        /// <param name="left">Left-hand operand.</param>
        /// <param name="right">Right-hand operand.</param>
        public static Zdd operator ^(Zdd left, Zdd right) => left.Manager.SymmetricDifference(left, right);

        /// <summary>Product <c>F * G</c>. Same as <see cref="Product"/>.</summary>
        /// <param name="left">Left-hand operand.</param>
        /// <param name="right">Right-hand operand.</param>
        public static Zdd operator *(Zdd left, Zdd right) => left.Manager.Product(left, right);

        /// <summary>Quotient <c>F / G</c>. Same as <see cref="Quotient"/>.</summary>
        /// <param name="left">Dividend family.</param>
        /// <param name="right">Divisor family.</param>
        public static Zdd operator /(Zdd left, Zdd right) => left.Manager.Quotient(left, right);

        /// <summary>Remainder <c>F % G</c>. Same as <see cref="Remainder"/>.</summary>
        /// <param name="left">Dividend family.</param>
        /// <param name="right">Divisor family.</param>
        public static Zdd operator %(Zdd left, Zdd right) => left.Manager.Remainder(left, right);

        /// <summary>Complement <c>2^U &#8726; F</c>. Same as <see cref="Complement"/>.</summary>
        /// <param name="operand">The family to complement.</param>
        public static Zdd operator ~(Zdd operand) => operand.Manager.Complement(operand);

        /// <summary>Writes this family as Graphviz DOT source, ready for <c>dot -Tsvg</c>.</summary>
        /// <returns>DOT source, with <c>\n</c> line endings regardless of platform.</returns>
        /// <example><code>File.WriteAllText("family.dot", family.ToDot());</code></example>
        /// <remarks>
        /// 0-branches are dashed, 1-branches solid; terminals ⊥/⊤ are drawn as boxes, and nodes for
        /// the same item share a rank. See <see cref="Io.DotWriter"/> for the full convention. For
        /// large families prefer <see cref="WriteDot(TextWriter)"/>, which streams instead of buffering
        /// the whole string.
        /// </remarks>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public string ToDot() => DotWriter.Write(this);

        /// <summary>
        /// Writes this family as Graphviz DOT source as <see cref="ToDot()"/> does, additionally
        /// applying <paramref name="options"/> (M5-4, issue #56): state labels, level labels,
        /// partial-display cutoffs and styling.
        /// </summary>
        /// <param name="options">Rendering knobs; the same as <see cref="ToDot()"/> when <see langword="null"/>.</param>
        /// <returns>DOT source, with <c>\n</c> line endings regardless of platform.</returns>
        /// <remarks>
        /// Consider <see cref="DotOptions.MaxLevels"/> / <see cref="DotOptions.MaxNodes"/> for a large
        /// family, since Graphviz cannot render one either way.
        /// </remarks>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public string ToDot(DotOptions? options) => DotWriter.Write(this, options);

        /// <summary>Streams this family's DOT representation to <paramref name="writer"/>, avoiding buffering it all in memory.</summary>
        /// <param name="writer">The destination writer.</param>
        /// <remarks>
        /// Same output as <see cref="ToDot()"/>. The list of reachable nodes is still built in
        /// memory once, to group nodes by rank (proportional to node count).
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public void WriteDot(TextWriter writer) => DotWriter.Write(this, writer);

        /// <summary>
        /// Streams this family's DOT representation to <paramref name="writer"/> as
        /// <see cref="WriteDot(TextWriter)"/> does, additionally applying <paramref name="options"/>
        /// (M5-4, issue #56): state labels, level labels, partial-display cutoffs and styling.
        /// </summary>
        /// <param name="writer">The destination writer.</param>
        /// <param name="options">Rendering knobs; the same as <see cref="WriteDot(TextWriter)"/> when <see langword="null"/>.</param>
        /// <remarks>
        /// The list of reachable nodes is still built in memory once, to group nodes by rank
        /// (proportional to node count, or to <see cref="DotOptions.MaxNodes"/> when that is lower).
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">This is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public void WriteDot(TextWriter writer, DotOptions? options) => DotWriter.Write(this, writer, options);

        /// <summary>A short debug representation. Does not expand the family's contents.</summary>
        public override string ToString()
        {
            if (_manager is null)
            {
                return "Zdd(default)";
            }

            return _id switch
            {
                NodeTable.Bottom => "Zdd(empty)",
                NodeTable.Top => "Zdd(base)",
                _ => $"Zdd(#{_id})",
            };
        }

        /// <summary>The owning manager, or <see langword="null"/> for <c>default(Zdd)</c>.</summary>
        internal ZddManager? Owner => _manager;

        /// <summary>This family's root node ID. Meaningless for <c>default(Zdd)</c>.</summary>
        internal int Id => _id;

        /// <summary>The manager generation this handle was stamped with at creation. Meaningless for <c>default(Zdd)</c>.</summary>
        internal int Generation => _generation;

        private void EnsureNotDefault()
        {
            if (_manager is null)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    "This is a default Zdd handle, which does not belong to any manager. Obtain a Zdd from a ZddManager instead.");
            }
        }
    }
}
