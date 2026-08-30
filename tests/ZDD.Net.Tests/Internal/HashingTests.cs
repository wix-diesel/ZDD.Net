using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
using ZDD.Net.Internal;

namespace ZDD.Net.Tests.Internal
{
    public class HashingTests
    {
        [Fact]
        public void CombineIsDeterministic()
        {
            ulong first = Hashing.Combine(level: 3, lo: 17, hi: 42);
            ulong second = Hashing.Combine(level: 3, lo: 17, hi: 42);

            Assert.Equal(first, second);
        }

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(0, 0, 1)]
        [InlineData(1, 0, 0)]
        [InlineData(-1, -1, -1)]
        [InlineData(int.MaxValue, int.MinValue, 0)]
        public void CombineDoesNotThrowForEdgeCaseInputs(int level, int lo, int hi)
        {
            ulong hash = Hashing.Combine(level, lo, hi);

            // A well-mixed hash of a fixed input should not degenerate to zero.
            Assert.NotEqual(0UL, hash);
        }

        [Fact]
        public void CombineHasNoCollisionsForDenseSequentialKeys()
        {
            var seen = new HashSet<ulong>();

            for (int level = 0; level < 20; level++)
            {
                for (int lo = 0; lo < 20; lo++)
                {
                    for (int hi = 0; hi < 20; hi++)
                    {
                        ulong hash = Hashing.Combine(level, lo, hi);
                        Assert.True(seen.Add(hash), $"Collision for ({level},{lo},{hi}) -> {hash}");
                    }
                }
            }
        }

        [Fact]
        public void CombineHasNoCollisionsForRandomKeys()
        {
            var random = new Random(12345);
            var seen = new HashSet<ulong>();

            for (int i = 0; i < 200_000; i++)
            {
                int level = random.Next();
                int lo = random.Next();
                int hi = random.Next();

                ulong hash = Hashing.Combine(level, lo, hi);
                Assert.True(seen.Add(hash), $"Collision for ({level},{lo},{hi}) -> {hash}");
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void CombineAvalanchesWhenAnArgumentChangesBySingleBit(int argumentIndex)
        {
            var random = new Random(2026);

            // For a random base key, flipping a single bit of one argument should flip
            // roughly half of the 64 output bits (the avalanche property). We check the
            // average over many trials rather than any single trial, since individual
            // trials can legitimately land far from 32.
            double totalFlippedBits = 0;
            const int trials = 500;

            for (int trial = 0; trial < trials; trial++)
            {
                int level = random.Next();
                int lo = random.Next();
                int hi = random.Next();
                ulong baseline = Hashing.Combine(level, lo, hi);

                int bitToFlip = random.Next(32);
                int[] args = [level, lo, hi];
                args[argumentIndex] ^= 1 << bitToFlip;

                ulong flipped = Hashing.Combine(args[0], args[1], args[2]);

                totalFlippedBits += BitOperations.PopCount(baseline ^ flipped);
            }

            double averageFlippedBits = totalFlippedBits / trials;

            // Ideal avalanche is 32/64 bits flipped; allow a generous band since this
            // is a statistical property, not an exact one.
            Assert.InRange(averageFlippedBits, 24.0, 40.0);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(1024)]
        [InlineData(1 << 20)]
        public void IndexForStaysWithinTableBounds(int tableSize)
        {
            var random = new Random(7);

            for (int i = 0; i < 10_000; i++)
            {
                ulong hash = Hashing.Mix64((ulong)random.NextInt64());
                int index = Hashing.IndexFor(hash, tableSize);

                Assert.InRange(index, 0, tableSize - 1);
            }
        }

        [Theory]
        [InlineData(2)]
        [InlineData(1024)]
        [InlineData(1 << 20)]
        public void IndexForPowerOfTwoAgreesWithTheValidatingOverload(int tableSize)
        {
            Random random = new Random(Seed: 12345);

            for (int i = 0; i < 10_000; i++)
            {
                ulong hash = Hashing.Mix64((ulong)random.NextInt64());

                Assert.Equal(Hashing.IndexFor(hash, tableSize), Hashing.IndexForPowerOfTwo(hash, tableSize));
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(3)]
        [InlineData(6)]
        public void IndexForRejectsNonPowerOfTwoTableSizes(int tableSize)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Hashing.IndexFor(0UL, tableSize));
        }

        [Fact]
        public void IndexForDistributesRoughlyUniformlyAcrossBuckets()
        {
            const int tableSize = 1 << 10; // 1024 buckets
            const int sampleCount = 200_000;
            var buckets = new int[tableSize];
            var random = new Random(99);

            for (int i = 0; i < sampleCount; i++)
            {
                int level = random.Next();
                int lo = random.Next();
                int hi = random.Next();

                ulong hash = Hashing.Combine(level, lo, hi);
                int index = Hashing.IndexFor(hash, tableSize);
                buckets[index]++;
            }

            double expectedPerBucket = (double)sampleCount / tableSize;

            // Pearson's chi-squared statistic against the uniform distribution.
            double chiSquared = 0;
            foreach (int count in buckets)
            {
                double diff = count - expectedPerBucket;
                chiSquared += diff * diff / expectedPerBucket;
            }

            // With 1024 buckets (1023 degrees of freedom), the chi-squared statistic is
            // extremely unlikely to exceed ~1200 for a well-distributed hash. A skewed or
            // degenerate hash function would blow far past this threshold.
            Assert.True(chiSquared < 1200, $"Chi-squared statistic too high: {chiSquared}");

            int maxBucket = 0;
            foreach (int count in buckets)
            {
                maxBucket = Math.Max(maxBucket, count);
            }

            // No single bucket should be wildly overloaded relative to the expectation.
            Assert.True(maxBucket < expectedPerBucket * 3, $"Bucket overloaded: {maxBucket} vs expected {expectedPerBucket}");
        }
    }
}
