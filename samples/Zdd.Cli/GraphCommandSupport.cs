using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Io;

namespace ZDD.Net.Samples.Cli
{
    /// <summary>
    /// The options shared by every graph subcommand (<c>grid-path</c> / <c>spanning-tree</c> /
    /// <c>partition</c> / <c>matching</c>): edge order, progress, DOT output, the binary save/load
    /// round trip (M5-1), sampling, and minimum-weight member.
    /// </summary>
    internal sealed class GraphRunOptions
    {
        public EdgeOrderStrategy EdgeOrder { get; set; } = EdgeOrderStrategy.AsGiven;

        public bool ShowProgress { get; set; }

        public string? DotPath { get; set; }

        public string? SavePath { get; set; }

        public string? LoadPath { get; set; }

        public int? SampleCount { get; set; }

        public string? MinWeightPath { get; set; }

        public bool Estimate { get; set; }

        public bool ShowStatistics { get; set; }

        public bool ShowHelp { get; set; }
    }

    /// <summary>
    /// Argument parsing and the build/report/dot/save/sample/min-weight pipeline shared by every graph
    /// subcommand. Each subcommand only supplies its own positional arguments, its own extra flags
    /// (e.g. <c>--perfect</c>), the <see cref="Graph"/> to search, and the spec to build it with.
    /// </summary>
    internal static class GraphCommandSupport
    {
        /// <summary>The <c>--help</c> text block common to every graph subcommand, appended after the command-specific text.</summary>
        public const string CommonOptionsHelp =
            "  --edge-order <as-given|bfs|dfs|grid|beam>\n" +
            "                   reorder edges before building, to keep the frontier narrow (default: as-given)\n" +
            "  --progress       print each level's frontier size to stderr while building\n" +
            "  --dot <path>     write Graphviz DOT of the built family to <path>, or to stdout when <path> is '-'\n" +
            "  --estimate       print the peak frontier size the build would reach, without building, and exit\n" +
            "  --save <path>    after building, write the family to <path> in ZDD.Net's binary format (M5-1)\n" +
            "  --load <path>    skip building; read the family from <path> instead\n" +
            "  --sample <n>     print <n> random members of the family\n" +
            "  --min-weight <path>\n" +
            "                   print the minimum-weight member; <path> has one integer weight per line,\n" +
            "                   in the edge order used for the build (i.e. after --edge-order)\n" +
            "  --stats          print the manager's table statistics\n" +
            "  --help           print this text\n";

        /// <summary>
        /// Splits a subcommand's arguments into its positional arguments (everything before the first
        /// token that looks like a flag) and the flags that follow.
        /// </summary>
        public static (string[] Positional, string[] Flags) SplitPositional(string[] args)
        {
            int flagStart = 0;
            while (flagStart < args.Length
                && args[flagStart] != "-h"
                && !args[flagStart].StartsWith("--", StringComparison.Ordinal))
            {
                flagStart++;
            }

            return (args[..flagStart], args[flagStart..]);
        }

