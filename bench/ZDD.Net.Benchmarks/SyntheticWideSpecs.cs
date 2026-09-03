using ZDD.Net.Frontier;

namespace ZDD.Net.Benchmarks
{
    /// <summary>
    /// A wide, sustained frontier with a trivially cheap <c>GetChild</c>: doubles each level
    /// (<c>state = state * 2 + value</c>) until <c>width</c> distinct values are reachable, then plateaus
    /// there for the rest of the build — see <see cref="ParallelFrontierReport"/>'s remarks (M4-3, issue
    /// #46) for why this isolates the state table's own cost from <c>GetChild</c>'s.
    /// </summary>
    internal readonly struct ScratchWideSpec : IDdSpec<int>
    {
        private readonly int _itemCount;
        private readonly int _width;

        public ScratchWideSpec(int itemCount, int width)
        {
            _itemCount = itemCount;
            _width = width;
        }

        public int GetRoot(ref int state)
        {
            state = 0;
            return _itemCount;
        }

        public int GetChild(ref int state, int level, int value)
        {
            state = (state * 2 + value) % _width;
            return level == 1 ? DdResult.True : level - 1;
        }

        public bool StateEquals(in int left, in int right) => left == right;

        public int StateHashCode(in int state) => state;
    }

    /// <summary>
    /// Same shape as <see cref="ScratchWideSpec"/>, but <c>GetChild</c> spends <c>work</c> iterations of
    /// artificial (if pointless) computation before deciding the child state, isolating the opposite
    /// case: a build whose per-state cost really is dominated by <c>GetChild</c>.
    /// </summary>
    internal readonly struct ScratchExpensiveWideSpec : IDdSpec<int>
    {
        private readonly int _itemCount;
        private readonly int _width;
        private readonly int _work;

        public ScratchExpensiveWideSpec(int itemCount, int width, int work)
        {
            _itemCount = itemCount;
            _width = width;
            _work = work;
        }

        public int GetRoot(ref int state)
        {
            state = 0;
            return _itemCount;
        }

        public int GetChild(ref int state, int level, int value)
        {
            // A SplitMix64-shaped churn, purely to burn cycles; folding the low bit into the real state
            // transition (rather than discarding acc) keeps the JIT from proving the loop dead and
            // eliding it.
            long acc = state;
            for (int i = 0; i < _work; i++)
            {
                acc = (acc * 2862933555777941757L + 3037000493L) ^ i;
            }

            state = (int)((state * 2 + value + (acc & 1)) % _width);
            return level == 1 ? DdResult.True : level - 1;
        }

        public bool StateEquals(in int left, in int right) => left == right;

        public int StateHashCode(in int state) => state;
    }
}
