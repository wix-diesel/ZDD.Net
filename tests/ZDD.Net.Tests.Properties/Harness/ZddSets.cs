using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Properties.Harness
{
    /// <summary>
    /// ZDD が表す族を読み出し、法則の破れを読める形で報告する。
    /// </summary>
    /// <remarks>
    /// 読み出しは <see cref="Zdd.OnSet"/> / <see cref="Zdd.OffSet"/> だけで行う（内部表現に触らない）。
    /// この 2 つは M1-5・M1-6 で素朴実装と総当たり照合済みなので、ここでの読み出しは
    /// 「検証したいものを検証に使う」ことにはならない。
    /// </remarks>
    internal static class ZddSets
    {
        /// <summary>この宇宙の冪集合 2^U。<c>~∅</c> がそれに当たる。</summary>
        public static Zdd PowerSet(ZddManager manager)
        {
            ArgumentNullException.ThrowIfNull(manager);
            return manager.Empty.Complement();
        }

        /// <summary>族に属する集合をビットマスクの昇順で読み出す。</summary>
        public static int[] ToMasks(in Zdd family)
        {
            ZddManager manager = family.Manager;
            int variableCount = manager.VariableCount;

            List<int> masks = new List<int>();
            Stack<(Zdd Family, int Item, int Mask)> pending = new Stack<(Zdd, int, int)>();
            pending.Push((family, 0, 0));

            // 再帰しない。族が深くてもスタックを消費しないため（docs/PLAN.md §4.5 と同じ方針）。
            while (pending.Count > 0)
            {
                (Zdd current, int item, int mask) = pending.Pop();

                if (current.IsEmpty)
                {
                    continue;
                }

                if (item == variableCount)
                {
                    // 全 item を振り分け終えて空でないなら、残るのは {∅} だけ。
                    if (!current.IsBase)
                    {
                        throw new InvalidOperationException(
                            $"A family of {variableCount} variable(s) still had structure below the last item.");
                    }

                    masks.Add(mask);
                    continue;
                }

                pending.Push((current.OffSet(item), item + 1, mask));
                pending.Push((current.OnSet(item), item + 1, mask | (1 << item)));
            }

            masks.Sort();
            return masks.ToArray();
        }

        /// <summary>
        /// 同じマネージャの 2 つの族が等しいことを確かめる。ZDD は正準形なので、
        /// 等しい族はノード ID まで一致していなければならない。
        /// </summary>
        /// <param name="law">破れたときに頭に出す法則の名前（<c>"f | g == g | f"</c> など）。</param>
        /// <param name="left">左辺。</param>
        /// <param name="right">右辺。</param>
        /// <param name="input">生成された入力。失敗時のメッセージに出す。</param>
        public static void AssertSame(string law, in Zdd left, in Zdd right, object? input = null)
        {
            if (left.Equals(right))
            {
                return;
            }

            Assert.Fail(Describe(law, left, right, input));
        }

        /// <summary>
        /// マネージャが違う 2 つの族が同じ集合たちを表すことを確かめる。ノード ID はマネージャごとに
        /// 別物なので、こちらは読み出した中身どうしを比べる。
        /// </summary>
        public static void AssertSameFamily(string law, in Zdd left, in Zdd right, object? input = null)
        {
            if (ToMasks(left).SequenceEqual(ToMasks(right)))
            {
                return;
            }

            Assert.Fail(Describe(law, left, right, input));
        }

        private static string Describe(string law, in Zdd left, in Zdd right, object? input)
        {
            int[] leftMasks = ToMasks(left);
            int[] rightMasks = ToMasks(right);

            StringBuilder text = new StringBuilder();
            text.Append(law).AppendLine(" does not hold.");

            if (input is not null)
            {
                text.Append("  input : ").AppendLine(input.ToString());
            }

            text.Append("  left  : ").Append(leftMasks.Length).Append(" set(s) ")
                .AppendLine(FamilySpec.Format(leftMasks));
            text.Append("  right : ").Append(rightMasks.Length).Append(" set(s) ")
                .AppendLine(FamilySpec.Format(rightMasks));
            AppendDifference(text, "only left ", leftMasks, rightMasks);
            AppendDifference(text, "only right", rightMasks, leftMasks);

            return text.ToString();
        }

        private static void AppendDifference(StringBuilder text, string label, int[] masks, int[] other)
        {
            int[] only = masks.Where(mask => !other.Contains(mask)).ToArray();
            text.Append("  ").Append(label).Append(": ").AppendLine(FamilySpec.Format(only));
        }
    }
}
