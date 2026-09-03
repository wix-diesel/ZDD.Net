namespace ZDD.Net.Frontier
{
    /// <summary>Fluent composition of two <see cref="IDdSpec{TState}"/> specs.</summary>
    /// <remarks>
    /// <c>TStateA</c>/<c>TStateB</c> only ever appear in a <c>where</c> clause here, never in a
    /// parameter or return type, so the compiler cannot infer them the way it infers <c>TSpecA</c>/
    /// <c>TSpecB</c> from the arguments; call sites give all four type arguments explicitly
    /// (<c>a.And&lt;SpecA, StateA, SpecB, StateB&gt;(b)</c>), the same as <see cref="FrontierBuilder.Build{TSpec, TState}(Core.ZddManager, TSpec, BuildOptions)"/>.
    /// </remarks>
    public static class SpecExtensions
    {
        /// <summary>The conjunction of <paramref name="specA"/> and <paramref name="specB"/>. See <see cref="AndSpec{TSpecA, TStateA, TSpecB, TStateB}"/>.</summary>
        public static AndSpec<TSpecA, TStateA, TSpecB, TStateB> And<TSpecA, TStateA, TSpecB, TStateB>(
            this TSpecA specA, TSpecB specB)
            where TSpecA : struct, IDdSpec<TStateA>
            where TSpecB : struct, IDdSpec<TStateB>
        {
            return new AndSpec<TSpecA, TStateA, TSpecB, TStateB>(specA, specB);
        }

        /// <summary>The disjunction of <paramref name="specA"/> and <paramref name="specB"/>. See <see cref="OrSpec{TSpecA, TStateA, TSpecB, TStateB}"/>.</summary>
        public static OrSpec<TSpecA, TStateA, TSpecB, TStateB> Or<TSpecA, TStateA, TSpecB, TStateB>(
            this TSpecA specA, TSpecB specB)
            where TSpecA : struct, IDdSpec<TStateA>
            where TSpecB : struct, IDdSpec<TStateB>
        {
            return new OrSpec<TSpecA, TStateA, TSpecB, TStateB>(specA, specB);
        }
    }
}
