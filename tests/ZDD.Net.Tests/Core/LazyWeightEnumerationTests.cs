using System.Collections.Generic;
using System.Linq;
using Xunit;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Core
{
    /// <summary>
    /// Regression coverage for a bug a PR review caught in <see cref="LazyWeightEnumeration"/>:
    /// the per-node completion bound must use <see cref="IWeightOps{TWeight}.Zero"/> for the &#8868;
    /// terminal (the empty completion), not <c>default(TWeight)</c> &#8212; a distinction invisible for
    /// <see cref="int"/>/<see cref="long"/>/<see cref="double"/> (whose zero and default coincide) but
    /// not for a user-defined type like a rational number, whose <c>Zero</c> is <c>(0, 1)</c> while
    /// <c>default</c> is <c>(0, 0)</c> (a zero denominator that corrupts every comparison downstream).
    /// </summary>
    public class LazyWeightEnumerationTests
    {
        [Fact]
        public void EnumeratesNonPrimitiveWeightsInAscendingOrder()
        {
            using ZddManager manager = new ZddManager(2);
            Zdd powerSet = manager.Empty.Complement(); // {}, {0}, {1}, {0, 1}

            Rational[] weights = { new Rational(1, 2), new Rational(1, 3) }; // item 0 = 1/2, item 1 = 1/3

            List<int[]> ascending = LazyWeightEnumeration
                .Enumerate<Rational, RationalWeightOps>(manager, powerSet.Id, weights, maximize: false)
                .Select(w => w.Items)
                .ToList();

            // True order by value: {} = 0, {1} = 1/3, {0} = 1/2, {0, 1} = 5/6.
            Assert.Equal(
                new[] { System.Array.Empty<int>(), new[] { 1 }, new[] { 0 }, new[] { 0, 1 } },
                ascending);
        }

        [Fact]
        public void EnumeratesNonPrimitiveWeightsInDescendingOrder()
        {
            using ZddManager manager = new ZddManager(2);
            Zdd powerSet = manager.Empty.Complement();

            Rational[] weights = { new Rational(1, 2), new Rational(1, 3) };

            List<int[]> descending = LazyWeightEnumeration
                .Enumerate<Rational, RationalWeightOps>(manager, powerSet.Id, weights, maximize: true)
                .Select(w => w.Items)
                .ToList();

            Assert.Equal(
                new[] { new[] { 0, 1 }, new[] { 0 }, new[] { 1 }, System.Array.Empty<int>() },
                descending);
        }

        /// <summary>A user-defined weight type whose default value is not its additive identity.</summary>
        private readonly record struct Rational(long Numerator, long Denominator);

        private readonly struct RationalWeightOps : IWeightOps<Rational>
        {
            public static Rational Zero => new Rational(0, 1);

            public static Rational Add(Rational left, Rational right) =>
                new Rational(
                    (left.Numerator * right.Denominator) + (right.Numerator * left.Denominator),
                    left.Denominator * right.Denominator);

            public static int Compare(Rational left, Rational right) =>
                (left.Numerator * right.Denominator).CompareTo(right.Numerator * left.Denominator);
        }
    }
}
