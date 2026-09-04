using System;
using System.Globalization;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;

namespace ZDD.Net.Samples.Cli
{
    /// <summary>
    /// <c>zdd-cli partition &lt;graph-file&gt; &lt;k&gt;</c>: counts (and, with the shared options,
    /// inspects) the <c>k</c>-way balanced partitions of a graph loaded from a file, built with
    /// <see cref="GraphPartitionSpec"/>.
    /// </summary>
    internal static class PartitionCommand
    {
        private static string Usage =>
            "usage: zdd-cli partition <graph-file> <k> [options]\n" +
            "\n" +
            "  counts ways to split the graph in <graph-file> into exactly <k> connected\n" +
            "  blocks (an edge is 'kept' when its endpoints stay in the same block, 'cut'\n" +
            "  otherwise), each block's vertex count within [--min-block, --max-block].\n" +
            "\n" +
            "  --min-block <n>  minimum vertices per block (default: 1)\n" +
            "  --max-block <n>  maximum vertices per block (default: the graph's vertex count)\n" +
            "  --format <dimacs|edges|simple>\n" +
            "                   graph file format (default: guessed from the file extension,\n" +
            "                   falling back to ZDD.Net's own simple text format)\n" +
            GraphCommandSupport.CommonOptionsHelp +
            "\n" +
            "example: zdd-cli partition graph.dimacs 3 --min-block 2 --stats\n";

        public static int Run(string[] args)
        {
            if (Array.IndexOf(args, "-h") >= 0 || Array.IndexOf(args, "--help") >= 0)
            {
                Console.Out.Write(Usage);
                return Program.ExitSuccess;
            }

            (string[] positional, string[] flags) = GraphCommandSupport.SplitPositional(args);

            if (positional.Length != 2)
            {
                return Fail($"partition needs exactly 2 arguments (graph-file, k), but got {positional.Length}.");
            }

            if (!int.TryParse(positional[1], NumberStyles.None, CultureInfo.InvariantCulture, out int k) || k <= 0)
            {
                return Fail($"k must be a positive integer, but was '{positional[1]}'.");
            }

            string? format = null;
            int? minBlock = null;
            int? maxBlock = null;
            GraphRunOptions options = new GraphRunOptions();

            for (int i = 0; i < flags.Length; i++)
            {
                if (!GraphCommandSupport.TryParseCommonFlag(flags, ref i, options, out bool matched, out string? error))
                {
                    return Fail(error!);
                }

                if (matched)
                {
                    continue;
                }

                string flag = flags[i];
                switch (flag)
                {
                    case "--format":
                        if (!CliOutput.TryTakeValue(flags, ref i, flag, out format, out error))
                        {
                            return Fail(error!);
                        }

                        break;

                    case "--min-block":
                        if (!CliOutput.TryTakeValue(flags, ref i, flag, out string? minText, out error)
                            || !int.TryParse(minText, NumberStyles.None, CultureInfo.InvariantCulture, out int minValue)
                            || minValue <= 0)
                        {
                            return Fail(error ?? $"--min-block must be a positive integer, but was '{minText}'.");
                        }

                        minBlock = minValue;
                        break;

                    case "--max-block":
                        if (!CliOutput.TryTakeValue(flags, ref i, flag, out string? maxText, out error)
                            || !int.TryParse(maxText, NumberStyles.None, CultureInfo.InvariantCulture, out int maxValue)
                            || maxValue <= 0)
                        {
                            return Fail(error ?? $"--max-block must be a positive integer, but was '{maxText}'.");
                        }

                        maxBlock = maxValue;
                        break;

                    default:
                        return Fail($"unknown option '{flag}'.");
                }
            }

            Graph graph = Program.LoadGraph(positional[0], format);
            int resolvedMinBlock = minBlock ?? 1;
            int resolvedMaxBlock = maxBlock ?? graph.VertexCount;

            return GraphCommandSupport.Run(
                graph,
                g => new GraphPartitionSpec(g, k, resolvedMinBlock, resolvedMaxBlock),
                options,
                new (string, string)[]
                {
                    ("graph-file", positional[0]),
                    ("k", k.ToString(CultureInfo.InvariantCulture)),
                    ("min-block", resolvedMinBlock.ToString(CultureInfo.InvariantCulture)),
                    ("max-block", resolvedMaxBlock.ToString(CultureInfo.InvariantCulture)),
                });
        }

        private static int Fail(string message)
        {
            Console.Error.WriteLine(message);
            Console.Error.WriteLine();
            Console.Error.Write(Usage);
            return Program.ExitUsage;
        }
    }
}
