using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace ZDD.Net.Internal
{
    /// <summary>
    /// Centralizes exception construction and throwing. Validation methods (<c>ThrowIfXxx</c>)
    /// stay small enough to inline; the actual <c>throw</c> and message building live in
    /// non-inlined, <see cref="StackTraceHiddenAttribute"/>-tagged methods so the hot path
    /// stays cheap when nothing is wrong.
    /// </summary>
    internal static class ThrowHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfNull([NotNull] object? argument, string paramName)
        {
            if (argument is null)
            {
                ThrowArgumentNullException(paramName);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfNegative(int value, string paramName)
        {
            if (value < 0)
            {
                ThrowArgumentOutOfRangeException(paramName, $"'{paramName}' must be non-negative, but was {value}.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfNegativeOrZero(int value, string paramName)
        {
            if (value <= 0)
            {
                ThrowArgumentOutOfRangeException(paramName, $"'{paramName}' must be positive, but was {value}.");
            }
        }

        /// <summary>Validates that <paramref name="value"/> is a positive power of two, as required for Fibonacci hashing.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfNotPositivePowerOfTwo(int value, string paramName)
        {
            if (value <= 0 || !BitOperations.IsPow2(value))
            {
                ThrowArgumentOutOfRangeException(paramName, $"'{paramName}' must be a positive power of two, but was {value}.");
            }
        }

        [DoesNotReturn]
        [StackTraceHidden]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowArgumentNullException(string paramName) =>
            throw new ArgumentNullException(paramName);

        [DoesNotReturn]
        [StackTraceHidden]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowArgumentOutOfRangeException(string paramName, string message) =>
            throw new ArgumentOutOfRangeException(paramName, message);

        [DoesNotReturn]
        [StackTraceHidden]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowArgumentException(string paramName, string message) =>
            throw new ArgumentException(message, paramName);

        [DoesNotReturn]
        [StackTraceHidden]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowInvalidOperationException(string message) =>
            throw new InvalidOperationException(message);

        [DoesNotReturn]
        [StackTraceHidden]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowObjectDisposedException(string objectName) =>
            throw new ObjectDisposedException(objectName);
    }
}
