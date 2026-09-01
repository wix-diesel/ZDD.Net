using System;
using System.Linq;
using System.Numerics;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Specs;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Specs
{
    /// <summary>
    /// M2-5 completion criteria for <see cref="CardinalitySpec"/>: exact-k against the binomial
    /// coefficient, a range against their sum, full enumeration staying in range, an empty case, and
    /// pruning actually shrinking the diagram.
    /// </summary>
    public class CardinalitySpecTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 0)]
        [InlineData(1, 1)]
        [InlineData(5, 0)]
        [InlineData(5, 2)]
        [InlineData(5, 5)]
        [InlineData(10, 4)]
        [InlineData(16, 8)]
        public void ExactlyKMatchesTheBinomialCoefficient(int itemCount, int k)
        {
            using ZddManager manager = new ZddManager(itemCount);

            Zdd built = FrontierBuilder.Build<CardinalitySpec, int>(manager, new CardinalitySpec(itemCount, k, k));

            Assert.Equal(Binomial(itemCount, k), built.Count);
            FamilyAssert.AssertSameFamily(built, FilterByPopCount(itemCount, count => count == k));
        }

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(6, 2, 4)]
        [InlineData(8, 0, 8)]
        [InlineData(10, 3, 7)]
        [InlineData(16, 6, 10)]
        public void RangeMatchesTheSumOfBinomialCoefficients(int itemCount, int min, int max)
        {
            using ZddManager manager = new ZddManager(itemCount);

            Zdd built = FrontierBuilder.Build<CardinalitySpec, int>(manager, new CardinalitySpec(itemCount, min, max));

            BigInteger expected = BigInteger.Zero;
            for (int k = min; k <= max; k++)
            {
                expected += Binomial(itemCount, k);
            }

            Assert.Equal(expected, built.Count);
            FamilyAssert.AssertSameFamily(built, FilterByPopCount(itemCount, count => count >= min && count <= max));
        }

        [Theory]
        [InlineData(12, 3, 3)]
        [InlineData(12, 4, 9)]
        [InlineData(0, 0, 0)]
        public void EveryEnumeratedSetSatisfiesTheRange(int itemCount, int min, int max)
        {
            using ZddManager manager = new ZddManager(itemCount);

            Zdd built = FrontierBuilder.Build<CardinalitySpec, int>(manager, new CardinalitySpec(itemCount, min, max));

            int enumerated = 0;
            foreach (int[] set in built.Sets())
            {
                Assert.InRange(set.Length, min, max);
                enumerated++;
            }

            Assert.Equal(built.Count, enumerated);
        }

        [Theory]
        [InlineData(5, 6, 6)]  // min > n
        [InlineData(5, 6, 10)] // min > n, even with a slack max
        public void MinGreaterThanItemCountIsEmpty(int itemCount, int min, int max)
        {
            using ZddManager manager = new ZddManager(itemCount);

            Zdd built = FrontierBuilder.Build<CardinalitySpec, int>(manager, new CardinalitySpec(itemCount, min, max));

            Assert.Equal(BigInteger.Zero, built.Count);
            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void PruningProducesFewerNodesThanTheSameFamilyWithoutIt()
        {
            // The reduced Zdd is canonical, so it is the same DAG either way (checked below); pruning's
            // effect shows up only in the unreduced top-down expansion it doesn't have to build.
            const int itemCount = 24;
            const int k = 3;

            using ZddManager manager = new ZddManager(itemCount);

            long prunedNodeCount = TopDownExpander<CardinalitySpec, int>.Expand(new CardinalitySpec(itemCount, k, k)).NodeCount;
            long unprunedNodeCount = TopDownExpander<UnprunedExactlyKSpec, int>.Expand(new UnprunedExactlyKSpec(itemCount, k)).NodeCount;

            Assert.True(prunedNodeCount < unprunedNodeCount,
                $"expected pruning to shrink the top-down expansion, got {prunedNodeCount} (pruned) vs {unprunedNodeCount} (unpruned)");

            Zdd pruned = FrontierBuilder.Build<CardinalitySpec, int>(manager, new CardinalitySpec(itemCount, k, k));
            Zdd unpruned = FrontierBuilder.Build<UnprunedExactlyKSpec, int>(manager, new UnprunedExactlyKSpec(itemCount, k));
            Assert.Equal(pruned, unpruned); // same canonical family regardless of pruning
        }

        /// <summary>
        /// Same family as <c>CardinalitySpec(itemCount, k, k)</c>, but without the lookahead pruning
        /// (<c>taken == max</c> jump-to-True, <c>taken + remaining &lt; min</c> cut). Every branch is
        /// carried all the way to the last level before being judged, so a state's <c>taken</c> can be
        /// any value in <c>0 .. itemCount</c> instead of being capped at <c>k</c> — the width this keeps
        /// alive per level is what the real spec's pruning collapses.
        /// </summary>
        private readonly struct UnprunedExactlyKSpec : IDdSpec<int>
        {
            private readonly int _itemCount;
            private readonly int _k;

            public UnprunedExactlyKSpec(int itemCount, int k)
            {
                _itemCount = itemCount;
                _k = k;
            }

            public int GetRoot(ref int taken)
            {
                taken = 0;
                return _itemCount == 0 ? (_k == 0 ? DdResult.True : DdResult.False) : _itemCount;
            }

            public int GetChild(ref int taken, int level, int value)
            {
                taken += value;
                int remaining = level - 1;
                return remaining == 0 ? (taken == _k ? DdResult.True : DdResult.False) : remaining;
            }

            public bool StateEquals(in int left, in int right) => left == right;

            public int StateHashCode(in int state) => state;
        }

        private static BruteForceFamily FilterByPopCount(int itemCount, Func<int, bool> accept)
        {
            BruteForceFamily universe = BruteForceFamily.PowerSet(itemCount);
            return BruteForceFamily.FromMasks(itemCount, universe.Masks.Where(mask => accept(BitCount(mask))));
        }

        private static int BitCount(int mask) => BitOperations.PopCount((uint)mask);

        private static BigInteger Binomial(int n, int k)
        {
            if (k < 0 || k > n)
            {
                return BigInteger.Zero;
            }

            BigInteger result = BigInteger.One;
            for (int i = 0; i < k; i++)
            {
                result = result * (n - i) / (i + 1);
            }

            return result;
        }
    }
}
