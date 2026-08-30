using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Harness
{
    /// <summary>
    /// ZDD の演算結果を素朴な族と照合するアサーション。
    /// </summary>
    /// <remarks>
    /// 失敗したときに「どの集合が欠けていて、どの集合が余分か」を出すのが主目的。
    /// 「族が一致しない」だけのメッセージでは、どの枝を間違えたのか当たりが付けられない。
    /// </remarks>
    internal static class FamilyAssert
    {
        /// <summary>差分として並べる集合の最大個数。これを超えた分は件数だけ出す。</summary>
        public const int MaxReportedSets = 8;

        /// <summary>ZDD が表す族が、素朴な実装の答えと一致することを確かめる。</summary>
        public static void AssertSameFamily(in Zdd actual, BruteForceFamily expected) =>
            AssertSameFamily(null, actual, expected);

        /// <summary>
        /// ZDD が表す族が、素朴な実装の答えと一致することを確かめる。
        /// </summary>
        /// <param name="context">
        /// 失敗時に頭へ付ける説明（<c>"Change(1)"</c> など）。どの演算の照合かを示す。
        /// </param>
        /// <param name="actual">検証したい ZDD。</param>
        /// <param name="expected">素朴な実装が出した答え。</param>
        /// <param name="source">
        /// 演算の入力になった族。渡しておくと失敗時のメッセージに出る（成功時は文字列化しない）。
        /// </param>
        public static void AssertSameFamily(
            string? context,
            in Zdd actual,
            BruteForceFamily expected,
            BruteForceFamily? source = null)
        {
            ArgumentNullException.ThrowIfNull(expected);

            ZddManager manager = actual.Manager;
            BruteForceFamily produced = ZddFamilies.ToBruteForce(actual);

            if (!produced.Equals(expected))
            {
                Assert.Fail(DescribeMismatch(context, expected, produced, source));
            }

            // ZDD は正準形なので、同じ族なら同じノード ID になっていなければならない。
            // 集合としては合っているのにハンドルが違うなら、削減規則か一意化表のほうが壊れている。
            Zdd canonical = ZddFamilies.Build(manager, expected);

            if (!canonical.Equals(actual))
            {
                Assert.Fail(
                    $"{Prefix(context)}the family is right but the handle is not canonical: " +
                    $"got {actual}, expected {canonical} for {expected.Describe(MaxReportedSets)}.");
            }
        }

        /// <summary>素朴な族どうしの一致を、同じメッセージの形で確かめる。</summary>
        public static void AssertSameFamily(
            string? context,
            BruteForceFamily actual,
            BruteForceFamily expected,
            BruteForceFamily? source = null)
        {
            ArgumentNullException.ThrowIfNull(actual);
            ArgumentNullException.ThrowIfNull(expected);

            if (!actual.Equals(expected))
            {
                Assert.Fail(DescribeMismatch(context, expected, actual, source));
            }
        }

        /// <summary>
        /// 食い違いを読める形にする。欠けている集合（expected にあって actual にない）と
        /// 余分な集合（actual にあって expected にない）を、最大 <see cref="MaxReportedSets"/> 件ずつ並べる。
        /// </summary>
        public static string DescribeMismatch(
            string? context,
            BruteForceFamily expected,
            BruteForceFamily actual,
            BruteForceFamily? source = null)
        {
            ArgumentNullException.ThrowIfNull(expected);
            ArgumentNullException.ThrowIfNull(actual);

            List<int> missing = expected.Masks.Where(mask => !actual.Contains(mask)).ToList();
            List<int> unexpected = actual.Masks.Where(mask => !expected.Contains(mask)).ToList();

            StringBuilder text = new StringBuilder();
            text.Append(Prefix(context)).AppendLine("the family does not match the brute-force result.");
            text.Append("  variables: ").Append(expected.VariableCount).AppendLine();

            if (source is not null)
            {
                text.Append("  input    : ").Append(source.Count).Append(" set(s) ")
                    .AppendLine(source.Describe(MaxReportedSets));
            }

            text.Append("  expected : ").Append(expected.Count).Append(" set(s) ")
                .AppendLine(expected.Describe(MaxReportedSets));
            text.Append("  actual   : ").Append(actual.Count).Append(" set(s) ")
                .AppendLine(actual.Describe(MaxReportedSets));
            AppendSets(text, "missing", missing);
            AppendSets(text, "unexpected", unexpected);

            return text.ToString();
        }

        private static void AppendSets(StringBuilder text, string label, List<int> masks)
        {
            text.Append("  ").Append(label).Append(" (").Append(masks.Count).Append("): ");

            if (masks.Count == 0)
            {
                text.AppendLine("none");
                return;
            }

            text.Append(string.Join(", ", masks.Take(MaxReportedSets).Select(BruteForceFamily.FormatSet)));

            if (masks.Count > MaxReportedSets)
            {
                text.Append(", … (+").Append(masks.Count - MaxReportedSets).Append(" more)");
            }

            text.AppendLine();
        }

        private static string Prefix(string? context) =>
            string.IsNullOrEmpty(context) ? string.Empty : $"[{context}] ";
    }
}
