using System;
using System.Numerics;
using Xunit;
using Xunit.Abstractions;
using ZDD.Net.Core;
using ZDD.Net.Tests.Properties.Harness;

namespace ZDD.Net.Tests.Properties.Properties
{
    /// <summary>
    /// ボトムアップ評価（<c>Count</c> / <c>CountApprox</c> / <c>CountBySize</c>）が満たすべき性質。
    /// </summary>
    /// <remarks>
    /// 数え上げは「答が合っているか目で見て分からない」典型なので、生成した族の集合数という
    /// <b>外から分かる答</b>との一致に加えて、包除原理や補元のように<b>数え方に依らない等式</b>を
    /// 突き合わせる。素朴実装との総当たり照合（M1-12 の単体テスト）とは別方向の網になる。
    /// </remarks>
    public class CountingProperties
    {
        private readonly ITestOutputHelper _output;

        public CountingProperties(ITestOutputHelper output) => _output = output;

        [Fact]
        public void CountEqualsTheNumberOfGeneratedSets() =>
            PropertyCheck.Sample(
                FamilyGen.Family,
                spec =>
                {
                    using ZddManager manager = new ZddManager(spec.VariableCount);
                    Zdd family = spec.Build(manager);

                    // 生成器は重複を正規化してから渡してくるので、集合の個数がそのまま濃度。
                    Assert.Equal(new BigInteger(spec.Count), family.Count);
                    Assert.Equal((double)spec.Count, family.CountApprox);
                },
                _output);

        [Fact]
        public void CountObeysInclusionExclusion() =>
            PropertyCheck.Sample(
                FamilyGen.Pair,
                input =>
                {
                    using ZddManager manager = new ZddManager(input.First.VariableCount);
                    Zdd f = input.First.Build(manager);
                    Zdd g = input.Second.Build(manager);

                    // |F ∪ G| + |F ∩ G| == |F| + |G|
                    Assert.Equal(f.Count + g.Count, (f | g).Count + (f & g).Count);
                },
                _output);

        [Fact]
        public void ComplementSplitsThePowerSet() =>
            PropertyCheck.Sample(
                FamilyGen.Family,
                spec =>
                {
                    using ZddManager manager = new ZddManager(spec.VariableCount);
                    Zdd family = spec.Build(manager);

                    // 補は冪集合をちょうど 2 つに分ける。
                    Assert.Equal(BigInteger.Pow(2, spec.VariableCount), family.Count + (~family).Count);
                    Assert.Equal(BigInteger.Pow(2, spec.VariableCount), ZddSets.PowerSet(manager).Count);
                },
                _output);

        [Fact]
        public void AnItemSplitsTheCountIntoTwo() =>
            PropertyCheck.Sample(
                FamilyGen.FamilyAndItem,
                input =>
                {
                    using ZddManager manager = new ZddManager(input.Family.VariableCount);
                    Zdd family = input.Family.Build(manager);
                    int item = input.Item;

                    // どの集合も「item を含む」か「含まない」かのどちらか一方。
                    Assert.Equal(family.Count, family.OnSet(item).Count + family.OffSet(item).Count);
                },
                _output);

        [Fact]
        public void TheSizeDistributionSumsToTheCount() =>
            PropertyCheck.Sample(
                FamilyGen.Family,
                spec =>
                {
                    using ZddManager manager = new ZddManager(spec.VariableCount);
                    Zdd family = spec.Build(manager);

                    BigInteger[] bySize = family.CountBySize();

                    BigInteger total = BigInteger.Zero;
                    foreach (BigInteger count in bySize)
                    {
                        total += count;
                    }

                    Assert.Equal(family.Count, total);

                    // 分布の長さは「最大の集合の要素数 + 1」。空の族だけが長さ 0 になる。
                    int largest = -1;
                    foreach (int mask in spec.Masks)
                    {
                        largest = Math.Max(largest, BitOperations.PopCount((uint)mask));
                    }

                    Assert.Equal(largest + 1, bySize.Length);
                },
                _output);

        [Fact]
        public void SievesSplitTheCountInTwo() =>
            PropertyCheck.Sample(
                FamilyGen.Pair,
                input =>
                {
                    using ZddManager manager = new ZddManager(input.First.VariableCount);
                    Zdd f = input.First.Build(manager);
                    Zdd g = input.Second.Build(manager);

                    // ふるいとその否定版は f をちょうど 2 つに分ける（docs/PLAN.md §5.2）。
                    Assert.Equal(f.Count, f.SupersetsOf(g).Count + f.NonSupersetsOf(g).Count);
                    Assert.Equal(f.Count, f.SubsetsOf(g).Count + f.NonSubsetsOf(g).Count);

                    // 差と交わりも同じく 2 分割。
                    Assert.Equal(f.Count, (f - g).Count + (f & g).Count);
                },
                _output);

    }
}
