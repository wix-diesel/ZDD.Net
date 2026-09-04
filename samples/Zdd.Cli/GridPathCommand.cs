using System;
using System.Collections.Generic;
using System.Globalization;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;

namespace ZDD.Net.Samples.Cli
{
    /// <summary>
    /// <c>zdd-cli grid-path &lt;rows&gt; &lt;cols&gt;</c>: counts (and, with the shared options, inspects)
    /// simple <c>s</c>–<c>t</c> paths on a <c>rows</c> × <c>cols</c> grid graph, built with
    /// <see cref="PathSpec"/>. With the default endpoints (opposite corners), this reproduces OEIS
    /// A007764 — <c>grid-path 7 7</c> reports <c>575780564</c> (M5-5's completion criterion).
    /// </summary>
    internal static class GridPathCommand
    {
        private static string Usage =>
            "usage: zdd-cli grid-path <rows> <cols> [options]\n" +
            "\n" +
            "  counts simple s-t paths on a rows x cols grid graph. With the default\n" +
            "  endpoints (opposite corners), this is OEIS A007764: 'grid-path 7 7' reports\n" +
            "  575780564.\n" +
            "\n" +
            "  --s <vertex>     one endpoint, indexed row * cols + col (default: 0, the top-left corner)\n" +
            "  --t <vertex>     the other endpoint (default: rows * cols - 1, the bottom-right corner)\n" +
            "  --any-endpoints  count every simple path, ignoring --s/--t\n" +
            GraphCommandSupport.CommonOptionsHelp +
            "\n" +
            "example: zdd-cli grid-path 7 7 --stats\n";

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
                return Fail($"grid-path needs exactly 2 arguments (rows, cols), but got {positional.Length}.");
            }

            if (!TryParsePositiveInt(positional[0], "rows", out int rows, out string? error)
                || !TryParsePositiveInt(positional[1], "cols", out int cols, out error))
            {
                return Fail(error!);
            }

            int? s = null;
            int? t = null;
            bool anyEndpoints = false;
            GraphRunOptions options = new GraphRunOptions();

            for (int i = 0; i < flags.Length; i++)
            {
                if (!GraphCommandSupport.TryParseCommonFlag(flags, ref i, options, out bool matched, out error))
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
                    case "--s":
                        if (!CliOutput.TryTakeValue(flags, ref i, flag, out string? sText, out error)
                            || !int.TryParse(sText, NumberStyles.None, CultureInfo.InvariantCulture, out int sValue))
                        {
                            return Fail(error ?? $"--s must be a non-negative integer, but was '{sText}'.");
                        }

                        s = sValue;
                        break;

                    case "--t":
                        if (!CliOutput.TryTakeValue(flags, ref i, flag, out string? tText, out error)
                            || !int.TryParse(tText, NumberStyles.None, CultureInfo.InvariantCulture, out int tValue))
                        {
                            return Fail(error ?? $"--t must be a non-negative integer, but was '{tText}'.");
                        }

                        t = tValue;
                        break;

                    case "--any-endpoints":
                        anyEndpoints = true;
                        break;

                    default:
                        return Fail($"unknown option '{flag}'.");
                }
            }

            Graph graph = Graph.Grid(rows, cols);
            int resolvedS = s ?? 0;
            int resolvedT = t ?? graph.VertexCount - 1;

            if (!anyEndpoints)
            {
                if ((uint)resolvedS >= (uint)graph.VertexCount)
                {
                    return Fail($"--s must be in 0 .. {graph.VertexCount - 1}, but was {resolvedS}.");
                }

                if ((uint)resolvedT >= (uint)graph.VertexCount)
                {
                    return Fail($"--t must be in 0 .. {graph.VertexCount - 1}, but was {resolvedT}.");
                }
            }

            List<(string, string)> extraFields = new List<(string, string)>
            {
                ("rows", rows.ToString(CultureInfo.InvariantCulture)),
                ("cols", cols.ToString(CultureInfo.InvariantCulture)),
            };

            if (anyEndpoints)
            {
                extraFields.Add(("any-endpoints", "true"));
            }
            else
            {
                extraFields.Add(("s", resolvedS.ToString(CultureInfo.InvariantCulture)));
                extraFields.Add(("t", resolvedT.ToString(CultureInfo.InvariantCulture)));
            }

            return GraphCommandSupport.Run(
                graph,
                g => new PathSpec(g, resolvedS, resolvedT, anyEndpoints),
                options,
                extraFields);
        }

        private static bool TryParsePositiveInt(string text, string name, out int value, out string? error)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) || value <= 0)
            {
                error = $"{name} must be a positive integer, but was '{text}'.";
                return false;
            }

            error = null;
            return true;
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
