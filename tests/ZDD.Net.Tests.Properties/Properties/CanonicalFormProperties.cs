using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;
using ZDD.Net.Core;
using ZDD.Net.Tests.Properties.Harness;

namespace ZDD.Net.Tests.Properties.Properties
{
    /// <summary>
    /// 正準性と、実装の都合（キャッシュの大きさ・表の初期容量）からの独立性。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>正準性</b>: ZDD は既約かつ共有された形なので、同じ族はどう組み立てても同じノードになる。
    /// 手順を変えて作った族どうしを <see cref="Zdd.Equals(Zdd)"/>（＝ノード ID の一致）で比べれば、
    /// 削減規則か一意化表の綻びがそのまま見える。
    /// </para>
    /// <para>
    /// <b>キャッシュ非依存性</b>: 演算キャッシュは速さのためのもので、答えを変えてはならない。
    /// キャッシュを切った・小さくして追い出しを起こした・大きく取った、のどれでも結果は同じはず。
    /// ノード表と一意化表の初期容量も同様（倍化のたびに壊れていないか）。
    /// </para>
    /// </remarks>
    public class CanonicalFormProperties
    {
        private readonly ITestOutputHelper _output;

        public CanonicalFormProperties(ITestOutputHelper output) => _output = output;

        /// <summary>キャッシュと表の大きさだけを変えた設定。どれで計算しても答えは同じでなければならない。</summary>
        public static IEnumerable<ZddManagerOptions> Layouts()
        {
            yield return new ZddManagerOptions();
            yield return new ZddManagerOptions { MaxCacheCapacity = 0 };
            yield return new ZddManagerOptions { InitialCacheCapacity = 0, MaxCacheCapacity = 16 };
            yield return new ZddManagerOptions
            {
                InitialNodeCapacity = 1,
                InitialUniqueTableCapacity = 1,
                InitialCacheCapacity = 1,
            };
            yield return new ZddManagerOptions { InitialCacheCapacity = 4096 };
        }

        [Fact]
        public void BuildingTheSameFamilyInThreeWaysGivesTheSameHandle() =>
            PropertyCheck.Sample(
                FamilyGen.Family,
                spec =>
                {
                    using ZddManager manager = new ZddManager(spec.VariableCount);

                    Zdd bySingletons = spec.Build(manager);
                    Zdd byChange = spec.BuildByChange(manager);
                    Zdd byFlip = spec.BuildByFlip(manager);

                    ZddSets.AssertSame("Build == BuildByChange", bySingletons, byChange, spec);
                    ZddSets.AssertSame("Build == BuildByFlip", bySingletons, byFlip, spec);
                },
                _output);

        [Fact]
        public void TheUnionOfTwoFamiliesIsTheFamilyOfTheUnionOfTheirSets() =>
            PropertyCheck.Sample(
                FamilyGen.Pair,
                input =>
                {
                    using ZddManager manager = new ZddManager(input.First.VariableCount);

                    Zdd computed = input.First.Build(manager) | input.Second.Build(manager);
                    Zdd assembled = input.First.UnionOfMasks(input.Second).Build(manager);

                    ZddSets.AssertSame("f | g == Build(masks(f) ∪ masks(g))", computed, assembled, input);
                },
                _output);

        [Fact]
        public void OperationOrderDoesNotChangeTheHandle() =>
            PropertyCheck.Sample(
                FamilyGen.Triple,
                input =>
                {
                    using ZddManager manager = new ZddManager(input.First.VariableCount);

                    Zdd f = input.First.Build(manager);
                    Zdd g = input.Second.Build(manager);
                    Zdd h = input.Third.Build(manager);

                    // 同じ族に至る 3 通りの道筋。正準形なら 3 つとも同じノード ID になる。
                    Zdd direct = (f | g) | h;
                    Zdd reordered = h | (g | f);
                    Zdd viaDeMorgan = ~(~f & ~g & ~h);

                    ZddSets.AssertSame("(f | g) | h == h | (g | f)", direct, reordered, input);
                    ZddSets.AssertSame("(f | g) | h == ~(~f & ~g & ~h)", direct, viaDeMorgan, input);
                },
                _output);

        [Fact]
        public void TheCacheAndTableSizesDoNotChangeTheResult() =>
            PropertyCheck.Sample(
                FamilyGen.Triple,
                input =>
                {
                    using ZddManager reference = new ZddManager(input.First.VariableCount);
                    Zdd[] expected = RunEveryOperation(reference, input);

                    foreach (ZddManagerOptions options in Layouts())
                    {
                        using ZddManager manager = new ZddManager(input.First.VariableCount, options);
                        Zdd[] actual = RunEveryOperation(manager, input);

                        for (int i = 0; i < expected.Length; i++)
                        {
                            string law =
                                $"operation #{i} does not depend on the cache " +
                                $"(max {options.MaxCacheCapacity}, initial {options.InitialCacheCapacity})";

                            ZddSets.AssertSameFamily(law, expected[i], actual[i], input);

                            // 族が同じなら、正準形である以上ノードの個数まで同じでなければならない。
                            Assert.True(
                                expected[i].NodeCount == actual[i].NodeCount,
                                $"{law}: {expected[i].NodeCount} node(s) vs {actual[i].NodeCount}.");
                        }
                    }
                },
                _output);

        /// <summary>全演算を 1 通り回して、結果を並べて返す。</summary>
        private static Zdd[] RunEveryOperation(ZddManager manager, FamilyTriple input)
        {
            Zdd f = input.First.Build(manager);
            Zdd g = input.Second.Build(manager);
            Zdd h = input.Third.Build(manager);
            int item = 0;

            return new[]
            {
                f | g,
                f & g,
                f - g,
                f ^ g,
                f * g,
                f / g,
                f % g,
                f.Meet(g),
                f.Restrict(g),
                f.Permit(g),
                f.NonSubsetsOf(g),
                f.NonSupersetsOf(g),
                f.Minimal(),
                f.Maximal(),
                f.HittingSets(),
                ~f,
                f.Change(item),
                f.OnSet(item),
                f.OffSet(item),
                (f | g) * h,
                (f * g).Minimal(),
                (f | g).HittingSets().Maximal(),
            };
        }
    }
}
