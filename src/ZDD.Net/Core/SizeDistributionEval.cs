using System;
using System.Numerics;

namespace ZDD.Net.Core
{
    /// <summary>
    /// 族に属する集合の個数を、集合の要素数（サイズ）ごとに数える評価器。
    /// <see cref="Zdd.CountBySize"/> の中身。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>値の形</b>: 添字 <c>k</c> に「要素数 <c>k</c> の集合の個数」が入った配列。
    /// 長さはその部分族に現れる<b>最大の要素数 + 1</b>で、空の族 ∅ では長さ 0 になる。
    /// マネージャの変数の個数ではなく実際の最大サイズに合わせるのは、
    /// 変数 10 万のマネージャで小さな族を数えるときに 10 万要素の配列を
    /// ノードごとに作らないため。
    /// </para>
    /// <para>
    /// <b>漸化式</b>: ⊤ は「要素数 0 の集合が 1 つ」なので <c>[1]</c>、⊥ は空配列。
    /// 非終端ノードでは、item を含まない側はそのまま、item を含む側は集合の要素数が
    /// 1 つ増えるので<b>1 段ずらして</b>足す:
    /// <c>result[k] = lo[k] + hi[k - 1]</c>。多項式の言葉でいえば
    /// <c>result(x) = lo(x) + x · hi(x)</c> で、係数が各サイズの個数にあたる。
    /// </para>
    /// <para>
    /// <b>費用</b>: ノードごとに長さ最大 <c>(そのノード以下の最大サイズ + 1)</c> の配列を作るので、
    /// 時間・メモリとも <c>O(ノード数 × 最大サイズ)</c> かかる。濃度だけが要るなら
    /// <see cref="CardinalityEval"/> のほうが桁違いに軽い。
    /// </para>
    /// <para>
    /// <b>配列は書き換えない</b>: 途中結果の配列はノード間で共有されうる（片側が空なら
    /// もう片側をそのまま返す）ので、この評価器は受け取った配列を変更しない。
    /// </para>
    /// </remarks>
    public readonly struct SizeDistributionEval : IDdEval<BigInteger[]>
    {
        /// <inheritdoc/>
        public BigInteger[] EvalTerminal(bool isTrue) =>
            isTrue ? new BigInteger[] { BigInteger.One } : Array.Empty<BigInteger>();

        /// <inheritdoc/>
        public BigInteger[] EvalNode(int item, BigInteger[] lo, BigInteger[] hi)
        {
            ArgumentNullException.ThrowIfNull(lo);
            ArgumentNullException.ThrowIfNull(hi);

            // 片側が空の族なら、もう片側がそのまま答（1 段ずらす必要のある hi は除く）。
            if (hi.Length == 0)
            {
                return lo;
            }

            BigInteger[] result = new BigInteger[Math.Max(lo.Length, hi.Length + 1)];

            for (int size = 0; size < lo.Length; size++)
            {
                result[size] = lo[size];
            }

            // item を含む側は、集合の要素数が item のぶんだけ 1 つ大きくなる。
            for (int size = 0; size < hi.Length; size++)
            {
                result[size + 1] += hi[size];
            }

            return result;
        }
    }
}
