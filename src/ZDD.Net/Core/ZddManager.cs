using System;
using System.Collections.Generic;
using System.Numerics;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Owns ZDD creation: holds the node table and unique table that every <see cref="Zdd"/>
    /// handle from this instance belongs to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Callers use 0-based <i>item index</i> (0 .. <see cref="VariableCount"/> - 1); nodes
    /// internally use 1-based <i>level</i> (1 = bottom/leaf side .. <see cref="VariableCount"/> =
    /// top/root side), converted only by <see cref="LevelOf"/> / <see cref="ItemOf"/>
    /// (<c>level = VariableCount - item</c>). This orientation makes depth-first, 0-branch-first
    /// enumeration match lexicographic order on indicator vectors (<see cref="ZddEnumerationOrder.Default"/>).
    /// </para>
    /// <para>Variable count is fixed at construction and cannot grow afterward.</para>
    /// <para>
    /// Nodes are always built through the unique table, so "same family" always implies "same
    /// node id", and <see cref="Zdd"/> equality is just an id comparison.
    /// </para>
    /// <para>
    /// <b>Not thread-safe.</b> A single manager must not be touched from multiple threads at once,
    /// even for reads that traverse the node table (e.g. <see cref="Zdd.NodeCount"/>).
    /// </para>
    /// <para>
    /// <see cref="Dispose"/> drops references to the node table, unique table, and operation cache
    /// (no unmanaged resources, so skipping it just delays GC). After disposal, operations that read
    /// the tables (<see cref="Empty"/>, <see cref="Base"/>, <see cref="Singleton"/>, <see cref="NodeCount"/>,
    /// <see cref="GetStatistics"/>, and <see cref="Zdd.NodeCount"/> / <see cref="Zdd.Support"/> on its
    /// handles) throw <see cref="ObjectDisposedException"/>; others (<see cref="VariableCount"/>,
    /// <see cref="IsDisposed"/>, <see cref="Zdd"/> equality, <see cref="Zdd.IsEmpty"/>, <see cref="Zdd.IsBase"/>)
    /// keep working.
    /// </para>
    /// </remarks>
    public sealed class ZddManager : IDisposable
    {
        /// <summary>Initial depth of the workspace rental pool. Nesting is at most one level deep (product calling union), so this suffices.</summary>
        private const int InitialWorkspaceDepth = 2;

        private readonly int _variableCount;

        /// <summary><see langword="null"/> once disposed; also doubles as the disposed check.</summary>
        private UniqueTable? _table;

        /// <summary>Memo table for operation results; released together with <see cref="_table"/>.</summary>
        private OperationCache? _cache;

        /// <summary>
        /// Root node id of the power set <c>2^U</c>; <see cref="NodeTable.Bottom"/> means not yet computed.
        /// </summary>
        /// <remarks>
        /// Stable for the manager's lifetime, since variable count is fixed and the unique table
        /// always returns the same id for the same family; <see cref="NodeTable.Bottom"/> is safe as
        /// the "uncomputed" sentinel because <c>2^U</c> is never the empty family. Must be reset
        /// alongside <see cref="OperationCache.Clear"/> if a future node-GC pass ever renumbers nodes.
        /// </remarks>
        private int _powerSetRoot;

        /// <summary>
        /// Rental pool of workspaces used by iterative operation implementations, reused across
        /// calls instead of reallocated. Indexed by nesting depth: depth 0 is a normal call,
        /// depth &#8805; 1 is an operation invoked from within another operation's composition
        /// (e.g. product calling union, quotient calling intersect).
        /// </summary>
        /// <remarks>
        /// Slots are never freed after use — the grown array carries over to the next call at that
        /// depth. Without this, an operation that recurses into another operation on every step
        /// (product calls union once per node) would reallocate its workspace per node.
        /// </remarks>
        private OperationWorkspace?[] _workspaces;

        /// <summary>Number of workspaces currently on loan (also the index of the next slot to lend).</summary>
        private int _workspaceDepth;

        /// <summary>Creates a manager for a fixed number of variables.</summary>
        /// <param name="variableCount">
        /// Number of item variables; valid item indices are 0 .. <paramref name="variableCount"/> - 1.
        /// 0 is allowed (only <see cref="Empty"/> and <see cref="Base"/> can then be built).
        /// </param>
        /// <param name="options">Initial-capacity tuning; <see langword="null"/> uses defaults. Read once at construction; later mutation of the passed instance has no effect.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="variableCount"/> is negative.</exception>
        public ZddManager(int variableCount, ZddManagerOptions? options = null)
        {
            ThrowHelper.ThrowIfNegative(variableCount, nameof(variableCount));

            ZddManagerOptions effective = options ?? new ZddManagerOptions();

            _variableCount = variableCount;
            _table = new UniqueTable(
                new NodeTable(NodeTable.FirstNodeId + effective.InitialNodeCapacity),
                effective.InitialUniqueTableCapacity);
            _cache = new OperationCache(effective.InitialCacheCapacity, effective.MaxCacheCapacity);
            _workspaces = new OperationWorkspace?[InitialWorkspaceDepth];
        }

        /// <summary>The number of item variables this manager handles. Fixed after construction.</summary>
        public int VariableCount => _variableCount;

        /// <summary>Total number of non-terminal nodes this manager has allocated, shared across all families (not per-family).</summary>
        /// <exception cref="ObjectDisposedException">This manager has been disposed.</exception>
        public long NodeCount => Table.Count;

        /// <summary>Whether this manager has been <see cref="Dispose"/>d.</summary>
        public bool IsDisposed => _table is null;

        /// <summary>The empty family &#8709; (no sets), corresponding to terminal &#8869;.</summary>
        /// <exception cref="ObjectDisposedException">This manager has been disposed.</exception>
        public Zdd Empty
        {
            get
            {
                EnsureNotDisposed();
                return new Zdd(this, NodeTable.Bottom);
            }
        }

        /// <summary>The family <c>{&#8709;}</c> containing only the empty set, corresponding to terminal &#8868;.</summary>
        /// <exception cref="ObjectDisposedException">This manager has been disposed.</exception>
        public Zdd Base
        {
            get
            {
                EnsureNotDisposed();
                return new Zdd(this, NodeTable.Top);
            }
        }

        /// <summary>Returns the family <c>{{item}}</c> containing only the single-element set.</summary>
        /// <param name="item">Item index in 0 .. <see cref="VariableCount"/> - 1.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="item"/> is out of range.</exception>
        /// <exception cref="ObjectDisposedException">This manager has been disposed.</exception>
        public Zdd Singleton(int item)
        {
            UniqueTable table = Table;
            int level = LevelOf(item);

            // No set omits item, so the 0-branch is bottom; every set that keeps it minus item is empty, so the 1-branch is top.
            return new Zdd(this, table.GetNode(level, NodeTable.Bottom, NodeTable.Top));
        }

        /// <summary>Snapshots the current state of the internal tables (see docs/PLAN.md &#167;4.6).</summary>
        /// <returns>A copy taken at call time; later manager changes don't affect it.</returns>
        /// <remarks>Reads the tables only, in constant time (unlike <see cref="Zdd.NodeCount"/>). Cache counters are cumulative since construction; call twice and diff for a windowed view.</remarks>
        /// <exception cref="ObjectDisposedException">This manager has been disposed.</exception>
        public ZddStatistics GetStatistics()
        {
            UniqueTable table = Table;
            OperationCache cache = Cache;
            NodeTable nodes = table.Nodes;

            return new ZddStatistics(
                nodeCount: nodes.Count,
                peakNodeCount: nodes.PeakCount,
                nodeTableCapacity: nodes.Capacity,
                uniqueTableCapacity: table.Capacity,
                uniqueTableCollisions: table.Collisions,
                cacheCapacity: cache.Capacity,
                maxCacheCapacity: cache.MaxCapacity,
                cacheLookups: cache.Lookups,
                cacheHits: cache.Hits,
                cacheOverwrites: cache.Collisions);
        }

        /// <summary>
        /// Releases references to the node table, unique table, and operation cache. Subsequent
        /// operations on this manager or its <see cref="Zdd"/> handles throw
        /// <see cref="ObjectDisposedException"/>. Calling this more than once is a no-op.
        /// </summary>
        public void Dispose()
        {
            _table = null;
            _cache = null;
            _powerSetRoot = NodeTable.Bottom;
            _workspaces = Array.Empty<OperationWorkspace?>();
            _workspaceDepth = 0;
        }

        /// <summary>Union <c>f &#8746; g</c>: sets belonging to either family.</summary>
        /// <param name="f">The left family; must belong to this manager.</param>
        /// <param name="g">The right family; must belong to this manager.</param>
        internal Zdd Union(in Zdd f, in Zdd g) => ApplyBinary(ZddOperation.Union, f, g);

        /// <summary>Intersection <c>f &#8745; g</c>: sets belonging to both families.</summary>
        /// <param name="f">The left family; must belong to this manager.</param>
        /// <param name="g">The right family; must belong to this manager.</param>
        internal Zdd Intersect(in Zdd f, in Zdd g) => ApplyBinary(ZddOperation.Intersect, f, g);

        /// <summary>Difference <c>f &#8726; g</c>: sets in <paramref name="f"/> that are not in <paramref name="g"/>.</summary>
        /// <param name="f">The left family; must belong to this manager.</param>
        /// <param name="g">The right family; must belong to this manager.</param>
        internal Zdd Difference(in Zdd f, in Zdd g) => ApplyBinary(ZddOperation.Difference, f, g);

        /// <summary>Symmetric difference <c>f &#8710; g</c>: sets belonging to exactly one family.</summary>
        /// <param name="f">The left family; must belong to this manager.</param>
        /// <param name="g">The right family; must belong to this manager.</param>
        internal Zdd SymmetricDifference(in Zdd f, in Zdd g) =>
            ApplyBinary(ZddOperation.SymmetricDifference, f, g);

        /// <summary>Family product <c>f * g</c>: <c>{ a &#8746; b : a &#8712; f, b &#8712; g }</c>.</summary>
        /// <param name="f">The left family; must belong to this manager.</param>
        /// <param name="g">The right family; must belong to this manager.</param>
        internal Zdd Product(in Zdd f, in Zdd g) => ApplyBinary(ZddOperation.Product, f, g);

        /// <summary>Quotient <c>f / g</c>.</summary>
        /// <param name="f">The dividend family; must belong to this manager.</param>
        /// <param name="g">The divisor family; must belong to this manager.</param>
        internal Zdd Quotient(in Zdd f, in Zdd g) => ApplyBinary(ZddOperation.Quotient, f, g);

        /// <summary>Remainder <c>f % g</c>.</summary>
        /// <param name="f">The dividend family; must belong to this manager.</param>
        /// <param name="g">The divisor family; must belong to this manager.</param>
        internal Zdd Remainder(in Zdd f, in Zdd g) => ApplyBinary(ZddOperation.Remainder, f, g);

        /// <summary>Meet <c>f &#8851; g</c>: <c>{ a &#8745; b : a &#8712; f, b &#8712; g }</c>.</summary>
        /// <param name="f">The left family; must belong to this manager.</param>
        /// <param name="g">The right family; must belong to this manager.</param>
        internal Zdd Meet(in Zdd f, in Zdd g) => ApplyBinary(ZddOperation.Meet, f, g);

        /// <summary>Keeps only elements of <paramref name="f"/> that contain some member of <paramref name="g"/>.</summary>
        /// <param name="f">The family being filtered; must belong to this manager.</param>
        /// <param name="g">The family supplying the condition; must belong to this manager.</param>
        internal Zdd SupersetsOf(in Zdd f, in Zdd g) => ApplyBinary(ZddOperation.SupersetsOf, f, g);

        /// <summary>Keeps only elements of <paramref name="f"/> contained in some member of <paramref name="g"/>.</summary>
        /// <param name="f">The family being filtered; must belong to this manager.</param>
        /// <param name="g">The family supplying the condition; must belong to this manager.</param>
        internal Zdd SubsetsOf(in Zdd f, in Zdd g) => ApplyBinary(ZddOperation.SubsetsOf, f, g);

        /// <summary>Keeps only elements of <paramref name="f"/> that are not a subset of any member of <paramref name="g"/>.</summary>
        /// <param name="f">The family being filtered; must belong to this manager.</param>
        /// <param name="g">The family supplying the condition; must belong to this manager.</param>
        internal Zdd NonSubsetsOf(in Zdd f, in Zdd g) => ApplyBinary(ZddOperation.NonSubsetsOf, f, g);

        /// <summary>Keeps only elements of <paramref name="f"/> that are not a superset of any member of <paramref name="g"/>.</summary>
        /// <param name="f">The family being filtered; must belong to this manager.</param>
        /// <param name="g">The family supplying the condition; must belong to this manager.</param>
        internal Zdd NonSupersetsOf(in Zdd f, in Zdd g) => ApplyBinary(ZddOperation.NonSupersetsOf, f, g);

        /// <summary>Returns the family with membership of <paramref name="item"/> flipped in every set.</summary>
        /// <param name="f">The family; must belong to this manager.</param>
        /// <param name="item">Item index in 0 .. <see cref="VariableCount"/> - 1.</param>
        internal Zdd Change(in Zdd f, int item) => ApplyUnary(ZddOperation.Change, f, item, nameof(f));

        /// <summary>Selects the sets containing <paramref name="item"/>, then removes <paramref name="item"/> from each.</summary>
        /// <param name="f">The family; must belong to this manager.</param>
        /// <param name="item">Item index in 0 .. <see cref="VariableCount"/> - 1.</param>
        internal Zdd OnSet(in Zdd f, int item) => ApplyUnary(ZddOperation.OnSet, f, item, nameof(f));

        /// <summary>Keeps only sets that don't contain <paramref name="item"/>.</summary>
        /// <param name="f">The family; must belong to this manager.</param>
        /// <param name="item">Item index in 0 .. <see cref="VariableCount"/> - 1.</param>
        internal Zdd OffSet(in Zdd f, int item) => ApplyUnary(ZddOperation.OffSet, f, item, nameof(f));

        /// <summary>Flips membership of each item in <paramref name="items"/> (generalizes <see cref="Change"/>).</summary>
        /// <param name="f">The family; must belong to this manager.</param>
        /// <param name="items">Item indices to flip; empty returns <paramref name="f"/> unchanged.</param>
        /// <remarks>Equivalent to applying <see cref="Change"/> in sequence; duplicate items cancel out since it is an involution. All range validation happens before any flip is applied.</remarks>
        internal Zdd Flip(in Zdd f, ReadOnlySpan<int> items)
        {
            EnsureOwns(f, nameof(f));

            // Validate the whole span before mutating anything, so a bad item never leaves f partially flipped.
            foreach (int item in items)
            {
                _ = LevelOf(item);
            }

            // Throws ObjectDisposedException here if disposed (touches both table and cache).
            TuneCache();

            int result = f.Id;

            foreach (int item in items)
            {
                result = UnaryOperations.Apply(this, ZddOperation.Change, result, item);
            }

            return new Zdd(this, result);
        }

        /// <summary>Keeps only elements maximal under inclusion.</summary>
        /// <param name="f">The family; must belong to this manager.</param>
        internal Zdd Maximal(in Zdd f) => ApplyExtremal(ZddOperation.Maximal, f);

        /// <summary>Keeps only elements minimal under inclusion.</summary>
        /// <param name="f">The family; must belong to this manager.</param>
        internal Zdd Minimal(in Zdd f) => ApplyExtremal(ZddOperation.Minimal, f);

        /// <summary>Returns the family of all sets that intersect every element of <paramref name="f"/>.</summary>
        /// <param name="f">The family; must belong to this manager.</param>
        internal Zdd HittingSets(in Zdd f) => ApplyExtremal(ZddOperation.HittingSets, f);

        /// <summary>Complement <c>2^U &#8726; f</c> (<c>U</c> is this manager's full variable set).</summary>
        /// <param name="f">The family; must belong to this manager.</param>
        internal Zdd Complement(in Zdd f) => ApplyExtremal(ZddOperation.Complement, f);

        /// <summary>Returns whether the set described by <paramref name="items"/> belongs to <paramref name="f"/>.</summary>
        /// <param name="f">The family; must belong to this manager.</param>
        /// <param name="items">Item indices of the set to check; order and duplicates don't matter.</param>
        /// <remarks>Builds no family, so no cache tuning or workspace rental is needed.</remarks>
        internal bool Contains(in Zdd f, ReadOnlySpan<int> items)
        {
            EnsureOwns(f, nameof(f));

            return QueryOperations.Contains(this, f.Id, items);
        }

        /// <summary>Returns the <paramref name="index"/>-th set of <paramref name="f"/> (unranking).</summary>
        /// <param name="f">The family; must belong to this manager.</param>
        /// <param name="index">Rank of the set to retrieve; 0 or more and less than the family's cardinality.</param>
        /// <param name="order">How rank is counted (matches enumeration order).</param>
        /// <remarks>Builds no family, so no cache tuning or workspace rental is needed.</remarks>
        internal int[] ElementAt(in Zdd f, BigInteger index, ZddEnumerationOrder order)
        {
            EnsureOwns(f, nameof(f));

            return SetRanking.ElementAt(this, f.Id, index, order);
        }

        /// <summary>Returns the rank of the set described by <paramref name="items"/> within <paramref name="f"/> (ranking).</summary>
        /// <param name="f">The family; must belong to this manager.</param>
        /// <param name="items">Item indices of the set to rank; order and duplicates don't matter.</param>
        /// <param name="order">How rank is counted (matches enumeration order).</param>
        internal BigInteger IndexOf(in Zdd f, ReadOnlySpan<int> items, ZddEnumerationOrder order)
        {
            EnsureOwns(f, nameof(f));

            return SetRanking.IndexOf(this, f.Id, items, order);
        }

        /// <summary>Picks one set from <paramref name="f"/> uniformly at random.</summary>
        /// <param name="f">The family; must belong to this manager.</param>
        /// <param name="random">The source of randomness.</param>
        internal int[] Sample(in Zdd f, Random random)
        {
            EnsureOwns(f, nameof(f));

            return SetRanking.Sample(this, f.Id, random);
        }

        /// <summary>Picks <paramref name="count"/> sets from <paramref name="f"/>, uniformly at random.</summary>
        /// <param name="f">The family; must belong to this manager.</param>
        /// <param name="count">Number of sets to pick; 0 or more.</param>
        /// <param name="random">The source of randomness.</param>
        internal int[][] Sample(in Zdd f, int count, Random random)
        {
            EnsureOwns(f, nameof(f));

            return SetRanking.Sample(this, f.Id, count, random);
        }

        /// <summary>Returns the set in <paramref name="f"/> with maximum weight, together with its weight.</summary>
        /// <typeparam name="TWeight">The weight type.</typeparam>
        /// <typeparam name="TOps">The weight operations; must be a <c>struct</c>.</typeparam>
        /// <param name="f">The family; must belong to this manager.</param>
        /// <param name="weights">Per-item weights; length must equal <see cref="VariableCount"/>.</param>
        /// <remarks>Builds no family, so no cache tuning or workspace rental is needed.</remarks>
        internal WeightedSet<TWeight> MaxWeight<TWeight, TOps>(in Zdd f, ReadOnlySpan<TWeight> weights)
            where TOps : struct, IWeightOps<TWeight>
        {
            EnsureOwns(f, nameof(f));

            return WeightOperations.Optimize<TWeight, TOps>(this, f.Id, weights, maximize: true);
        }

        /// <summary>Returns the set in <paramref name="f"/> with minimum weight, together with its weight.</summary>
        /// <typeparam name="TWeight">The weight type.</typeparam>
        /// <typeparam name="TOps">The weight operations; must be a <c>struct</c>.</typeparam>
        /// <param name="f">The family; must belong to this manager.</param>
        /// <param name="weights">Per-item weights; length must equal <see cref="VariableCount"/>.</param>
        internal WeightedSet<TWeight> MinWeight<TWeight, TOps>(in Zdd f, ReadOnlySpan<TWeight> weights)
            where TOps : struct, IWeightOps<TWeight>
        {
            EnsureOwns(f, nameof(f));

            return WeightOperations.Optimize<TWeight, TOps>(this, f.Id, weights, maximize: false);
        }

        /// <summary>Returns the top <paramref name="k"/> sets in <paramref name="f"/> by weight, descending.</summary>
        /// <typeparam name="TWeight">The weight type.</typeparam>
        /// <typeparam name="TOps">The weight operations; must be a <c>struct</c>.</typeparam>
        /// <param name="f">The family; must belong to this manager.</param>
        /// <param name="weights">Per-item weights; length must equal <see cref="VariableCount"/>.</param>
        /// <param name="k">Number of sets to return; 0 or more.</param>
        internal WeightedSet<TWeight>[] TopK<TWeight, TOps>(in Zdd f, ReadOnlySpan<TWeight> weights, int k)
            where TOps : struct, IWeightOps<TWeight>
        {
            EnsureOwns(f, nameof(f));

            return WeightOperations.TopK<TWeight, TOps>(this, f.Id, weights, k);
        }

        /// <summary>
        /// The probability that the resulting set belongs to <paramref name="f"/>, when each item
        /// is independently included with probability <paramref name="probabilities"/>.
        /// </summary>
        /// <param name="f">The family; must belong to this manager.</param>
        /// <param name="probabilities">Per-item probabilities; length must equal <see cref="VariableCount"/>, each in [0, 1].</param>
        internal double Probability(in Zdd f, ReadOnlySpan<double> probabilities)
        {
            EnsureOwns(f, nameof(f));

            return WeightOperations.Probability(this, f.Id, probabilities);
        }

        /// <summary>The expected weight of a set drawn uniformly from <paramref name="f"/>.</summary>
        /// <param name="f">The family; must belong to this manager.</param>
        /// <param name="weights">Per-item weights; length must equal <see cref="VariableCount"/>.</param>
        internal double ExpectedValue(in Zdd f, ReadOnlySpan<double> weights)
        {
            EnsureOwns(f, nameof(f));

            return WeightOperations.ExpectedValue(this, f.Id, weights);
        }

        /// <summary>For a set drawn uniformly from <paramref name="f"/>, the per-item probability of inclusion.</summary>
        /// <param name="f">The family; must belong to this manager.</param>
        internal double[] ItemFrequency(in Zdd f)
        {
            EnsureOwns(f, nameof(f));

            return WeightOperations.ItemFrequency(this, f.Id);
        }

        /// <summary>Whether every set in <paramref name="f"/> also belongs to <paramref name="g"/>.</summary>
        /// <param name="f">The left family; must belong to this manager.</param>
        /// <param name="g">The right family; must belong to this manager.</param>
        internal bool IsSubsetOf(in Zdd f, in Zdd g)
        {
            EnsureOwns(f, nameof(f));
            EnsureOwns(g, nameof(g));

            return QueryOperations.IsSubsetOf(this, f.Id, g.Id);
        }

        /// <summary>Whether <paramref name="f"/> and <paramref name="g"/> share any set.</summary>
        /// <param name="f">The left family; must belong to this manager.</param>
        /// <param name="g">The right family; must belong to this manager.</param>
        internal bool Overlaps(in Zdd f, in Zdd g)
        {
            EnsureOwns(f, nameof(f));
            EnsureOwns(g, nameof(g));

            return QueryOperations.Overlaps(this, f.Id, g.Id);
        }

        /// <summary>Root node id of the power set <c>2^U</c> (all subsets of the <see cref="VariableCount"/> items).</summary>
        /// <remarks>
        /// Each level's 0- and 1-branches point at the same family (every item is optional), so
        /// the node count is linear in variable count, not 2^n. Built once here so
        /// <see cref="ZddOperation.Quotient"/> (<c>f / &#8709;</c>) and <see cref="ZddOperation.Complement"/>
        /// agree on the same universe, and cached in <see cref="_powerSetRoot"/> since rebuilding
        /// costs one unique-table lookup per variable.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">This manager has been disposed.</exception>
        internal int PowerSetRoot()
        {
            // Throws if disposed, before checking the cached value.
            UniqueTable table = Table;

            if (_powerSetRoot != NodeTable.Bottom)
            {
                return _powerSetRoot;
            }

            int result = NodeTable.Top;

            for (int level = 1; level <= _variableCount; level++)
            {
                result = table.GetNode(level, result, result);
            }

            _powerSetRoot = result;
            return result;
        }

        /// <summary>
        /// Common entry point for unary operations that don't take an item: checks ownership, tunes
        /// the cache, then delegates to <see cref="ExtremalOperations.Apply"/>.
        /// </summary>
        private Zdd ApplyExtremal(ZddOperation op, in Zdd f)
        {
            EnsureOwns(f, nameof(f));

            // Throws ObjectDisposedException here if disposed (touches both table and cache).
            TuneCache();

            return new Zdd(this, ExtremalOperations.Apply(this, op, f.Id));
        }

        /// <summary>
        /// Common entry point for unary operations that take an item: checks ownership, tunes the
        /// cache, then delegates to <see cref="UnaryOperations.Apply"/>. <paramref name="item"/>
        /// range validation happens inside via <see cref="LevelOf"/>.
        /// </summary>
        private Zdd ApplyUnary(ZddOperation op, in Zdd f, int item, string paramName)
        {
            EnsureOwns(f, paramName);

            // Throws ObjectDisposedException here if disposed (touches both table and cache).
            TuneCache();

            return new Zdd(this, UnaryOperations.Apply(this, op, f.Id, item));
        }

        /// <summary>
        /// Common entry point for binary operations: checks both operands belong to this manager,
        /// tunes the cache, then dispatches to the matching implementation.
        /// </summary>
        /// <remarks>
        /// Set operations (<see cref="BinaryOperations"/>), family-algebra product/quotient/remainder
        /// (<see cref="FamilyAlgebraOperations"/>), and containment filters
        /// (<see cref="ContainmentOperations"/>) traverse differently and so are implemented
        /// separately, but share the same argument checks and cache tuning.
        /// </remarks>
        private Zdd ApplyBinary(ZddOperation op, in Zdd f, in Zdd g)
        {
            EnsureOwns(f, nameof(f));
            EnsureOwns(g, nameof(g));

            // Throws ObjectDisposedException here if disposed (touches both table and cache).
            TuneCache();

            int result = op switch
            {
                ZddOperation.Product or ZddOperation.Quotient or ZddOperation.Remainder =>
                    FamilyAlgebraOperations.Apply(this, op, f.Id, g.Id),
                ZddOperation.Meet
                    or ZddOperation.SupersetsOf
                    or ZddOperation.SubsetsOf
                    or ZddOperation.NonSubsetsOf
                    or ZddOperation.NonSupersetsOf =>
                    ContainmentOperations.Apply(this, op, f.Id, g.Id),
                _ => BinaryOperations.Apply(this, op, f.Id, g.Id),
            };

            return new Zdd(this, result);
        }

        /// <summary>Rents a workspace for an iterative operation implementation. Must be returned via <see cref="ReturnWorkspace"/>.</summary>
        /// <remarks>
        /// Renting again while one is already on loan returns a <b>different</b> workspace, so
        /// nested calls never share one. Slots are never freed on return, so a grown array carries
        /// over to the next call at the same nesting depth.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">This manager has been disposed.</exception>
        internal OperationWorkspace RentWorkspace()
        {
            // Guards against rebuilding the rental pool after disposal; operation entry points
            // normally reject this first, so this is rarely reached.
            EnsureNotDisposed();

            if (_workspaceDepth == _workspaces.Length)
            {
                Array.Resize(ref _workspaces, _workspaces.Length * 2);
            }

            OperationWorkspace workspace = _workspaces[_workspaceDepth] ??= new OperationWorkspace();
            _workspaceDepth++;
            return workspace;
        }

        /// <summary>Returns a rented workspace. Its contents are cleared for reuse; the slot itself is kept.</summary>
        /// <remarks>Rentals are strictly LIFO; returning anything but the innermost rental is a no-op, so out-of-order returns never free the wrong slot.</remarks>
        internal void ReturnWorkspace(OperationWorkspace workspace)
        {
            workspace.Reset();

            if (_workspaceDepth > 0 && ReferenceEquals(_workspaces[_workspaceDepth - 1], workspace))
            {
                _workspaceDepth--;
            }
        }

        /// <summary>The unique table this manager uses. Throws <see cref="ObjectDisposedException"/> after disposal.</summary>
        internal UniqueTable Table
        {
            get
            {
                UniqueTable? table = _table;
                if (table is null)
                {
                    ThrowHelper.ThrowObjectDisposedException(nameof(ZddManager));
                }

                return table!;
            }
        }

        /// <summary>The operation cache this manager uses. Throws <see cref="ObjectDisposedException"/> after disposal.</summary>
        internal OperationCache Cache
        {
            get
            {
                OperationCache? cache = _cache;
                if (cache is null)
                {
                    ThrowHelper.ThrowObjectDisposedException(nameof(ZddManager));
                }

                return cache!;
            }
        }

        /// <summary>Grows the operation cache to match the current node count. Called at each operation's entry point.</summary>
        /// <remarks>Safe to skip: the cache still works correctly at its initial size, just with a lower hit rate.</remarks>
        internal void TuneCache() => Cache.Tune(Table.Count);

        /// <summary>Converts an item index to its internal variable level. <c>level = VariableCount - item</c>.</summary>
        /// <param name="item">Item index in 0 .. <see cref="VariableCount"/> - 1.</param>
        /// <returns>Level in 1 .. <see cref="VariableCount"/>.</returns>
        internal int LevelOf(int item)
        {
            if ((uint)item >= (uint)_variableCount)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(item),
                    _variableCount == 0
                        ? $"This manager has no variables, so there is no valid item index; '{nameof(item)}' was {item}."
                        : $"'{nameof(item)}' must be in the range 0..{_variableCount - 1}, but was {item}.");
            }

            return _variableCount - item;
        }

        /// <summary>Converts an internal variable level to an item index. <c>item = VariableCount - level</c>.</summary>
        /// <param name="level">Level in 1 .. <see cref="VariableCount"/> (terminal level 0 is not valid).</param>
        /// <returns>Item index in 0 .. <see cref="VariableCount"/> - 1.</returns>
        internal int ItemOf(int level)
        {
            if (level < 1 || level > _variableCount)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(level),
                    _variableCount == 0
                        ? $"This manager has no variables, so there is no valid level; '{nameof(level)}' was {level}."
                        : $"'{nameof(level)}' must be in the range 1..{_variableCount}, but was {level}.");
            }

            return _variableCount - level;
        }

        /// <summary>
        /// Creates a single node branching on <paramref name="item"/>. The unique table applies
        /// zero-suppression and unification, so an equivalent existing family returns the same id.
        /// </summary>
        /// <param name="item">The branch variable's item index.</param>
        /// <param name="lo">The family for the branch that excludes <paramref name="item"/>.</param>
        /// <param name="hi">The family for the branch that includes <paramref name="item"/>, with <paramref name="item"/> already removed.</param>
        /// <remarks>Internal entry point for hand-assembling families; not yet decided whether to expose publicly (see docs/ROADMAP.md).</remarks>
        /// <exception cref="ArgumentException">
        /// <paramref name="lo"/> / <paramref name="hi"/> belongs to a different manager, is an
        /// invalid handle, or branches on a variable that is not below <paramref name="item"/>.
        /// </exception>
        internal Zdd CreateNode(int item, in Zdd lo, in Zdd hi)
        {
            UniqueTable table = Table;
            int level = LevelOf(item);

            EnsureOwns(lo, nameof(lo));
            EnsureOwns(hi, nameof(hi));
            EnsureBelow(level, lo.Id, nameof(lo));
            EnsureBelow(level, hi.Id, nameof(hi));

            return new Zdd(this, table.GetNode(level, lo.Id, hi.Id));
        }

        /// <summary>
        /// Confirms <paramref name="zdd"/> belongs to this manager. Mixing families from different
        /// managers always throws, since node ids are only meaningful within their own manager.
        /// </summary>
        internal void EnsureOwns(in Zdd zdd, string paramName)
        {
            if (ReferenceEquals(zdd.Owner, this))
            {
                return;
            }

            ThrowHelper.ThrowArgumentException(
                paramName,
                zdd.Owner is null
                    ? $"'{paramName}' is a default Zdd handle, which does not belong to any manager."
                    : $"'{paramName}' belongs to a different ZddManager; node ids are only meaningful within the manager that created them.");
        }

        /// <summary>Counts the non-terminal nodes reachable from <paramref name="rootId"/>.</summary>
        internal long CountReachableNodes(int rootId)
        {
            if (NodeTable.IsTerminal(rootId))
            {
                return 0;
            }

            HashSet<int> visited = new HashSet<int>();
            Traverse(rootId, visited);
            return visited.Count;
        }

        /// <summary>Returns, in ascending order, the items actually used by nodes reachable from <paramref name="rootId"/>.</summary>
        internal int[] CollectSupport(int rootId)
        {
            if (NodeTable.IsTerminal(rootId))
            {
                return Array.Empty<int>();
            }

            HashSet<int> visited = new HashSet<int>();
            Traverse(rootId, visited);

            NodeTable nodes = Table.Nodes;
            HashSet<int> levels = new HashSet<int>();
            foreach (int id in visited)
            {
                levels.Add(nodes[id].Level);
            }

            int[] items = new int[levels.Count];
            int next = 0;
            foreach (int level in levels)
            {
                items[next++] = ItemOf(level);
            }

            Array.Sort(items);
            return items;
        }

        /// <summary>Collects the non-terminal nodes reachable from <paramref name="rootId"/> into <paramref name="visited"/>.</summary>
        /// <remarks>
        /// Iterative, not recursive (see docs/PLAN.md &#167;4.5): ZDD depth equals variable count,
        /// and recursion at 100k variables would trigger an uncatchable <c>StackOverflowException</c>.
        /// </remarks>
        private void Traverse(int rootId, HashSet<int> visited)
        {
            NodeTable nodes = Table.Nodes;

            int[] stack = new int[16];
            int top = 0;

            visited.Add(rootId);
            stack[top++] = rootId;

            while (top > 0)
            {
                ref ZddNode node = ref nodes[stack[--top]];
                int lo = node.Lo;
                int hi = node.Hi;

                if (!NodeTable.IsTerminal(lo) && visited.Add(lo))
                {
                    Push(ref stack, ref top, lo);
                }

                if (!NodeTable.IsTerminal(hi) && visited.Add(hi))
                {
                    Push(ref stack, ref top, hi);
                }
            }
        }

        private static void Push(ref int[] stack, ref int top, int id)
        {
            if (top == stack.Length)
            {
                Array.Resize(ref stack, stack.Length * 2);
            }

            stack[top++] = id;
        }

        /// <summary>
        /// Confirms a child node sits strictly below its parent's level, rejecting variable-order
        /// violations at construction time rather than letting canonicity break silently.
        /// </summary>
        private void EnsureBelow(int level, int childId, string paramName)
        {
            int childLevel = Table.Nodes[childId].Level;
            if (childLevel < level)
            {
                return;
            }

            ThrowHelper.ThrowArgumentException(
                paramName,
                $"'{paramName}' is rooted at item {ItemOf(childLevel)} (level {childLevel}), which is not below item {ItemOf(level)} (level {level}); " +
                "a node's children must branch on items that come later in the variable order.");
        }

        private void EnsureNotDisposed()
        {
            if (_table is null)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(ZddManager));
            }
        }
    }
}
