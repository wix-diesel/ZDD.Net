using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;
using ZDD.Net.Frontier;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Frontier
{
    /// <summary>
    /// トップダウン幅優先展開（<see cref="TopDownExpander{TSpec, TState}"/>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// フロンティア法の第 1 パスそのもの。「根の状態から始めて、水準を 1 つずつ降りながら
    /// 各状態の 0-枝／1-枝をたどり、次の水準の状態集合を作る」——ここが間違うと、
    /// 以降のパスが何をしても答えは合わない。
    /// </para>
    /// <para>
    /// 見どころは 3 つ。<b>同じ状態に至った枝が 1 つのノードに合流すること</b>（これが無いと指数爆発する）、
    /// <b>⊥ / ⊤ が正しく終端に着くこと</b>、そして<b>上限とキャンセルで途中で止まれること</b>
    /// （docs/PLAN.md §13 のリスク対策。メモリを使い切って落ちるのではなく、原因の分かる例外で止める）。
    /// </para>
    /// </remarks>
    public class TopDownExpanderTests
    {
        /// <summary>
        /// 状態が 1 種類しか無いスペックでは、どの水準も幅 1 になり、ノード数は水準の数と等しい。
        /// </summary>
        [Fact]
        public void AllStatesThatMergeLeaveOneNodePerLevel()
        {
            TemporaryNodeTable table = Expand(new FreeChoiceSpec(4));

            Assert.Equal(4, table.RootLevel);
            Assert.Equal(new TemporaryNodeId(4, 0), table.Root);
            Assert.Equal(4, table.NodeCount);

            for (int level = 1; level <= 4; level++)
            {
                Assert.Equal(1, table.Width(level));
            }

            // 途中の水準は 2 本とも 1 つ下のノードへ、最後の水準は 2 本とも ⊤ へ着く。
            Assert.Equal(new TemporaryNode(new TemporaryNodeId(3, 0), new TemporaryNodeId(3, 0)), table[4][0]);
            Assert.Equal(new TemporaryNode(TemporaryNodeId.Top, TemporaryNodeId.Top), table[1][0]);
        }

        /// <summary>水準ごとの幅は、その水準に現れる状態の種類数と一致する。</summary>
        /// <remarks>
        /// 「ちょうど 2 個選ぶ」を 4 変数で展開すると、状態（選んだ個数）は
        /// 水準 4 で {0}、水準 3 と 2 で {0, 1}、水準 1 で {1} の 6 種類しか現れない。
        /// 合流していなければ枝の数だけ（最大 2^3）ノードができるので、ここで差が出る。
        /// </remarks>
        [Fact]
        public void EachLevelIsAsWideAsTheStatesItHolds()
        {
            TemporaryNodeTable table = Expand(new ExactlyKSpec(4, 2));

            Assert.Equal(new[] { 1, 2, 2, 1 }, new[] { table.Width(4), table.Width(3), table.Width(2), table.Width(1) });
            Assert.Equal(6, table.NodeCount);
        }

        /// <summary>同じ状態に至った 2 本の枝は、1 つの一時ノードに合流する。</summary>
        /// <remarks>
        /// 水準 3 では「0 個選んだ状態の 1-枝」と「1 個選んだ状態の 0-枝」がどちらも
        /// 「1 個選んだ状態」に行き着く。この 2 本が同じノードを指していなければ、重複除去が効いていない。
        /// </remarks>
        [Fact]
        public void BranchesThatReachTheSameStateShareOneNode()
        {
            TemporaryNodeTable table = Expand(new ExactlyKSpec(4, 2));

            TemporaryNode fromZeroTaken = table[3][0];
            TemporaryNode fromOneTaken = table[3][1];

            Assert.Equal(fromZeroTaken.Hi, fromOneTaken.Lo);
            Assert.False(fromZeroTaken.Hi.IsTerminal);
            Assert.Equal(2, table.Width(2));
        }

        /// <summary><c>GetChild</c> が ⊥ / ⊤ を返した枝は、そのまま終端に着く。</summary>
        [Fact]
        public void TerminalResultsLandOnTheTerminals()
        {
            TemporaryNodeTable table = Expand(new ExactlyKSpec(4, 2));

            // 最後の水準に残る状態は「1 個選んだ」だけ。入れなければ届かず（⊥）、入れれば揃う（⊤）。
            Assert.Equal(new TemporaryNode(TemporaryNodeId.Bottom, TemporaryNodeId.Top), table[1][0]);

            // 「2 個目を入れて揃った」枝も ⊤ に着く。
            Assert.Equal(TemporaryNodeId.Top, table[2][1].Hi);
        }

        /// <summary>飛ばされた水準にはノードが 1 つも置かれず、枝は飛び先の水準を直接指す。</summary>
        [Fact]
        public void SkippedLevelsStayEmpty()
        {
            TemporaryNodeTable table = Expand(new SkipEveryOtherLevelSpec(5));

            Assert.Equal(new[] { 1, 0, 1, 0, 1 }, new[] { table.Width(5), table.Width(4), table.Width(3), table.Width(2), table.Width(1) });
            Assert.Equal(3, table.NodeCount);
            Assert.Equal(new TemporaryNodeId(3, 0), table[5][0].Lo);
            Assert.Equal(new TemporaryNodeId(3, 0), table[5][0].Hi);
        }

        /// <summary>
        /// できあがった表が受理する集合が、スペックを素直にたどった結果と<b>集合として</b>一致する。
        /// </summary>
        /// <remarks>
        /// 幅やノード数だけを見ていると、枝の付け替えを間違えても数が合ってしまう。
        /// <see cref="SpecWalker"/> は状態を共有せず全ての枝をたどるだけの実装なので、
        /// 展開の答え合わせに使える（docs/PLAN.md §11-1 の総当たり照合にあたる）。
        /// </remarks>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        [InlineData(6)]
        public void TheExpandedTableAcceptsExactlyWhatTheSpecAccepts(int k)
        {
            const int ItemCount = 6;
            ExactlyKSpec spec = new ExactlyKSpec(ItemCount, k);

            AssertSameSets($"ExactlyK({ItemCount}, {k})", Expand(spec), SpecWalker.Accepted<ExactlyKSpec, int>(spec, ItemCount), ItemCount);
        }

        /// <summary>水準を飛ばすスペックと、全部自由なスペックでも同じ照合が通る。</summary>
        [Fact]
        public void TheExpandedTableAcceptsExactlyWhatTheSpecAcceptsWhenLevelsAreSkipped()
        {
            const int ItemCount = 6;
            SkipEveryOtherLevelSpec skipping = new SkipEveryOtherLevelSpec(ItemCount);
            FreeChoiceSpec free = new FreeChoiceSpec(ItemCount);

            AssertSameSets(
                "SkipEveryOtherLevel",
                Expand(skipping),
                SpecWalker.Accepted<SkipEveryOtherLevelSpec, int>(skipping, ItemCount),
                ItemCount);

            AssertSameSets(
                "FreeChoice",
                Expand(free),
                SpecWalker.Accepted<FreeChoiceSpec, int>(free, ItemCount),
                ItemCount);
        }

        /// <summary>根が終端なら、水準は 1 つも作られない。</summary>
        [Theory]
        [InlineData(DdResult.True)]
        [InlineData(DdResult.False)]
        public void ATerminalRootExpandsToNothing(int rootResult)
        {
            TemporaryNodeTable table = Expand(new FixedRootSpec(rootResult));

            Assert.Equal(0, table.RootLevel);
            Assert.Equal(0, table.NodeCount);
            Assert.True(table.Root.IsTerminal);
            Assert.Equal(rootResult == DdResult.True, table.Root.IsTop);
        }

        /// <summary>規約外の水準を根が返したら、何が返ってきたのか分かる例外で止まる。</summary>
        [Fact]
        public void ARootLevelThatIsNeitherALevelNorATerminalIsRejected()
        {
            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() => Expand(new FixedRootSpec(-7)));

            Assert.Contains("-7", error.Message, StringComparison.Ordinal);
            Assert.Contains("GetRoot", error.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// <see cref="BuildOptions.MaxNodeCount"/> を超えたら、原因の分かる例外で止まる。
        /// </summary>
        /// <remarks>
        /// フロンティア幅の爆発は「数千辺想定なので現実的なリスク」（docs/PLAN.md §13）で、
        /// 対策は<b>メモリを使い切って落ちるのではなく上限で止める</b>こと。
        /// 止まったあと何をすればよいか分かるように、超えた上限の名前と値がメッセージに要る。
        /// </remarks>
        [Fact]
        public void PassingMaxNodeCountStopsTheBuildWithAReadableError()
        {
            BuildOptions options = new BuildOptions { MaxNodeCount = 100 };

            BuildLimitExceededException error = Assert.Throws<BuildLimitExceededException>(
                () => Expand(new DistinctStateSpec(20), options));

            Assert.Equal(BuildLimit.NodeCount, error.Limit);
            Assert.Equal(100, error.LimitValue);
            Assert.InRange(error.Level, 1, 20);
            Assert.Contains("MaxNodeCount", error.Message, StringComparison.Ordinal);
            Assert.Contains("100", error.Message, StringComparison.Ordinal);
        }

        /// <summary><see cref="BuildOptions.MaxFrontierSize"/> を超えたら、幅が広がった水準とともに止まる。</summary>
        [Fact]
        public void PassingMaxFrontierSizeStopsTheBuildWithAReadableError()
        {
            BuildOptions options = new BuildOptions { MaxFrontierSize = 8 };

            BuildLimitExceededException error = Assert.Throws<BuildLimitExceededException>(
                () => Expand(new DistinctStateSpec(20), options));

            Assert.Equal(BuildLimit.FrontierSize, error.Limit);
            Assert.Equal(8, error.LimitValue);

            // 幅は水準を 1 つ降りるごとに倍になるので、9 種類目が現れるのは水準 20-4 = 16 を埋めているとき。
            Assert.Equal(16, error.Level);
            Assert.Contains("MaxFrontierSize", error.Message, StringComparison.Ordinal);
            Assert.Contains("9 distinct state(s)", error.Message, StringComparison.Ordinal);
        }

        /// <summary>上限ちょうどまでは通る（境界で 1 つ手前に止めていない）。</summary>
        [Fact]
        public void TheLimitsAllowExactlyTheirOwnValue()
        {
            BuildOptions options = new BuildOptions { MaxNodeCount = 4, MaxFrontierSize = 1 };

            TemporaryNodeTable table = Expand(new FreeChoiceSpec(4), options);

            Assert.Equal(4, table.NodeCount);
        }

        /// <summary>展開の途中でキャンセルされたら、そこで止まる。</summary>
        [Fact]
        public void CancellationStopsTheBuild()
        {
            using CancellationTokenSource source = new CancellationTokenSource();
            BuildOptions options = new BuildOptions { CancellationToken = source.Token };
            CancellingSpec spec = new CancellingSpec(source, 10, 6);

            Assert.ThrowsAny<OperationCanceledException>(
                () => TopDownExpander<CancellingSpec, int>.Expand(spec, options));
        }

        /// <summary>最初から取り消されているトークンなら、1 水準も展開せずに止まる。</summary>
        [Fact]
        public void AnAlreadyCancelledTokenStopsTheBuildBeforeItStarts()
        {
            using CancellationTokenSource source = new CancellationTokenSource();
            source.Cancel();

            BuildOptions options = new BuildOptions { CancellationToken = source.Token };

            Assert.ThrowsAny<OperationCanceledException>(() => Expand(new FreeChoiceSpec(10), options));
        }

        /// <summary>進捗は、根の水準から 1 まで、展開が終わった水準ごとに 1 回ずつ届く。</summary>
        [Fact]
        public void ProgressArrivesOncePerLevel()
        {
            List<BuildProgress> reports = new List<BuildProgress>();
            BuildOptions options = new BuildOptions { Progress = new RecordingProgress(reports) };

            TemporaryNodeTable table = Expand(new FreeChoiceSpec(4), options);

            Assert.Equal(new[] { 4, 3, 2, 1 }, reports.ConvertAll(report => report.Level));
            Assert.All(reports, report => Assert.Equal(4, report.RootLevel));
            Assert.All(reports, report => Assert.Equal(1, report.FrontierSize));
            Assert.Equal(table.NodeCount, reports[^1].NodeCount);
        }

        /// <summary>
        /// 変数 10 万でも <c>StackOverflowException</c> にならない（展開が反復であることの回帰テスト）。
        /// </summary>
        /// <remarks>
        /// 展開の深さは変数の個数そのものなので、素直な再帰で書くとここで<b>プロセスごと落ちる</b>
        /// （docs/PLAN.md §4.5・§13）。幅 1 のお題を選んであるので、深さだけが試される。
        /// </remarks>
        [Fact]
        public void ADeepExpansionDoesNotOverflowTheStack()
        {
            const int ItemCount = 100_000;

            TemporaryNodeTable table = Expand(new FreeChoiceSpec(ItemCount));

            Assert.Equal(ItemCount, table.RootLevel);
            Assert.Equal(ItemCount, table.NodeCount);
            Assert.Equal(new TemporaryNodeId(1, 0), table[2][0].Lo);
        }

        private static TemporaryNodeTable Expand<TSpec>(TSpec spec, BuildOptions? options = null)
            where TSpec : struct, IDdSpec<int> =>
            TopDownExpander<TSpec, int>.Expand(spec, options);

        /// <summary>一時ノード表が表す族が、スペックを素直にたどった族と一致することを確かめる。</summary>
        private static void AssertSameSets(string context, TemporaryNodeTable table, List<int[]> expected, int itemCount)
        {
            List<int[]> produced = TemporaryTableSets.Accepted(table);

            // 先に個数を見る。集合として一致していても、同じ集合を 2 度受理していれば経路が重複している。
            Assert.Equal(expected.Count, produced.Count);

            FamilyAssert.AssertSameFamily(
                context,
                BruteForceFamily.FromSets(itemCount, produced.ToArray()),
                BruteForceFamily.FromSets(itemCount, expected.ToArray()));
        }

        private sealed class RecordingProgress : IProgress<BuildProgress>
        {
            private readonly List<BuildProgress> _reports;

            public RecordingProgress(List<BuildProgress> reports)
            {
                _reports = reports;
            }

            public void Report(BuildProgress value) => _reports.Add(value);
        }
    }
}
