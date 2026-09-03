using ZDD.Net.Core;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// Adapts an already-built <see cref="Zdd"/> into an <see cref="IDdSpec{TState}"/>, so it can take
    /// part in <see cref="AndSpec{TSpec1, TState1, TSpec2, TState2}"/> composition like any other spec.
    /// This is what <see cref="DdSpecExtensions.Subset{TSpec, TState}"/> builds on: <c>zdd.Subset(spec)</c>
    /// is exactly <c>FrontierBuilder.Build(zdd.Manager, new ZddSpec(zdd).And(spec))</c> (TdZdd's <c>zddSubset</c>).
    /// </summary>
    /// <remarks>
    /// The state is simply the current node id: a <see cref="Zdd"/> is already a canonical, reduced
    /// diagram, so its own node levels already skip exactly where <see cref="IDdSpec{TState}"/> allows
    /// skipping (zero-suppression), and following <c>Lo</c>/<c>Hi</c> needs no extra bookkeeping.
    /// </remarks>
    public readonly struct ZddSpec : IDdSpec<int>
    {
        private readonly ZddManager _manager;
        private readonly int _rootId;

        /// <summary>Wraps <paramref name="zdd"/> as a spec.</summary>
        /// <param name="zdd">The family to wrap.</param>
        /// <exception cref="System.InvalidOperationException"><paramref name="zdd"/> is <c>default(Zdd)</c>.</exception>
        /// <exception cref="System.ObjectDisposedException">The owning manager has been disposed.</exception>
        public ZddSpec(Zdd zdd)
        {
            _manager = zdd.Manager;
            _ = _manager.Table; // triggers ObjectDisposedException here rather than on first use
            _rootId = zdd.Id;
        }

        /// <inheritdoc/>
        public int GetRoot(ref int nodeId)
        {
            nodeId = _rootId;
            return LevelOf(nodeId);
        }

        /// <inheritdoc/>
        public int GetChild(ref int nodeId, int level, int value)
        {
            ref ZddNode node = ref _manager.Table.Nodes[nodeId];
            int childId = value == 0 ? node.Lo : node.Hi;
            nodeId = childId;
            return LevelOf(childId);
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
