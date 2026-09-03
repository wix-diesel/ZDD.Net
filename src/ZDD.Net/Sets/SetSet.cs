using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using ZDD.Net.Core;
using ZDD.Net.Internal;
using ZDD.Net.Io;

namespace ZDD.Net.Sets
{
    /// <summary>
    /// A family of sets over an arbitrary element type <typeparamref name="T"/> &#8212; a thin
    /// wrapper around <see cref="Core.Zdd"/> that translates between <typeparamref name="T"/> and
    /// the ZDD's <c>int</c> item indices via a shared <see cref="SetUniverse{T}"/>.
    /// </summary>
    /// <typeparam name="T">The element type; compared with <see cref="SetUniverse{T}.Comparer"/>.</typeparam>
    /// <remarks>
    /// <para>
    /// Two <see cref="SetSet{T}"/> instances can only be combined (<see cref="Union"/>, <see cref="Product"/>, ...)
    /// when they share the exact same <see cref="SetUniverse{T}"/> instance &#8212; mixing mappings
    /// would silently produce a meaningless result, so it throws instead, the same way mixing
    /// <see cref="ZddManager"/>s does on <see cref="Zdd"/>.
    /// </para>
    /// <para>
    /// Implements <see cref="IEnumerable{T}">IEnumerable&lt;IReadOnlySet&lt;T&gt;&gt;</see> but
    /// intentionally not <see cref="ICollection{T}"/>, since a family's cardinality does not fit
    /// in <c>int</c> (see <see cref="Count"/>). LINQ's
    /// <see cref="System.Linq.Enumerable.Count{TSource}(IEnumerable{TSource})"/> extension still
    /// resolves fine alongside the <see cref="Count"/> property (they have different
    /// signatures &#8212; no cast or rename needed), but it enumerates every member set and returns
    /// <c>int</c>, so it is slow and overflows for anything but small families. Prefer
    /// <see cref="Count"/> (exact <see cref="BigInteger"/>), <see cref="LongCount"/> (exact
    /// <see cref="long"/>), or <see cref="CountApprox"/> (approximate <see cref="double"/>), all
    /// computed in time proportional to node count rather than family size.
    /// </para>
    /// </remarks>
    public sealed class SetSet<T> : IEnumerable<IReadOnlySet<T>>, IEquatable<SetSet<T>>
        where T : notnull
    {
        internal SetSet(SetUniverse<T> universe, Zdd zdd)
        {
            Universe = universe;
            Zdd = zdd;
        }

        /// <summary>The element &#8596; item-index mapping this family is expressed over.</summary>
        public SetUniverse<T> Universe { get; }

        /// <summary>The underlying ZDD, for callers who want to drop down to the low-level API.</summary>
        public Zdd Zdd { get; }

        /// <summary>Builds a family from explicit member sets, sharing an existing <paramref name="universe"/>.</summary>
        /// <param name="universe">The element mapping; every element used by <paramref name="sets"/> must be part of it.</param>
        /// <param name="sets">The member sets. Duplicate elements within one set are collapsed; duplicate sets are collapsed.</param>
        /// <exception cref="ArgumentNullException"><paramref name="universe"/>, <paramref name="sets"/>, or one of its sets is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">An element is not part of <paramref name="universe"/>.</exception>
        public static SetSet<T> FromSets(SetUniverse<T> universe, IEnumerable<IEnumerable<T>> sets)
        {
            ArgumentNullException.ThrowIfNull(universe);
            ArgumentNullException.ThrowIfNull(sets);

            ZddManager manager = universe.Manager;
            Zdd result = manager.Empty;

            foreach (IEnumerable<T> set in sets)
            {
                ArgumentNullException.ThrowIfNull(set);

                Zdd single = manager.Base;

                foreach (T element in set)
                {
                    single *= manager.Singleton(universe.IndexOf(element));
                }

                result |= single;
            }

            return new SetSet<T>(universe, result);
        }

        /// <summary>Builds a family from explicit member sets, inferring a fresh universe from the elements encountered.</summary>
        /// <param name="sets">The member sets. Duplicate elements within one set are collapsed; duplicate sets are collapsed.</param>
        /// <param name="comparer">Equality comparer for elements; <see cref="EqualityComparer{T}.Default"/> if <see langword="null"/>.</param>
        /// <returns>A family with a new <see cref="SetUniverse{T}"/> whose elements are ordered by first appearance in <paramref name="sets"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="sets"/> or one of its sets is <see langword="null"/>.</exception>
        public static SetSet<T> FromSets(IEnumerable<IEnumerable<T>> sets, IEqualityComparer<T>? comparer = null)
        {
            ArgumentNullException.ThrowIfNull(sets);

            var materialized = new List<T[]>();
            var elements = new List<T>();
            var seen = new HashSet<T>(comparer ?? EqualityComparer<T>.Default);

            foreach (IEnumerable<T> set in sets)
            {
                ArgumentNullException.ThrowIfNull(set);

                T[] array = set as T[] ?? System.Linq.Enumerable.ToArray(set);
                materialized.Add(array);

                foreach (T element in array)
                {
                    if (seen.Add(element))
                    {
                        elements.Add(element);
                    }
                }
            }

            var universe = new SetUniverse<T>(elements, comparer);
            return FromSets(universe, materialized);
        }

        /// <summary>The empty family &#8709; over <paramref name="universe"/> (no member sets).</summary>
        /// <exception cref="ArgumentNullException"><paramref name="universe"/> is <see langword="null"/>.</exception>
        public static SetSet<T> Empty(SetUniverse<T> universe)
        {
            ArgumentNullException.ThrowIfNull(universe);
            return new SetSet<T>(universe, universe.Manager.Empty);
        }

        /// <summary>The power set 2^U of <paramref name="universe"/>: every possible subset of its elements.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="universe"/> is <see langword="null"/>.</exception>
        public static SetSet<T> PowerSet(SetUniverse<T> universe)
        {
            ArgumentNullException.ThrowIfNull(universe);
            return new SetSet<T>(universe, universe.Manager.Empty.Complement());
        }

        /// <summary>The power set 2^U over a fresh universe built from <paramref name="items"/>.</summary>
        /// <param name="items">The universe's elements; order fixes the item-index assignment.</param>
        /// <param name="comparer">Equality comparer for elements; <see cref="EqualityComparer{T}.Default"/> if <see langword="null"/>.</param>
        public static SetSet<T> PowerSet(IEnumerable<T> items, IEqualityComparer<T>? comparer = null) =>
            PowerSet(new SetUniverse<T>(items, comparer));

        /// <summary>The exact number of member sets, in time proportional to node count. See the class remarks on LINQ's <c>Count()</c>.</summary>
        public BigInteger Count => Zdd.Count;

        /// <summary>The number of member sets, approximated as a <see cref="double"/>. Faster than <see cref="Count"/>.</summary>
        public double CountApprox => Zdd.CountApprox;

        /// <summary>The exact number of member sets, as a <see cref="long"/>.</summary>
        /// <exception cref="OverflowException"><see cref="Count"/> does not fit in a <see cref="long"/>.</exception>
        public long LongCount() => checked((long)Count);

        /// <summary>Whether this family has no member sets.</summary>
        public bool IsEmpty => Zdd.IsEmpty;

        /// <summary>Union: sets belonging to either family.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="other"/> does not share this family's <see cref="Universe"/>.</exception>
        public SetSet<T> Union(SetSet<T> other) => Combine(other, static (f, g) => f.Union(g));

        /// <summary>Intersection: sets belonging to both families.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="other"/> does not share this family's <see cref="Universe"/>.</exception>
        public SetSet<T> Intersect(SetSet<T> other) => Combine(other, static (f, g) => f.Intersect(g));

        /// <summary>Difference: sets in this family that are not in <paramref name="other"/>.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="other"/> does not share this family's <see cref="Universe"/>.</exception>
        public SetSet<T> Difference(SetSet<T> other) => Combine(other, static (f, g) => f.Difference(g));

        /// <summary>Symmetric difference: sets belonging to exactly one of the two families.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="other"/> does not share this family's <see cref="Universe"/>.</exception>
        public SetSet<T> SymmetricDifference(SetSet<T> other) => Combine(other, static (f, g) => f.SymmetricDifference(g));

        /// <summary>Union. Same as <see cref="Union"/>.</summary>
        public static SetSet<T> operator |(SetSet<T> left, SetSet<T> right) => left.Union(right);

        /// <summary>Intersection. Same as <see cref="Intersect"/>.</summary>
        public static SetSet<T> operator &(SetSet<T> left, SetSet<T> right) => left.Intersect(right);

        /// <summary>Difference. Same as <see cref="Difference"/>.</summary>
        public static SetSet<T> operator -(SetSet<T> left, SetSet<T> right) => left.Difference(right);

        /// <summary>Symmetric difference. Same as <see cref="SymmetricDifference"/>.</summary>
        public static SetSet<T> operator ^(SetSet<T> left, SetSet<T> right) => left.SymmetricDifference(right);

        /// <summary>Product F * G: one member set from each family, unioned together.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="other"/> does not share this family's <see cref="Universe"/>.</exception>
        public SetSet<T> Product(SetSet<T> other) => Combine(other, static (f, g) => f.Product(g));

        /// <summary>Quotient F / G: what remains of this family after factoring out <paramref name="other"/> (see <see cref="Zdd.Quotient"/>).</summary>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="other"/> does not share this family's <see cref="Universe"/>.</exception>
        public SetSet<T> Quotient(SetSet<T> other) => Combine(other, static (f, g) => f.Quotient(g));

        /// <summary>Meet F &#8851; G: one member set from each family, intersected together.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="other"/> does not share this family's <see cref="Universe"/>.</exception>
        public SetSet<T> Meet(SetSet<T> other) => Combine(other, static (f, g) => f.Meet(g));

        /// <summary>Keeps only member sets that are a superset of some set in <paramref name="other"/>.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="other"/> does not share this family's <see cref="Universe"/>.</exception>
        public SetSet<T> SupersetsOf(SetSet<T> other) => Combine(other, static (f, g) => f.SupersetsOf(g));

        /// <summary>Keeps only member sets that are a subset of some set in <paramref name="other"/>.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="other"/> does not share this family's <see cref="Universe"/>.</exception>
        public SetSet<T> SubsetsOf(SetSet<T> other) => Combine(other, static (f, g) => f.SubsetsOf(g));

        /// <summary>Keeps only the member sets that are maximal under inclusion.</summary>
        public SetSet<T> Maximal() => new SetSet<T>(Universe, Zdd.Maximal());

        /// <summary>Keeps only the member sets that are minimal under inclusion.</summary>
        public SetSet<T> Minimal() => new SetSet<T>(Universe, Zdd.Minimal());

        /// <summary>Returns whether <paramref name="set"/> belongs to this family.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="set"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">An element of <paramref name="set"/> is not part of <see cref="Universe"/>.</exception>
        public bool Contains(IEnumerable<T> set)
        {
            ArgumentNullException.ThrowIfNull(set);
            return Zdd.Contains(Universe.ToIndices(set));
        }

        /// <summary>Returns the <paramref name="index"/>-th (0-based) member set in <paramref name="order"/> order (unranking).</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or at least <see cref="Count"/>.</exception>
        public IReadOnlySet<T> ElementAt(BigInteger index, ZddEnumerationOrder order = ZddEnumerationOrder.Default) =>
            Universe.ToElementSet(Zdd.ElementAt(index, order));

        /// <summary>Returns the rank of <paramref name="set"/> in <paramref name="order"/> order (ranking), or -1 if it is not a member.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="set"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">An element of <paramref name="set"/> is not part of <see cref="Universe"/>.</exception>
        public BigInteger IndexOf(IEnumerable<T> set, ZddEnumerationOrder order = ZddEnumerationOrder.Default)
        {
            ArgumentNullException.ThrowIfNull(set);
            return Zdd.IndexOf(Universe.ToIndices(set), order);
        }

        /// <summary>Picks one member set uniformly at random.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">This family is empty.</exception>
        public IReadOnlySet<T> Sample(Random random) => Universe.ToElementSet(Zdd.Sample(random));

        /// <summary>Picks <paramref name="count"/> member sets, drawn independently and uniformly at random (with replacement).</summary>
        /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
        /// <exception cref="InvalidOperationException">This family is empty.</exception>
        public IReadOnlySet<T>[] Sample(int count, Random random)
        {
            int[][] samples = Zdd.Sample(count, random);
            var result = new IReadOnlySet<T>[samples.Length];

            for (int i = 0; i < samples.Length; i++)
            {
                result[i] = Universe.ToElementSet(samples[i]);
            }

            return result;
        }

        /// <summary>Returns the maximum-weight member set, together with its weight.</summary>
        /// <param name="weights">Per-element weight; must have an entry for every element of <see cref="Universe"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="weights"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="weights"/> is missing an entry for a universe element.</exception>
        /// <exception cref="InvalidOperationException">This family is empty.</exception>
        public (IReadOnlySet<T> Set, int Weight) MaxWeight(IReadOnlyDictionary<T, int> weights) => Wrap(Zdd.MaxWeight(Universe.ToValueArray(weights, nameof(weights))));

        /// <inheritdoc cref="MaxWeight(IReadOnlyDictionary{T, int})"/>
        public (IReadOnlySet<T> Set, long Weight) MaxWeight(IReadOnlyDictionary<T, long> weights) => Wrap(Zdd.MaxWeight(Universe.ToValueArray(weights, nameof(weights))));

        /// <inheritdoc cref="MaxWeight(IReadOnlyDictionary{T, int})"/>
        public (IReadOnlySet<T> Set, double Weight) MaxWeight(IReadOnlyDictionary<T, double> weights) => Wrap(Zdd.MaxWeight(Universe.ToValueArray(weights, nameof(weights))));

        /// <summary>Returns the minimum-weight member set, together with its weight.</summary>
        /// <param name="weights">Per-element weight; must have an entry for every element of <see cref="Universe"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="weights"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="weights"/> is missing an entry for a universe element.</exception>
        /// <exception cref="InvalidOperationException">This family is empty.</exception>
        public (IReadOnlySet<T> Set, int Weight) MinWeight(IReadOnlyDictionary<T, int> weights) => Wrap(Zdd.MinWeight(Universe.ToValueArray(weights, nameof(weights))));

        /// <inheritdoc cref="MinWeight(IReadOnlyDictionary{T, int})"/>
        public (IReadOnlySet<T> Set, long Weight) MinWeight(IReadOnlyDictionary<T, long> weights) => Wrap(Zdd.MinWeight(Universe.ToValueArray(weights, nameof(weights))));

        /// <inheritdoc cref="MinWeight(IReadOnlyDictionary{T, int})"/>
        public (IReadOnlySet<T> Set, double Weight) MinWeight(IReadOnlyDictionary<T, double> weights) => Wrap(Zdd.MinWeight(Universe.ToValueArray(weights, nameof(weights))));

        /// <summary>Returns the <paramref name="k"/> highest-weight member sets, sorted by descending weight.</summary>
        /// <param name="weights">Per-element weight; must have an entry for every element of <see cref="Universe"/>.</param>
        /// <param name="k">Number of sets to return; 0 or more.</param>
        /// <exception cref="ArgumentNullException"><paramref name="weights"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="weights"/> is missing an entry for a universe element.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="k"/> is negative.</exception>
        public (IReadOnlySet<T> Set, int Weight)[] TopK(IReadOnlyDictionary<T, int> weights, int k) => Wrap(Zdd.TopK(Universe.ToValueArray(weights, nameof(weights)), k));

        /// <inheritdoc cref="TopK(IReadOnlyDictionary{T, int}, int)"/>
        public (IReadOnlySet<T> Set, long Weight)[] TopK(IReadOnlyDictionary<T, long> weights, int k) => Wrap(Zdd.TopK(Universe.ToValueArray(weights, nameof(weights)), k));

        /// <inheritdoc cref="TopK(IReadOnlyDictionary{T, int}, int)"/>
        public (IReadOnlySet<T> Set, double Weight)[] TopK(IReadOnlyDictionary<T, double> weights, int k) => Wrap(Zdd.TopK(Universe.ToValueArray(weights, nameof(weights)), k));

        /// <summary>
        /// Returns the probability that a set formed by independently including each universe
        /// element with its given probability belongs to this family.
        /// </summary>
        /// <param name="probabilities">Per-element inclusion probability, each between 0 and 1; must have an entry for every element of <see cref="Universe"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="probabilities"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="probabilities"/> is missing an entry for a universe element.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="probabilities"/> contains a value below 0, above 1, or <see cref="double.NaN"/>.</exception>
        public double Probability(IReadOnlyDictionary<T, double> probabilities) => Zdd.Probability(Universe.ToValueArray(probabilities, nameof(probabilities)));

        /// <summary>
        /// Enumerates the member sets lazily, in <see cref="ZddEnumerationOrder.Default"/> order
        /// (the order fixed by <see cref="Universe"/>'s item-index assignment).
        /// </summary>
        public IEnumerator<IReadOnlySet<T>> GetEnumerator()
        {
            foreach (int[] set in Zdd.Sets())
            {
                yield return Universe.ToElementSet(set);
            }
        }

        /// <inheritdoc cref="GetEnumerator"/>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>Whether two families are the same set of member sets over the same <see cref="Universe"/>.</summary>
        public bool Equals(SetSet<T>? other) =>
            other is not null && ReferenceEquals(Universe, other.Universe) && Zdd == other.Zdd;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is SetSet<T> other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Universe), Zdd.GetHashCode());

        /// <summary>Whether two families are the same set of member sets over the same <see cref="Universe"/>.</summary>
        public static bool operator ==(SetSet<T>? left, SetSet<T>? right) =>
            left is null ? right is null : left.Equals(right);

        /// <summary>Whether two families differ, or belong to different universes.</summary>
        public static bool operator !=(SetSet<T>? left, SetSet<T>? right) => !(left == right);

        /// <inheritdoc/>
        public override string ToString() => $"SetSet<{typeof(T).Name}>({Zdd})";

        /// <summary>
        /// Writes this family as Graphviz DOT source, labeling each level by its element instead of a
        /// bare item index, unless <paramref name="options"/> already sets
        /// <see cref="DotOptions.LevelLabel"/> itself.
        /// </summary>
        /// <param name="options">
        /// Extra rendering knobs (M5-4, issue #56); every default besides <see cref="DotOptions.LevelLabel"/>
        /// is <see cref="Zdd.ToDot(DotOptions)"/>'s own.
        /// </param>
        public string ToDot(DotOptions? options = null) => Zdd.ToDot(WithElementLevelLabel(options));

        /// <summary>Streams this family's DOT representation as <see cref="ToDot"/> does, without buffering it all in memory.</summary>
        /// <param name="writer">The destination writer.</param>
        /// <param name="options">Extra rendering knobs; see <see cref="ToDot"/>.</param>
        public void WriteDot(TextWriter writer, DotOptions? options = null) => Zdd.WriteDot(writer, WithElementLevelLabel(options));

        private DotOptions WithElementLevelLabel(DotOptions? options)
        {
            if (options?.LevelLabel is not null)
            {
                return options;
            }

            DotOptions effective = options?.Clone() ?? new DotOptions();
            effective.LevelLabel = item => Universe.ElementAt(item).ToString() ?? string.Empty;
            return effective;
        }

        private SetSet<T> Combine(SetSet<T> other, Func<Zdd, Zdd, Zdd> operation)
        {
            ArgumentNullException.ThrowIfNull(other);

            if (!ReferenceEquals(Universe, other.Universe))
            {
                ThrowHelper.ThrowArgumentException(nameof(other), "The two SetSet<T> instances do not share the same SetUniverse<T>; only families built over the same universe can be combined.");
            }

            return new SetSet<T>(Universe, operation(Zdd, other.Zdd));
        }

        private (IReadOnlySet<T> Set, TWeight Weight) Wrap<TWeight>(WeightedSet<TWeight> result) =>
            (Universe.ToElementSet(result.Items), result.Weight);

        private (IReadOnlySet<T> Set, TWeight Weight)[] Wrap<TWeight>(WeightedSet<TWeight>[] results)
        {
            var mapped = new (IReadOnlySet<T> Set, TWeight Weight)[results.Length];

            for (int i = 0; i < results.Length; i++)
            {
                mapped[i] = Wrap(results[i]);
            }

            return mapped;
        }
    }
}
