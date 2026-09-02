using System;
using System.Diagnostics;
using System.Threading;
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
    /// Iterative for the same reason as <see cref="TopDownExpander{TSpec, TState}"/> (docs/PLAN.md
    /// §4.5): a build is as deep as the item count. See that type for the rest of the rationale
    /// (one table per pending level, struct spec); this one only differs in how a state is stored.
    /// </remarks>
    internal sealed class ArrayTopDownExpander<TSpec>
        where TSpec : struct, IArrayDdSpec
    {
        private const int CancellationCheckInterval = 512;
        private const int InitialLevelCapacity = 64;

        private readonly TSpec _spec;
        private readonly int _arrayLength;
        private readonly int _rootLevel;
        private readonly int _maxNodeCount;
        private readonly int _maxFrontierSize;
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

        /// <summary>Turns every state of one level into a node, registering the children one level's worth ahead.</summary>
        private void ExpandLevel(ArrayLevelStateTable table, int level)
        {
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

                table.CopyStateTo(index, _current);
                nodes[index] = new TemporaryNode(
                    Branch(_current, level, 0),
                    Branch(_current, level, 1));
            }

            _levels[level] = nodes;
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
