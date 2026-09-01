using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;

namespace ZDD.Net.Tests.Frontier
{
    /// <summary>
    /// 一時ノード表が表している族を、根から ⊤ までの経路を全部たどって列挙するテスト用の読み手。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 展開の正しさは「幅やノード数が期待通り」だけでは足りない。枝の付け替えを間違えても数は合うので、
    /// <b>できあがった表が受理する集合</b>を <see cref="SpecWalker"/>（スペックを素直にたどるだけの実装）と
    /// 突き合わせる。ここが一致していれば、重複除去も終端への接続も水準飛ばしもまとめて確かめられる。
    /// </para>
    /// <para>
    /// 経路を 1 本ずつたどるので集合の個数だけ手間が掛かる。小さなお題専用である。
    /// </para>
    /// </remarks>
    internal static class TemporaryTableSets
    {
        /// <summary>表が受理する集合を、item の昇順に並べた配列として全部返す。</summary>
        /// <param name="table">読む一時ノード表。item は <c>RootLevel - level</c> とみなす。</param>
        public static List<int[]> Accepted(TemporaryNodeTable table)
        {
            List<int[]> accepted = new List<int[]>();

            if (table.Root.IsBottom)
            {
                return accepted;
            }

            if (table.Root.IsTop)
            {
                accepted.Add(Array.Empty<int>());
                return accepted;
            }

            Walk(table, table.Root, new List<int>(), accepted);

            return accepted;
        }

        private static void Walk(TemporaryNodeTable table, TemporaryNodeId id, List<int> chosen, List<int[]> accepted)
        {
            TemporaryNode node = table[id.Level][id.Index];

            for (int value = 0; value <= 1; value++)
            {
                TemporaryNodeId child = value == 0 ? node.Lo : node.Hi;

                if (child.IsBottom)
                {
                    continue;
                }

                if (value == 1)
                {
                    chosen.Add(table.RootLevel - id.Level);
                }

                if (child.IsTop)
                {
                    // ⊤ に着いた時点で、残りの item は「入れない」に確定する（ゼロサプレス）。
                    accepted.Add(chosen.ToArray());
                }
                else
                {
                    Walk(table, child, chosen, accepted);
                }

                if (value == 1)
                {
                    chosen.RemoveAt(chosen.Count - 1);
                }
            }
        }
    }
}
