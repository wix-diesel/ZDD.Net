using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;
using Xunit.Abstractions;
using ZDD.Net.Core;
using ZDD.Net.Tests.Properties.Harness;

namespace ZDD.Net.Tests.Properties.Properties
{
    /// <summary>
    /// 順位づけ（<c>ElementAt</c> / <c>IndexOf</c>）とサンプリング（<c>Sample</c>）が満たすべき性質。
    /// </summary>
    /// <remarks>
    /// 順位づけは「列挙を全部辿らずに k 番目を出す近道」なので、性質はすべて<b>列挙との一致</b>に
    /// 帰着する。<c>ElementAt</c> と <c>IndexOf</c> は互いに逆で、<c>Sample</c> は
    /// <c>ElementAt</c> に一様乱数を食わせたものだから、この 3 つは 1 本の性質の言い換えでもある。
    /// </remarks>
    public class RankingProperties
    {
        private readonly ITestOutputHelper _output;

        public RankingProperties(ITestOutputHelper output) => _output = output;

        [Fact]
        public void UnrankingEveryIndexReproducesTheEnumeration() =>
            PropertyCheck.Sample(
                FamilyGen.Family,
                spec =>
                {
                    using ZddManager manager = new ZddManager(spec.VariableCount);
                    Zdd family = spec.Build(manager);

                    foreach (ZddEnumerationOrder order in Orders)
                    {
                        int[][] enumerated = family.Sets(order).ToArray();

                        Assert.Equal(
                            enumerated,
                            Enumerable.Range(0, enumerated.Length).Select(k => family.ElementAt(k, order)));

                        // 端の外には集合が無い。
                        Assert.Throws<ArgumentOutOfRangeException>(() => family.ElementAt(enumerated.Length, order));
                    }
                },
                _output);

        [Fact]
        public void RankingIsTheInverseOfUnranking() =>
            PropertyCheck.Sample(
                FamilyGen.Family,
                spec =>
                {
                    using ZddManager manager = new ZddManager(spec.VariableCount);
                    Zdd family = spec.Build(manager);

                    foreach (ZddEnumerationOrder order in Orders)
                    {
                        for (BigInteger k = BigInteger.Zero; k < family.Count; k++)
                        {
                            int[] set = family.ElementAt(k, order);

                            Assert.Equal(k, family.IndexOf(set, order));
                            Assert.True(family.Contains(set));
                        }
                    }
                },
                _output);

        [Fact]
        public void RankingAnswersMinusOneExactlyWhenTheFamilyDoesNotHoldTheSet() =>
            PropertyCheck.Sample(
                FamilyGen.Family,
                spec =>
                {
                    using ZddManager manager = new ZddManager(spec.VariableCount);
                    Zdd family = spec.Build(manager);

                    // 生成される宇宙は小さい（FamilyGen.MaxVariableCount）ので、全部分集合を試せる。
                    for (int mask = 0; mask < 1 << spec.VariableCount; mask++)
                    {
                        int[] set = FamilySpec.ItemsOf(mask).ToArray();

                        foreach (ZddEnumerationOrder order in Orders)
                        {
                            BigInteger index = family.IndexOf(set, order);

                            // 順位が付くかどうかは、メンバシップとぴったり同じ問いである。
                            Assert.Equal(family.Contains(set), index >= BigInteger.Zero);

                            if (index >= BigInteger.Zero)
                            {
                                Assert.True(index < family.Count);
                                Assert.Equal(set, family.ElementAt(index, order));
                            }
                            else
                            {
                                Assert.Equal(BigInteger.MinusOne, index);
                            }
                        }
                    }
                },
                _output);

        [Fact]
        public void SamplingOnlyEverReturnsSetsOfTheFamily() =>
            PropertyCheck.Sample(
                FamilyGen.Family,
                spec =>
                {
                    using ZddManager manager = new ZddManager(spec.VariableCount);
                    Zdd family = spec.Build(manager);

                    if (family.IsEmpty)
                    {
                        // 選べる集合が 1 つも無いなら、引くこと自体が誤り。
                        Assert.Throws<InvalidOperationException>(() => family.Sample(new Random(1)));
                        return;
                    }

                    int[][] sample = family.Sample(SampleSize, new Random(1));

                    Assert.Equal(SampleSize, sample.Length);
                    Assert.All(sample, set => Assert.True(family.Contains(set)));

                    // 同じ種なら同じ並び（族は不変で、引き方も決定的）。
                    Assert.Equal(
                        sample.Select(Key).ToArray(),
                        family.Sample(SampleSize, new Random(1)).Select(Key).ToArray());
                },
                _output);

        [Fact]
        public void SamplingReachesEverySetOfASmallFamily() =>
            PropertyCheck.Sample(
                FamilyGen.Family,
                spec =>
                {
                    using ZddManager manager = new ZddManager(spec.VariableCount);
                    Zdd family = spec.Build(manager);

                    if (family.IsEmpty)
                    {
                        return;
                    }

                    // 生成される族は高々 8 個（FamilyGen.MaxSetCount）。一様なら、1 つあたりの
                    // 期待度数は 200/8 = 25 以上あるので、取り逃す確率は事実上ゼロである。
                    // 「一様かどうか」ではなく「到達できない集合が無いか」を見る性質
                    // （偏りの検定は決定的な種で ZDD.Net.Tests の RankingTests が受け持つ）。
                    HashSet<string> seen = new HashSet<string>(
                        family.Sample(200, new Random(2)).Select(Key),
                        StringComparer.Ordinal);

                    Assert.Equal(
                        family.Sets().Select(Key).OrderBy(key => key, StringComparer.Ordinal).ToArray(),
                        seen.OrderBy(key => key, StringComparer.Ordinal).ToArray());
                },
                _output);

        /// <summary>1 つの族から引く個数。</summary>
        private const int SampleSize = 16;

        private static readonly ZddEnumerationOrder[] Orders =
        {
            ZddEnumerationOrder.Default,
            ZddEnumerationOrder.Lexicographic,
        };

        /// <summary>集合を並びごと比べられる文字列に直す。</summary>
        private static string Key(int[] set) => string.Join(",", set);
    }
}
