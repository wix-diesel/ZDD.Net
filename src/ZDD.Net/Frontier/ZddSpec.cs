using ZDD.Net.Core;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// Adapts an already-built <see cref="Zdd"/> into an <see cref="IDdSpec{TState}"/>, so it can take
    /// part in a composition (<see cref="AndSpec{TSpecA, TStateA, TSpecB, TStateB}"/> /
    /// <see cref="OrSpec{TSpecA, TStateA, TSpecB, TStateB}"/>) exactly like any hand-written spec.
    /// </summary>
    /// <remarks>
    /// The state is simply the current node's ID within the ZDD's own manager; <c>GetChild</c> follows
    /// the node's <c>Lo</c>/<c>Hi</c> edge, which already encodes TdZdd-style level skipping (an
    /// internal ZDD never has a redundant node for an excluded item), so no extra bookkeeping is
    /// needed here. This is the basis of <see cref="ZddExtensions.Subset{TSpec, TState}"/> — a
    /// <c>Subset</c> is exactly <see cref="AndSpec{TSpecA, TStateA, TSpecB, TStateB}"/> between this
    /// adapter and the filtering spec.
    /// </remarks>
    public readonly struct ZddSpec : IDdSpec<int>
    {
        private readonly ZddManager _manager;
        private readonly int _rootId;

        /// <summary>Creates a spec that walks <paramref name="zdd"/>.</summary>
        /// <exception cref="System.InvalidOperationException"><paramref name="zdd"/> is <c>default(Zdd)</c>.</exception>
        public ZddSpec(Zdd zdd)
        {
            _manager = zdd.Manager;
            _rootId = zdd.Id;
        }

        /// <inheritdoc/>
        public int GetRoot(ref int nodeId)
        {
            nodeId = _rootId;
            return LevelOf(_rootId);
        }

        /// <inheritdoc/>
        public int GetChild(ref int nodeId, int level, int value)
        {
            ref ZddNode node = ref _manager.Table.Nodes[nodeId];
            int child = value == 0 ? node.Lo : node.Hi;
            nodeId = child;
            return LevelOf(child);
        }

        /// <inheritdoc/>
        public bool StateEquals(in int left, in int right) => left == right;

        /// <inheritdoc/>
        public int StateHashCode(in int state) => state;

        private int LevelOf(int nodeId)
        {
            if (nodeId == NodeTable.Bottom)
            {
                return DdResult.False;
            }

            if (nodeId == NodeTable.Top)
            {
                return DdResult.True;
            }

            return _manager.Table.Nodes[nodeId].Level;
        }
    }
}
