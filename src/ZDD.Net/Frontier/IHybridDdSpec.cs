using System;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// A frontier-search specification whose state is a scalar plus a fixed-length <see cref="int"/> array —
    /// a mate array over the frontier with a counter beside it. Levels and return values are those of
    /// <see cref="IDdSpec{TState}"/>.
    /// </summary>
    /// <typeparam name="TScalar">The scalar half of the state; use a <c>struct</c>, as for <see cref="IDdSpec{TState}"/>.</typeparam>
    /// <remarks>
    /// Two states match when <see cref="ScalarEquals"/> accepts the scalars and the arrays agree element-wise,
    /// which keeps the array comparison out of user code while the scalar may still ignore stale fields.
    /// </remarks>
    public interface IHybridDdSpec<TScalar>
    {
        /// <summary>The number of <see cref="int"/> slots in the array half; read once, before construction starts.</summary>
        int ArrayLength { get; }

        /// <summary>Initializes the root state and returns its level.</summary>
        /// <param name="scalar">Receives the scalar half; default-initialized on entry.</param>
        /// <param name="array">Receives the array half: <see cref="ArrayLength"/> slots, zero-filled, valid only during the call.</param>
        /// <returns>The root's level, or <see cref="DdResult.False"/> / <see cref="DdResult.True"/>.</returns>
        int GetRoot(ref TScalar scalar, Span<int> array);

        /// <summary>Moves the state along the <paramref name="value"/> branch and returns the child's level.</summary>
        /// <param name="scalar">The scalar at <paramref name="level"/> on entry, the child's on return.</param>
        /// <param name="array">The array at <paramref name="level"/> on entry, the child's on return; a per-branch copy, so overwrite it in place.</param>
        /// <param name="level">The level being decided; the item is <c>VariableCount - level</c>.</param>
        /// <param name="value">The branch taken: <c>0</c> excludes the item, <c>1</c> includes it.</param>
        /// <returns>The child's level, in <c>1 .. level - 1</c>, or <see cref="DdResult.False"/> / <see cref="DdResult.True"/>.</returns>
        int GetChild(ref TScalar scalar, Span<int> array, int level, int value);

        /// <summary>Tests whether two scalars at the same level are interchangeable from here on.</summary>
        /// <param name="left">The left scalar.</param>
        /// <param name="right">The right scalar.</param>
        bool ScalarEquals(in TScalar left, in TScalar right);

        /// <summary>
        /// Returns a hash code for the scalar half; scalars that <see cref="ScalarEquals"/> accepts must hash
        /// equally. The builder combines it with the hash of the array half.
        /// </summary>
        /// <param name="scalar">The scalar to hash.</param>
        int ScalarHashCode(in TScalar scalar);
    }
}
