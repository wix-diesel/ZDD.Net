using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace ZDD.Net.Internal
{
    /// <summary>
    /// 64bit ハッシュ関数群。ノードの一意化表（<c>(level, lo, hi)</c> をキーとするオープンアドレス法の
    /// ハッシュ表）向けに、<see cref="System.HashCode"/> より軽量な専用実装を提供する。
    /// <see cref="System.HashCode"/> はランダム化されたシード（DoS 耐性のため）を毎プロセス起動時に
    /// 生成する汎用実装であり、hot path で毎回呼び出すにはオーバーヘッドが大きい。
    /// </summary>
    internal static class Hashing
    {
        /// <summary>
        /// 黄金比から導かれる 64bit の奇数定数（<c>floor(2^64 / phi)</c>）。
        /// 乗算ハッシュ・Fibonacci hashing のどちらでも使う。
        /// </summary>
        private const ulong GoldenRatio64 = 0x9E3779B97F4A7C15UL;

        /// <summary>
        /// 64bit の値を撹拌する。SplittableRandom / SplitMix64 のファイナライザとして知られる
        /// 混合関数で、単一ビットの入力差が出力のほぼ半分のビットに伝播する（雪崩効果）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Mix64(ulong value)
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            value ^= value >> 31;
            return value;
        }

        /// <summary>
        /// ノードの一意化表のキー <c>(level, lo, hi)</c> を単一の 64bit ハッシュに混ぜる。
        /// 同じ引数からは必ず同じ値を返す。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Combine(int level, int lo, int hi)
        {
            ulong hash = GoldenRatio64;
            hash = Mix64(hash ^ (uint)level);
            hash = Mix64(hash ^ (uint)lo);
            hash = Mix64(hash ^ (uint)hi);
            return hash;
        }

        /// <summary>
        /// 既に撹拌済みの 64bit ハッシュから、サイズが 2 の冪である表のスロット index を
        /// Fibonacci hashing で求める。剰余（<c>%</c>）より高速で、下位ビットに偏りがある
        /// ハッシュでも上位ビットを使うため分布が安定する。
        /// </summary>
        /// <param name="hash">撹拌済みの 64bit ハッシュ値。</param>
        /// <param name="tableSize">ハッシュ表のサイズ。2 の冪でなければならない（1 以上）。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexFor(ulong hash, int tableSize)
        {
            ThrowHelper.ThrowIfNotPositivePowerOfTwo(tableSize, nameof(tableSize));

            int bits = BitOperations.TrailingZeroCount(tableSize);
            if (bits == 0)
            {
                // A single-slot table always resolves to index 0. Shifting a 64bit value by
                // 64 is undefined in general, and C#'s shift-count masking (mod 64) would
                // otherwise turn this into a no-op shift instead of the intended "all bits".
                return 0;
            }

            return (int)((hash * GoldenRatio64) >> (64 - bits));
        }

        /// <summary>
        /// <see cref="IndexFor"/> と同じ Fibonacci hashing だが、<paramref name="tableSize"/> の検証を
        /// Debug ビルドの表明に落としたもの。サイズを自分で管理していて 2 の冪であることが
        /// 構造的に保証されている表（一意化表・演算キャッシュ）の hot path 用。
        /// </summary>
        /// <param name="hash">撹拌済みの 64bit ハッシュ値。</param>
        /// <param name="tableSize">
        /// ハッシュ表のサイズ。<b>2 以上の 2 の冪</b>でなければならない（検証されない）。
        /// サイズ 1 の表は <see cref="IndexFor"/> を使うこと。
        /// </param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexForPowerOfTwo(ulong hash, int tableSize)
        {
            Debug.Assert(
                tableSize > 1 && BitOperations.IsPow2(tableSize),
                $"'{nameof(tableSize)}' must be a power of two greater than one, but was {tableSize}.");

            int bits = BitOperations.TrailingZeroCount(tableSize);
            return (int)((hash * GoldenRatio64) >> (64 - bits));
        }
    }
}
