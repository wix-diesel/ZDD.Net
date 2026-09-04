using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using ZDD.Net.Internal;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// The first frontier pass for <see cref="IArrayDdSpec"/>: the array-state twin of
    /// <see cref="TopDownExpander{TSpec, TState}"/>, built on <see cref="ArrayLevelStateTable"/>
    /// instead of a per-<c>TState</c> table so a state sized only at run time never needs its own
    /// generic instantiation.
    /// </summary>
    /// <typeparam name="TSpec">The spec, taken as a type parameter so <c>GetChild</c> devirtualizes and inlines.</typeparam>
    /// <remarks>
    /// <para>
    /// Iterative for the same reason as <see cref="TopDownExpander{TSpec, TState}"/> (docs/PLAN.md
    /// §4.5): a build is as deep as the item count. See that type for the rest of the rationale
    /// (one table per pending level, struct spec); this one only differs in how a state is stored.
    /// </para>
    /// <para>
    /// <b>Parallel level expansion (M4-3, issue #46)</b> follows the same partition-then-merge design as
    /// <see cref="TopDownExpander{TSpec, TState}"/> — see that type's remarks for the full rationale,
    /// including why registration happens once, on a single merge thread, instead of once per partition.
    /// That single-threaded merge is what makes this type simpler than an earlier version that gave each
    /// partition its own dedup table: this type's <see cref="ArrayLevelStateTable"/> shares one mutable
    /// <see cref="PackedStateLayout"/> across every level (<see cref="_layout"/>), which widens itself in
    /// place when a value outside its current window is packed — a table private to each partition would
    /// have needed its own private layout too, to avoid two partitions racing on that widening. Since a
    /// partition here never packs or registers anything (it only computes children into plain,
    /// unregistered <see cref="int"/> buffers), no such private layout is needed at all.
    /// </para>
    /// </remarks>
    internal sealed class ArrayTopDownExpander<TSpec>
        where TSpec : struct, IArrayDdSpec
    {
        private const int CancellationCheckInterval = 512;
        private const int InitialLevelCapacity = 64;

        /// <summary>See <see cref="TopDownExpander{TSpec, TState}.MinPartitionWidth"/> for the rationale; same value.</summary>
        private const int MinPartitionWidth = 2048;

        private readonly TSpec _spec;
        private readonly int _arrayLength;
        private readonly int _rootLevel;
        private readonly int _maxNodeCount;
        private readonly int _maxFrontierSize;
        private readonly int _maxDegreeOfParallelism;
        private readonly CancellationToken _cancellationToken;
        private readonly IProgress<BuildProgress>? _progress;

        /// <summary>The state table of each level that still has states to expand; null once dropped.</summary>
        private readonly ArrayLevelStateTable?[] _tables;

        /// <summary>The nodes of each expanded level, in state-table index order.</summary>
        private readonly TemporaryNode[][] _levels;

        /// <summary>Scratch buffer a branch's child state is built into before being registered.</summary>
        private readonly int[] _scratch;

        /// <summary>Scratch buffer the state being expanded is unpacked into, once for both branches.</summary>
        private readonly int[] _current;

        /// <summary>How states are packed; shared by every level, so a widening is learned once.</summary>
        private readonly PackedStateLayout _layout;

        private long _nodeCount;

        private ArrayTopDownExpander(TSpec spec, int arrayLength, int rootLevel, BuildOptions options)
        {
            _spec = spec;
            _arrayLength = arrayLength;
            _rootLevel = rootLevel;
            _maxNodeCount = options.MaxNodeCount;
            _maxFrontierSize = options.MaxFrontierSize;
            _maxDegreeOfParallelism = options.MaxDegreeOfParallelism;
            _cancellationToken = options.CancellationToken;
            _progress = options.Progress;
            _tables = new ArrayLevelStateTable?[rootLevel + 1];
            _levels = new TemporaryNode[rootLevel + 1][];
            _scratch = new int[arrayLength];
            _current = new int[arrayLength];
            _layout = new PackedStateLayout();

            Array.Fill(_levels, Array.Empty<TemporaryNode>());
        }

        /// <summary>Expands <paramref name="spec"/> into a temporary node table.</summary>
        /// <param name="spec">The spec to unroll; its <c>GetRoot</c> decides how many levels there are.</param>
        /// <param name="options">Limits, cancellation and progress; defaults when null.</param>
        /// <returns>The unreduced diagram, or a terminal table when the root is a terminal.</returns>
        /// <exception cref="BuildLimitExceededException">A limit of <paramref name="options"/> was passed.</exception>
        /// <exception cref="OperationCanceledException">The options' token was cancelled.</exception>
        public static TemporaryNodeTable Expand(TSpec spec, BuildOptions? options = null)
        {
            BuildOptions effective = options ?? new BuildOptions();
            int arrayLength = spec.ArrayLength;

            if (arrayLength < 0)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    $"The spec's ArrayLength returned {arrayLength}, which is negative; it must be 0 or more.");
            }

            Span<int> rootState = arrayLength == 0 ? default : new int[arrayLength];
            int rootLevel = spec.GetRoot(rootState);

            if (DdResult.IsTerminal(rootLevel))
            {
                return TemporaryNodeTable.Terminal(rootLevel == DdResult.True);
            }

            if (rootLevel < 1)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    $"The spec's GetRoot returned {rootLevel}, which is neither a level (1 or above) nor a " +
                    $"terminal ({DdResult.False} = bottom, {DdResult.True} = top).");
            }

            if (arrayLength == 0)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    "The spec's ArrayLength is 0, so it has no state to distinguish states with, but GetRoot " +
                    $"returned level {rootLevel} instead of a terminal; a zero-length array spec can only ever " +
                    "build a terminal family. Give the spec at least one array slot, or write it against " +
                    "IDdSpec<TState> instead.");
            }

            ArrayTopDownExpander<TSpec> expander = new ArrayTopDownExpander<TSpec>(spec, arrayLength, rootLevel, effective);

            try
            {
                return expander.Run(rootState);
            }
            finally
            {
                expander.DropTables();
            }
        }

        /// <summary>Expands every level from the root down, filling <see cref="_levels"/> as it goes.</summary>
        private TemporaryNodeTable Run(ReadOnlySpan<int> rootState)
        {
            AddState(rootState, _rootLevel);

            for (int level = _rootLevel; level >= 1; level--)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                ArrayLevelStateTable? table = _tables[level];

                if (table is not null)
                {
                    ExpandLevel(table, level);
                    DropTable(level);
                }

                _progress?.Report(new BuildProgress(_rootLevel, level, _levels[level].Length, _nodeCount));
            }

            return new TemporaryNodeTable(_rootLevel, _levels);
        }

        /// <summary>
        /// Turns every state of one level into a node, registering the children one level's worth
        /// ahead. Dispatches to <see cref="ExpandLevelParallel"/> when the level is both wide enough to
        /// be worth splitting and <see cref="BuildOptions.MaxDegreeOfParallelism"/> allows more than one
        /// worker (see <see cref="TopDownExpander{TSpec, TState}.ExpandLevel"/> for the same design).
        /// </summary>
        private void ExpandLevel(ArrayLevelStateTable table, int level)
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
        private void ExpandLevelSequential(ArrayLevelStateTable table, int level, int width)
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

                table.CopyStateTo(index, _current);
                nodes[index] = new TemporaryNode(
                    Branch(_current, level, 0),
                    Branch(_current, level, 1));
            }

            _levels[level] = nodes;
        }

        /// <summary>
        /// Splits the level's states across <paramref name="partitionCount"/> contiguous, independently
        /// computed partitions (<see cref="RunPartition"/>), then replays their results into the shared
        /// per-level tables in partition order (<see cref="MergePartitions"/>) — see
        /// <see cref="TopDownExpander{TSpec, TState}.ExpandLevelParallel"/> for why that ordering is what
        /// keeps the result deterministic, and why registration itself stays single-threaded.
        /// </summary>
        private void ExpandLevelParallel(ArrayLevelStateTable table, int level, int width, int partitionCount)
        {
            int[] starts = ComputePartitionStarts(width, partitionCount);
            TemporaryNode[] nodes = new TemporaryNode[width];

            // Every branch of every state gets one scratch slot in pendingChildren, whether it turns
            // out terminal or not — slot (index - starts[p]) * 2 + value occupies
            // pendingChildren[slot * _arrayLength .. (slot + 1) * _arrayLength). A terminal's
            // TemporaryNodeId is already final; a non-terminal one carries the slot as its own Index,
            // which MergePartitions resolves by registering that slice through AddState.
            TemporaryNodeId[][] pendingIds = new TemporaryNodeId[partitionCount][];
            int[][] pendingChildren = new int[partitionCount][];
            int[][] currents = new int[partitionCount][];

            for (int p = 0; p < partitionCount; p++)
            {
                int partitionWidth = starts[p + 1] - starts[p];
                pendingIds[p] = new TemporaryNodeId[partitionWidth * 2];
                pendingChildren[p] = new int[partitionWidth * 2 * _arrayLength];
                currents[p] = new int[_arrayLength];
            }

            ParallelOptions parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = partitionCount,
                CancellationToken = _cancellationToken,
            };

            try
            {
                Parallel.For(0, partitionCount, parallelOptions, partitionIndex =>
                    RunPartition(table, level, starts, partitionIndex, pendingIds[partitionIndex], pendingChildren[partitionIndex], currents[partitionIndex]));
            }
            catch (AggregateException aggregate) when (aggregate.InnerExceptions.Count == 1)
            {
                // See TopDownExpander<TSpec, TState>.ExpandLevelParallel for why unwrapping the
                // single-exception case is the right default and a genuine multi-exception
                // AggregateException is left as-is (docs/frontier-guide.md §6.3).
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
            ArrayLevelStateTable table,
            int level,
            int[] starts,
            int partitionIndex,
            TemporaryNodeId[] pendingIds,
            int[] pendingChildren,
            int[] current)
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

                table.CopyStateTo(index, current);

                int slot = (index - start) * 2;
                pendingIds[slot] = ComputeChild(current, level, 0, pendingChildren, slot);
                pendingIds[slot + 1] = ComputeChild(current, level, 1, pendingChildren, slot + 1);
            }
        }

        /// <summary>
        /// The parallel-path twin of <see cref="Branch"/>: a non-terminal child is written straight into
        /// its <paramref name="pendingChildren"/> slice rather than registered, since registration must
        /// stay on the single merge thread (see the type's remarks).
        /// </summary>
        private TemporaryNodeId ComputeChild(ReadOnlySpan<int> state, int level, int value, int[] pendingChildren, int slot)
        {
            Span<int> child = pendingChildren.AsSpan(slot * _arrayLength, _arrayLength);
            state.CopyTo(child);
            int childLevel = _spec.GetChild(child, level, value);

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

            // The Index here is this slot, not a table index yet — ResolvePending reads it back.
            return new TemporaryNodeId(childLevel, slot);
        }

        /// <summary>
        /// Replays every partition's pending children into the shared per-level tables, one partition at
        /// a time in index order, filling in <paramref name="nodes"/> as it goes — see
        /// <see cref="TopDownExpander{TSpec, TState}.MergePartitions"/> for why partition order is what
        /// keeps this deterministic.
        /// </summary>
        private void MergePartitions(
            int[] starts,
            int partitionCount,
            TemporaryNode[] nodes,
            TemporaryNodeId[][] pendingIds,
            int[][] pendingChildren)
        {
            int nextCancellationCheck = CancellationCheckInterval;

            for (int p = 0; p < partitionCount; p++)
            {
                int start = starts[p];
                int end = starts[p + 1];
                TemporaryNodeId[] ids = pendingIds[p];
                int[] children = pendingChildren[p];

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
        private TemporaryNodeId ResolvePending(TemporaryNodeId pending, int[] pendingChildren)
        {
            if (pending.IsTerminal)
            {
                return pending;
            }

            ReadOnlySpan<int> child = pendingChildren.AsSpan(pending.Index * _arrayLength, _arrayLength);
            return new TemporaryNodeId(pending.Level, AddState(child, pending.Level));
        }

        /// <summary>Follows one branch of one state and returns where it lands.</summary>
        private TemporaryNodeId Branch(ReadOnlySpan<int> state, int level, int value)
        {
            Span<int> child = _scratch;
            state.CopyTo(child);
            int childLevel = _spec.GetChild(child, level, value);

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
        private int AddState(ReadOnlySpan<int> state, int level)
        {
            ArrayLevelStateTable table =
                _tables[level] ??= new ArrayLevelStateTable(_arrayLength, InitialLevelCapacity, _layout);

            int before = table.Count;
            int index = table.GetOrAdd(state);

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

            return index;
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
                "fits, or make the spec merge more states.",
                thrownByExpander: true);

        private BuildLimitExceededException FrontierSizeExceeded(int level, int frontierSize) =>
            new BuildLimitExceededException(
                BuildLimit.FrontierSize,
                _maxFrontierSize,
                level,
                $"The build passed BuildOptions.MaxFrontierSize ({_maxFrontierSize}) while filling level {level}: " +
                $"that level already holds {frontierSize} distinct state(s). Raise the limit if the build " +
                "nearly fits, or reduce the width (a state that keeps less, or a better item order).",
                thrownByExpander: true);
    }
}
