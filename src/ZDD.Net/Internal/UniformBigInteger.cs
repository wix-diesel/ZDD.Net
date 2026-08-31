using System;
using System.Diagnostics;
using System.Numerics;

namespace ZDD.Net.Internal
{
    /// <summary>
    /// <c>0</c> 以上 <c>bound</c> 未満の <see cref="BigInteger"/> を<b>偏りなく</b>返す乱数源。
    /// 一様サンプリング（<see cref="ZDD.Net.Core.Zdd.Sample(Random)"/>）が使う。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>剰余を取ってはいけない</b>。<c>乱数 % bound</c> は、乱数の取りうる範囲が
    /// <c>bound</c> の倍数でない限り必ず偏る（余りが小さい値だけが 1 回多く当たる）。
    /// 範囲が広ければ偏りは小さいが、ここで扱うのは 10^20 個の解から 1 個選ぶ話であり、
    /// 「一様である」ことがこの API の売りそのものなので、偏りは許されない。
    /// </para>
    /// <para>
    /// <b>棄却法</b>: <c>bound - 1</c> を表すのに必要なビット数ぶんだけ乱数ビットを取り、
    /// <c>bound</c> 以上なら捨てて引き直す。取りうる値は <c>2^bits</c> 通りで、
    /// <c>bound &gt; 2^(bits-1)</c>（ビット数の定義より）だから、1 回で当たる確率は必ず
    /// <b>1/2 より大きい</b>。したがって引き直しの回数の期待値は 2 回未満で、
    /// 桁数がいくら大きくなってもこれは変わらない。
    /// </para>
    /// <para>
    /// <b>バッファは使い回す</b>。<c>n</c> 個まとめて取るサンプリングでは同じ <c>bound</c> で
    /// 何度も引くので、必要なバイト数と最上位バイトのマスクは作るときに 1 度だけ求め、
    /// 乱数バイトを受ける配列も 1 本を使い回す。
    /// </para>
    /// <para>
    /// <b>乱数の質は <see cref="Random"/> のもの</b>。この型がするのは「与えられた乱数ビットを
    /// 偏りなく範囲へ写す」ことだけで、暗号論的な強度は求められた <see cref="Random"/> 次第である。
    /// </para>
    /// </remarks>
    internal readonly struct UniformBigInteger
    {
        /// <summary>返す値の上限（この値は返さない）。</summary>
        private readonly BigInteger _bound;

        /// <summary>乱数バイトを受ける作業配列。<c>bound</c> が 1 のときだけ長さ 0。</summary>
        private readonly byte[] _buffer;

        /// <summary>最上位バイトのうち、使ってよいビットだけを残すマスク。</summary>
        private readonly byte _topByteMask;

        /// <summary>上限を決めて乱数源を作る。</summary>
        /// <param name="exclusiveUpperBound">返す値の上限。<b>1 以上</b>でなければならない。</param>
        public UniformBigInteger(BigInteger exclusiveUpperBound)
        {
            Debug.Assert(
                exclusiveUpperBound > BigInteger.Zero,
                $"The exclusive upper bound must be positive, but was {exclusiveUpperBound}.");

            _bound = exclusiveUpperBound;

            // 必要なのは「bound - 1 を表せるビット数」。bound が 2 の冪ならぴったり収まり、
            // 棄却は 1 度も起きない。bound が 1 なら 0 ビット＝乱数を引くまでもなく 0 が答。
            long bitLength = (exclusiveUpperBound - BigInteger.One).GetBitLength();
            int byteCount = (int)((bitLength + 7) / 8);
            int topBits = (int)(bitLength & 7);

            _buffer = byteCount == 0 ? Array.Empty<byte>() : new byte[byteCount];
            _topByteMask = topBits == 0 ? byte.MaxValue : (byte)((1 << topBits) - 1);
        }

        /// <summary><c>0</c> 以上 <c>bound</c> 未満の値を 1 つ、一様に返す。</summary>
        /// <param name="random">乱数の供給元。</param>
        public BigInteger Next(Random random)
        {
            Debug.Assert(random is not null, "The random source must not be null.");

            if (_buffer.Length == 0)
            {
                // 上限が 1 なら答は 0 しかない。乱数は 1 ビットも消費しない。
                return BigInteger.Zero;
            }

            while (true)
            {
                random.NextBytes(_buffer);

                // 最上位バイトの余ったビットを落とす。落とさないと、そのぶんだけ
                // 棄却される確率が上がる（答が偏るわけではないが、無駄に引き直す）。
                _buffer[^1] &= _topByteMask;

                // 符号なし・下位バイト先頭として読む（NextBytes が埋める向きに合わせる）。
                BigInteger value = new BigInteger(_buffer, isUnsigned: true, isBigEndian: false);

                if (value < _bound)
                {
                    return value;
                }
            }
        }
    }
}
