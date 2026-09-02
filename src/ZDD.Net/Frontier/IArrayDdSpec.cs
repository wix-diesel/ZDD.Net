using System;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// A frontier-search specification whose state is a fixed-length <see cref="int"/> array, for states
    /// sized only at run time (mate and component arrays). Levels and return values are those of
    /// <see cref="IDdSpec{TState}"/>.
    /// </summary>
    /// <remarks>
    /// Equality and hashing are element-wise over the array, so a slot that no longer matters must be
    /// cleared to a fixed value; leftovers keep equivalent states from merging. Slots are stored packed
    /// into one to four bytes each, by the range of the values they hold, so small values cost less.
    /// </remarks>
    public interface IArrayDdSpec
    {
        /// <summary>The number of <see cref="int"/> slots in a state; read once, before construction starts.</summary>
        int ArrayLength { get; }

        /// <summary>Initializes the root state and returns its level.</summary>
        /// <param name="state">Receives the root state: <see cref="ArrayLength"/> slots, zero-filled, valid only during the call.</param>
        /// <returns>The root's level, or <see cref="DdResult.False"/> / <see cref="DdResult.True"/>.</returns>
        int GetRoot(Span<int> state);

        /// <summary>Moves <paramref name="state"/> along the <paramref name="value"/> branch and returns the child's level.</summary>
        /// <param name="state">The state at <paramref name="level"/> on entry, the child's on return; a per-branch copy, so overwrite it in place.</param>
        /// <param name="level">The level being decided; the item is <c>VariableCount - level</c>.</param>
        /// <param name="value">The branch taken: <c>0</c> excludes the item, <c>1</c> includes it.</param>
        /// <returns>The child's level, in <c>1 .. level - 1</c>, or <see cref="DdResult.False"/> / <see cref="DdResult.True"/>.</returns>
        int GetChild(Span<int> state, int level, int value);
    }
}
