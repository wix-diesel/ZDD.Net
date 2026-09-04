using System;
using System.IO;
using ZDD.Net.Io;

namespace ZDD.Net.Samples.Cli
{
    /// <summary>
    /// ZDD.Net を外から触ってみるための CLI。<c>family</c>（最小の組合せ族デモ）に加えて、
    /// グラフ問題の組み込みスペックをそのまま叩ける <c>grid-path</c> / <c>spanning-tree</c> /
    /// <c>partition</c> / <c>matching</c> の各サブコマンドを持つ。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ここは<b>公開 API だけ</b>で書いてある。ライブラリを参照した人が最初に書くコードと
    /// 同じ形になっているので、公開 API だけで一通りの用が足りるかどうかの確認も兼ねる。
    /// </para>
    /// <para>
    /// 使い方: <c>dotnet run --project samples/Zdd.Cli -- grid-path 7 7 --stats</c>
    /// （<c>grid-path 7 7</c> は OEIS A007764 の <c>575780564</c> を出す）。
    /// サブコマンド一覧は <c>--help</c>、各サブコマンドの詳細は
    /// <c>&lt;command&gt; --help</c> を付けて実行する。
    /// </para>
    /// </remarks>
    internal static class Program
    {
        /// <summary>正常終了。</summary>
        internal const int ExitSuccess = 0;

        /// <summary>引数が解釈できなかった。</summary>
        internal const int ExitUsage = 2;

        /// <summary>引数は解釈できたが、実行時にエラーが起きた（ファイルが読めない、形式が壊れているなど）。</summary>
        internal const int ExitError = 1;

        private const string TopLevelUsage =
            "usage: zdd-cli <command> [options]\n" +
            "\n" +
            "commands:\n" +
            "  family         powerset / singletons / full family over N items (original demo)\n" +
            "  grid-path      count s-t simple paths on a grid graph (OEIS A007764)\n" +
            "  spanning-tree  count spanning trees of a graph read from a file\n" +
            "  partition      count balanced k-way partitions of a graph read from a file\n" +
            "  matching       count matchings of a graph read from a file\n" +
            "\n" +
            "run '<command> --help' for command-specific options.\n";

        private static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or IOException or InvalidOperationException)
            {
                // Known, expected failure modes (bad arguments, bad files, spec/manager mismatches):
                // report the message only, never a stack trace. Anything else is a real bug and should
                // surface with its full trace instead of being swallowed here.
                Console.Error.WriteLine($"error: {ex.Message}");
                return ExitError;
            }
        }

        private static int Run(string[] args)
        {
            if (args.Length == 0)
            {
                Console.Error.Write(TopLevelUsage);
                return ExitUsage;
            }

            string command = args[0];
            string[] rest = args[1..];

            switch (command)
            {
                case "-h":
                case "--help":
                    Console.Out.Write(TopLevelUsage);
                    return ExitSuccess;

                case "family":
                    return FamilyCommand.Run(rest);

                case "grid-path":
                    return GridPathCommand.Run(rest);

                case "spanning-tree":
                    return SpanningTreeCommand.Run(rest);

                case "partition":
                    return PartitionCommand.Run(rest);

                case "matching":
                    return MatchingCommand.Run(rest);

                default:
                    Console.Error.WriteLine($"unknown command '{command}'.");
                    Console.Error.WriteLine();
                    Console.Error.Write(TopLevelUsage);
                    return ExitUsage;
            }
        }

        /// <summary>
        /// Loads a graph from <paramref name="path"/>, picking the text format by extension
        /// (<c>.dimacs</c>/<c>.gr</c>/<c>.col</c> → DIMACS, <c>.edges</c>/<c>.el</c> → plain edge list,
        /// anything else → ZDD.Net's own simple text format), or <paramref name="format"/> when given.
        /// </summary>
        /// <exception cref="FileNotFoundException"><paramref name="path"/> does not exist.</exception>
        /// <exception cref="GraphFormatException">The file's contents do not parse as the chosen format.</exception>
        internal static Graphs.Graph LoadGraph(string path, string? format)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"graph file '{path}' was not found.", path);
            }

            string kind = format ?? DetectFormat(path);
            using StreamReader reader = new StreamReader(path);

            return kind switch
            {
                "dimacs" => DimacsGraph.Read(reader),
                "edges" => EdgeListGraph.Read(reader),
                "simple" => SimpleTextGraph.Read(reader).Graph,
                _ => throw new FormatException($"unknown --format '{kind}'; expected dimacs, edges or simple."),
            };
        }

        private static string DetectFormat(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension switch
            {
                ".dimacs" or ".gr" or ".col" => "dimacs",
                ".edges" or ".el" => "edges",
                _ => "simple",
            };
        }
    }
}