        /// <summary>
        /// Tries to parse <c>flags[index]</c> as one of the flags common to every graph subcommand.
        /// </summary>
        /// <param name="matched">
        /// Whether <c>flags[index]</c> named a common flag at all — <see langword="false"/> means the
        /// caller should try its own subcommand-specific flags next, not that parsing failed.
        /// </param>
        /// <returns><see langword="false"/>, with <paramref name="error"/> set, only when the flag was recognized but malformed.</returns>
        public static bool TryParseCommonFlag(
            string[] flags,
            ref int index,
            GraphRunOptions options,
            out bool matched,
            out string? error)
        {
            matched = true;
            string flag = flags[index];

            switch (flag)
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    error = null;
                    return true;

                case "--progress":
                    options.ShowProgress = true;
                    error = null;
                    return true;

                case "--estimate":
                    options.Estimate = true;
                    error = null;
                    return true;

                case "--stats":
                    options.ShowStatistics = true;
                    error = null;
                    return true;

                case "--edge-order":
                    if (!CliOutput.TryTakeValue(flags, ref index, flag, out string? orderText, out error))
                    {
                        return false;
                    }

                    if (!TryParseEdgeOrder(orderText, out EdgeOrderStrategy strategy))
                    {
                        error = $"unknown --edge-order '{orderText}'; expected as-given, bfs, dfs, grid or beam.";
                        return false;
                    }

                    options.EdgeOrder = strategy;
                    return true;

                case "--dot":
                    if (!CliOutput.TryTakeValue(flags, ref index, flag, out string? dotPath, out error))
                    {
                        return false;
                    }

                    options.DotPath = dotPath;
                    return true;

                case "--save":
                    if (!CliOutput.TryTakeValue(flags, ref index, flag, out string? savePath, out error))
                    {
                        return false;
                    }

                    options.SavePath = savePath;
                    return true;

                case "--load":
                    if (!CliOutput.TryTakeValue(flags, ref index, flag, out string? loadPath, out error))
                    {
                        return false;
                    }

                    options.LoadPath = loadPath;
                    return true;

                case "--min-weight":
                    if (!CliOutput.TryTakeValue(flags, ref index, flag, out string? minWeightPath, out error))
                    {
                        return false;
                    }

                    options.MinWeightPath = minWeightPath;
                    return true;

                case "--sample":
                    if (!CliOutput.TryTakeValue(flags, ref index, flag, out string? sampleText, out error))
                    {
                        return false;
                    }

                    if (!int.TryParse(sampleText, NumberStyles.None, CultureInfo.InvariantCulture, out int sampleCount))
                    {
                        error = $"--sample must be a non-negative integer, but was '{sampleText}'.";
                        return false;
                    }

                    options.SampleCount = sampleCount;
                    return true;

                default:
                    matched = false;
                    error = null;
                    return true;
            }
        }

        private static bool TryParseEdgeOrder(string text, out EdgeOrderStrategy strategy)
        {
            switch (text)
            {
                case "as-given":
                    strategy = EdgeOrderStrategy.AsGiven;
                    return true;
                case "bfs":
                    strategy = EdgeOrderStrategy.Bfs;
                    return true;
                case "dfs":
                    strategy = EdgeOrderStrategy.Dfs;
                    return true;
                case "grid":
                    strategy = EdgeOrderStrategy.Grid;
                    return true;
                case "beam":
                    strategy = EdgeOrderStrategy.BeamSearchPathWidth;
                    return true;
                default:
                    strategy = EdgeOrderStrategy.AsGiven;
                    return false;
            }
        }

        /// <summary>The inverse of <see cref="TryParseEdgeOrder"/>: the CLI token for a strategy, so reported output uses the same vocabulary as <c>--edge-order</c> takes.</summary>
        private static string EdgeOrderToken(EdgeOrderStrategy strategy)
        {
            switch (strategy)
            {
                case EdgeOrderStrategy.Bfs:
                    return "bfs";
                case EdgeOrderStrategy.Dfs:
                    return "dfs";
                case EdgeOrderStrategy.Grid:
                    return "grid";
                case EdgeOrderStrategy.BeamSearchPathWidth:
                    return "beam";
                default:
                    return "as-given";
            }
        }

