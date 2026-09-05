using System;
using ZDD.Net.Core;
using ZDD.Net.Frontier;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// Cost filters on an already-built <see cref="Zdd"/>: Graphillion's <c>cost_le</c> (M6-8),
    /// translated to .NET naming (docs/PLAN.md &#167;8). Each one is a thin wrapper around
    /// <see cref="ZddExtensions.Subset{TSpec, TState}"/> with a <see cref="LinearConstraintSpec"/>, so
    /// it is applied during the frontier walk that filters <c>zdd</c> rather than after materializing
    /// it &#8212; the intermediate diagram never grows past what the filtered result needs.
    /// </summary>
    public static class LinearConstraintExtensions
    {
        /// <summary>Keeps only the sets of <paramref name="zdd"/> whose total cost is at most <paramref name="bound"/>.</summary>
        /// <param name="zdd">The family to filter.</param>
        /// <param name="costs">Per-item cost <c>a[i]</c>, indexed by item; may contain negatives.</param>
        /// <param name="bound">The maximum total cost <c>b</c>.</param>
        /// <param name="options">Limits, cancellation and progress for the build; defaults when null.</param>
        /// <returns>The subset of <paramref name="zdd"/> satisfying <c>Σ costs[i] x[i] &lt;= bound</c>.</returns>
        public static Zdd CostAtMost(this Zdd zdd, ReadOnlySpan<long> costs, long bound, BuildOptions? options = null) =>
            zdd.Subset<LinearConstraintSpec, long>(new LinearConstraintSpec(costs, LinearConstraintOperator.LessOrEqual, bound), options);

        /// <summary>Keeps only the sets of <paramref name="zdd"/> whose total cost is at least <paramref name="bound"/>.</summary>
        /// <param name="zdd">The family to filter.</param>
        /// <param name="costs">Per-item cost <c>a[i]</c>, indexed by item; may contain negatives.</param>
        /// <param name="bound">The minimum total cost <c>b</c>.</param>
        /// <param name="options">Limits, cancellation and progress for the build; defaults when null.</param>
        /// <returns>The subset of <paramref name="zdd"/> satisfying <c>Σ costs[i] x[i] &gt;= bound</c>.</returns>
        public static Zdd CostAtLeast(this Zdd zdd, ReadOnlySpan<long> costs, long bound, BuildOptions? options = null) =>
            zdd.Subset<LinearConstraintSpec, long>(new LinearConstraintSpec(costs, LinearConstraintOperator.GreaterOrEqual, bound), options);

        /// <summary>Keeps only the sets of <paramref name="zdd"/> whose total cost is exactly <paramref name="value"/>.</summary>
        /// <param name="zdd">The family to filter.</param>
        /// <param name="costs">Per-item cost <c>a[i]</c>, indexed by item; may contain negatives.</param>
        /// <param name="value">The required total cost <c>b</c>.</param>
        /// <param name="options">Limits, cancellation and progress for the build; defaults when null.</param>
        /// <returns>The subset of <paramref name="zdd"/> satisfying <c>Σ costs[i] x[i] == value</c>.</returns>
        public static Zdd CostEquals(this Zdd zdd, ReadOnlySpan<long> costs, long value, BuildOptions? options = null) =>
            zdd.Subset<LinearConstraintSpec, long>(new LinearConstraintSpec(costs, LinearConstraintOperator.Equal, value), options);
    }
}
