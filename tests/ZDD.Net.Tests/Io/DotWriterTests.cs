using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Io
{
    /// <summary>
    /// <see cref="Zdd.ToDot"/> / <see cref="Zdd.WriteDot"/> の検証。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 小さな族については<b>出力を丸ごと</b>期待値と突き合わせる（スナップショット）。DOT は
    /// 目で見るための出力なので、「壊れていないこと」より「同じ族から同じ絵が出ること」のほうが
    /// 効く。族はすべて <see cref="ZddManager.CreateNode"/> で下から組み立てる。ノード ID は
    /// 確保した順に振られるので、こうすると期待値の <c>n2</c> … が演算の実装に左右されない。
    /// </para>
    /// <para>
    /// 大きめの族や境界の族については、形が崩れていないことを <see cref="DotSyntax"/> で確かめる。
    /// 本物の Graphviz に通す検証は CI が行う（.github/workflows/ci.yml）。
    /// </para>
    /// </remarks>
    public class DotWriterTests
    {
        /// <summary>スタックオーバーフローの回帰テストで使う変数の個数（docs/PLAN.md §4.5）。</summary>
        private const int DeepVariableCount = 100_000;

        // ---- スナップショット ----

        [Fact]
        public void ASmallFamilyIsWrittenExactly()
        {
            using ZddManager manager = new ZddManager(3);

            // {{0}, {1, 2}}。下から順に作るので、ノード ID は 2, 3, 4 になる。
            Zdd two = manager.CreateNode(2, lo: manager.Empty, hi: manager.Base);
            Zdd oneTwo = manager.CreateNode(1, lo: manager.Empty, hi: two);
            Zdd family = manager.CreateNode(0, lo: oneTwo, hi: manager.Base);

            Assert.Equal(
                """
                digraph zdd {
                    graph [rankdir=TB];
                    node [shape=circle, fontname="sans-serif"];
                    edge [fontname="sans-serif"];

                    root [shape=none, label="", width=0, height=0];
                    n4 [label="x0"];
                    n3 [label="x1"];
                    n2 [label="x2"];
                    bottom [shape=box, label="⊥"];
                    top [shape=box, label="⊤"];

                    root -> n4;
                    n4 -> n3 [style=dashed];
                    n4 -> top [style=solid];
                    n3 -> bottom [style=dashed];
                    n3 -> n2 [style=solid];
                    n2 -> bottom [style=dashed];
                    n2 -> top [style=solid];

                    { rank=same; n4; }
                    { rank=same; n3; }
                    { rank=same; n2; }
                    { rank=same; bottom; top; }
                }

                """.ReplaceLineEndings("\n"),
                family.ToDot());
        }

        [Fact]
        public void TheEmptyFamilyIsJustTheBottomTerminal()
        {
            using ZddManager manager = new ZddManager(3);

            Assert.Equal(
                """
                digraph zdd {
                    graph [rankdir=TB];
                    node [shape=circle, fontname="sans-serif"];
                    edge [fontname="sans-serif"];

                    root [shape=none, label="", width=0, height=0];
                    bottom [shape=box, label="⊥"];

                    root -> bottom;

                    { rank=same; bottom; }
                }

                """.ReplaceLineEndings("\n"),
                manager.Empty.ToDot());
        }

        [Fact]
        public void TheBaseFamilyIsJustTheTopTerminal()
        {
            using ZddManager manager = new ZddManager(3);

            string dot = manager.Base.ToDot();

            Assert.Contains("root -> top;", dot, StringComparison.Ordinal);
            Assert.Contains("top [shape=box, label=\"⊤\"];", dot, StringComparison.Ordinal);

            // 到達しない終端は描かない。⊥ は ∅ を表すので、出ていたら「空の族も混ざっている」と読めてしまう。
            Assert.DoesNotContain("bottom", dot, StringComparison.Ordinal);
        }

        // ---- 絵の約束 ----

        [Fact]
        public void ZeroEdgesAreDashedAndOneEdgesAreSolid()
        {
            using ZddManager manager = new ZddManager(2);

            // {{1}, {0}}: item 0 のノードは 0-枝で {{1}} に、1-枝で {∅} に降りる。
            Zdd one = manager.CreateNode(1, lo: manager.Empty, hi: manager.Base);
            Zdd family = manager.CreateNode(0, lo: one, hi: manager.Base);

            string dot = family.ToDot();

            Assert.Contains("n3 -> n2 [style=dashed];", dot, StringComparison.Ordinal);
            Assert.Contains("n3 -> top [style=solid];", dot, StringComparison.Ordinal);
        }

        [Fact]
        public void SharedNodesAreWrittenOnce()
        {
            using ZddManager manager = new ZddManager(3);

            // item 2 のノードを 2 つの親が指す形。共有されているノードは 1 度しか出てはならない。
            Zdd two = manager.CreateNode(2, lo: manager.Empty, hi: manager.Base);
            Zdd left = manager.CreateNode(1, lo: two, hi: manager.Base);
            Zdd family = manager.CreateNode(0, lo: left, hi: two);

            string dot = family.ToDot();

            IReadOnlyList<string> declared = DotSyntax.Validate(dot);

            // root と終端 2 個、非終端 3 個。
            Assert.Equal(new[] { "root", "n4", "n3", "n2", "bottom", "top" }, declared);
            Assert.Equal(3L, family.NodeCount);
        }

        [Fact]
        public void NodesOnTheSameLevelShareARank()
        {
            using ZddManager manager = new ZddManager(3);

            // item 2 の位置に 2 つのノード（{{2}} と {∅, {2}}）が並ぶ族を作る。
            Zdd two = manager.CreateNode(2, lo: manager.Empty, hi: manager.Base);
            Zdd twoOrNot = manager.CreateNode(2, lo: manager.Base, hi: manager.Base);
            Zdd one = manager.CreateNode(1, lo: two, hi: twoOrNot);

            string dot = one.ToDot();

            // 段の並びは根側が先で、同じ段では ID の小さい順。
            Assert.Contains("{ rank=same; n4; }\n    { rank=same; n2; n3; }", dot, StringComparison.Ordinal);
            DotSyntax.Validate(dot);
        }

        [Fact]
        public void LevelsSkippedByTheReductionRuleLeaveNoNode()
        {
            using ZddManager manager = new ZddManager(4);

            // {{0, 3}}。item 1 と 2 はどの集合にも現れないので、段そのものが無い。
            Zdd three = manager.CreateNode(3, lo: manager.Empty, hi: manager.Base);
            Zdd family = manager.CreateNode(0, lo: manager.Empty, hi: three);

            string dot = family.ToDot();

            Assert.Contains("[label=\"x0\"]", dot, StringComparison.Ordinal);
            Assert.Contains("[label=\"x3\"]", dot, StringComparison.Ordinal);
            Assert.DoesNotContain("[label=\"x1\"]", dot, StringComparison.Ordinal);
            Assert.DoesNotContain("[label=\"x2\"]", dot, StringComparison.Ordinal);
        }

        // ---- 出力先 ----

        [Fact]
        public void WriteDotProducesTheSameTextAsToDot()
        {
            using ZddManager manager = new ZddManager(6);

            Zdd family = manager.Empty.Complement();

            using StringWriter writer = new StringWriter();
            family.WriteDot(writer);

            Assert.Equal(family.ToDot(), writer.ToString());
        }

        [Fact]
        public void WriteDotRejectsANullWriter()
        {
            using ZddManager manager = new ZddManager(2);

            Assert.Equal(
                "writer",
                Assert.Throws<ArgumentNullException>(() => manager.Base.WriteDot(null!)).ParamName);
        }

        [Fact]
        public void TheOutputIsTheSameEveryTime()
        {
            using ZddManager manager = new ZddManager(8);

            Zdd family = manager.Empty;
            for (int item = 0; item < 8; item += 2)
            {
                family |= manager.Singleton(item);
            }

            Assert.Equal(family.ToDot(), family.ToDot());
        }

        // ---- 形の検証 ----

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(12)]
        public void EveryFamilyProducesWellFormedDot(int variableCount)
        {
            using ZddManager manager = new ZddManager(variableCount);

            foreach (Zdd family in Families(manager, variableCount))
            {
                IReadOnlyList<string> declared = DotSyntax.Validate(family.ToDot());

                // 宣言のうち n から始まるものが非終端。残りは root と、族が使っている終端。
                Assert.Equal((int)family.NodeCount, declared.Count(name => name.StartsWith('n')));
            }
        }

        // ---- 深い ZDD（docs/PLAN.md §4.5 の回帰テスト）----

        [Fact]
        [Trait("Category", "Slow")]
        public void ADeepFamilyDoesNotOverflowTheStack()
        {
            using ZddManager manager = new ZddManager(DeepVariableCount);

            // 変数 10 万個すべてを含む集合 1 つだけの族。ノードが 10 万段に連なる。
            Zdd chain = manager.Base;
            for (int item = DeepVariableCount - 1; item >= 0; item--)
            {
                chain = manager.CreateNode(item, lo: manager.Empty, hi: chain);
            }

            // 出力そのものは溜め込まないので、10 万段でもメモリに載せずに書き切れる。
            CountingWriter counter = new CountingWriter();
            chain.WriteDot(counter);

            // 内訳: digraph 行 1 ＋ 既定属性 3 ＋ 空行 3 ＋ root の宣言 1 ＋ 終端の宣言 2
            //     ＋ root からの辺 1 ＋ 閉じ括弧 1 ＝ 12 行に、ノード 1 個あたり
            //     宣言 1 ＋ 辺 2 ＋ 段 1 ＝ 4 行、それに終端の段の 1 行を足したもの。
            Assert.Equal((4L * DeepVariableCount) + 13, counter.Lines);
        }

        // ---- 補助 ----

        private static IEnumerable<Zdd> Families(ZddManager manager, int variableCount)
        {
            yield return manager.Empty;
            yield return manager.Base;
            yield return manager.Empty.Complement();

            Zdd singletons = manager.Empty;
            Zdd full = manager.Base;

            for (int item = 0; item < variableCount; item++)
            {
                singletons |= manager.Singleton(item);
                full *= manager.Singleton(item);
            }

            yield return singletons;
            yield return full;
            yield return singletons | full;
            yield return manager.Empty.Complement() - full;
        }

        /// <summary>書かれた内容を捨てて行数だけ数える <see cref="TextWriter"/>。</summary>
        private sealed class CountingWriter : TextWriter
        {
            public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

            public long Lines { get; private set; }

            public override void Write(char value)
            {
                if (value == '\n')
                {
                    Lines++;
                }
            }
        }
    }
}
