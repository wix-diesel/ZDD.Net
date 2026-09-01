using System;
using System.Diagnostics;
using System.Threading;
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

        private readonly TSpec _spec;
        private readonly int _rootLevel;
        private readonly int _maxNodeCount;
        private readonly int _maxFrontierSize;
        private readonly CancellationToken _cancellationToken;
        private readonly IProgress<BuildProgress>? _progress;

        /// <summary>The state table of each level that still has states to expand; null once dropped.</summary>
        private readonly StructLevelStateTable<TSpec, TState>?[] _tables;

        /// <summary>The nodes of each expanded level, in state-table index order.</summary>
        private readonly TemporaryNode[][] _levels;

        private long _nodeCount;

        private TopDownExpander(TSpec spec, int rootLevel, BuildOptions options)
        {
            _spec = spec;
            _rootLevel = rootLevel;
            _maxNodeCount = options.MaxNodeCount;
            _maxFrontierSize = options.MaxFrontierSize;
            _cancellationToken = options.CancellationToken;
            _progress = options.Progress;
            _tables = new StructLevelStateTable<TSpec, TState>?[rootLevel + 1];
            _levels = new TemporaryNode[rootLevel + 1][];

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

            TState rootState = default!;
            int rootLevel = spec.GetRoot(ref rootState);

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

            TopDownExpander<TSpec, TState> expander = new TopDownExpander<TSpec, TState>(spec, rootLevel, effective);

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
        private void ExpandLevel(StructLevelStateTable<TSpec, TState> table, int level)
        {
            // Expanding a level only ever adds to lower levels, so the width is fixed once it starts.
            int width = table.Count;
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
