using ZDD.Net.Core;

namespace ZDD.Net.Frontier
{
    /// <summary>Frontier-level operations on an already-built <see cref="Zdd"/>.</summary>
    public static class ZddExtensions
    {
        /// <summary>
        /// Filters <paramref name="zdd"/> down to the sets <paramref name="spec"/> also accepts —
        /// TdZdd's <c>zddSubset</c>. Built directly as one expansion (<see cref="ZddSpec"/> composed
        /// with <paramref name="spec"/> via <see cref="AndSpec{TSpecA, TStateA, TSpecB, TStateB}"/>),
        /// so it never re-materializes <paramref name="zdd"/> as a whole before filtering it.
        /// </summary>
        /// <typeparam name="TSpec">The filtering spec's type.</typeparam>
        /// <typeparam name="TState">The filtering spec's state.</typeparam>
        /// <param name="zdd">The family to filter.</param>
        /// <param name="spec">The spec every kept set must also satisfy.</param>
        /// <param name="options">Limits, cancellation and progress for the build; defaults when null.</param>
        /// <returns>
        /// The family of sets accepted by both <paramref name="zdd"/> and <paramref name="spec"/> —
        /// the same result as <c>zdd.Intersect(FrontierBuilder.Build&lt;TSpec, TState&gt;(zdd.Manager, spec))</c>,
        /// without building <paramref name="spec"/>'s ZDD on its own first.
        /// </returns>
        /// <exception cref="System.InvalidOperationException"><paramref name="zdd"/> is <c>default(Zdd)</c>.</exception>
        /// <exception cref="BuildLimitExceededException">A limit of <paramref name="options"/> was passed.</exception>
        /// <exception cref="System.OperationCanceledException">The options' token was cancelled.</exception>
        public static Zdd Subset<TSpec, TState>(this Zdd zdd, TSpec spec, BuildOptions? options = null)
            where TSpec : struct, IDdSpec<TState>
        {
            ZddManager manager = zdd.Manager;
            AndSpec<ZddSpec, int, TSpec, TState> composed = new AndSpec<ZddSpec, int, TSpec, TState>(new ZddSpec(zdd), spec);
            return FrontierBuilder.Build<AndSpec<ZddSpec, int, TSpec, TState>, AndState<int, TState>>(manager, composed, options);
        }
    }
}
