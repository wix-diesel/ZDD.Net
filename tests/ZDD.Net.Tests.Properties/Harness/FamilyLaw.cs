using System;
using System.Runtime.CompilerServices;
using Xunit.Abstractions;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Properties.Harness
{
    /// <summary>
    /// 「左辺と右辺が同じ族になる」形の法則を、ランダムな族で確かめる。
    /// </summary>
    /// <remarks>
    /// 法則ごとに要るのは「入力から左辺と右辺をどう作るか」だけなので、マネージャの用意と
    /// 後始末、族の組み立て、失敗時の報告はここにまとめる。<c>property</c> は既定で
    /// 呼び出し元のテストメソッド名になり、そこから種が決まる（<see cref="PropertyCheck.SeedFor"/>）。
    /// </remarks>
    internal static class FamilyLaw
    {
        /// <summary>族 1 つについての法則。</summary>
        public static void Single(
            string law,
            Func<ZddManager, Zdd, (Zdd Left, Zdd Right)> sides,
            ITestOutputHelper? output = null,
            [CallerMemberName] string property = "")
        {
            ArgumentNullException.ThrowIfNull(sides);

            PropertyCheck.Sample(
                FamilyGen.Family,
                spec =>
                {
                    using ZddManager manager = new ZddManager(spec.VariableCount);
                    (Zdd left, Zdd right) = sides(manager, spec.Build(manager));
                    ZddSets.AssertSame(law, left, right, spec);
                },
                output,
                property: property);
        }

        /// <summary>同じ宇宙の族 2 つについての法則。</summary>
        public static void Pair(
            string law,
            Func<ZddManager, Zdd, Zdd, (Zdd Left, Zdd Right)> sides,
            ITestOutputHelper? output = null,
            [CallerMemberName] string property = "")
        {
            ArgumentNullException.ThrowIfNull(sides);

            PropertyCheck.Sample(
                FamilyGen.Pair,
                input =>
                {
                    using ZddManager manager = new ZddManager(input.First.VariableCount);
                    (Zdd left, Zdd right) = sides(manager, input.First.Build(manager), input.Second.Build(manager));
                    ZddSets.AssertSame(law, left, right, input);
                },
                output,
                property: property);
        }

        /// <summary>同じ宇宙の族 3 つについての法則。</summary>
        public static void Triple(
            string law,
            Func<ZddManager, Zdd, Zdd, Zdd, (Zdd Left, Zdd Right)> sides,
            ITestOutputHelper? output = null,
            [CallerMemberName] string property = "")
        {
            ArgumentNullException.ThrowIfNull(sides);

            PropertyCheck.Sample(
                FamilyGen.Triple,
                input =>
                {
                    using ZddManager manager = new ZddManager(input.First.VariableCount);
                    (Zdd left, Zdd right) = sides(
                        manager,
                        input.First.Build(manager),
                        input.Second.Build(manager),
                        input.Third.Build(manager));
                    ZddSets.AssertSame(law, left, right, input);
                },
                output,
                property: property);
        }

        /// <summary>族 1 つと item 1 つについての法則（単項演算用）。</summary>
        public static void WithItem(
            string law,
            Func<ZddManager, Zdd, int, (Zdd Left, Zdd Right)> sides,
            ITestOutputHelper? output = null,
            [CallerMemberName] string property = "")
        {
            ArgumentNullException.ThrowIfNull(sides);

            PropertyCheck.Sample(
                FamilyGen.FamilyAndItem,
                input =>
                {
                    using ZddManager manager = new ZddManager(input.Family.VariableCount);
                    (Zdd left, Zdd right) = sides(manager, input.Family.Build(manager), input.Item);
                    ZddSets.AssertSame(law, left, right, input);
                },
                output,
                property: property);
        }
    }
}
