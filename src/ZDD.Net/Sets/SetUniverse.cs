using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ZDD.Net.Core;
using ZDD.Net.Internal;

namespace ZDD.Net.Sets
{
    /// <summary>
    /// The element &#8596; ZDD-variable mapping shared by a family of <see cref="SetSet{T}"/>
    /// instances. Owns the <see cref="ZddManager"/> it hands out variable indices for.
    /// </summary>
    /// <typeparam name="T">The element type. Compared with <see cref="Comparer"/>.</typeparam>
    /// <example>
    /// <code>
    /// SetUniverse&lt;string&gt; universe = new SetUniverse&lt;string&gt;(new[] { "a", "b", "c" });
    ///
    /// // Both families share `universe`, so they can be combined directly.
    /// SetSet&lt;string&gt; f = SetSet&lt;string&gt;.FromSets(universe, new[] { new[] { "a" }, new[] { "a", "b" } });
    /// SetSet&lt;string&gt; g = SetSet&lt;string&gt;.FromSets(universe, new[] { new[] { "a" }, new[] { "b", "c" } });
    /// SetSet&lt;string&gt; union = f | g;
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>
    /// Elements are indexed 0 .. <see cref="Count"/> - 1 in first-seen order from the constructor's
    /// <c>elements</c> sequence, deduplicated by <see cref="Comparer"/>; that index is also the
    /// element's <see cref="ZddManager"/> item index (<see cref="Manager"/>'s variable count equals
    /// <see cref="Count"/>). Only <see cref="SetSet{T}"/> instances built from the very same
    /// <see cref="SetUniverse{T}"/> instance can be combined with each other.
    /// </para>
    /// <para>Immutable once constructed: the element set and the manager's variable count never change.</para>
    /// </remarks>
    public sealed class SetUniverse<T>
        where T : notnull
    {
        private readonly T[] _elements;
        private readonly Dictionary<T, int> _indexOf;
        private readonly ReadOnlyCollection<T> _elementsView;

        /// <summary>Creates a universe from a set of elements, allocating a fresh <see cref="ZddManager"/> sized to fit them.</summary>
        /// <param name="elements">The elements; duplicates (per <paramref name="comparer"/>) are dropped, keeping the first occurrence.</param>
        /// <param name="comparer">Equality comparer for elements; <see cref="EqualityComparer{T}.Default"/> if <see langword="null"/>.</param>
        /// <param name="managerOptions">Tuning knobs forwarded to the <see cref="ZddManager"/> constructor; <see langword="null"/> uses defaults.</param>
        /// <exception cref="ArgumentNullException"><paramref name="elements"/> is <see langword="null"/>.</exception>
        public SetUniverse(IEnumerable<T> elements, IEqualityComparer<T>? comparer = null, ZddManagerOptions? managerOptions = null)
        {
            ArgumentNullException.ThrowIfNull(elements);

            Comparer = comparer ?? EqualityComparer<T>.Default;

            var list = new List<T>();
            var indexOf = new Dictionary<T, int>(Comparer);

            foreach (T element in elements)
            {
                if (indexOf.TryAdd(element, list.Count))
                {
                    list.Add(element);
                }
            }

            _elements = list.ToArray();
            _indexOf = indexOf;
            _elementsView = new ReadOnlyCollection<T>(_elements);

            Manager = new ZddManager(_elements.Length, managerOptions);
        }

        /// <summary>The manager that owns every <see cref="Zdd"/> built over this universe.</summary>
        public ZddManager Manager { get; }

        /// <summary>The equality comparer used to deduplicate and look up elements.</summary>
        public IEqualityComparer<T> Comparer { get; }

        /// <summary>The elements, in index order (element at position <c>i</c> has item index <c>i</c>).</summary>
        public IReadOnlyList<T> Elements => _elementsView;

        /// <summary>The number of distinct elements, equal to <see cref="ZddManager.VariableCount"/> of <see cref="Manager"/>.</summary>
        public int Count => _elements.Length;

        /// <summary>Returns whether <paramref name="element"/> is part of this universe.</summary>
        public bool Contains(T element) => _indexOf.ContainsKey(element);

        /// <summary>Returns the item index assigned to <paramref name="element"/>.</summary>
        /// <exception cref="ArgumentException"><paramref name="element"/> is not part of this universe.</exception>
        public int IndexOf(T element)
        {
            if (!_indexOf.TryGetValue(element, out int index))
            {
                ThrowHelper.ThrowArgumentException(nameof(element), $"Element '{element}' is not part of this universe.");
            }

            return index;
        }

        /// <summary>Returns the element assigned item index <paramref name="index"/> (the inverse of <see cref="IndexOf(T)"/>).</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside 0 .. <see cref="Count"/> - 1.</exception>
        public T ElementAt(int index)
        {
            if ((uint)index >= (uint)_elements.Length)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(nameof(index), $"'{nameof(index)}' must be in 0 .. {_elements.Length - 1}, but was {index}.");
            }

            return _elements[index];
        }

