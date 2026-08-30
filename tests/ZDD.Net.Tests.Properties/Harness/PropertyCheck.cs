using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using CsCheck;
using Xunit.Abstractions;

namespace ZDD.Net.Tests.Properties.Harness
{
    /// <summary>
    /// プロパティを走らせる入口。CsCheck の <c>Sample</c> をそのまま呼ばず、ここを通す。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>なぜ包むのか</b>: CsCheck の <c>seed</c> は<b>最初の 1 回</b>の種でしかなく、2 回目以降は
    /// スレッドごとの乱数源から取る。それだと「シードを固定すれば同じ入力列が出る」が成り立たず、
    /// CI で落ちたときに手元で同じ列を再生できない。そこでここでは種から
    /// <see cref="PCG"/> を 1 本立て、<b>各回の生成直前の状態</b>を種文字列として控えながら
    /// 自分で回す。控えた種はそのまま CsCheck の <c>seed</c> に渡せる形なので、
    /// 失敗した回だけを後から再生できる。
    /// </para>
    /// <para>
    /// <b>シュリンク</b>: 失敗を捕まえたら、その回の種を <c>seed</c> にして CsCheck の
    /// <c>Sample</c> を <see cref="ShrinkIterations"/> 回だけ回し直す。1 回目で同じ失敗が再現し、
    /// 残りの回が「より小さい反例」の探索に使われる。反例と、それを再生する種は
    /// <see cref="CsCheckException"/> のメッセージに載る。
    /// </para>
    /// <para>
    /// <b>実行時間</b>: 既定は 1 プロパティ <see cref="DefaultIterations"/> 回。
    /// 環境変数 <see cref="IterationsVariable"/> で増やせる（夜間や調査用）。
    /// 種は環境変数 <see cref="SeedVariable"/> で差し替えられる。
    /// </para>
    /// </remarks>
    internal static class PropertyCheck
    {
        /// <summary>1 プロパティあたりの既定の試行回数。</summary>
        public const long DefaultIterations = 100;

        /// <summary>失敗を縮めるときに使う試行回数。</summary>
        public const long ShrinkIterations = 1000;

        /// <summary>試行回数を上書きする環境変数の名前。</summary>
        public const string IterationsVariable = "ZDD_PROPERTY_ITER";

        /// <summary>種を上書きする環境変数の名前。</summary>
        public const string SeedVariable = "ZDD_PROPERTY_SEED";

        /// <summary>プロパティ名から種を作るときの乱数列の番号。</summary>
        private const uint SeedStream = 1;

        /// <summary>いま有効な試行回数。</summary>
        public static long Iterations =>
            ResolveIterations(Environment.GetEnvironmentVariable(IterationsVariable));

        /// <summary>
        /// プロパティを走らせる。
        /// </summary>
        /// <param name="gen">入力の生成器。</param>
        /// <param name="assert">入力 1 つを検査する。破れていれば例外を投げる。</param>
        /// <param name="output">経過を書き出す先。種はここに出る。</param>
        /// <param name="seed">種。省略すると <paramref name="property"/> から決まる固定の種を使う。</param>
        /// <param name="iterations">試行回数。省略すると <see cref="Iterations"/>。</param>
        /// <param name="property">プロパティの名前。呼び出し元のメソッド名が既定。</param>
        public static void Sample<T>(
            Gen<T> gen,
            Action<T> assert,
            ITestOutputHelper? output = null,
            string? seed = null,
            long? iterations = null,
            [CallerMemberName] string property = "")
        {
            ArgumentNullException.ThrowIfNull(gen);
            ArgumentNullException.ThrowIfNull(assert);

            string baseSeed = seed ?? ResolveSeed(Environment.GetEnvironmentVariable(SeedVariable), property);
            long count = iterations ?? Iterations;

            output?.WriteLine(
                $"[{property}] seed {baseSeed}, {count} iteration(s). " +
                $"Set {SeedVariable} / {IterationsVariable} to change.");

            PCG pcg = ParseSeed(baseSeed);

            for (long iteration = 0; iteration < count; iteration++)
            {
                // 生成の直前の状態を控える。この文字列を CsCheck の seed に渡すと、
                // 次の 1 件がそのまま再現される。
                string caseSeed = pcg.ToString(pcg.State);
                T value = gen.Generate(pcg, null!, out Size _);

                try
                {
                    assert(value);
                }
                catch (Exception failure)
                {
                    output?.WriteLine(
                        $"[{property}] failed on iteration {iteration} with seed {caseSeed}: {value}");
                    output?.WriteLine(failure.Message);
                    output?.WriteLine($"[{property}] shrinking from seed {caseSeed} …");

                    Shrink(gen, assert, caseSeed, output);

                    // 縮め直しで再現しなかった（検査が実行ごとに揺れている）。元の失敗をそのまま出す。
                    throw;
                }
            }
        }

