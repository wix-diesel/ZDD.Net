using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using ZDD.Net.Core;
using ZDD.Net.Frontier;

namespace ZDD.Net.Benchmarks
{
    /// <summary>
    /// Times the 10 representative builds of docs/benchmarks.md. One <see cref="ZddManager"/> per
    /// invocation (not reused across iterations) so a run measures a build from a cold manager, which is
    /// what every acceptance-condition comparison in issue #31's follow-ups (M3-1 etc.) will also measure.
    /// </summary>
    /// <remarks>
    /// A short job (1 warmup, 3 measured iterations): the cases span milliseconds (SpanningTree_Complete8)
    /// to tens of seconds (Cardinality_5000...), and a baseline snapshot for M3+ comparisons does not need
    /// BenchmarkDotNet's full statistical rigor to already be useful — just to be reproducible.
    /// </remarks>
    [MemoryDiagnoser]
    [SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 1, iterationCount: 3)]
    public class FrontierBuildBenchmarks
    {
        [ParamsSource(nameof(CaseNames))]
        public string Case { get; set; } = string.Empty;

        public static IEnumerable<string> CaseNames()
        {
            foreach ((string name, _, _) in Cases.All)
            {
                yield return name;
            }
        }

        [Benchmark]
        public long Build()
        {
            (_, Func<ZddManager, BuildOptions?, Zdd> build, int variableCount) = Find(Case);
            using ZddManager manager = new ZddManager(variableCount);
            return build(manager, null).NodeCount;
        }

        private static (string Name, Func<ZddManager, BuildOptions?, Zdd> Build, int VariableCount) Find(string name)
        {
            foreach ((string caseName, Func<ZddManager, BuildOptions?, Zdd> build, int variableCount) in Cases.All)
            {
                if (caseName == name)
                {
                    return (caseName, build, variableCount);
                }
            }

            throw new InvalidOperationException($"Unknown benchmark case '{name}'.");
        }
    }
}
