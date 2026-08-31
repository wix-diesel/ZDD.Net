using System.Runtime.CompilerServices;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Packs a binary operation's subproblem (a pair of node IDs) into the single <c>long</c> key
    /// used by <see cref="OperationWorkspace"/>.
    /// </summary>
    /// <remarks>
    /// The packed key is always non-negative, as required by <see cref="OperationWorkspace"/>
    /// (negative keys mark "compose"). For commutative operations, operands are sorted ascending
    /// before packing so that mirrored subproblems (e.g. <c>(f0, g)</c> vs <c>(g, f0)</c>) hit the
    /// same cache entry; <see cref="OperationCache"/> normalizes with the same predicate, keeping
    /// both sides' key semantics consistent.
    /// </remarks>
    internal static class OperationKey
    {
        /// <summary>Sentinel meaning "no subproblem here". Keys are always non-negative, so this never collides.</summary>
        public const long None = -1;

        /// <summary>Packs two node IDs into a single non-negative <c>long</c>.</summary>
        /// <param name="op">The operation kind; commutative operations get their operands normalized.</param>
        /// <param name="f">Left operand's node ID.</param>
        /// <param name="g">Right operand's node ID.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Of(ZddOperation op, int f, int g)
        {
            if (f > g && ZddOperations.IsCommutative(op))
            {
                (f, g) = (g, f);
            }

            return (long)(((ulong)(uint)f << 32) | (uint)g);
        }

        /// <summary>The left operand packed into the key.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int LeftOf(long key) => (int)((ulong)key >> 32);

        /// <summary>The right operand packed into the key.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int RightOf(long key) => (int)key;
    }
}
