using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Specs;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Specs
{
    /// <summary>
    /// M2-5 completion criteria for <see cref="LinearConstraintSpec"/>: matches an independent
    /// subset-sum DP for all three operators (including negative coefficients), full enumeration
    /// stays within the constraint, an unreachable bound is empty, and pruning shrinks the diagram.
    /// </summary>
    public class LinearConstraintSpecTests
    {
        [Theory]
        [InlineData(new[] { 1, 2, 3, 4, 5 }, LinearConstraintOperator.LessOrEqual, 6L)]
        [InlineData(new[] { 1, 2, 3, 4, 5 }, LinearConstraintOperator.Equal, 6L)]
        [InlineData(new[] { 1, 2, 3, 4, 5 }, LinearConstraintOperator.GreaterOrEqual, 6L)]
        [InlineData(new[] { 3, 3, 3, 3, 3, 3 }, LinearConstraintOperator.Equal, 9L)]
        [InlineData(new[] { 1, -2, 3, -4, 5, -6 }, LinearConstraintOperator.LessOrEqual, 0L)]
        [InlineData(new[] { 1, -2, 3, -4, 5, -6 }, LinearConstraintOperator.Equal, -3L)]
        [InlineData(new[] { 1, -2, 3, -4, 5, -6 }, LinearConstraintOperator.GreaterOrEqual, 2L)]
        [InlineData(new[] { -5, -3, -1, 1, 3, 5 }, LinearConstraintOperator.Equal, 0L)]
        public void CountMatchesAnIndependentSubsetSumDp(int[] coefficients, LinearConstraintOperator op, long bound)
        {
            using ZddManager manager = new ZddManager(coefficients.Length);
            LinearConstraintSpec spec = new LinearConstraintSpec(coefficients, op, bound);

            Zdd built = FrontierBuilder.Build<LinearConstraintSpec, long>(manager, spec);

            BigInteger expected = CountViaSubsetSumDp(coefficients, sum => Accepts(op, sum, bound));
            Assert.Equal(expected, built.Count);
        }

        [Theory]
        [InlineData(LinearConstraintOperator.LessOrEqual)]
        [InlineData(LinearConstraintOperator.Equal)]
        [InlineData(LinearConstraintOperator.GreaterOrEqual)]
        public void EveryEnumeratedSetSatisfiesTheConstraintAndMatchesTheCanonicalBruteForceFamily(LinearConstraintOperator op)
        {
            int[] coefficients = { 2, -3, 5, 7, -1, 4, -6, 3, 1, -2, 8, 2 };
            const long bound = 4;

            using ZddManager manager = new ZddManager(coefficients.Length);
            LinearConstraintSpec spec = new LinearConstraintSpec(coefficients, op, bound);

            Zdd built = FrontierBuilder.Build<LinearConstraintSpec, long>(manager, spec);

            foreach (int[] set in built.Sets())
            {
                long sum = 0;
                foreach (int item in set)
                {
                    sum += coefficients[item];
                }

                Assert.True(Accepts(op, sum, bound), $"set [{string.Join(",", set)}] sums to {sum}, violating {op} {bound}");
            }

            BruteForceFamily expected = BruteForceFamily.FromMasks(
                coefficients.Length,
                MasksSatisfying(coefficients, mask => Accepts(op, SumOf(coefficients, mask), bound)));
            FamilyAssert.AssertSameFamily(built, expected);
        }

        [Fact]
        public void UnreachableBoundIsEmpty()
        {
            int[] coefficients = { 2, 2, 2, 2 }; // every achievable sum is even
            using ZddManager manager = new ZddManager(coefficients.Length);

            Zdd built = FrontierBuilder.Build<LinearConstraintSpec, long>(
                manager, new LinearConstraintSpec(coefficients, LinearConstraintOperator.Equal, 3));

            Assert.Equal(BigInteger.Zero, built.Count);
            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void PruningProducesFewerNodesThanTheSameFamilyWithoutIt()
        {
            int[] coefficients = new int[22];
            for (int i = 0; i < coefficients.Length; i++)
            {
                coefficients[i] = i % 2 == 0 ? i + 1 : -(i + 1);
            }

            const long bound = 3;
            const LinearConstraintOperator op = LinearConstraintOperator.Equal;

            long prunedNodeCount = TopDownExpander<LinearConstraintSpec, long>.Expand(new LinearConstraintSpec(coefficients, op, bound)).NodeCount;
            long unprunedNodeCount = TopDownExpander<UnprunedLinearConstraintSpec, long>.Expand(
                new UnprunedLinearConstraintSpec(coefficients, op, bound)).NodeCount;

            Assert.True(prunedNodeCount < unprunedNodeCount,
                $"expected pruning to shrink the top-down expansion, got {prunedNodeCount} (pruned) vs {unprunedNodeCount} (unpruned)");

            using ZddManager manager = new ZddManager(coefficients.Length);
            Zdd pruned = FrontierBuilder.Build<LinearConstraintSpec, long>(manager, new LinearConstraintSpec(coefficients, op, bound));
            Zdd unpruned = FrontierBuilder.Build<UnprunedLinearConstraintSpec, long>(
                manager, new UnprunedLinearConstraintSpec(coefficients, op, bound));
            Assert.Equal(pruned, unpruned); // same canonical family regardless of pruning
        }

        /// <summary>
        /// Same family as <see cref="LinearConstraintSpec"/>, but without the suffix-bound lookahead:
        /// every branch is carried to the last level before being judged, instead of being cut the
        /// moment neither extreme of the remaining items could satisfy the operator.
        /// </summary>
        private readonly struct UnprunedLinearConstraintSpec : IDdSpec<long>
        {
            private readonly int[] _coefficients;
            private readonly LinearConstraintOperator _op;
            private readonly long _bound;

            public UnprunedLinearConstraintSpec(int[] coefficients, LinearConstraintOperator op, long bound)
            {
                _coefficients = coefficients;
                _op = op;
                _bound = bound;
            }

            public int GetRoot(ref long sum)
            {
                sum = 0;
                return _coefficients.Length == 0 ? (Accepts(_op, 0, _bound) ? DdResult.True : DdResult.False) : _coefficients.Length;
            }

            public int GetChild(ref long sum, int level, int value)
            {
                int idx = _coefficients.Length - level;
                sum += (long)_coefficients[idx] * value;
                int remaining = level - 1;
                return remaining == 0 ? (Accepts(_op, sum, _bound) ? DdResult.True : DdResult.False) : remaining;
            }

            public bool StateEquals(in long left, in long right) => left == right;

            public int StateHashCode(in long state) => state.GetHashCode();
        }

        private static bool Accepts(LinearConstraintOperator op, long sum, long bound) => op switch
        {
            LinearConstraintOperator.LessOrEqual => sum <= bound,
            LinearConstraintOperator.GreaterOrEqual => sum >= bound,
            LinearConstraintOperator.Equal => sum == bound,
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };

        private static long SumOf(int[] coefficients, int mask)
        {
            long sum = 0;
            for (int i = 0; i < coefficients.Length; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    sum += coefficients[i];
                }
            }

            return sum;
        }

        private static IEnumerable<int> MasksSatisfying(int[] coefficients, Func<int, bool> accept)
        {
            int universe = 1 << coefficients.Length;
            for (int mask = 0; mask < universe; mask++)
            {
                if (accept(mask))
                {
                    yield return mask;
                }
            }
        }

        /// <summary>
        /// Counts subsets satisfying <paramref name="accepts"/> by a subset-sum DP over achievable sums
        /// (a <see cref="Dictionary{TKey, TValue}"/> from sum to the number of subsets reaching it) —
        /// entirely independent of <see cref="LinearConstraintSpec"/> or the ZDD it builds.
        /// </summary>
        private static BigInteger CountViaSubsetSumDp(int[] coefficients, Func<long, bool> accepts)
        {
            Dictionary<long, BigInteger> waysToReach = new Dictionary<long, BigInteger> { [0] = BigInteger.One };

            foreach (int coefficient in coefficients)
            {
                Dictionary<long, BigInteger> next = new Dictionary<long, BigInteger>(waysToReach);

                foreach (KeyValuePair<long, BigInteger> entry in waysToReach)
                {
                    long reached = entry.Key + coefficient;
                    next[reached] = next.TryGetValue(reached, out BigInteger existing) ? existing + entry.Value : entry.Value;
                }

                waysToReach = next;
            }

            BigInteger total = BigInteger.Zero;
            foreach (KeyValuePair<long, BigInteger> entry in waysToReach)
            {
                if (accepts(entry.Key))
                {
                    total += entry.Value;
                }
            }

            return total;
        }
    }
}
