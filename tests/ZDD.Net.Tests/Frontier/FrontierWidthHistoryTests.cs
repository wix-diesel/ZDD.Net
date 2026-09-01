using System;
using System.Collections.Generic;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;

namespace ZDD.Net.Tests.Frontier
{
    /// <summary>
    /// M2-11 completion criteria: a caller collecting <see cref="BuildOptions.Progress"/> into a list gets,
    /// after the build, the frontier-width history the issue asks for, and that history is consistent with
    /// <see cref="FrontierManager.FrontierSize"/> — the estimate a caller can get *before* running a build at all.
    /// </summary>
    /// <remarks>
    /// <see cref="FrontierManager.FrontierSize"/> counts frontier <i>vertices</i> (the state array's length);
    /// <see cref="BuildProgress.FrontierSize"/> counts distinct <i>states</i> (the DD's actual width). For a
    /// spec that holds one binary flag per frontier vertex (<see cref="MatchingSpec"/>), the two relate by
    /// <c>states &lt;= 2 ^ vertices</c>: that is the "予測値と整合する" check, and it is what tells a caller
    /// whether an observed build is anywhere near the width its graph's edge order predicted.
    /// </remarks>
    public class FrontierWidthHistoryTests
    {
        [Fact]
        public void ObservedWidthHistoryNeverExceedsTheGraphsFrontierSizeBound()
        {
            Graph grid = Graph.Grid(4, 4);
            FrontierManager frontierManager = new FrontierManager(grid);

            List<BuildProgress> history = new List<BuildProgress>();
            BuildOptions options = new BuildOptions { Progress = new RecordingProgress(history) };

            using ZddManager manager = new ZddManager(grid.EdgeCount);
            FrontierBuilder.Build<MatchingSpec>(manager, new MatchingSpec(grid), options);

            // The reports arrive root-level-first, one per level, so the history is already the
            // per-level width curve the issue asks to make available after a build finishes.
            Assert.Equal(grid.EdgeCount, history.Count);

            int observedPeak = 0;
            foreach (BuildProgress report in history)
            {
                int edgeIndex = grid.LevelToEdgeIndex(report.Level);
                int predictedVertexCount = frontierManager.FrontierSize(edgeIndex);

                Assert.True(
                    report.FrontierSize <= (1 << predictedVertexCount),
                    $"Level {report.Level}: observed width {report.FrontierSize} exceeds the " +
                    $"2^{predictedVertexCount} bound FrontierManager.FrontierSize predicts for edge {edgeIndex}.");

                observedPeak = Math.Max(observedPeak, report.FrontierSize);
            }

            Assert.True(observedPeak <= (1 << frontierManager.MaxFrontierSize));
        }

        private sealed class RecordingProgress : IProgress<BuildProgress>
        {
            private readonly List<BuildProgress> _history;

            public RecordingProgress(List<BuildProgress> history)
            {
                _history = history;
            }

            public void Report(BuildProgress value) => _history.Add(value);
        }
    }
}
