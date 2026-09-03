using ZDD.Net.Core;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// <c>spec1.And(spec2)</c> / <c>spec1.Or(spec2)</c> / <c>zdd.Subset(spec)</c> (docs/PLAN.md &#167;6.3):
    /// spec composition that builds the combined family directly, without ever materializing either
    /// side's own family in full. <see cref="And{TSpec1, TState1, TSpec2, TState2}"/> and
    /// <see cref="Or{TSpec1, TState1, TSpec2, TState2}"/> take two <see cref="IDdSpec{TState}"/> operands;
    /// an <see cref="IArrayDdSpec"/> operand (e.g. a graph spec such as <c>PathSpec</c>) joins in via
    /// <see cref="AsSpec{TSpec}"/> first — <c>pathSpec.AsSpec().And(cardinalitySpec)</c> — which bridges
    /// it through <see cref="ArrayDdSpecAdapter{TSpec}"/>. A single overload per operator, rather than one
    /// per operand-kind combination, is deliberate: with several generic overloads differing only in
    /// which parameter is array-vs-scalar, C#'s overload resolution picks whichever needs the least type
    /// inference before checking constraints, not whichever actually satisfies them — so a scalar spec
    /// could resolve to the array overload and fail to compile there instead of finding the right one.
    /// </summary>
    /// <remarks>
    /// Every method here takes a state type parameter (<c>TState1</c>/<c>TState2</c>/<c>TState</c>) that
    /// appears only in a <c>where TSpecN : IDdSpec&lt;TStateN&gt;</c> constraint, never in a parameter's
    /// own type — and C# only infers a type argument from where it is used in a parameter type, not from
    /// a constraint on an already-inferred one. So none of these type arguments are inferred; give all of
    /// them explicitly, e.g. <c>spec1.And&lt;Spec1, int, Spec2, int[]&gt;(spec2)</c>. This matches
    /// <see cref="FrontierBuilder.Build{TSpec, TState}"/>, which needs the same for the same reason.
    /// </remarks>
    public static class DdSpecExtensions
    {
        /// <summary>Wraps <paramref name="spec"/> as an <see cref="IDdSpec{TState}"/>, so it can be passed to <see cref="And{TSpec1, TState1, TSpec2, TState2}"/> / <see cref="Or{TSpec1, TState1, TSpec2, TState2}"/> / <see cref="Subset{TSpec, TState}"/> alongside another spec.</summary>
        /// <typeparam name="TSpec">The array-state spec's type.</typeparam>
        /// <param name="spec">The array-state spec to wrap.</param>
        public static ArrayDdSpecAdapter<TSpec> AsSpec<TSpec>(this TSpec spec)
            where TSpec : struct, IArrayDdSpec
            => new(spec);

        /// <summary>Intersection: the family accepted by both <paramref name="spec1"/> and <paramref name="spec2"/>.</summary>
        public static AndSpec<TSpec1, TState1, TSpec2, TState2> And<TSpec1, TState1, TSpec2, TState2>(
            this TSpec1 spec1, TSpec2 spec2)
            where TSpec1 : struct, IDdSpec<TState1>
            where TSpec2 : struct, IDdSpec<TState2>
            => new(spec1, spec2);

        /// <summary>Union: the family accepted by either <paramref name="spec1"/> or <paramref name="spec2"/>.</summary>
        public static OrSpec<TSpec1, TState1, TSpec2, TState2> Or<TSpec1, TState1, TSpec2, TState2>(
            this TSpec1 spec1, TSpec2 spec2)
            where TSpec1 : struct, IDdSpec<TState1>
            where TSpec2 : struct, IDdSpec<TState2>
            => new(spec1, spec2);

        /// <summary>
        /// Restricts <paramref name="zdd"/> to the subset also accepted by <paramref name="spec"/>, built
        /// directly (TdZdd's <c>zddSubset</c>) — equivalent to <c>spec.And(new ZddSpec(zdd))</c> built
        /// straight into <c>zdd.Manager</c>, without ever building <paramref name="spec"/>'s own family in full.
        /// </summary>
        /// <typeparam name="TSpec">The spec's type.</typeparam>
        /// <typeparam name="TState">The spec's state.</typeparam>
        /// <param name="zdd">The family to restrict.</param>
        /// <param name="spec">The spec giving the extra condition; an <see cref="IArrayDdSpec"/> joins in via <see cref="AsSpec{TSpec}"/>.</param>
        /// <returns>The restricted family, belonging to <c>zdd.Manager</c>.</returns>
        /// <exception cref="System.InvalidOperationException"><paramref name="zdd"/> is <c>default(Zdd)</c>, or the spec's root level exceeds the manager's variable count.</exception>
        /// <exception cref="System.ObjectDisposedException">The owning manager has been disposed.</exception>
        public static Zdd Subset<TSpec, TState>(this Zdd zdd, TSpec spec)
            where TSpec : struct, IDdSpec<TState>
        {
            ZddManager manager = zdd.Manager;
            return FrontierBuilder.Build<AndSpec<ZddSpec, int, TSpec, TState>, PairState<int, TState>>(
                manager, new AndSpec<ZddSpec, int, TSpec, TState>(new ZddSpec(zdd), spec));
        }
    }
}
