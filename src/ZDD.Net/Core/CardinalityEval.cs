using System.Numerics;

namespace ZDD.Net.Core
{
    /// <summary>
    /// 族に属する集合の個数（濃度）を厳密に数える評価器。<see cref="Zdd.Count"/> の中身。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>漸化式</b>: 終端 ⊤（<c>{∅}</c>）は集合を 1 つ持ち、終端 ⊥（∅）は 1 つも持たない。
    /// 非終端ノードでは「item を含まない集合の個数」と「item を含む集合の個数」の和になるので、
    /// 単に <c>lo + hi</c>。これが「10^24 個の解を数えられる」ことの正体で、
    /// ノード数ぶんの足し算しか行わない。
    /// </para>
    /// <para>
    /// <b><see cref="BigInteger"/> である理由</b>: 濃度は変数の個数に対して指数的に増える
    /// （n 変数の冪集合なら 2^n）ので、64bit では 64 変数で溢れる。速さが要るときは
    /// <see cref="ApproximateCardinalityEval"/>（<see cref="double"/> 近似）を使う
    /// （docs/PLAN.md §10-5）。
    /// </para>
    /// </remarks>
    public readonly struct CardinalityEval : IDdEval<BigInteger>
    {
        /// <inheritdoc/>
        public BigInteger EvalTerminal(bool isTrue) => isTrue ? BigInteger.One : BigInteger.Zero;

        /// <inheritdoc/>
        public BigInteger EvalNode(int item, BigInteger lo, BigInteger hi) => lo + hi;
    }
}
