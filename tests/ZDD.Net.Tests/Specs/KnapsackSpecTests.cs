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
    /// M2-5 completion criteria for <see cref="KnapsackSpec"/>: matches an independent knapsack DP,
    /// full enumeration stays within capacity, a negative capacity is empty, and pruning shrinks the
    /// diagram.
    /// </summary>
    public class KnapsackSpecTests
    {
        [Theory]
        [InlineData(new[] { 2, 3, 4, 5 }, 5L)]
        [InlineData(new[] { 1, 1, 1, 1, 1 }, 3L)]
        [InlineData(new[] { 10, 20, 30 }, 25L)]
        [InlineData(new[] { 0, 0, 3 }, 0L)] // zero-weight items are always free to include
        [InlineData(new[] { 4, 4, 4, 4, 4, 4 }, 100L)] // capacity far beyond total weight
        public void CountMatchesAnIndependentKnapsackDp(int[] weights, long capacity)
        {
            using ZddManager manager = new ZddManager(weights.Length);

            Zdd built = FrontierBuilder.Build<KnapsackSpec, long>(manager, new KnapsackSpec(weights, capacity));

            Assert.Equal(CountFeasibleSubsetsViaDp(weights, capacity), built.Count);
        }

        [Fact]
        public void EveryEnumeratedSetFitsCapacityAndMatchesTheCanonicalBruteForceFamily()
        {
            int[] weights = { 3, 1, 4, 1, 5, 9, 2, 6, 5, 3, 5, 8 };
            const long capacity = 15;

            using ZddManager manager = new ZddManager(weights.Length);

            Zdd built = FrontierBuilder.Build<KnapsackSpec, long>(manager, new KnapsackSpec(weights, capacity));

            foreach (int[] set in built.Sets())
            {
                long totalWeight = 0;
                foreach (int item in set)
                {
                    totalWeight += weights[item];
                }

                Assert.True(totalWeight <= capacity, $"set [{string.Join(",", set)}] weighs {totalWeight} > {capacity}");
            }

            BruteForceFamily universe = BruteForceFamily.PowerSet(weights.Length);
            List<int> expectedMasks = new List<int>();
            foreach (int mask in universe.Masks)
            {
                if (WeightOf(weights, mask) <= capacity)
                {
                    expectedMasks.Add(mask);
                }
            }

            FamilyAssert.AssertSameFamily(built, BruteForceFamily.FromMasks(weights.Length, expectedMasks));
        }

        [Fact]
        public void NegativeCapacityIsEmpty()
        {
            int[] weights = { 1, 2, 3 };
            using ZddManager manager = new ZddManager(weights.Length);

            Zdd built = FrontierBuilder.Build<KnapsackSpec, long>(manager, new KnapsackSpec(weights, -1));

            Assert.Equal(BigInteger.Zero, built.Count);
            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void PruningProducesFewerNodesThanTheSameFamilyWithoutIt()
        {
            // Descending weights: the suffix weight total shrinks fast toward the end, so once it
            // drops below the capacity the clamp merges every "more than enough room left" state into
            // one — that merge is what the unpruned variant (exact remaining capacity) cannot do.
            int[] weights = new int[20];
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = weights.Length - i;
            }

            const long capacity = 8;

            long prunedNodeCount = TopDownExpander<KnapsackSpec, long>.Expand(new KnapsackSpec(weights, capacity)).NodeCount;
            long unprunedNodeCount = TopDownExpander<UnprunedKnapsackSpec, long>.Expand(new UnprunedKnapsackSpec(weights, capacity)).NodeCount;

            Assert.True(prunedNodeCount < unprunedNodeCount,
                $"expected pruning to shrink the top-down expansion, got {prunedNodeCount} (pruned) vs {unprunedNodeCount} (unpruned)");

            using ZddManager manager = new ZddManager(weights.Length);
            Zdd pruned = FrontierBuilder.Build<KnapsackSpec, long>(manager, new KnapsackSpec(weights, capacity));
            Zdd unpruned = FrontierBuilder.Build<UnprunedKnapsackSpec, long>(manager, new UnprunedKnapsackSpec(weights, capacity));
            Assert.Equal(pruned, unpruned); // same canonical family regardless of pruning
        }

        /// <summary>
        /// Same family as <see cref="KnapsackSpec"/>, but without the suffix clamp that merges every
        /// "plenty of capacity left" state into one: remaining capacity is tracked exactly instead, so
        /// distinct amounts of surplus room stay distinct states all the way to the last level.
        /// </summary>
        private readonly struct UnprunedKnapsackSpec : IDdSpec<long>
        {
            private readonly int[] _weights;
            private readonly long _capacity;

            public UnprunedKnapsackSpec(int[] weights, long capacity)
            {
                _weights = weights;
                _capacity = capacity;
            }

            public int GetRoot(ref long remainingCapacity)
            {
                if (_capacity < 0)
                {
                    remainingCapacity = 0;
                    return DdResult.False;
                }

                remainingCapacity = _capacity;
                return _weights.Length == 0 ? DdResult.True : _weights.Length;
            }

            public int GetChild(ref long remainingCapacity, int level, int value)
            {
                int idx = _weights.Length - level;
                if (value == 1)
                {
                    remainingCapacity -= _weights[idx];
                    if (remainingCapacity < 0)
                    {
                        return DdResult.False;
                    }
                }

                int remaining = level - 1;
                return remaining == 0 ? DdResult.True : remaining;
            }

            public bool StateEquals(in long left, in long right) => left == right;

            public int StateHashCode(in long state) => state.GetHashCode();
        }

        private static long WeightOf(int[] weights, int mask)
        {
            long total = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    total += weights[i];
                }
            }

            return total;
        }

        /// <summary>
        /// Counts subsets fitting <paramref name="capacity"/> by a knapsack DP over achievable total
        /// weights (a <see cref="Dictionary{TKey, TValue}"/> from weight to subset count) — entirely
        /// independent of <see cref="KnapsackSpec"/> or the ZDD it builds.
        /// </summary>
        private static BigInteger CountFeasibleSubsetsViaDp(int[] weights, long capacity)
        {
            if (capacity < 0)
            {
                return BigInteger.Zero;
            }

            Dictionary<long, BigInteger> waysToReach = new Dictionary<long, BigInteger> { [0] = BigInteger.One };

            foreach (int weight in weights)
            {
                Dictionary<long, BigInteger> next = new Dictionary<long, BigInteger>(waysToReach);

                foreach (KeyValuePair<long, BigInteger> entry in waysToReach)
                {
                    long reached = entry.Key + weight;
                    next[reached] = next.TryGetValue(reached, out BigInteger existing) ? existing + entry.Value : entry.Value;
                }

                waysToReach = next;
            }

            BigInteger total = BigInteger.Zero;
            foreach (KeyValuePair<long, BigInteger> entry in waysToReach)
            {
                if (entry.Key <= capacity)
                {
                    total += entry.Value;
                }
            }

            return total;
        }
    }
}
