using System;
using System.Collections.Generic;
using System.Linq;

namespace ZDD.Net.Tests.Harness
{
    /// <summary>
    /// 生成した DOT が Graphviz に食わせられる形になっているかを、Graphviz 抜きで確かめる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ここで見るのは「構造」だけ</b>。DOT の文法を丸ごと実装するのは割に合わないので、
    /// <see cref="ZDD.Net.Core.Zdd.ToDot"/> が実際に出す形（1 行 1 文、属性は角括弧、
    /// 段のまとまりは <c>{ rank=same; … }</c> の 1 行）に限って検証する。
    /// 文法そのものは CI が本物の <c>dot -Tsvg</c> に通して確かめる（.github/workflows/ci.yml）。
    /// </para>
    /// <para>
    /// それでもここに置く価値があるのは、<b>Graphviz が入っていない環境でも回る</b>ことと、
    /// 「宣言していないノードを辺で参照している」のように <c>dot</c> が黙って受け入れてしまう
    /// （勝手にノードを作る）間違いを捕まえられることによる。
    /// </para>
    /// </remarks>
    internal static class DotSyntax
    {
        /// <summary>
        /// <paramref name="dot"/> を検証し、宣言されたノード名を出てきた順に返す。
        /// </summary>
        /// <param name="dot">検証する DOT のソース。</param>
        /// <exception cref="InvalidOperationException">形が崩れている場合。</exception>
        public static IReadOnlyList<string> Validate(string dot)
        {
            ArgumentNullException.ThrowIfNull(dot);

            if (!dot.StartsWith("digraph zdd {\n", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The output must open with a 'digraph zdd {' line.");
            }

            if (!dot.EndsWith("\n}\n", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The output must close with a '}' line.");
            }

            // 改行は環境に依らず \n に固定してあるので、\r が混じっていたら決定性が壊れている。
            if (dot.Contains('\r', StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The output must use '\\n' line endings only.");
            }

            List<string> declared = new List<string>();
            HashSet<string> declaredSet = new HashSet<string>(StringComparer.Ordinal);
            List<string> referenced = new List<string>();

            string[] lines = dot.Split('\n');

            // 先頭の digraph 行と、末尾の '}' 行・最後の空要素は本体ではない。
            for (int index = 1; index < lines.Length - 2; index++)
            {
                string line = lines[index];
                if (line.Length == 0)
                {
                    continue;
                }

                if (!line.StartsWith("    ", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Line {index + 1} is not indented inside the graph: '{line}'.");
                }

                string statement = line.Substring(4);

                if (statement.Count(character => character == '"') % 2 != 0)
                {
                    throw new InvalidOperationException($"Line {index + 1} has an unbalanced quote: '{line}'.");
                }

                if (statement.StartsWith("{ ", StringComparison.Ordinal))
                {
                    referenced.AddRange(ParseRankGroup(statement, index + 1));
                    continue;
                }

                if (statement.Contains('{', StringComparison.Ordinal) || statement.Contains('}', StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Line {index + 1} opens a block that never closes on the same line: '{line}'.");
                }

                if (!statement.EndsWith(';'))
                {
                    throw new InvalidOperationException($"Line {index + 1} does not end with ';': '{line}'.");
                }

                string body = TrimAttributes(statement.Substring(0, statement.Length - 1), index + 1);

                int arrow = body.IndexOf("->", StringComparison.Ordinal);
                if (arrow >= 0)
                {
                    referenced.Add(body.Substring(0, arrow).Trim());
                    referenced.Add(body.Substring(arrow + 2).Trim());
                    continue;
                }

                string name = body.Trim();

                // graph / node / edge は既定の属性を与える予約語で、ノードの宣言ではない。
                if (name is "graph" or "node" or "edge")
                {
                    continue;
                }

                if (!declaredSet.Add(name))
                {
                    throw new InvalidOperationException($"Line {index + 1} declares '{name}' a second time.");
                }

                declared.Add(name);
            }

            foreach (string name in referenced)
            {
                if (name.Length == 0)
                {
                    throw new InvalidOperationException("An edge or rank group referred to an empty node name.");
                }

                if (!declaredSet.Contains(name))
                {
                    // dot は未宣言のノードを黙って作るので、ここで捕まえないと絵が静かに狂う。
                    throw new InvalidOperationException($"'{name}' is used but never declared.");
                }
            }

            return declared;
        }

        /// <summary><c>{ rank=same; a; b; }</c> の中身のノード名を返す。</summary>
        private static IEnumerable<string> ParseRankGroup(string statement, int lineNumber)
        {
            if (!statement.StartsWith("{ rank=same;", StringComparison.Ordinal)
                || !statement.EndsWith(" }", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Line {lineNumber} is not a well-formed rank group: '{statement}'.");
            }

            string inner = statement["{ rank=same;".Length..^2];

            return inner
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
        }

        /// <summary>末尾の <c>[…]</c> を取り除く。角括弧の対応が取れていなければ例外。</summary>
        private static string TrimAttributes(string statement, int lineNumber)
        {
            int open = statement.IndexOf('[', StringComparison.Ordinal);
            if (open < 0)
            {
                if (statement.Contains(']', StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Line {lineNumber} closes an attribute list it never opened.");
                }

                return statement;
            }

            if (!statement.EndsWith(']'))
            {
                throw new InvalidOperationException($"Line {lineNumber} has an unterminated attribute list.");
            }

            string attributes = statement[(open + 1)..^1];
            if (attributes.Contains('[', StringComparison.Ordinal) || attributes.Contains(']', StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Line {lineNumber} nests attribute lists.");
            }

            return statement.Substring(0, open);
        }
    }
}
