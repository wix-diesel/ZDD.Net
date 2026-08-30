using System;
using System.Diagnostics;

namespace ZDD.Net.Core
{
    /// <summary>
    /// 二項演算の部分問題（ノードの対）を 1 段だけ分解する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 「両者の最上位 item のうち根側のレベルで割る」という手つきは、対を辿るどの演算でも同じである。
    /// 演算ごとに違うのは<b>割った 4 つの断片をどう組み直すか</b>だけなので、割るところだけをここに置く。
    /// <see cref="BinaryOperations"/> は分解と同時に部分問題のキーまで作ってしまうため独自の
    /// <c>Decompose</c> を持つが、断片をそのまま受け取りたい演算
    /// （<see cref="FamilyAlgebraOperations"/> / <see cref="ContainmentOperations"/>）はこれを使う。
    /// </para>
    /// <para>
    /// <b>ノード表への <c>ref</c> は持ち出さない</b>。<see cref="UniqueTable.GetNode"/> が表を伸ばすと
    /// 古い配列を指しうるので、必要な値はここで読み切って値渡しで返す。
    /// </para>
    /// </remarks>
    internal static class NodePair
    {
        /// <summary>
        /// 部分問題 <c>(f, g)</c> を、両者の最上位 item のうち根側のレベルで 1 段分解する。
        /// </summary>
        /// <param name="nodes">ノード表。</param>
        /// <param name="f">左オペランドのノード ID。</param>
        /// <param name="g">右オペランドのノード ID。</param>
        /// <param name="level">分解したレベル（1 以上）。</param>
        /// <param name="f0"><paramref name="f"/> のうち item を含まない側。</param>
        /// <param name="f1"><paramref name="f"/> のうち item を含む側から、item を除いたもの。</param>
        /// <param name="g0"><paramref name="g"/> のうち item を含まない側。</param>
        /// <param name="g1"><paramref name="g"/> のうち item を含む側から、item を除いたもの。</param>
        /// <remarks>
        /// 片方だけが上（根側）にあるときは、下の族はその item に一度も言及していない
        /// ＝ どの集合も item を含まないので、0-枝がその族自身、1-枝が ∅ になる。
        /// </remarks>
        public static void Split(
            NodeTable nodes,
            int f,
            int g,
            out int level,
            out int f0,
            out int f1,
            out int g0,
            out int g1)
        {
            Debug.Assert(
                !NodeTable.IsTerminal(f) || !NodeTable.IsTerminal(g),
                "A pair of terminals is always settled by the base case and never reaches Split.");

            int fLevel = NodeTable.IsTerminal(f) ? 0 : nodes[f].Level;
            int gLevel = NodeTable.IsTerminal(g) ? 0 : nodes[g].Level;

            level = Math.Max(fLevel, gLevel);

            if (fLevel == level)
            {
                ref ZddNode node = ref nodes[f];
                f0 = node.Lo;
                f1 = node.Hi;
            }
            else
            {
                f0 = f;
                f1 = NodeTable.Bottom;
            }

            if (gLevel == level)
            {
                ref ZddNode node = ref nodes[g];
                g0 = node.Lo;
                g1 = node.Hi;
            }
            else
            {
                g0 = g;
                g1 = NodeTable.Bottom;
            }
        }
    }
}
