using System;
using System.Globalization;
using System.IO;
using ZDD.Net.Core;

namespace ZDD.Net.Samples.Cli
{
    /// <summary>
    /// <c>zdd-cli family</c>: builds one of a handful of textbook families over <c>N</c> items
    /// (powerset, singletons, or the single "everything" set) and reports its size. The library's
    /// original minimal demo (M1-16); kept as-is once the CLI grew subcommands for the grid/graph
    /// specs (M5-5).
    /// </summary>
    internal static class FamilyCommand
    {
        private static string Usage =>
            "usage: zdd-cli family [options]\n" +
            "\n" +
            "  --family <kind>  powerset | singletons | full  (default: powerset)\n" +
            "  --items <n>      number of variables, 0.." + Options.MaxItems.ToString(CultureInfo.InvariantCulture) +
            "  (default: " + Options.DefaultItems.ToString(CultureInfo.InvariantCulture) + ")\n" +
            "  --dot <path>     write Graphviz DOT to <path>, or to stdout when <path> is '-'\n" +
            "  --stats          print the manager's table statistics\n" +
            "  --help           print this text\n" +
            "\n" +
            "example: zdd-cli family --family singletons --items 5 --dot - --stats\n";

        public static int Run(string[] args)
        {
            if (!Options.TryParse(args, out Options options, out string? error))
            {
                Console.Error.WriteLine(error);
                Console.Error.WriteLine();
                Console.Error.Write(Usage);
                return Program.ExitUsage;
            }

            if (options.ShowHelp)
            {
                Console.Out.Write(Usage);
                return Program.ExitSuccess;
            }

            using ZddManager manager = new ZddManager(options.Items);
            Zdd family = Build(manager, options.Family, options.Items);

            Report(family, options);

            if (options.DotPath is not null)
            {
                WriteDot(family, options.DotPath);
            }

            if (options.ShowStatistics)
            {
                Console.Out.WriteLine();
                Console.Out.WriteLine(manager.GetStatistics().ToString());
            }

            return Program.ExitSuccess;
        }

        /// <summary>お題の族を組み立てる。どれも公開 API の組み合わせだけで作れる。</summary>
        private static Zdd Build(ZddManager manager, FamilyKind kind, int items)
        {
            switch (kind)
            {
                // 2^U。補の定義（2^U ∖ ∅）をそのまま使う。
                case FamilyKind.PowerSet:
                    return manager.Empty.Complement();

                // {{0}, {1}, …}。1 要素集合を全部集めたもの。
                case FamilyKind.Singletons:
                    Zdd singletons = manager.Empty;
                    for (int item = 0; item < items; item++)
                    {
                        singletons |= manager.Singleton(item);
                    }

                    return singletons;

                // {{0, 1, …, n-1}}。全部入りの集合 1 つだけを持つ族。積で 1 要素ずつ足していく。
                default:
                    Zdd full = manager.Base;
                    for (int item = 0; item < items; item++)
                    {
                        full *= manager.Singleton(item);
                    }

                    return full;
            }
        }

        private static void Report(in Zdd family, in Options options)
        {
            TextWriter output = Console.Out;

            CliOutput.WriteField(output, "family", options.Family.ToString());
            CliOutput.WriteField(output, "items", options.Items.ToString(CultureInfo.InvariantCulture));
            CliOutput.WriteField(output, "sets", family.Count.ToString(CultureInfo.InvariantCulture));
            CliOutput.WriteField(output, "nodes", family.NodeCount.ToString(CultureInfo.InvariantCulture));
            CliOutput.WriteField(output, "support", string.Join(", ", family.Support()));
        }

        /// <summary>
        /// DOT を書き出す。<c>-</c> なら標準出力へ。ファイルへ書くときも
        /// <see cref="Zdd.WriteDot(TextWriter)"/> に直に流し、文字列に載せない。
        /// </summary>
        private static void WriteDot(in Zdd family, string path)
        {
            if (path == "-")
            {
                Console.Out.WriteLine();
                family.WriteDot(Console.Out);
                return;
            }

            using StreamWriter file = new StreamWriter(path);
            family.WriteDot(file);
        }

        /// <summary>組み立てられる族の種類。</summary>
        private enum FamilyKind
        {
            /// <summary>2^U（全部分集合）。</summary>
            PowerSet,

            /// <summary>1 要素集合をすべて集めた族。</summary>
            Singletons,

            /// <summary>全部入りの集合 1 つだけを持つ族。</summary>
            Full,
        }

        /// <summary>コマンドラインの読み取り結果。</summary>
        private readonly struct Options
        {
            /// <summary><c>--items</c> の既定値。DOT を目で追える大きさにしてある。</summary>
            public const int DefaultItems = 4;

            /// <summary><c>--items</c> の上限。サンプルなので、うっかり巨大な族を作らせない。</summary>
            public const int MaxItems = 24;

            private Options(int items, FamilyKind family, string? dotPath, bool showStatistics, bool showHelp)
            {
                Items = items;
                Family = family;
                DotPath = dotPath;
                ShowStatistics = showStatistics;
                ShowHelp = showHelp;
            }

            public int Items { get; }

            public FamilyKind Family { get; }

            /// <summary>DOT の書き出し先。<see langword="null"/> なら書き出さない。</summary>
            public string? DotPath { get; }

            public bool ShowStatistics { get; }

            public bool ShowHelp { get; }

            public static bool TryParse(string[] args, out Options options, out string? error)
            {
                int items = DefaultItems;
                FamilyKind family = FamilyKind.PowerSet;
                string? dotPath = null;
                bool showStatistics = false;

                for (int index = 0; index < args.Length; index++)
                {
                    string argument = args[index];

                    switch (argument)
                    {
                        case "--help":
                        case "-h":
                            options = new Options(items, family, dotPath, showStatistics, showHelp: true);
                            error = null;
                            return true;

                        case "--stats":
                            showStatistics = true;
                            break;

                        case "--items":
                            if (!CliOutput.TryTakeValue(args, ref index, argument, out string? itemsText, out error))
                            {
                                options = default;
                                return false;
                            }

                            if (!int.TryParse(itemsText, NumberStyles.None, CultureInfo.InvariantCulture, out items)
                                || items > MaxItems)
                            {
                                options = default;
                                error = $"--items must be a number between 0 and {MaxItems}, but was '{itemsText}'.";
                                return false;
                            }

                            break;

                        case "--family":
                            if (!CliOutput.TryTakeValue(args, ref index, argument, out string? familyText, out error))
                            {
                                options = default;
                                return false;
                            }

                            if (!TryParseFamily(familyText, out family))
                            {
                                options = default;
                                error = $"unknown family '{familyText}'; expected powerset, singletons or full.";
                                return false;
                            }

                            break;

                        case "--dot":
                            if (!CliOutput.TryTakeValue(args, ref index, argument, out dotPath, out error))
                            {
                                options = default;
                                return false;
                            }

                            break;

                        default:
                            options = default;
                            error = $"unknown option '{argument}'.";
                            return false;
                    }
                }

                options = new Options(items, family, dotPath, showStatistics, showHelp: false);
                error = null;
                return true;
            }

            private static bool TryParseFamily(string text, out FamilyKind family)
            {
                switch (text)
                {
                    case "powerset":
                        family = FamilyKind.PowerSet;
                        return true;
                    case "singletons":
                        family = FamilyKind.Singletons;
                        return true;
                    case "full":
                        family = FamilyKind.Full;
                        return true;
                    default:
                        family = FamilyKind.PowerSet;
                        return false;
                }
            }
        }
    }
}
