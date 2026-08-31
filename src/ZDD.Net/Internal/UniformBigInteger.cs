using System;
using System.Numerics;

namespace ZDD.Net.Internal
{
    /// <summary>
    /// An unbiased random source for <see cref="BigInteger"/> values in <c>[0, bound)</c>.
    /// Used by uniform sampling (<see cref="ZDD.Net.Core.Zdd.Sample(Random)"/>).
    /// </summary>
    /// <remarks>
    /// Uses rejection sampling rather than <c>random % bound</c>, which is biased unless
    /// <c>bound</c> divides the random range evenly. Draws just enough bits to cover <c>bound - 1</c>
    /// and rejects out-of-range draws; the rejection probability is always below 1/2, so the
    /// expected number of draws stays under 2 regardless of magnitude. The internal buffer is
    /// reused across repeated draws with the same bound.
    /// </remarks>
    internal readonly struct UniformBigInteger
    {
        /// <summary>Exclusive upper bound of returned values.</summary>
        private readonly BigInteger _bound;

        /// <summary>Scratch buffer for random bytes; length 0 only when <c>bound</c> is 1.</summary>
        private readonly byte[] _buffer;

        /// <summary>Mask keeping only the usable bits of the top byte.</summary>
        private readonly byte _topByteMask;

        /// <summary>Creates a random source for a given upper bound.</summary>
        /// <param name="exclusiveUpperBound">Exclusive upper bound; must be at least 1.</param>
        /// <remarks>
        /// Validated eagerly (not via <c>Debug.Assert</c>): a bound of 0 would silently always
        /// return 0, and a negative bound would make rejection sampling loop forever.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="exclusiveUpperBound"/> is less than 1.</exception>
        public UniformBigInteger(BigInteger exclusiveUpperBound)
        {
            if (exclusiveUpperBound < BigInteger.One)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(exclusiveUpperBound),
                    $"'{nameof(exclusiveUpperBound)}' must be at least 1 so that the range holds a value, but was {exclusiveUpperBound}.");
            }

            _bound = exclusiveUpperBound;

            // The number of bits needed to represent bound - 1. A power-of-two bound needs no
            // rejection at all; a bound of 1 needs zero bits, so the answer is always 0.
            long bitLength = (exclusiveUpperBound - BigInteger.One).GetBitLength();
            int byteCount = (int)((bitLength + 7) / 8);
            int topBits = (int)(bitLength & 7);

            _buffer = byteCount == 0 ? Array.Empty<byte>() : new byte[byteCount];
            _topByteMask = topBits == 0 ? byte.MaxValue : (byte)((1 << topBits) - 1);
        }

        /// <summary>Returns a value in <c>[0, bound)</c>, uniformly at random.</summary>
        /// <param name="random">The source of randomness.</param>
        /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
        public BigInteger Next(Random random)
        {
            ThrowHelper.ThrowIfNull(random, nameof(random));

            if (_buffer.Length == 0)
            {
                // Bound of 1 means the only possible answer is 0; no randomness needed.
                return BigInteger.Zero;
            }

            while (true)
            {
                random.NextBytes(_buffer);

                // Drop the unused high bits of the top byte to reduce (not eliminate) rejections.
                _buffer[^1] &= _topByteMask;

                // Read as unsigned, little-endian, matching how NextBytes fills the buffer.
                BigInteger value = new BigInteger(_buffer, isUnsigned: true, isBigEndian: false);

                if (value < _bound)
                {
                    return value;
                }
            }
        }
    }
}
