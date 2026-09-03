using System.Collections;
using System.Collections.Generic;

namespace ZDD.Net.Sets
{
    /// <summary>
    /// A read-only set with deterministic enumeration order, backed by an ordered array plus a
    /// <see cref="HashSet{T}"/> for O(1) membership checks.
    /// </summary>
    /// <remarks>
    /// Returned by <see cref="SetUniverse{T}.ToElementSet(int[])"/> so a <see cref="SetSet{T}"/>
    /// member set's element order is reproducible (ascending item index) &#8212; unlike a bare
    /// <see cref="HashSet{T}"/>, whose enumeration order is a bucket-layout detail, not a contract.
    /// </remarks>
    internal sealed class ElementSet<T> : IReadOnlySet<T>
    {
        private readonly T[] _ordered;
        private readonly HashSet<T> _lookup;

        /// <param name="ordered">Elements in the order this set should enumerate them.</param>
        /// <param name="comparer">Equality comparer backing membership checks.</param>
        internal ElementSet(T[] ordered, IEqualityComparer<T> comparer)
        {
            _ordered = ordered;
            _lookup = new HashSet<T>(ordered, comparer);
        }

        /// <inheritdoc/>
        public int Count => _ordered.Length;

        /// <inheritdoc/>
        public bool Contains(T item) => _lookup.Contains(item);

        /// <inheritdoc/>
        public bool IsProperSubsetOf(IEnumerable<T> other) => _lookup.IsProperSubsetOf(other);

        /// <inheritdoc/>
        public bool IsProperSupersetOf(IEnumerable<T> other) => _lookup.IsProperSupersetOf(other);

        /// <inheritdoc/>
        public bool IsSubsetOf(IEnumerable<T> other) => _lookup.IsSubsetOf(other);

        /// <inheritdoc/>
        public bool IsSupersetOf(IEnumerable<T> other) => _lookup.IsSupersetOf(other);

        /// <inheritdoc/>
        public bool Overlaps(IEnumerable<T> other) => _lookup.Overlaps(other);

        /// <inheritdoc/>
        public bool SetEquals(IEnumerable<T> other) => _lookup.SetEquals(other);

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_ordered).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
