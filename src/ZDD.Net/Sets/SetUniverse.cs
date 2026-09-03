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

        /// <summary>Converts item indices back to a set of elements.</summary>
        internal HashSet<T> ToElementSet(int[] indices)
        {
            var set = new HashSet<T>(indices.Length, Comparer);

            foreach (int index in indices)
            {
                set.Add(_elements[index]);
            }

            return set;
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
