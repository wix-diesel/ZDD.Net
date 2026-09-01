using ZDD.Net.Core;
using ZDD.Net.Internal;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// Turns a frontier-search specification into a canonical <see cref="Zdd"/>: the top-down
    /// expansion (<see cref="TopDownExpander{TSpec, TState}"/> / <see cref="ArrayTopDownExpander{TSpec}"/>)
    /// followed by the bottom-up reduction into the manager's Core tables (<see cref="BottomUpReducer"/>).
    /// </summary>
    /// <remarks>
    /// This is the library's central entry point: write an <see cref="IDdSpec{TState}"/> (or
    /// <see cref="IArrayDdSpec"/> / <see cref="IHybridDdSpec{TScalar}"/>) and call one of the
    /// <c>Build</c> overloads to get a <see cref="Zdd"/> that every Core operation (<c>Count</c>,
    /// enumeration, <c>Sample</c>, <c>MaxWeight</c>, ...) works on unchanged.
    /// </remarks>
    public static class FrontierBuilder
    {
        /// <summary>Builds a <see cref="Zdd"/> from a fixed-<c>struct</c>-state spec.</summary>
        /// <typeparam name="TSpec">The spec type; a <c>struct</c>, so calls devirtualize and inline.</typeparam>
        /// <typeparam name="TState">The state carried between levels.</typeparam>
        /// <param name="manager">The manager the resulting family, and every node it needs, belongs to.</param>
        /// <param name="spec">The specification to unroll.</param>
        /// <param name="options">Limits, cancellation and progress for the top-down pass; defaults when null.</param>
        /// <returns>The family <paramref name="spec"/> describes, canonical within <paramref name="manager"/>.</returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="manager"/> is null.</exception>
        /// <exception cref="System.InvalidOperationException">
        /// The spec's root level exceeds <paramref name="manager"/>'s <see cref="ZddManager.VariableCount"/>.
        /// </exception>
        /// <exception cref="BuildLimitExceededException">A limit of <paramref name="options"/> was passed.</exception>
        /// <exception cref="System.OperationCanceledException">The options' token was cancelled.</exception>
        /// <exception cref="System.ObjectDisposedException"><paramref name="manager"/> has been disposed.</exception>
        public static Zdd Build<TSpec, TState>(ZddManager manager, TSpec spec, BuildOptions? options = null)
            where TSpec : struct, IDdSpec<TState>
        {
            ThrowHelper.ThrowIfNull(manager, nameof(manager));

            TemporaryNodeTable table = TopDownExpander<TSpec, TState>.Expand(spec, options);
            EnsureFitsManager(manager, table);
            return BottomUpReducer.Reduce(manager, table);
        }

        /// <summary>Builds a <see cref="Zdd"/> from a variable-length array-state spec.</summary>
        /// <typeparam name="TSpec">The spec type; a <c>struct</c>, so calls devirtualize and inline.</typeparam>
        /// <param name="manager">The manager the resulting family, and every node it needs, belongs to.</param>
        /// <param name="spec">The specification to unroll.</param>
        /// <param name="options">Limits, cancellation and progress for the top-down pass; defaults when null.</param>
        /// <returns>The family <paramref name="spec"/> describes, canonical within <paramref name="manager"/>.</returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="manager"/> is null.</exception>
        /// <exception cref="System.InvalidOperationException">
        /// The spec's <see cref="IArrayDdSpec.ArrayLength"/> is negative, or its root level exceeds
        /// <paramref name="manager"/>'s <see cref="ZddManager.VariableCount"/>.
        /// </exception>
        /// <exception cref="BuildLimitExceededException">A limit of <paramref name="options"/> was passed.</exception>
        /// <exception cref="System.OperationCanceledException">The options' token was cancelled.</exception>
        /// <exception cref="System.ObjectDisposedException"><paramref name="manager"/> has been disposed.</exception>
        public static Zdd Build<TSpec>(ZddManager manager, TSpec spec, BuildOptions? options = null)
            where TSpec : struct, IArrayDdSpec
        {
            ThrowHelper.ThrowIfNull(manager, nameof(manager));

            TemporaryNodeTable table = ArrayTopDownExpander<TSpec>.Expand(spec, options);
            EnsureFitsManager(manager, table);
            return BottomUpReducer.Reduce(manager, table);
        }

        /// <summary>
        /// Rejects a table whose root level does not fit <paramref name="manager"/>: a family built
        /// from it would hold nodes at levels <see cref="ZddManager.ItemOf"/> cannot convert back to
        /// an item, which later operations would surface as a confusing failure deep inside Core.
        /// </summary>
        private static void EnsureFitsManager(ZddManager manager, TemporaryNodeTable table)
        {
            if (table.RootLevel > manager.VariableCount)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    $"The spec's root level ({table.RootLevel}) exceeds the manager's VariableCount " +
                    $"({manager.VariableCount}); use a manager with enough variables for the spec, or make " +
                    "the spec return lower levels.");
            }
        }
    }
}
