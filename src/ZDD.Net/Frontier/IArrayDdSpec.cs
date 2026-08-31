using System;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// A frontier-search specification whose state is a fixed-length <see cref="int"/> array,
    /// for states whose size is known only at run time (mate and component arrays, counters per vertex).
    /// </summary>
    /// <remarks>
    /// Equality and hashing are element-wise over the whole array, so the builder needs no
    /// <c>StateEquals</c> here — but for the same reason every slot must be normalized: a slot that no
    /// longer matters has to be cleared to a fixed value, otherwise equivalent states stop merging.
    /// The level convention and the return-value encoding are those of <see cref="IDdSpec{TState}"/>.
    /// </remarks>
    public interface IArrayDdSpec
    {
        /// <summary>The number of <see cref="int"/> slots in a state.</summary>
        /// <remarks>Read once before construction starts; it must not change afterwards.</remarks>
        int ArrayLength { get; }

        /// <summary>Initializes the root state and returns its level.</summary>
        /// <param name="state">
        /// Receives the root state. Exactly <see cref="ArrayLength"/> slots, zero-filled on entry, and
        /// valid only for the duration of the call — the builder owns the storage, so never store the span.
        /// </param>
        /// <returns>The level of the root, or <see cref="DdResult.False"/> / <see cref="DdResult.True"/>.</returns>
        int GetRoot(Span<int> state);

        /// <summary>Moves <paramref name="state"/> along the <paramref name="value"/> branch and returns the child's level.</summary>
        /// <param name="state">On entry the state at <paramref name="level"/>, on return the child's state; a private copy, so edit it in place.</param>
        /// <param name="level">The level being decided; the item is <c>VariableCount - level</c>.</param>
        /// <param name="value">The branch taken: <c>0</c> excludes the item, <c>1</c> includes it.</param>
        /// <returns>
        /// The child's level, strictly less than <paramref name="level"/>, or
        /// <see cref="DdResult.False"/> / <see cref="DdResult.True"/>.
        /// </returns>
        int GetChild(Span<int> state, int level, int value);
    }
}