        /// <summary>試行回数の環境変数を読む。空なら既定値、数として読めなければ例外。</summary>
        public static long ResolveIterations(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DefaultIterations;
            }

            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
                || parsed <= 0)
            {
                throw new ArgumentException(
                    $"'{IterationsVariable}' must be a positive integer, but was '{value}'.",
                    nameof(value));
            }

            return parsed;
        }

        /// <summary>種の環境変数を読む。空ならプロパティ名から決まる固定の種を使う。</summary>
        public static string ResolveSeed(string? value, string property) =>
            string.IsNullOrWhiteSpace(value) ? SeedFor(property) : value;

        /// <summary>
        /// プロパティ名から種を作る。名前が同じなら毎回同じ種になり、名前が違えば別の入力列になる。
        /// </summary>
        /// <remarks>
        /// <see cref="string.GetHashCode()"/> はプロセスごとに値が変わるので使えない。
        /// ここでは FNV-1a を自分で回す。
        /// </remarks>
        public static string SeedFor(string property)
        {
            ArgumentNullException.ThrowIfNull(property);

            const ulong Offset = 14695981039346656037;
            const ulong Prime = 1099511628211;

            ulong hash = Offset;

            foreach (char c in property)
            {
                hash = (hash ^ c) * Prime;
            }

            return new PCG(SeedStream, hash).ToString();
        }

        /// <summary>
        /// 種を読む。CsCheck の <c>PCG.Parse</c> は形の合わない文字列に対して
        /// 素の <see cref="IndexOutOfRangeException"/> を投げるので、ここで言い直す。
        /// </summary>
        public static PCG ParseSeed(string seed)
        {
            ArgumentNullException.ThrowIfNull(seed);

            try
            {
                return PCG.Parse(seed);
            }
            catch (Exception malformed) when (
                malformed is IndexOutOfRangeException or ArgumentException or FormatException)
            {
                throw new ArgumentException(
                    $"'{seed}' is not a CsCheck seed. A seed looks like \"7o-_oPeNCbE1\" (12 characters); " +
                    $"the one printed in a failure report can be pasted as is.",
                    nameof(seed),
                    malformed);
            }
        }

        /// <summary>
        /// 失敗した回の種から CsCheck に縮めさせる。縮んだ反例と、それを再生する種を載せた
        /// <see cref="CsCheckException"/> が飛ぶ。
        /// </summary>
        private static void Shrink<T>(Gen<T> gen, Action<T> assert, string caseSeed, ITestOutputHelper? output)
        {
            try
            {
                // threads: 1 は必須。並列に走らせると、縮めている最中の失敗の順序が実行ごとに変わる。
                gen.Sample(assert, seed: caseSeed, iter: ShrinkIterations, threads: 1, print: Print);
            }
            catch (CsCheckException shrunk)
            {
                output?.WriteLine(shrunk.Message);
                throw;
            }
        }

        private static string Print<T>(T value) => value?.ToString() ?? "(null)";
    }
}
