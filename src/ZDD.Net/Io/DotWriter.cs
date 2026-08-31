using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ZDD.Net.Core;
using ZDD.Net.Internal;

namespace ZDD.Net.Io
{
    /// <summary>
    /// 族を Graphviz の DOT 形式で書き出す（<see cref="Zdd.ToDot"/> / <see cref="Zdd.WriteDot"/> の中身）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ZDD は目で見ないとデバッグできない</b>（docs/PLAN.md §14-10）。共有された部分グラフや
    /// ゼロサプレス規則で消えた段は、数値だけ眺めていても分からない。DOT は
    /// <c>dot -Tsvg</c> に流すだけで絵になるので、依存を増やさずに「見る」手段を用意できる。
    /// </para>
    /// <para>
    /// <b>絵の約束</b>:
    /// </para>
    /// <list type="bullet">
    /// <item><description>非終端ノードは円、ラベルは分岐する item（<c>x0</c>, <c>x1</c>, …）</description></item>
    /// <item><description>終端は角丸でない箱で、⊥ が空の族 ∅、⊤ が <c>{∅}</c></description></item>
    /// <item><description>0-枝（item を含まない側）は<b>破線</b>、1-枝（含む側）は<b>実線</b></description></item>
    /// <item><description>同じ item のノードは <c>rank=same</c> で同じ段に並ぶ。段は根側が上</description></item>
    /// <item><description>根の位置が分かるよう、根には印だけの無地のノードから矢印を引く</description></item>
    /// </list>
    /// <para>
    /// <b>再帰しない</b>（docs/PLAN.md §4.5）。ZDD の深さは変数の個数そのもので、10 万規模の族を
    /// 素直な再帰で辿ると <c>StackOverflowException</c> になり、.NET ではこれを catch できずに
    /// プロセスが即死する。走査は <c>int</c> 配列の明示スタックで行う。
    /// </para>
    /// <para>
    /// <b>出力は決定的</b>: ノードは (段, ノード ID) の順に並べ、改行は環境に依らず <c>\n</c> に固定する。
    /// 同じ族からは常に同じ文字列が出るので、スナップショットとして突き合わせられる。
    /// </para>
    /// <para>
    /// <b>費用</b>: 出力そのものは <see cref="TextWriter"/> へ流すので溜め込まないが、
    /// 段ごとに並べるために到達ノードの一覧は一度メモリに載せる（ノード数に比例）。
    /// </para>
    /// </remarks>
    internal static class DotWriter
    {
        /// <summary>明示スタックの初期段数。足りなくなれば倍化する。</summary>
        private const int InitialStackCapacity = 32;

        /// <summary>終端 ⊥ に与える DOT のノード名。実ノードは <c>n</c> + ID なので衝突しない。</summary>
        private const string BottomName = "bottom";

        /// <summary>終端 ⊤ に与える DOT のノード名。</summary>
        private const string TopName = "top";

        /// <summary>根の位置を指す印のノード名。</summary>
        private const string RootName = "root";

        /// <summary>族の DOT 表現を文字列にして返す。</summary>
        /// <param name="zdd">書き出す族。</param>
        public static string Write(in Zdd zdd)
        {
            // 改行は Write(zdd, writer) が \n を直に書くので、StringWriter の NewLine には依らない。
            using StringWriter writer = new StringWriter(CultureInfo.InvariantCulture);
            Write(zdd, writer);
            return writer.ToString();
        }

        /// <summary>族の DOT 表現を <paramref name="writer"/> へ流す。</summary>
        /// <param name="zdd">書き出す族。</param>
        /// <param name="writer">書き出し先。</param>
        public static void Write(in Zdd zdd, TextWriter writer)
        {
            ThrowHelper.ThrowIfNull(writer, nameof(writer));

            ZddManager manager = zdd.Manager;

            // 破棄済みならここで ObjectDisposedException になる。
            NodeTable nodes = manager.Table.Nodes;

            int rootId = zdd.Id;
            int[] ids = CollectByLevel(manager, nodes, rootId, out bool usesBottom, out bool usesTop);

            writer.Write("digraph zdd {\n");
            writer.Write("    graph [rankdir=TB];\n");
            writer.Write("    node [shape=circle, fontname=\"sans-serif\"];\n");
            writer.Write("    edge [fontname=\"sans-serif\"];\n");
            writer.Write('\n');

            WriteNodeDeclarations(writer, manager, nodes, ids, usesBottom, usesTop);
            WriteEdges(writer, nodes, ids, rootId);
            WriteRanks(writer, nodes, ids, usesBottom, usesTop);

            writer.Write("}\n");
        }