        /// <summary>
        /// Runs the shared pipeline: reorder edges (if asked), build (or load) the family, report it, and
        /// act on <c>--dot</c>/<c>--save</c>/<c>--sample</c>/<c>--min-weight</c>/<c>--stats</c>.
        /// </summary>
        /// <typeparam name="TSpec">The frontier spec type; every graph subcommand's spec is array-state (<see cref="IArrayDdSpec"/>).</typeparam>
        /// <param name="graph">The graph to search, before any <c>--edge-order</c> reordering.</param>
        /// <param name="createSpec">Builds the spec against the (possibly reordered) graph actually used.</param>
        /// <param name="extraFields">Command-specific report lines (e.g. grid-path's <c>s</c>/<c>t</c>), printed before the generic graph/family fields.</param>
        public static int Run<TSpec>(
            Graph graph,
            Func<Graph, TSpec> createSpec,
            GraphRunOptions options,
            IEnumerable<(string Name, string Value)> extraFields)
            where TSpec : struct, IArrayDdSpec
        {
            if (options.Estimate)
            {
                int estimate = options.EdgeOrder == EdgeOrderStrategy.AsGiven
                    ? graph.EstimateMaxFrontierSize()
                    : graph.EstimateMaxFrontierSize(options.EdgeOrder);

                Console.Out.WriteLine($"estimated peak frontier size: {estimate.ToString(CultureInfo.InvariantCulture)}");
                return Program.ExitSuccess;
            }

            Graph built = options.EdgeOrder == EdgeOrderStrategy.AsGiven ? graph : graph.Optimize(options.EdgeOrder);

            ZddManager manager;
            Zdd family;

            if (options.LoadPath is not null)
            {
                using FileStream loadStream = File.OpenRead(options.LoadPath);
                family = ZddBinaryFormat.Read(loadStream);
                manager = family.Manager;

                if (manager.VariableCount != built.EdgeCount)
                {
                    int loadedVariableCount = manager.VariableCount;
                    manager.Dispose();
                    throw new InvalidOperationException(
                        $"'{options.LoadPath}' was saved with {loadedVariableCount} variable(s), but this graph "
                        + $"(after --edge-order) has {built.EdgeCount} edge(s); --load only makes sense for a file "
                        + "saved from the same graph and edge order used here.");
                }
            }
            else
            {
                manager = new ZddManager(built.EdgeCount);

                BuildOptions buildOptions = new BuildOptions();
                if (options.ShowProgress)
                {
                    buildOptions.Progress = new ConsoleProgress();
                }

                family = FrontierBuilder.Build(manager, createSpec(built), buildOptions);
            }

            using (manager)
            {
                Report(built, family, options, extraFields);

                if (options.DotPath is not null)
                {
                    WriteDot(built, family, options.DotPath);
                }

                if (options.SavePath is not null)
                {
                    using FileStream saveStream = File.Create(options.SavePath);
                    ZddBinaryFormat.Write(family, saveStream);
                }

                if (options.SampleCount is int sampleCount)
                {
                    PrintSample(built, family, sampleCount);
                }

                if (options.MinWeightPath is not null)
                {
                    PrintMinWeight(built, family, options.MinWeightPath);
                }

                if (options.ShowStatistics)
                {
                    Console.Out.WriteLine();
                    Console.Out.WriteLine(manager.GetStatistics().ToString());
                }
            }

            return Program.ExitSuccess;
        }

