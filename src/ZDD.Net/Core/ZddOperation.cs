namespace ZDD.Net.Core
{
    /// <summary>
    /// Identifies the kind of operation for an entry in the operation cache
    /// (<see cref="OperationCache"/>), which shares one table across all operations.
    /// </summary>
    /// <remarks><see cref="None"/> = 0 is fixed, used as the "empty slot" sentinel; other values may change.</remarks>
    internal enum ZddOperation
    {
        /// <summary>Not an operation; used only as the empty-slot sentinel.</summary>
        None = 0,

        // ---- Binary operations ----

        /// <summary>Union <c>f &#8746; g</c>.</summary>
        Union,

        /// <summary>Intersection <c>f &#8745; g</c>.</summary>
        Intersect,

        /// <summary>Difference <c>f &#8726; g</c>.</summary>
        Difference,

        /// <summary>Symmetric difference <c>f &#8853; g</c>.</summary>
        SymmetricDifference,

        /// <summary>Family product <c>f * g</c>.</summary>
        Product,

        /// <summary>Quotient <c>f / g</c>.</summary>
        Quotient,

        /// <summary>Remainder <c>f % g</c>.</summary>
        Remainder,

        /// <summary>Family of pairwise intersections <c>f &#8851; g</c>.</summary>
        Meet,

        /// <summary>Elements that contain at least one member of <c>g</c>.</summary>
        SupersetsOf,

        /// <summary>Elements contained in at least one member of <c>g</c>.</summary>
        SubsetsOf,

        /// <summary>Elements that are not a subset of any member of <c>g</c>.</summary>
        NonSubsetsOf,

        /// <summary>Elements that are not a superset of any member of <c>g</c>.</summary>
        NonSupersetsOf,

        // ---- Queries that don't build a family ----

        /// <summary>Whether every set in <c>f</c> belongs to <c>g</c>.</summary>
        IsSubsetOf,

        /// <summary>Whether <c>f</c> and <c>g</c> share any set.</summary>
        Overlaps,

        // ---- Unary operations (with or without an item) ----

        /// <summary>Flips membership of <c>item</c> in every element.</summary>
        Change,

        /// <summary>Selects elements containing <c>item</c>, then removes it.</summary>
        OnSet,

        /// <summary>Keeps only elements that don't contain <c>item</c>.</summary>
        OffSet,

        /// <summary>Elements maximal under inclusion.</summary>
        Maximal,

        /// <summary>Elements minimal under inclusion.</summary>
        Minimal,

        /// <summary>The family of hitting sets.</summary>
        HittingSets,

        /// <summary>Complement, <c>2^V &#8726; f</c>.</summary>
        Complement,
    }

    /// <summary>
    /// Predicates about <see cref="ZddOperation"/>, used for cache-key normalization of commutative
    /// operations and for Debug-build argument checks.
    /// </summary>
    internal static class ZddOperations
    {
        /// <summary>Whether swapping the operands of this binary operation leaves the result unchanged.</summary>
        /// <remarks>
        /// Lets the cache normalize <c>(op, f, g)</c> and <c>(op, g, f)</c> to the same key. An
        /// operation belongs here only if its recursive decomposition is genuinely symmetric, not
        /// merely mathematically commutative — misclassifying one here silently returns wrong
        /// results, so anything doubtful stays on the non-commutative side.
        /// </remarks>
        public static bool IsCommutative(ZddOperation op) =>
            op is ZddOperation.Union
                or ZddOperation.Intersect
                or ZddOperation.SymmetricDifference
                or ZddOperation.Product
                or ZddOperation.Meet
                or ZddOperation.Overlaps;

        /// <summary>Whether this is a unary operation (second operand is an item index, or there is none).</summary>
        public static bool IsUnary(ZddOperation op) =>
            op is ZddOperation.Change
                or ZddOperation.OnSet
                or ZddOperation.OffSet
                or ZddOperation.Maximal
                or ZddOperation.Minimal
                or ZddOperation.HittingSets
                or ZddOperation.Complement;
    }
}