        /// <summary>
        /// 根から到達できる非終端ノードを「段が根側のものから、同じ段では ID の小さいものから」
        /// の順に並べて返す。終端に着くかどうかも同時に調べる。
        /// </summary>
        private static int[] CollectByLevel(
            ZddManager manager,
            NodeTable nodes,
            int rootId,
            out bool usesBottom,
            out bool usesTop)
        {
            usesBottom = rootId == NodeTable.Bottom;
            usesTop = rootId == NodeTable.Top;

            if (NodeTable.IsTerminal(rootId))
            {
                return Array.Empty<int>();
            }

            HashSet<int> visited = new HashSet<int> { rootId };
            List<int> found = new List<int>();

            int[] stack = new int[InitialStackCapacity];
            int top = 0;
            stack[top++] = rootId;

            while (top > 0)
            {
                int id = stack[--top];
                found.Add(id);

                int lo;
                int hi;
                {
                    ref ZddNode node = ref nodes[id];
                    lo = node.Lo;
                    hi = node.Hi;
                }

                Visit(nodes, visited, ref stack, ref top, lo, ref usesBottom, ref usesTop);
                Visit(nodes, visited, ref stack, ref top, hi, ref usesBottom, ref usesTop);
            }

            int[] ids = found.ToArray();

            // 並べ替えの鍵は「根側の段が先、同じ段なら ID の小さい順」。上位 32bit に
            // 段の深さ（根側ほど小さい）、下位 32bit に ID を詰めれば、long 1 本の昇順で済む。
            // ID は非負なので、そのまま下位に置いても順序は狂わない。
            long[] keys = new long[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                long depth = manager.VariableCount - nodes[ids[i]].Level;
                keys[i] = (depth << 32) | (uint)ids[i];
            }

            Array.Sort(keys, ids);
            return ids;
        }

        /// <summary>子を 1 つ見る。終端なら印を立て、未訪問の非終端なら積む。</summary>
        private static void Visit(
            NodeTable nodes,
            HashSet<int> visited,
            ref int[] stack,
            ref int top,
            int childId,
            ref bool usesBottom,
            ref bool usesTop)
        {
            if (NodeTable.IsTerminal(childId))
            {
                if (childId == NodeTable.Bottom)
                {
                    usesBottom = true;
                }
                else
                {
                    usesTop = true;
                }

                return;
            }

            if (!visited.Add(childId))
            {
                return;
            }

            if (top == stack.Length)
            {
                Array.Resize(ref stack, stack.Length * 2);
            }

            stack[top++] = childId;
        }

        private static void WriteNodeDeclarations(
            TextWriter writer,
            ZddManager manager,
            NodeTable nodes,
            int[] ids,
            bool usesBottom,
            bool usesTop)
        {
            writer.Write("    ");
            writer.Write(RootName);
            writer.Write(" [shape=none, label=\"\", width=0, height=0];\n");

            foreach (int id in ids)
            {
                writer.Write("    ");
                WriteName(writer, id);
                writer.Write(" [label=\"x");
                writer.Write(manager.ItemOf(nodes[id].Level).ToString(CultureInfo.InvariantCulture));
                writer.Write("\"];\n");
            }

            if (usesBottom)
            {
                writer.Write("    ");
                writer.Write(BottomName);
                writer.Write(" [shape=box, label=\"⊥\"];\n");
            }

            if (usesTop)
            {
                writer.Write("    ");
                writer.Write(TopName);
                writer.Write(" [shape=box, label=\"⊤\"];\n");
            }

            writer.Write('\n');
        }

        private static void WriteEdges(TextWriter writer, NodeTable nodes, int[] ids, int rootId)
        {
            writer.Write("    ");
            writer.Write(RootName);
            writer.Write(" -> ");
            WriteName(writer, rootId);
            writer.Write(";\n");

            foreach (int id in ids)
            {
                int lo;
                int hi;
                {
                    ref ZddNode node = ref nodes[id];
                    lo = node.Lo;
                    hi = node.Hi;
                }

                // 0-枝は破線、1-枝は実線。実線は DOT の既定だが、対であることが読み取れるよう明示する。
                WriteEdge(writer, id, lo, "dashed");
                WriteEdge(writer, id, hi, "solid");
            }

            writer.Write('\n');
        }

        private static void WriteEdge(TextWriter writer, int from, int to, string style)
        {
            writer.Write("    ");
            WriteName(writer, from);
            writer.Write(" -> ");
            WriteName(writer, to);
            writer.Write(" [style=");
            writer.Write(style);
            writer.Write("];\n");
        }

        /// <summary>
        /// 同じ item のノードを <c>rank=same</c> で束ねる。<paramref name="ids"/> は段ごとに
        /// 並んでいるので、段が変わる境目で区切るだけで済む。終端は最下段にまとめる。
        /// </summary>
        private static void WriteRanks(
            TextWriter writer,
            NodeTable nodes,
            int[] ids,
            bool usesBottom,
            bool usesTop)
        {
            int index = 0;
            while (index < ids.Length)
            {
                int level = nodes[ids[index]].Level;

                writer.Write("    { rank=same;");

                while (index < ids.Length && nodes[ids[index]].Level == level)
                {
                    writer.Write(' ');
                    WriteName(writer, ids[index]);
                    writer.Write(';');
                    index++;
                }

                writer.Write(" }\n");
            }

            if (!usesBottom && !usesTop)
            {
                return;
            }

            writer.Write("    { rank=same;");

            if (usesBottom)
            {
                writer.Write(' ');
                writer.Write(BottomName);
                writer.Write(';');
            }

            if (usesTop)
            {
                writer.Write(' ');
                writer.Write(TopName);
                writer.Write(';');
            }

            writer.Write(" }\n");
        }

        /// <summary>ノード ID を DOT のノード名に写す。終端だけ名前を固定する。</summary>
        private static void WriteName(TextWriter writer, int id)
        {
            switch (id)
            {
                case NodeTable.Bottom:
                    writer.Write(BottomName);
                    return;
                case NodeTable.Top:
                    writer.Write(TopName);
                    return;
                default:
                    writer.Write('n');
                    writer.Write(id.ToString(CultureInfo.InvariantCulture));
                    return;
            }
        }
    }
}
