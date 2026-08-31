using System;
using System.Globalization;
using System.IO;
using ZDD.Net.Core;

namespace ZDD.Net.Samples.Cli
{
    /// <summary>
    /// ZDD.Net を外から触ってみるための最小の CLI。族を 1 つ組み立て、その大きさと
    /// マネージャの統計を表示し、必要なら Graphviz の DOT を書き出す。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ここは<b>公開 API だけ</b>で書いてある。ライブラリを参照した人が最初に書くコードと
    /// 同じ形になっているので、公開 API だけで一通りの用が足りるかどうかの確認も兼ねる。
    /// </para>
    /// <para>
    /// 使い方:
    /// <c>dotnet run --project samples/Zdd.Cli -- --family singletons --items 5 --stats --dot out.gv</c>
    /// </para>
    /// </remarks>
    internal static class Program
    {
        /// <summary>正常終了。</summary>
        private const int ExitSuccess = 0;

        /// <summary>引数が解釈できなかった。</summary>
        private const int ExitUsage = 2;

        private static int Main(string[] args)
        {
            if (!Options.TryParse(args, out Options options, out string? error))
            {
                Console.Error.WriteLine(error);
                Console.Error.WriteLine();
                Console.Error.Write(Options.Usage);
                return ExitUsage;
            }

            if (options.ShowHelp)
            {
                Console.Out.Write(Options.Usage);
                return ExitSuccess;
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

            return ExitSuccess;
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

            Write(output, "family", options.Family.ToString());
            Write(output, "items", options.Items.ToString(CultureInfo.InvariantCulture));
            Write(output, "sets", family.Count.ToString(CultureInfo.InvariantCulture));
            Write(output, "nodes", family.NodeCount.ToString(CultureInfo.InvariantCulture));
            Write(output, "support", string.Join(", ", family.Support()));
        }

        private static void Write(TextWriter output, string name, string value)
        {
            output.Write(name.PadRight(8));
            output.Write(": ");
            output.WriteLine(value);
        }

        /// <summary>
        /// DOT を書き出す。<c>-</c> なら標準出力へ。ファイルへ書くときも
        /// <see cref="Zdd.WriteDot"/> に直に流し、文字列に載せない。
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
            private const int DefaultItems = 4;

            /// <summary><c>--items</c> の上限。サンプルなので、うっかり巨大な族を作らせない。</summary>
            private const int MaxItems = 24;

            private Options(int items, FamilyKind family, string? dotPath, bool showStatistics, bool showHelp)
            {
                Items = items;
                Family = family;
                DotPath = dotPath;
                ShowStatistics = showStatistics;
                ShowHelp = showHelp;
            }

            public static string Usage =>
                "usage: zdd-cli [options]\n" +
                "\n" +
                "  --family <kind>  powerset | singletons | full  (default: powerset)\n" +
                "  --items <n>      number of variables, 0.." + MaxItems.ToString(CultureInfo.InvariantCulture) +
                "  (default: " + DefaultItems.ToString(CultureInfo.InvariantCulture) + ")\n" +
                "  --dot <path>     write Graphviz DOT to <path>, or to stdout when <path> is '-'\n" +
                "  --stats          print the manager's table statistics\n" +
                "  --help           print this text\n" +
                "\n" +
                "example: zdd-cli --family singletons --items 5 --dot - --stats\n";

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
                            if (!TryTakeValue(args, ref index, argument, out string? itemsText, out error))
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
                            if (!TryTakeValue(args, ref index, argument, out string? familyText, out error))
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
                            if (!TryTakeValue(args, ref index, argument, out dotPath, out error))
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

            private static bool TryTakeValue(
                string[] args,
                ref int index,
                string option,
                out string value,
                out string? error)
            {
                if (index + 1 >= args.Length)
                {
                    value = string.Empty;
                    error = $"{option} needs a value.";
                    return false;
                }

                value = args[++index];
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
