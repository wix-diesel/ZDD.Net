using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Specs;

namespace ZDD.Net.Tests.Frontier
{
    /// <summary>
    /// レベル内展開の並列化（M4-3、issue #46）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// このお題の要点は「速いこと」ではなく<b>決定的なこと</b>: 並列度をいくつにしても、
    /// できあがる一時ノード表——ひいては最終的な ZDD のノード ID——が逐次実行と完全に一致しなければ
    /// ならない。ここでは <see cref="WideFrontierSpec"/>（水準を下るごとに幅が倍になり、途中で頭打ちに
    /// なるだけの単純なスペック）で実際にパーティション分割・結合が動くだけの幅を作り、
    /// <see cref="BuildOptions.MaxDegreeOfParallelism"/> を 1・2・4・既定値と変えて構築した表を
    /// 1 つ 1 つ突き合わせる。
    /// </para>
    /// <para>
    /// 幅とパーティション閾値の関係は <c>TopDownExpander&lt;TSpec, TState&gt;.MinPartitionWidth</c>
    /// （既定 2048）に依存するため、ここでは <c>ZDD_FORCE_PARALLEL_FRONTIER</c> を使わず、
    /// 幅そのものを 2048 の数倍まで育てて実際に並列パスを起動させている——CI 専用の
    /// 環境変数（.github/workflows/ci.yml の <c>build-test-parallel-frontier</c> ジョブ）は、
    /// この幅を用意しなくても既存の全テストで同じコード経路を通すためのもので、ここでの
    /// 決定性の主張はそれとは独立に成り立つ。
    /// </para>
    /// </remarks>
    public class ParallelFrontierTests
    {
        /// <summary>
        /// 幅がパーティション閾値の 4 倍に達するだけの規模。4 パーティション（既定の論理コア数程度）
        /// までは確実にパーティション分割が起きる大きさにしてある。
        /// </summary>
        private const int WideItemCount = 20;
        private const int WideWidth = 8200;

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        public void StructStateBuildsAreDeterministicAcrossDegreesOfParallelism(int degreeOfParallelism)
        {
            TemporaryNodeTable sequential = Expand(new WideFrontierSpec(WideItemCount, WideWidth), new BuildOptions { MaxDegreeOfParallelism = 1 });

            for (int attempt = 0; attempt < 3; attempt++)
            {
                TemporaryNodeTable parallel = Expand(
                    new WideFrontierSpec(WideItemCount, WideWidth),
                    new BuildOptions { MaxDegreeOfParallelism = degreeOfParallelism });

                AssertStructurallyIdentical(sequential, parallel);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        public void ArrayStateBuildsAreDeterministicAcrossDegreesOfParallelism(int degreeOfParallelism)
        {
            TemporaryNodeTable sequential = ExpandArray(
                new WideFrontierArraySpec(WideItemCount, WideWidth), new BuildOptions { MaxDegreeOfParallelism = 1 });

            for (int attempt = 0; attempt < 3; attempt++)
            {
                TemporaryNodeTable parallel = ExpandArray(
                    new WideFrontierArraySpec(WideItemCount, WideWidth),
                    new BuildOptions { MaxDegreeOfParallelism = degreeOfParallelism });

                AssertStructurallyIdentical(sequential, parallel);
            }
        }

        /// <summary>
        /// <see cref="FrontierBuilder.Build"/> 経由でも同じ決定性が保たれる（一時ノード表だけでなく、
        /// マネージャに登録された最終的なノード数・集合数まで一致する）ことの回帰。
        /// </summary>
        [Fact]
        public void FrontierBuilderProducesTheSameZddRegardlessOfDegreeOfParallelism()
        {
            using ZddManager sequentialManager = new ZddManager(WideItemCount);
            Zdd sequential = FrontierBuilder.Build<WideFrontierSpec, int>(
                sequentialManager, new WideFrontierSpec(WideItemCount, WideWidth), new BuildOptions { MaxDegreeOfParallelism = 1 });

            using ZddManager parallelManager = new ZddManager(WideItemCount);
            Zdd parallel = FrontierBuilder.Build<WideFrontierSpec, int>(
                parallelManager, new WideFrontierSpec(WideItemCount, WideWidth), new BuildOptions { MaxDegreeOfParallelism = 4 });

            Assert.Equal(sequentialManager.NodeCount, parallelManager.NodeCount);
            Assert.Equal(sequential.Count, parallel.Count);
            Assert.Equal(sequential.ToDot(), parallel.ToDot());
        }

        /// <summary>
        /// 状態記録（<see cref="BuildOptions.RecordStates"/>、M5-4、issue #56）はマージスレッド上の
        /// <c>AddState</c> だけを通るので、並列度をいくつにしても記録されるラベルの集合は変わらない。
        /// </summary>
        [Fact]
        public void RecordedStateLabelsAreTheSameRegardlessOfDegreeOfParallelism()
        {
            using ZddManager sequentialManager = new ZddManager(WideItemCount);
            Zdd sequential = FrontierBuilder.Build<WideFrontierSpec, int>(
                sequentialManager,
                new WideFrontierSpec(WideItemCount, WideWidth),
                new BuildOptions { MaxDegreeOfParallelism = 1, RecordStates = true },
                out IReadOnlyDictionary<int, string> sequentialLabels);

            using ZddManager parallelManager = new ZddManager(WideItemCount);
            Zdd parallel = FrontierBuilder.Build<WideFrontierSpec, int>(
                parallelManager,
                new WideFrontierSpec(WideItemCount, WideWidth),
                new BuildOptions { MaxDegreeOfParallelism = 4, RecordStates = true },
                out IReadOnlyDictionary<int, string> parallelLabels);

            // 両方とも同じマネージャ内なので、ノード ID とラベルの対応がそのまま一致するはず。
            Assert.Equal(sequentialLabels.Count, parallelLabels.Count);
            foreach (KeyValuePair<int, string> entry in sequentialLabels)
            {
                Assert.Equal(entry.Value, parallelLabels[entry.Key]);
            }
        }

        /// <summary>
        /// 幅がパーティション閾値に届かない水準しか無いスペックでは、並列度をいくつに設定しても
        /// 逐次実行と完全に同じ結果になる(=常に逐次パスへフォールバックしている)。
        /// </summary>
        [Fact]
        public void NarrowLevelsFallBackToSequentialRegardlessOfDegreeOfParallelism()
        {
            TemporaryNodeTable sequential = Expand(new ExactlyKSpec(200, 90), new BuildOptions { MaxDegreeOfParallelism = 1 });
            TemporaryNodeTable parallel = Expand(new ExactlyKSpec(200, 90), new BuildOptions { MaxDegreeOfParallelism = 4 });

            AssertStructurallyIdentical(sequential, parallel);
        }

        /// <summary><see cref="BuildOptions.MaxDegreeOfParallelism"/> の既定値は論理コア数。</summary>
        [Fact]
        public void MaxDegreeOfParallelismDefaultsToProcessorCount()
        {
            Assert.Equal(Environment.ProcessorCount, new BuildOptions().MaxDegreeOfParallelism);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void MaxDegreeOfParallelismRejectsNonPositiveValues(int value)
        {
            BuildOptions options = new BuildOptions();

            Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxDegreeOfParallelism = value);
        }

        /// <summary>並列展開中のキャンセルは、逐次実行と同じように <see cref="OperationCanceledException"/> で止まる。</summary>
        [Fact]
        public void CancellationStopsAParallelBuild()
        {
            using CancellationTokenSource source = new CancellationTokenSource();
            BuildOptions options = new BuildOptions { MaxDegreeOfParallelism = 4, CancellationToken = source.Token };

            // 頭打ちになった、確実に並列パスを通る水準に届いた時点でキャンセルする。
            CancellingSpec spec = new CancellingSpec(source, WideItemCount, WideItemCount - 15);

            Assert.ThrowsAny<OperationCanceledException>(
                () => TopDownExpander<CancellingSpec, int>.Expand(spec, options));
        }

        /// <summary>
        /// 並列展開中に複数パーティションが同時に例外を投げたら、そのまま
        /// <see cref="AggregateException"/> として伝播する(1 個だけの特別扱いはしない)。
        /// </summary>
        [Fact]
        public void MultipleFailuresDuringAParallelRoundPropagateAsAnAggregateException()
        {
            // 幅が頭打ちになった水準（並列パスが動く）を必ず例外にするので、そのラウンドの
            // 全パーティションが同時に投げる。パーティション数は WideWidth (8200) / MinPartitionWidth
            // (2048) = 4 と決まるので、Barrier の参加者数もそれに合わせる。
            const int ExpectedPartitionCount = 4;
            using Barrier rendezvous = new Barrier(ExpectedPartitionCount);
            AlwaysThrowingWideSpec spec = new AlwaysThrowingWideSpec(WideItemCount, WideWidth, poisonLevel: WideItemCount - 15, rendezvous);
            BuildOptions options = new BuildOptions { MaxDegreeOfParallelism = ExpectedPartitionCount };

            AggregateException error = Assert.Throws<AggregateException>(
                () => TopDownExpander<AlwaysThrowingWideSpec, int>.Expand(spec, options));

            Assert.Equal(ExpectedPartitionCount, error.InnerExceptions.Count);
            Assert.All(error.InnerExceptions, inner => Assert.IsType<FrontierPoisonException>(inner));
        }

        /// <summary>
        /// 並列展開中にちょうど 1 回だけ例外が起きたら、<see cref="AggregateException"/> に包まれず、
        /// 逐次実行と同じ見た目(元の例外そのもの)で伝播する。
        /// </summary>
        [Fact]
        public void ASingleFailureDuringAParallelRoundUnwrapsToTheOriginalException()
        {
            SingleThrowCounter counter = new SingleThrowCounter();
            SingleThrowWideSpec spec = new SingleThrowWideSpec(WideItemCount, WideWidth, poisonLevel: WideItemCount - 15, counter);
            BuildOptions options = new BuildOptions { MaxDegreeOfParallelism = 4 };

            Assert.Throws<FrontierPoisonException>(
                () => TopDownExpander<SingleThrowWideSpec, int>.Expand(spec, options));
        }

        /// <summary>実際のグラフスペック（配列状態）でも並列度で結果が変わらないことの回帰。</summary>
        [Fact]
        public void GraphSpecBuildsAreDeterministicAcrossDegreesOfParallelism()
        {
            ZDD.Net.Graphs.Graph grid = ZDD.Net.Graphs.Graph.Grid(9, 9);

            using ZddManager sequentialManager = new ZddManager(grid.EdgeCount);
            Zdd sequential = FrontierBuilder.Build(
                sequentialManager, new PathSpec(grid, 0, grid.VertexCount - 1), new BuildOptions { MaxDegreeOfParallelism = 1 });

            using ZddManager parallelManager = new ZddManager(grid.EdgeCount);
            Zdd parallel = FrontierBuilder.Build(
                parallelManager, new PathSpec(grid, 0, grid.VertexCount - 1), new BuildOptions { MaxDegreeOfParallelism = 4 });

            Assert.Equal(sequential.Count, parallel.Count);
            Assert.Equal(sequentialManager.NodeCount, parallelManager.NodeCount);
            Assert.Equal(sequential.ToDot(), parallel.ToDot());
        }

        private static TemporaryNodeTable Expand<TSpec>(TSpec spec, BuildOptions options)
            where TSpec : struct, IDdSpec<int> =>
            TopDownExpander<TSpec, int>.Expand(spec, options);

        private static TemporaryNodeTable ExpandArray<TSpec>(TSpec spec, BuildOptions options)
            where TSpec : struct, IArrayDdSpec =>
            ArrayTopDownExpander<TSpec>.Expand(spec, options);

        /// <summary>2 つの一時ノード表が、水準・幅・全ノードの Lo/Hi まで完全に一致することを確かめる。</summary>
        /// <remarks>
        /// <see cref="TemporaryTableSets"/> による集合としての一致では、ノード ID が違っても同じ族なら
        /// 一致してしまう。ここで見たいのは「並列度をいくつにしても<b>同じノード ID</b> が付く」ことそのもの
        /// なので、構造そのものを 1 対 1 で突き合わせる。
        /// </remarks>
        private static void AssertStructurallyIdentical(TemporaryNodeTable expected, TemporaryNodeTable actual)
        {
            Assert.Equal(expected.RootLevel, actual.RootLevel);
            Assert.Equal(expected.Root, actual.Root);
            Assert.Equal(expected.NodeCount, actual.NodeCount);

            for (int level = 0; level <= expected.RootLevel; level++)
            {
                Assert.Equal(expected.Width(level), actual.Width(level));

                ReadOnlySpan<TemporaryNode> expectedNodes = expected[level];
                ReadOnlySpan<TemporaryNode> actualNodes = actual[level];

                for (int index = 0; index < expectedNodes.Length; index++)
                {
                    Assert.Equal(expectedNodes[index], actualNodes[index]);
                }
            }
        }
    }
}