        /// <summary>
        /// Returns a new universe with <paramref name="additionalElements"/> appended after this
        /// universe's own elements (M6-6, issue #141). This universe, its <see cref="Manager"/>, and
        /// every <see cref="SetSet{T}"/> built over it are left untouched and keep working.
        /// </summary>
        /// <param name="additionalElements">
        /// Extra elements to add. An element already in this universe (per <see cref="Comparer"/>)
        /// is dropped, keeping its original index; duplicates among <paramref name="additionalElements"/>
        /// themselves keep only the first occurrence &#8212; the same dedup rule the constructor applies.
        /// </param>
        /// <returns>
        /// A universe whose first <see cref="Count"/> elements are this universe's, in the same order
        /// (so they keep the same item index), followed by the newly seen elements of
        /// <paramref name="additionalElements"/> &#8212; which may add nothing new (an empty or
        /// fully-duplicate <paramref name="additionalElements"/> still returns a same-size universe on a
        /// fresh manager, not this one). Because item indices are stable variable identities (B7), this
        /// universe's <see cref="ZddManager"/> can't simply grow in place: the result always gets a fresh
        /// manager instead (B19) &#8212; move a <see cref="SetSet{T}"/> onto it with
        /// <see cref="SetSet{T}.ToUniverse"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="additionalElements"/> is <see langword="null"/>.</exception>
        public SetUniverse<T> Extend(IEnumerable<T> additionalElements)
        {
            ArgumentNullException.ThrowIfNull(additionalElements);

            var combined = new List<T>(_elements);
            combined.AddRange(additionalElements);

            return new SetUniverse<T>(combined, Comparer);
        }

        /// <summary>Converts a sequence of elements to their item indices, in the given order (not deduplicated or sorted).</summary>
        /// <exception cref="ArgumentNullException"><paramref name="elements"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">An element is not part of this universe.</exception>
        internal int[] ToIndices(IEnumerable<T> elements)
        {
            ArgumentNullException.ThrowIfNull(elements);

            T[] array = elements as T[] ?? System.Linq.Enumerable.ToArray(elements);
            var indices = new int[array.Length];

            for (int i = 0; i < array.Length; i++)
            {
                indices[i] = IndexOf(array[i]);
            }

            return indices;
        }

        /// <summary>
        /// Converts item indices back to a set of elements, preserving <paramref name="indices"/>'s
        /// order (ascending, per <see cref="Zdd"/>'s convention) so enumeration is deterministic.
        /// </summary>
        internal IReadOnlySet<T> ToElementSet(int[] indices)
        {
            var ordered = new T[indices.Length];

            for (int i = 0; i < indices.Length; i++)
            {
                ordered[i] = _elements[indices[i]];
            }

            return new ElementSet<T>(ordered, Comparer);
        }

        /// <summary>
        /// Builds a per-item value array (indexed like <see cref="Manager"/>'s variables) from a
        /// dictionary keyed by element, as required by <see cref="Zdd"/>'s weight/probability APIs.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="values"/> has no entry for some universe element.</exception>
        internal TValue[] ToValueArray<TValue>(IReadOnlyDictionary<T, TValue> values, string paramName)
        {
            ArgumentNullException.ThrowIfNull(values);

            var array = new TValue[_elements.Length];

            for (int i = 0; i < _elements.Length; i++)
            {
                T element = _elements[i];

                if (!values.TryGetValue(element, out TValue? value))
                {
                    ThrowHelper.ThrowArgumentException(paramName, $"Missing a value for universe element '{element}'.");
                }

                array[i] = value;
            }

            return array;
        }
    }
}