        private static void Report(
            Graph graph, in Zdd family, GraphRunOptions options, IEnumerable<(string Name, string Value)> extraFields)
        {
            TextWriter output = Console.Out;

            foreach ((string name, string value) in extraFields)
            {
                CliOutput.WriteField(output, name, value);
            }

            CliOutput.WriteField(output, "vertices", graph.VertexCount.ToString(CultureInfo.InvariantCulture));
            CliOutput.WriteField(output, "edges", graph.EdgeCount.ToString(CultureInfo.InvariantCulture));
            CliOutput.WriteField(output, "edge-order", EdgeOrderToken(options.EdgeOrder));
            CliOutput.WriteField(output, "sets", family.Count.ToString(CultureInfo.InvariantCulture));
            CliOutput.WriteField(output, "nodes", family.NodeCount.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Writes DOT of the built family, labeling each level with the edge it decides (as
        /// <c>(u,v)</c>) rather than the bare variable index — the same DOT-label extension
        /// M5-4 added (<see cref="DotOptions.LevelLabel"/>).
        /// </summary>
        private static void WriteDot(Graph graph, in Zdd family, string path)
        {
            DotOptions dotOptions = new DotOptions
            {
                LevelLabel = variableIndex => FormatEdge(graph, graph.VariableIndexToEdgeIndex(variableIndex)),
            };

            if (path == "-")
            {
                Console.Out.WriteLine();
                family.WriteDot(Console.Out, dotOptions);
                return;
            }

            using StreamWriter file = new StreamWriter(path);
            family.WriteDot(file, dotOptions);
        }

        private static void PrintSample(Graph graph, in Zdd family, int count)
        {
            int[][] samples = family.Sample(count, new Random());

            Console.Out.WriteLine();
            Console.Out.WriteLine($"sample ({count.ToString(CultureInfo.InvariantCulture)}):");
            foreach (int[] set in samples)
            {
                Console.Out.Write("  ");
                Console.Out.WriteLine(FormatEdgeSet(graph, set));
            }
        }

        private static void PrintMinWeight(Graph graph, in Zdd family, string weightsPath)
        {
            int[] weights = ReadWeights(weightsPath, graph.EdgeCount);
            WeightedSet<int> result = family.MinWeight(weights);

            Console.Out.WriteLine();
            Console.Out.Write("min-weight: ");
            Console.Out.Write(FormatEdgeSet(graph, result.Items));
            Console.Out.Write(" (weight ");
            Console.Out.Write(result.Weight.ToString(CultureInfo.InvariantCulture));
            Console.Out.WriteLine(')');
        }

        private static int[] ReadWeights(string path, int expectedCount)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"weights file '{path}' was not found.", path);
            }

            List<int> weights = new List<int>();
            foreach (string rawLine in File.ReadLines(path))
            {
                string trimmed = rawLine.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#')
                {
                    continue;
                }

                if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int weight))
                {
                    throw new FormatException($"'{path}': expected one integer per line, but found '{trimmed}'.");
                }

                weights.Add(weight);
            }

            if (weights.Count != expectedCount)
            {
                throw new FormatException(
                    $"'{path}': expected {expectedCount.ToString(CultureInfo.InvariantCulture)} weight(s) " +
                    $"(one per edge), but found {weights.Count.ToString(CultureInfo.InvariantCulture)}.");
            }

            return weights.ToArray();
        }

        private static string FormatEdgeSet(Graph graph, int[] edgeIndices)
        {
            if (edgeIndices.Length == 0)
            {
                return "∅";
            }

            string[] parts = new string[edgeIndices.Length];
            for (int i = 0; i < edgeIndices.Length; i++)
            {
                parts[i] = FormatEdge(graph, edgeIndices[i]);
            }

            return "{" + string.Join(", ", parts) + "}";
        }

        private static string FormatEdge(Graph graph, int edgeIndex)
        {
            Edge edge = graph.GetEdge(edgeIndex);
            return $"({edge.U.ToString(CultureInfo.InvariantCulture)},{edge.V.ToString(CultureInfo.InvariantCulture)})";
        }

        /// <summary>
        /// Reports build progress to stderr, one line per level. A plain <see cref="IProgress{T}"/>
        /// implementation rather than <see cref="Progress{T}"/>: the latter posts through the captured
        /// <see cref="System.Threading.SynchronizationContext"/> (the thread pool, with none captured, as
        /// here), which would print out of order; this reports synchronously on the building thread, as
        /// <see cref="BuildOptions.Progress"/> documents it will be called.
        /// </summary>
        private sealed class ConsoleProgress : IProgress<BuildProgress>
        {
            public void Report(BuildProgress value)
            {
                Console.Error.WriteLine(
                    $"level {value.Level.ToString(CultureInfo.InvariantCulture),6} / {value.RootLevel.ToString(CultureInfo.InvariantCulture)}"
                    + $"  frontier={value.FrontierSize.ToString(CultureInfo.InvariantCulture)}"
                    + $"  nodes={value.NodeCount.ToString(CultureInfo.InvariantCulture)}");
            }
        }
    }
}
