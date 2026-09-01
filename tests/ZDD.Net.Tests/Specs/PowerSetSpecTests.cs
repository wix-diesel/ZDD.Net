using System.Numerics;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Specs;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Specs
{
    /// <summary>M2-5 completion criterion for <see cref="PowerSetSpec"/>: <c>Count == 2^n</c>.</summary>
    public class PowerSetSpecTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        [InlineData(9)]
        [InlineData(14)]
        public void CountIsTwoToTheN(int itemCount)
        {
            using ZddManager manager = new ZddManager(itemCount);

            Zdd built = FrontierBuilder.Build<PowerSetSpec, byte>(manager, new PowerSetSpec(itemCount));

            Assert.Equal(BigInteger.Pow(2, itemCount), built.Count);
            FamilyAssert.AssertSameFamily(built, BruteForceFamily.PowerSet(itemCount));
        }
    }
}
