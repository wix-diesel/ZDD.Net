using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using ZDD.Net.Internal;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// The first frontier pass: walks a spec from its root level down to level 1, merging the states
    /// of each level, and returns the unreduced <see cref="TemporaryNodeTable"/> the second pass reads.
    /// </summary>
    /// <typeparam name="TSpec">
    /// The spec, taken as a type parameter so <c>GetChild</c> is devirtualized and inlined; it is
    /// called once per state and branch, which is the innermost loop of a build (docs/PLAN.md §10-2).
    /// </typeparam>
    /// <typeparam name="TState">The state carried between levels.</typeparam>
    /// <remarks>
    /// <para>
    /// The walk is a loop over levels, not a recursion: a build is as deep as the item count, and a
    /// recursive one would die of <c>StackOverflowException</c> on the deep diagrams this library
    /// targets (docs/PLAN.md §4.5).
    /// </para>
    /// <para>
    /// One state table per pending level rather than the two of <see cref="LevelStateTablePair{TTable}"/>:
    /// a spec may skip levels, so a level's children can land several levels below, and the states of
    /// a level must all meet in one table or equal states stop merging. Each table is dropped as soon
    /// as its level is expanded, so a spec that never skips still keeps only two alive.
    /// </para>
    /// <para>
    /// Write the spec as a <c>readonly struct</c>: it is held in a readonly field, so a mutable one is
    /// defensively copied on every call.
    /// </para>
    /// <para>
    /// <b>Parallel level expansion (M4-3, issue #46).</b> A level wide enough to be worth it is split
    /// into contiguous, index-ordered partitions; each runs on its own thread, calling <c>GetChild</c>
    /// independently and stashing a non-terminal child's state into a scratch slot private to that
    /// partition (<see cref="RunPartition"/>) — never touching the shared per-level tables at all.
    /// Once every partition finishes, a single thread walks their scratch slots back in partition order
    /// — the same order a sequential run would have visited those states in — registering each one into
    /// the shared tables through the ordinary <see cref="AddState"/> (<see cref="MergePartitions"/>).
    /// That single-threaded replay is what makes the result deterministic: node IDs come from a state's
    /// position in its level's table, so as long as states reach <see cref="AddState"/> in the same
    /// order, the IDs match regardless of how many partitions ran or how their threads happened to
    /// finish. An earlier version of this design gave each partition its own dedup table instead, so
    /// that only the distinct states left after a partition-local pass paid for a second, shared-table
    /// registration; docs/benchmarks.md's M4-3 section measured that against this one and found the
    /// second hash-table pass cost more than the parallel <c>GetChild</c> calls saved, on every case
    /// tried — so registration happens exactly once, on the merge thread, and only the state
    /// computation itself runs in parallel. This requires the spec to share no mutable state across
    /// calls (docs/frontier-spec-guide.md §4) — every built-in spec in this library satisfies that
    /// already, since none holds anything beyond immutable reference data computed once at construction
    /// (<c>Graph</c>, <c>FrontierManager</c>, precomputed arrays).
    /// </para>
    /// </remarks>
    internal sealed class TopDownExpander<TSpec, TState>
        where TSpec : struct, IDdSpec<TState>
    {
        /// <summary>States expanded between two cancellation checks; also bounds how late a cancel is seen.</summary>
        private const int CancellationCheckInterval = 512;

        /// <summary>
        /// Slots a level's state table starts with. Levels are usually far narrower than the table's
        /// own default, and a table that has to grow costs one rehash per doubling.
        /// </summary>
        private const int InitialLevelCapacity = 64;

        /// <summary>
        /// States a partition must hold before splitting a level any further pays for the thread
        /// scheduling and merge overhead it costs (docs/benchmarks.md's M4-3 section). Also doubles as
        /// the "level too small to parallelize" threshold: a level under twice this width never gets a
        /// second partition, so it always takes the sequential path.
        /// </summary>
        private const int MinPartitionWidth = 2048;

        private readonly TSpec _spec;
        private readonly int _rootLevel;
        private readonly int _maxNodeCount;
        private readonly int _maxFrontierSize;
        private readonly int _maxDegreeOfParallelism;
        private readonly CancellationToken _cancellationToken;
        private readonly IProgress<BuildProgress>? _progress;

        /// <summary>
        /// Describes a newly registered state for <see cref="BuildOptions.RecordStates"/> (M5-4, issue
        /// #56), or null when recording is off — the only per-state cost paid in that case is this one
        /// field's null check in <see cref="AddState"/>.
        /// </summary>
        private readonly Func<TState, string>? _describeState;

        /// <summary>The state table of each level that still has states to expand; null once dropped.</summary>
        private readonly StructLevelStateTable<TSpec, TState>?[] _tables;

        /// <summary>The nodes of each expanded level, in state-table index order.</summary>
        private readonly TemporaryNode[][] _levels;

        /// <summary>One label list per level, in state-table index order; only allocated when <see cref="_describeState"/> is set.</summary>
        private readonly List<string>?[] _labels;

        private long _nodeCount;

        private TopDownExpander(TSpec spec, int rootLevel, BuildOptions options, Func<TState, string>? describeState)
        {
            _spec = spec;
            _rootLevel = rootLevel;
            _maxNodeCount = options.MaxNodeCount;
            _maxFrontierSize = options.MaxFrontierSize;
            _maxDegreeOfParallelism = options.MaxDegreeOfParallelism;
            _cancellationToken = options.CancellationToken;
            _progress = options.Progress;
            _describeState = describeState;
            _tables = new StructLevelStateTable<TSpec, TState>?[rootLevel + 1];
            _levels = new TemporaryNode[rootLevel + 1][];
            _labels = describeState is null ? Array.Empty<List<string>?>() : new List<string>?[rootLevel + 1];

            Array.Fill(_levels, Array.Empty<TemporaryNode>());
        }

        /// <summary>Expands <paramref name="spec"/> into a temporary node table.</summary>
        /// <param name="spec">The spec to unroll; its <c>GetRoot</c> decides how many levels there are.</param>
        /// <param name="options">Limits, cancellation and progress; defaults when null.</param>
        /// <returns>The unreduced diagram, or a terminal table when the root is a terminal.</returns>
        /// <exception cref="BuildLimitExceededException">A limit of <paramref name="options"/> was passed.</exception>
        /// <exception cref="OperationCanceledException">The options' token was cancelled.</exception>
        public static TemporaryNodeTable Expand(TSpec spec, BuildOptions? options = null) =>
            Expand(spec, options, null, out _);

        /// <summary>
        /// Expands <paramref name="spec"/> into a temporary node table, additionally describing every
        /// state <paramref name="describeState"/> is given for (M5-4, issue #56).
        /// </summary>
        /// <param name="spec">The spec to unroll; its <c>GetRoot</c> decides how many levels there are.</param>
        /// <param name="options">Limits, cancellation and progress; defaults when null.</param>
        /// <param name="describeState">
        /// Called once per newly registered state when non-null; its result becomes that state's label.
        /// </param>
        /// <param name="labelsByLevel">
        /// <paramref name="describeState"/>'s results, indexed like the returned table's levels and, within
        /// a level, like its node array — empty when <paramref name="describeState"/> is null.
        /// </param>
        /// <returns>The unreduced diagram, or a terminal table when the root is a terminal.</returns>
        /// <exception cref="BuildLimitExceededException">A limit of <paramref name="options"/> was passed.</exception>
        /// <exception cref="OperationCanceledException">The options' token was cancelled.</exception>
        public static TemporaryNodeTable Expand(
            TSpec spec,
            BuildOptions? options,
            Func<TState, string>? describeState,
            out string?[][] labelsByLevel)
        {
            BuildOptions effective = options ?? new BuildOptions();

            TState rootState = default!;
            int rootLevel = spec.GetRoot(ref rootState);

            if (DdResult.IsTerminal(rootLevel))
            {
                labelsByLevel = Array.Empty<string?[]>();
                return TemporaryNodeTable.Terminal(rootLevel == DdResult.True);
            }

            if (rootLevel < 1)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    $"The spec's GetRoot returned {rootLevel}, which is neither a level (1 or above) nor a " +
                    $"terminal ({DdResult.False} = bottom, {DdResult.True} = top).");
            }

            TopDownExpander<TSpec, TState> expander =
                new TopDownExpander<TSpec, TState>(spec, rootLevel, effective, describeState);

            try
            {
                TemporaryNodeTable table = expander.Run(rootState);
                labelsByLevel = expander.CollectLabels();
                return table;
            }
            finally
            {
                expander.DropTables();
            }
        }

        /// <summary>Expands every level from the root down, filling <see cref="_levels"/> as it goes.</summary>
        private TemporaryNodeTable Run(in TState rootState)
        {
            AddState(rootState, _rootLevel);

            for (int level = _rootLevel; level >= 1; level--)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                StructLevelStateTable<TSpec, TState>? table = _tables[level];

                // A level no branch reached keeps its empty node array; a spec that skips levels leaves such gaps.
                if (table is not null)
                {
                    ExpandLevel(table, level);
                    DropTable(level);
                }

                // Every level is reported, the empty ones included: the reports are then the width of
                // each level in turn, and a build that skips levels still counts down one at a time.
                _progress?.Report(new BuildProgress(_rootLevel, level, _levels[level].Length, _nodeCount));
            }

            return new TemporaryNodeTable(_rootLevel, _levels);
        }

        /// <summary>Turns every state of one level into a node, registering the children one level's worth ahead.</summary>
        /// <remarks>
        /// Expanding a level only ever adds to lower levels, so the width is fixed once it starts.
        /// Dispatches to <see cref="ExpandLevelParallel"/> when the level is both wide enough to be
        /// worth splitting and <see cref="BuildOptions.MaxDegreeOfParallelism"/> allows more than one
        /// worker; otherwise runs the plain sequential loop.
        /// </remarks>
        private void ExpandLevel(StructLevelStateTable<TSpec, TState> table, int level)
        {
            int width = table.Count;
            int partitionCount = _maxDegreeOfParallelism > 1 ? ComputePartitionCount(width) : 1;

            if (partitionCount <= 1)
            {
                ExpandLevelSequential(table, level, width);
            }
            else
            {
                ExpandLevelParallel(table, level, width, partitionCount);
            }
        }

        /// <summary>How many contiguous partitions a level of <paramref name="width"/> states should split into.</summary>
        /// <remarks>
        /// <see cref="FrontierParallelDiagnostics.ForceParallelForTesting"/> drops the width floor so
        /// the parallel path — merge included — runs under the existing M1&#8211;M3 regression suite,
        /// not only under dedicated wide-frontier tests (docs/PLAN.md's CI requirement for M4-3).
        /// </remarks>
        private int ComputePartitionCount(int width)
        {
            if (FrontierParallelDiagnostics.ForceParallelForTesting)
            {
                return Math.Min(_maxDegreeOfParallelism, width);
            }

            int byWidth = width / MinPartitionWidth;
            return Math.Min(_maxDegreeOfParallelism, Math.Max(1, byWidth));
        }

        /// <summary>The plain, single-threaded expansion: every level takes this path when parallelism would not pay.</summary>
        private void ExpandLevelSequential(StructLevelStateTable<TSpec, TState> table, int level, int width)
        {
            TemporaryNode[] nodes = new TemporaryNode[width];
            int nextCancellationCheck = CancellationCheckInterval;

            for (int index = 0; index < width; index++)
            {
                if (index == nextCancellationCheck)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    nextCancellationCheck += CancellationCheckInterval;
                }

                nodes[index] = new TemporaryNode(
                    Branch(table[index], level, 0),
                    Branch(table[index], level, 1));
            }

            _levels[level] = nodes;
        }

        /// <summary>
        /// Splits the level's states across <paramref name="partitionCount"/> contiguous, independently
        /// computed partitions (<see cref="RunPartition"/>), then replays their results into the shared
        /// per-level tables in partition order (<see cref="MergePartitions"/>) — see the type's remarks
        /// for why that ordering is what keeps the result deterministic, and why registration itself
        /// stays single-threaded instead of also being split per partition.
        /// </summary>
        private void ExpandLevelParallel(StructLevelStateTable<TSpec, TState> table, int level, int width, int partitionCount)
        {
            int[] starts = ComputePartitionStarts(width, partitionCount);
            TemporaryNode[] nodes = new TemporaryNode[width];

            // Every branch of every state gets one scratch slot, whether it turns out terminal or not:
            // slot (index - starts[p]) * 2 + value. A terminal's TemporaryNodeId is already final and
            // never touches pendingChildren; a non-terminal one stores its still-unregistered child
            // state there and carries the slot as its own Index, which MergePartitions resolves.
            TemporaryNodeId[][] pendingIds = new TemporaryNodeId[partitionCount][];
            TState[][] pendingChildren = new TState[partitionCount][];

            for (int p = 0; p < partitionCount; p++)
            {
                int partitionWidth = starts[p + 1] - starts[p];
                pendingIds[p] = new TemporaryNodeId[partitionWidth * 2];
                pendingChildren[p] = new TState[partitionWidth * 2];
            }

            ParallelOptions parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = partitionCount,
                CancellationToken = _cancellationToken,
            };

            try
            {
                Parallel.For(0, partitionCount, parallelOptions, partitionIndex =>
                    RunPartition(table, level, starts, partitionIndex, pendingIds[partitionIndex], pendingChildren[partitionIndex]));
            }
            catch (AggregateException aggregate) when (aggregate.InnerExceptions.Count == 1)
            {
                // A spec's GetChild throwing is the only expected non-cancellation failure here, and
                // Parallel.For always wraps it, even with a single partition at fault (docs/frontier-
                // guide.md §6.3). Unwrapping the common single-exception case keeps that failure look
                // the same as it would from the sequential path; a genuine multi-partition failure is
                // rare enough (and only possible with a spec whose GetChild is not itself deterministic)
                // that surfacing the real AggregateException, documented, is the honest choice.
                ExceptionDispatchInfo.Capture(aggregate.InnerExceptions[0]).Throw();
                throw; // Unreachable: Throw() above always throws.
            }

            MergePartitions(starts, partitionCount, nodes, pendingIds, pendingChildren);

            _levels[level] = nodes;
        }

        /// <summary>Balanced contiguous partition boundaries: partition <c>p</c> is <c>[starts[p], starts[p + 1])</c>.</summary>
        private static int[] ComputePartitionStarts(int width, int partitionCount)
        {
            int[] starts = new int[partitionCount + 1];

            for (int p = 0; p <= partitionCount; p++)
            {
                starts[p] = (int)((long)width * p / partitionCount);
            }

            return starts;
        }

        /// <summary>
        /// Computes one partition's share of a level: every branch of every state in
        /// <c>[starts[partitionIndex], starts[partitionIndex + 1])</c>, calling <c>GetChild</c> but
        /// never touching the shared per-level tables — a non-terminal result is just stashed in
        /// <paramref name="pendingChildren"/> for <see cref="MergePartitions"/> to register afterwards.
        /// </summary>
        private void RunPartition(
            StructLevelStateTable<TSpec, TState> table,
            int level,
            int[] starts,
            int partitionIndex,
            TemporaryNodeId[] pendingIds,
            TState[] pendingChildren)
        {
            int start = starts[partitionIndex];
            int end = starts[partitionIndex + 1];
            int nextCancellationCheck = start + CancellationCheckInterval;

            for (int index = start; index < end; index++)
            {
                if (index == nextCancellationCheck)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    nextCancellationCheck += CancellationCheckInterval;
                }

                int slot = (index - start) * 2;
                pendingIds[slot] = ComputeChild(table[index], level, 0, pendingChildren, slot);
                pendingIds[slot + 1] = ComputeChild(table[index], level, 1, pendingChildren, slot + 1);
            }
        }

        /// <summary>
        /// The parallel-path twin of <see cref="Branch"/>: a non-terminal child is stashed in
        /// <paramref name="pendingChildren"/> rather than registered, since registration must stay on
        /// the single merge thread (see the type's remarks).
        /// </summary>
        private TemporaryNodeId ComputeChild(in TState state, int level, int value, TState[] pendingChildren, int slot)
        {
            TState child = state;
            int childLevel = _spec.GetChild(ref child, level, value);

            Debug.Assert(
                DdResult.IsTerminal(childLevel) || (childLevel >= 1 && childLevel < level),
                "GetChild must return a level below the one it was given, or a terminal.");

            if (childLevel == DdResult.False)
            {
                return TemporaryNodeId.Bottom;
            }

            if (childLevel == DdResult.True)
            {
                return TemporaryNodeId.Top;
            }

            pendingChildren[slot] = child;

            // The Index here is this slot, not a table index yet — ResolvePending reads it back.
            return new TemporaryNodeId(childLevel, slot);
        }

        /// <summary>
        /// Replays every partition's pending children into the shared per-level tables, one partition at
        /// a time in index order, filling in <paramref name="nodes"/> as it goes. Processing partitions
        /// in order — never completion order — is what makes a state's final index the same one the
        /// sequential path would have given it: partition 0's states reach <see cref="AddState"/> before
        /// partition 1's, exactly as they would if a single thread had walked the whole level in order.
        /// </summary>
        private void MergePartitions(
            int[] starts,
            int partitionCount,
            TemporaryNode[] nodes,
            TemporaryNodeId[][] pendingIds,
            TState[][] pendingChildren)
        {
            int nextCancellationCheck = CancellationCheckInterval;

            for (int p = 0; p < partitionCount; p++)
            {
                int start = starts[p];
                int end = starts[p + 1];
                TemporaryNodeId[] ids = pendingIds[p];
                TState[] children = pendingChildren[p];

                for (int index = start; index < end; index++)
                {
                    if (index == nextCancellationCheck)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        nextCancellationCheck += CancellationCheckInterval;
                    }

                    int slot = (index - start) * 2;
                    nodes[index] = new TemporaryNode(
                        ResolvePending(ids[slot], children),
                        ResolvePending(ids[slot + 1], children));
                }
            }
        }

        /// <summary>Registers a pending child (if not already a terminal) and returns its final, shared id.</summary>
        private TemporaryNodeId ResolvePending(TemporaryNodeId pending, TState[] pendingChildren)
        {
            if (pending.IsTerminal)
            {
                return pending;
            }

            return new TemporaryNodeId(pending.Level, AddState(pendingChildren[pending.Index], pending.Level));
        }

        /// <summary>Follows one branch of one state and returns where it lands.</summary>
        /// <param name="state">The parent state; copied before the spec sees it, so branches stay independent.</param>
        /// <param name="level">The level being decided.</param>
        /// <param name="value">The branch: 0 excludes the item, 1 includes it.</param>
        private TemporaryNodeId Branch(in TState state, int level, int value)
        {
            TState child = state;
            int childLevel = _spec.GetChild(ref child, level, value);

            // Release trusts the spec here: this runs once per state and branch, and a wrong level
            // can only be caught by re-reading the level array (docs/frontier-spec-guide.md §3).
            Debug.Assert(
                DdResult.IsTerminal(childLevel) || (childLevel >= 1 && childLevel < level),
                "GetChild must return a level below the one it was given, or a terminal.");

            if (childLevel == DdResult.False)
            {
                return TemporaryNodeId.Bottom;
            }

            if (childLevel == DdResult.True)
            {
                return TemporaryNodeId.Top;
            }

            return new TemporaryNodeId(childLevel, AddState(child, childLevel));
        }

        /// <summary>Registers a state in its level and returns its index, which is also its node's index.</summary>
        private int AddState(in TState state, int level)
        {
            StructLevelStateTable<TSpec, TState> table =
                _tables[level] ??= new StructLevelStateTable<TSpec, TState>(_spec, InitialLevelCapacity);

            int before = table.Count;
            int index = table.GetOrAdd(state);

            // An index the level already held means two branches met: one node, not two.
            if (table.Count == before)
            {
                return index;
            }

            _nodeCount++;

            if (_nodeCount > _maxNodeCount)
            {
                throw NodeCountExceeded(level);
            }

            if (table.Count > _maxFrontierSize)
            {
                throw FrontierSizeExceeded(level, table.Count);
            }

            if (_describeState is not null)
            {
                (_labels[level] ??= new List<string>()).Add(_describeState(state));
            }

            return index;
        }

        /// <summary>Snapshots the labels recorded so far, in the same (level, index) shape as <see cref="_levels"/>.</summary>
        private string?[][] CollectLabels()
        {
            if (_describeState is null)
            {
                return Array.Empty<string?[]>();
            }

            string?[][] result = new string?[_labels.Length][];

            for (int level = 0; level < _labels.Length; level++)
            {
                result[level] = _labels[level]?.ToArray() ?? Array.Empty<string?>();
            }

            return result;
        }

        private void DropTable(int level)
        {
            _tables[level]?.Dispose();
            _tables[level] = null;
        }

        /// <summary>Returns the pooled buffers of every level still holding a table, including on failure.</summary>
        private void DropTables()
        {
            for (int level = 1; level < _tables.Length; level++)
            {
                DropTable(level);
            }
        }

        private BuildLimitExceededException NodeCountExceeded(int level) =>
            new BuildLimitExceededException(
                BuildLimit.NodeCount,
                _maxNodeCount,
                level,
                $"The build passed BuildOptions.MaxNodeCount ({_maxNodeCount}) while filling level {level}: " +
                $"the diagram would hold {_nodeCount} temporary node(s). Raise the limit if the build nearly " +
                "fits, or make the spec merge more states.");

        private BuildLimitExceededException FrontierSizeExceeded(int level, int frontierSize) =>
            new BuildLimitExceededException(
                BuildLimit.FrontierSize,
                _maxFrontierSize,
                level,
                $"The build passed BuildOptions.MaxFrontierSize ({_maxFrontierSize}) while filling level {level}: " +
                $"that level already holds {frontierSize} distinct state(s). Raise the limit if the build " +
                "nearly fits, or reduce the width (a state that keeps less, or a better item order).");
    }
}
