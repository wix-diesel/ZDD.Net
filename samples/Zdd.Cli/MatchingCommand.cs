using System;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;

namespace ZDD.Net.Samples.Cli
{
    /// <summary>
    /// <c>zdd-cli matching &lt;graph-file&gt;</c>: counts (and, with the shared options, inspects) the
    /// matchings of a graph loaded from a file, built with <see cref="MatchingSpec"/>.
    /// </summary>
    internal static class MatchingCommand
    {
        private static string Usage =>
            "usage: zdd-cli matching <graph-file> [options]\n" +
            "\n" +
            "  counts matchings (no two chosen edges share an endpoint) of the graph in\n" +
            "  <graph-file>.\n" +
            "\n" +
            "  --perfect        only count matchings that cover every vertex\n" +
            "  --format <dimacs|edges|simple>\n" +
            "                   graph file format (default: guessed from the file extension,\n" +
            "                   falling back to ZDD.Net's own simple text format)\n" +
            GraphCommandSupport.CommonOptionsHelp +
            "\n" +
            "example: zdd-cli matching graph.dimacs --perfect --stats\n";

        public static int Run(string[] args)
        {
            if (Array.IndexOf(args, "-h") >= 0 || Array.IndexOf(args, "--help") >= 0)
            {
                Console.Out.Write(Usage);
                return Program.ExitSuccess;
            }

            (string[] positional, string[] flags) = GraphCommandSupport.SplitPositional(args);

            if (positional.Length != 1)
            {
                return Fail($"matching needs exactly 1 argument (graph-file), but got {positional.Length}.");
            }

            string? format = null;
            bool perfect = false;
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
                    case "--perfect":
                        perfect = true;
                        break;

                    case "--format":
                        if (!CliOutput.TryTakeValue(flags, ref i, flag, out format, out error))
                        {
                            return Fail(error!);
                        }

                        break;

                    default:
                        return Fail($"unknown option '{flag}'.");
                }
            }

            Graph graph = Program.LoadGraph(positional[0], format);

            return GraphCommandSupport.Run(
                graph,
                g => new MatchingSpec(g, perfect),
                options,
                new (string, string)[]
                {
                    ("graph-file", positional[0]),
                    ("perfect", perfect ? "true" : "false"),
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
