using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace ZDD.Net.Internal
{
    /// <summary>
    /// 例外の生成・送出をまとめる場所。呼び出し側の検証メソッド（<c>ThrowIfXxx</c>）は
    /// インライン化されるように小さく保ち、実際の <c>throw</c> と文字列組み立ては
    /// <see cref="StackTraceHiddenAttribute"/> を付けた非インラインのメソッドに切り出す。
    /// これにより、検証だけが行われる正常系のコード量を hot path 側で最小化できる。
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

        /// <summary>
        /// <paramref name="value"/> が正の 2 の冪であることを検証する。Fibonacci hashing など、
        /// テーブルサイズが 2 の冪であることを前提とする処理の入口で使う。
        /// </summary>
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
        public static void ThrowInvalidOperationException(string message) =>
            throw new InvalidOperationException(message);

        [DoesNotReturn]
        [StackTraceHidden]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowObjectDisposedException(string objectName) =>
            throw new ObjectDisposedException(objectName);
    }
}
