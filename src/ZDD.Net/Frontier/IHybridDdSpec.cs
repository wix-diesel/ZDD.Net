using System;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// A frontier-search specification whose state is a scalar plus a fixed-length <see cref="int"/> array —
    /// the usual shape for graph problems: a mate array over the frontier, plus a counter or flag beside it.
    /// </summary>
    /// <typeparam name="TScalar">
    /// The scalar half of the state. A <c>struct</c> is strongly recommended, for the reason given on
    /// <see cref="IDdSpec{TState}"/>.
    /// </typeparam>
    /// <remarks>
    /// Two states are the same when the scalars satisfy <see cref="ScalarEquals"/> and the arrays match
    /// element-wise; splitting the state this way keeps the array comparison out of user code while still
    /// allowing the scalar to ignore fields that no longer matter.
    /// The level convention and the return-value encoding are those of <see cref="IDdSpec{TState}"/>.
    /// </remarks>
    public interface IHybridDdSpec<TScalar>
    {
        /// <summary>The number of <see cref="int"/> slots in the array half of a state.</summary>
        /// <remarks>Read once before construction starts; it must not change afterwards.</remarks>
        int ArrayLength { get; }

        /// <summary>Initializes the root state and returns its level.</summary>
        /// <param name="scalar">Receives the scalar half, default-initialized on entry.</param>
        /// <param name="array">
        /// Receives the array half. Exactly <see cref="ArrayLength"/> slots, zero-filled on entry, and valid
        /// only for the duration of the call.
        /// </param>
        /// <returns>The level of the root, or <see cref="DdResult.False"/> / <see cref="DdResult.True"/>.</returns>
        int GetRoot(ref TScalar scalar, Span<int> array);

        /// <summary>Moves the state along the <paramref name="value"/> branch and returns the child's level.</summary>
        /// <param name="scalar">On entry the scalar at <paramref name="level"/>, on return the child's scalar.</param>
        /// <param name="array">On entry the array at <paramref name="level"/>, on return the child's array; a private copy, so edit it in place.</param>
        /// <param name="level">The level being decided; the item is <c>VariableCount - level</c>.</param>
        /// <param name="value">The branch taken: <c>0</c> excludes the item, <c>1</c> includes it.</param>
        /// <returns>
        /// The child's level, strictly less than <paramref name="level"/>, or
        /// <see cref="DdResult.False"/> / <see cref="DdResult.True"/>.
        /// </returns>
        int GetChild(ref TScalar scalar, Span<int> array, int level, int value);

        /// <summary>Tests whether two scalars at the same level are interchangeable from here on.</summary>
        /// <param name="left">The left scalar.</param>
        /// <param name="right">The right scalar.</param>
        /// <remarks>Must be an equivalence relation and agree with <see cref="ScalarHashCode"/>.</remarks>
        bool ScalarEquals(in TScalar left, in TScalar right);

        /// <summary>Returns a hash code for the scalar half of a state.</summary>
        /// <param name="scalar">The scalar to hash.</param>
        /// <remarks>
        /// Scalars that <see cref="ScalarEquals"/> accepts must hash equally; the builder combines this
        /// with the hash of the array half.
        /// </remarks>
        int ScalarHashCode(in TScalar scalar);
    }
}
