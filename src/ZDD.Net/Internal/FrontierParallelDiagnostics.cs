using System;

namespace ZDD.Net.Internal
{
    /// <summary>
    /// Test/CI knobs for the frontier builders' parallel path (M4-3, issue #46). Not part of the
    /// public API surface: production code tunes parallelism through <c>BuildOptions</c> instead.
    /// </summary>
    internal static class FrontierParallelDiagnostics
    {
        /// <summary>
        /// Escape hatch for tests and CI: with <c>ZDD_FORCE_PARALLEL_FRONTIER=1</c> in the
        /// environment, a level expansion goes through the parallel path whenever
        /// <c>BuildOptions.MaxDegreeOfParallelism</c> is above 1, ignoring the width a level would
        /// otherwise need before parallelism pays for itself. Most unit tests build specs far
        /// narrower than that width, so without this, the parallel merge logic would only ever run
        /// under the dedicated wide-frontier tests and the benchmarks — never under the existing
        /// M1&#8211;M3 regression suite. Read once: nothing in this process changes it afterwards.
        /// </summary>
        public static readonly bool ForceParallelForTesting =
            Environment.GetEnvironmentVariable("ZDD_FORCE_PARALLEL_FRONTIER") == "1";
    }
}
