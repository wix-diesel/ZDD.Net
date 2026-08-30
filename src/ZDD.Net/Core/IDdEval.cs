namespace ZDD.Net.Core
{
    /// <summary>
    /// ZDD を葉側から根へ 1 回走査して 1 つの値に畳み込む「評価器」。
    /// 濃度・確率・重み最適化などは、すべてこの形の DP として書ける。
    /// </summary>
    /// <typeparam name="TValue">畳み込みの途中結果と最終結果の型。</typeparam>
    /// <remarks>
    /// <para>
    /// <b>何を書くのか</b>: ZDD は「item を含まない側（0-枝）」と「item を含む側（1-枝）」に
    /// 族を二分する DAG である。評価器は
    /// </para>
    /// <list type="bullet">
    /// <item><description>終端 ⊥（空の族 ∅）と ⊤（<c>{∅}</c>）に対する値（<see cref="EvalTerminal"/>）</description></item>
    /// <item><description>
    /// 両側の値が出そろったときの合成の仕方（<see cref="EvalNode"/>）
    /// </description></item>
    /// </list>
    /// <para>
    /// の 2 つだけを書く。走査の順序・メモ化・スタックの管理は
    /// <see cref="ZddEvaluation.Evaluate{TEval, TValue}"/> が引き受ける。
    /// 濃度を数えるなら「終端は 1 と 0、合成は <c>lo + hi</c>」（<see cref="CardinalityEval"/>）、
    /// 集合サイズの分布なら「合成は <c>hi</c> を 1 段ずらして足す」（<see cref="SizeDistributionEval"/>）
    /// といった具合に、DP の漸化式がそのまま 2 つのメソッドになる。
    /// </para>
    /// <para>
    /// <b>実装は必ず <c>struct</c> にする</b>（docs/PLAN.md §10-2）。
    /// <see cref="ZddEvaluation.Evaluate{TEval, TValue}"/> はこのインタフェースを
    /// <b>interface 型で受け取らず</b>、<c>where TEval : struct, IDdEval&lt;TValue&gt;</c> の
    /// 型引数として受け取る。こうすると JIT が実装ごとに専用コードを生成し、
    /// ノード 1 個ごとに起きる <see cref="EvalNode"/> の呼び出しが
    /// 仮想呼び出しではなく直接呼び出し（多くはインライン展開）になる。
    /// interface 型で受けると同じコードが数倍遅くなるので、この制約は API 側で強制している。
    /// </para>
    /// <para>
    /// <b>呼ばれ方の約束</b>:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="EvalTerminal"/> は評価 1 回につき <see langword="false"/> と <see langword="true"/> で
    /// 1 度ずつ、走査を始める前に呼ばれる（族の形に依らない）。
    /// </description></item>
    /// <item><description>
    /// <see cref="EvalNode"/> は<b>到達できる非終端ノード 1 個につき 1 回だけ</b>呼ばれる。
    /// 同じノードを共有する親が何人いても 2 度は呼ばれない（メモ化されるため）。
    /// したがって「集合 1 つにつき 1 回」ではなく「ノード 1 つにつき 1 回」であり、
    /// 10^24 個の集合を持つ族でもノード数ぶんの呼び出しで済む。
    /// </description></item>
    /// <item><description>
    /// 子の値が先に確定してから親が呼ばれる（ボトムアップ）。呼ばれる順序はそれ以上規定しない。
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>状態を持ってもよい</b>: 評価器は値渡しで <see cref="ZddEvaluation.Evaluate{TEval, TValue}"/> に
    /// 渡され、その中で 1 つのコピーが使い回される。重みの配列のような読み取り専用の入力を
    /// フィールドに持たせて構わない。呼び出し側の変数には変更が伝わらない。
    /// </para>
    /// </remarks>
    /// <example>
    /// 族に属する集合の個数を数える評価器（<see cref="CardinalityEval"/> と同じもの）:
    /// <code>
    /// public readonly struct MyCountEval : IDdEval&lt;BigInteger&gt;
    /// {
    ///     public BigInteger EvalTerminal(bool isTrue) =&gt; isTrue ? BigInteger.One : BigInteger.Zero;
    ///     public BigInteger EvalNode(int item, BigInteger lo, BigInteger hi) =&gt; lo + hi;
    /// }
    ///
    /// BigInteger count = family.Evaluate&lt;MyCountEval, BigInteger&gt;(default);
    /// </code>
    /// </example>
    public interface IDdEval<TValue>
    {
        /// <summary>終端の値を返す。</summary>
        /// <param name="isTrue">
        /// 終端 ⊤（<c>{∅}</c>、空集合 1 つだけを持つ族）なら <see langword="true"/>、
        /// 終端 ⊥（空の族 ∅）なら <see langword="false"/>。
        /// </param>
        /// <remarks>
        /// 「⊤ に着いた ＝ ここまで辿ってきた枝の選び方が 1 つの集合を成す」、
        /// 「⊥ に着いた ＝ その選び方は族に無い」と読む。濃度なら 1 と 0、
        /// 重み最大なら 0 と「負の無限大」に当たる。
        /// </remarks>
        TValue EvalTerminal(bool isTrue);

        /// <summary>非終端ノード 1 個ぶんの合成を行う。</summary>
        /// <param name="item">
        /// このノードが分岐している item index（0 以上 <see cref="ZddManager.VariableCount"/> 未満）。
        /// 重み <c>w[item]</c> を引くなど、変数ごとの情報が要るときに使う。
        /// </param>
        /// <param name="lo"><paramref name="item"/> を<b>含まない</b>側（0-枝）の評価値。</param>
        /// <param name="hi">
        /// <paramref name="item"/> を<b>含む</b>側（1-枝）の評価値。1-枝の先にある族は
        /// <paramref name="item"/> を<b>取り除いた</b>ものなので、
        /// <paramref name="item"/> ぶんの寄与はここで足すことになる。
        /// </param>
        /// <remarks>
        /// <b>飛ばされた変数は現れない</b>: ZDD はゼロサプレス削減規則により
        /// 「1-枝が ⊥ に落ちるノード」を持たない。すなわち族のどの集合にも属さない item は
        /// 図から消えており、このメソッドにも渡ってこない。「出てこない item は
        /// どの集合にも入っていない」と読めばよく、飛ばされた段を補う処理は要らない。
        /// </remarks>
        TValue EvalNode(int item, TValue lo, TValue hi);
    }
}
