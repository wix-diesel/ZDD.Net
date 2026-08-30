using Xunit;
using Xunit.Abstractions;
using ZDD.Net.Core;
using ZDD.Net.Tests.Properties.Harness;

namespace ZDD.Net.Tests.Properties.Properties
{
    /// <summary>
    /// 積・商・剰余（<c>Product</c> / <c>Quotient</c> / <c>Remainder</c>）が満たすべき法則。
    /// </summary>
    /// <remarks>
    /// 積は unate product（<c>f * g = { a ∪ b : a ∈ f, b ∈ g }</c>）で、単位元は <c>{∅}</c>、
    /// 零元は <c>∅</c>。商と剰余はその割り算にあたり、<c>f == f / g * g + f % g</c> が要となる。
    /// </remarks>
    public class FamilyAlgebraProperties
    {
        private readonly ITestOutputHelper _output;

        public FamilyAlgebraProperties(ITestOutputHelper output) => _output = output;

        // ---- 積 ----

        [Fact]
        public void ProductIsCommutative() =>
            FamilyLaw.Pair("f * g == g * f", (manager, f, g) => (f * g, g * f), _output);

        [Fact]
        public void ProductIsAssociative() =>
            FamilyLaw.Triple("(f * g) * h == f * (g * h)", (manager, f, g, h) => ((f * g) * h, f * (g * h)), _output);

        [Fact]
        public void ProductDistributesOverUnion() =>
            FamilyLaw.Triple(
                "f * (g | h) == (f * g) | (f * h)",
                (manager, f, g, h) => (f * (g | h), (f * g) | (f * h)),
                _output);

        [Fact]
        public void TheBaseFamilyIsTheUnitOfProduct() =>
            FamilyLaw.Single("f * {∅} == f", (manager, f) => (f * manager.Base, f), _output);

        [Fact]
        public void TheEmptyFamilyIsTheZeroOfProduct() =>
            FamilyLaw.Single("f * ∅ == ∅", (manager, f) => (f * manager.Empty, manager.Empty), _output);

        // ---- 商と剰余 ----

        [Fact]
        public void DivisionRebuildsTheDividend() =>
            FamilyLaw.Pair("f == f / g * g + f % g", (manager, f, g) => (f, (f / g * g) | (f % g)), _output);

        [Fact]
        public void TheRemainderIsPartOfTheDividend() =>
            FamilyLaw.Pair("f % g <= f", (manager, f, g) => ((f % g) | f, f), _output);

        [Fact]
        public void TheQuotientTimesTheDivisorIsPartOfTheDividend() =>
            FamilyLaw.Pair("f / g * g <= f", (manager, f, g) => ((f / g * g) | f, f), _output);

        [Fact]
        public void DividingByTheBaseFamilyChangesNothing() =>
            FamilyLaw.Single("f / {∅} == f", (manager, f) => (f / manager.Base, f), _output);

        [Fact]
        public void DividingByTheEmptyFamilyGivesThePowerSet() =>
            FamilyLaw.Single("f / ∅ == 2^U", (manager, f) => (f / manager.Empty, ZddSets.PowerSet(manager)), _output);

        [Fact]
        public void TheRemainderOfDividingByTheEmptyFamilyIsTheDividend() =>
            FamilyLaw.Single("f % ∅ == f", (manager, f) => (f % manager.Empty, f), _output);

        [Fact]
        public void EveryFamilyDividedByItselfContainsTheEmptySet()
        {
            // ∅ は「どの b ∈ f についても ∅ ∩ b = ∅ かつ ∅ ∪ b = b ∈ f」を満たすので、必ず商に入る。
            // f が空なら商は冪集合になり、そこにも ∅ は入っている。
            FamilyLaw.Single("{∅} <= f / f", (manager, f) => ((f / f) | manager.Base, f / f), _output);
        }
    }
}
