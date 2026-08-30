using System;
using System.Linq;
using Xunit;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Harness
{
    /// <summary>
    /// 素朴な族 ↔ ZDD の往復（<see cref="ZddFamilies"/>）の検証。
    /// </summary>
    /// <remarks>
    /// 照合はこの往復の上に載るので、往復そのものが恒等でなければ何を比べても意味がない。
    /// </remarks>
    public class ZddFamiliesTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void EveryFamilyOfAFewVariablesSurvivesTheRoundTrip(int variableCount)
        {
            using ZddManager manager = new ZddManager(variableCount);

            foreach (BruteForceFamily family in FamilyCases.AllFamilies(variableCount))
            {
                Zdd zdd = ZddFamilies.Build(manager, family);

                Assert.Equal(family, ZddFamilies.ToBruteForce(zdd));

                // 正準形なので、同じ族を作り直せば同じハンドルになる。
                Assert.Equal(zdd, ZddFamilies.Build(manager, family));
            }
        }

        [Theory]
        [InlineData(4)]
        [InlineData(10)]
        [InlineData(FamilyCases.ExhaustiveVariableLimit)]
        public void RandomFamiliesSurviveTheRoundTrip(int variableCount)
        {
            using ZddManager manager = new ZddManager(variableCount);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(variableCount, 30, seed: 20260830))
            {
                Zdd zdd = ZddFamilies.Build(manager, family);

                Assert.Equal(family, ZddFamilies.ToBruteForce(zdd));
                Assert.Equal(family.Count, ZddFamilies.ToBruteForce(zdd).Count);
            }
        }

        [Fact]
        public void TheTerminalFamiliesBuildIntoTheTerminals()
        {
            const int VariableCount = 5;
            using ZddManager manager = new ZddManager(VariableCount);

            Assert.Equal(manager.Empty, ZddFamilies.Build(manager, BruteForceFamily.Empty(VariableCount)));
            Assert.Equal(manager.Base, ZddFamilies.Build(manager, BruteForceFamily.Base(VariableCount)));

            for (int item = 0; item < VariableCount; item++)
            {
                Assert.Equal(
                    manager.Singleton(item),
                    ZddFamilies.Build(manager, BruteForceFamily.Singleton(VariableCount, item)));
            }
        }

        [Fact]
        public void TheShorthandBuilderTakesSetsDirectly()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd zdd = ZddFamilies.Build(manager, [0, 2], [1], []);

            Assert.Equal(BruteForceFamily.FromSets(4, [0, 2], [1], []), ZddFamilies.ToBruteForce(zdd));
        }

        [Fact]
        public void SharedSubFamiliesBecomeSharedNodes()
        {
            // 冪集合は「どの item も自由」なので、n 変数でノードはちょうど n 個。
            // 組み立てが部分族を共有していなければ、ここで指数個のノードができる。
            const int VariableCount = 12;
            using ZddManager manager = new ZddManager(VariableCount);

            Zdd powerSet = ZddFamilies.Build(manager, BruteForceFamily.PowerSet(VariableCount));

            Assert.Equal((long)VariableCount, powerSet.NodeCount);
            Assert.Equal(1 << VariableCount, ZddFamilies.ToBruteForce(powerSet).Count);
        }

        [Fact]
        public void TheSupportIsTheSetOfItemsThatActuallyOccur()
        {
            using ZddManager manager = new ZddManager(6);

            Zdd zdd = ZddFamilies.Build(manager, [0, 3], [3, 5]);

            Assert.Equal(new[] { 0, 3, 5 }, zdd.Support().OrderBy(item => item));
        }

        [Fact]
        public void AFamilyOfADifferentSizeIsRejected()
        {
            using ZddManager manager = new ZddManager(3);

            ArgumentException error = Assert.Throws<ArgumentException>(
                () => ZddFamilies.Build(manager, BruteForceFamily.Base(4)));

            Assert.Equal("family", error.ParamName);
        }

        [Fact]
        public void ADefaultHandleCannotBeEnumerated()
        {
            Zdd none = default;

            Assert.Throws<InvalidOperationException>(() => ZddFamilies.ToBruteForce(none));
        }

        [Fact]
        public void ADiagramWithTooManyVariablesCannotBeEnumerated()
        {
            using ZddManager manager = new ZddManager(BruteForceFamily.MaxVariableCount + 1);

            ArgumentException error = Assert.Throws<ArgumentException>(
                () => ZddFamilies.ToBruteForce(manager.Base));

            Assert.Equal("zdd", error.ParamName);
        }
    }
}
