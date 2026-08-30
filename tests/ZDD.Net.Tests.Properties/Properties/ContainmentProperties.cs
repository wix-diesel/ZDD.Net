using Xunit;
using Xunit.Abstractions;
using ZDD.Net.Tests.Properties.Harness;

namespace ZDD.Net.Tests.Properties.Properties
{
    /// <summary>
    /// 包含系演算（<c>Meet</c> / <c>Restrict</c>（<c>SupersetsOf</c>）/ <c>Permit</c>
    /// （<c>SubsetsOf</c>）/ <c>NonSupersetsOf</c> / <c>NonSubsetsOf</c>）の法則。
    /// </summary>
    /// <remarks>
    /// 要は「<c>Restrict</c> と <c>NonSupersetsOf</c> は f を 2 つに割る」「<c>Permit</c> と
    /// <c>NonSubsetsOf</c> も同じく割る」の 2 つ。どちらか片方だけを間違えても、
    /// 和が f に戻らないか、共通部分が空にならないかで露見する。
    /// </remarks>
    public class ContainmentProperties
    {
        private readonly ITestOutputHelper _output;

        public ContainmentProperties(ITestOutputHelper output) => _output = output;

        // ---- Restrict / NonSupersetsOf の相補性 ----

        [Fact]
        public void RestrictAndNonSupersetsOfCoverTheWholeFamily() =>
            FamilyLaw.Pair(
                "f.Restrict(g) | f.NonSupersetsOf(g) == f",
                (manager, f, g) => (f.Restrict(g) | f.NonSupersetsOf(g), f),
                _output);

        [Fact]
        public void RestrictAndNonSupersetsOfDoNotOverlap() =>
            FamilyLaw.Pair(
                "f.Restrict(g) & f.NonSupersetsOf(g) == ∅",
                (manager, f, g) => (f.Restrict(g) & f.NonSupersetsOf(g), manager.Empty),
                _output);

        // ---- Permit / NonSubsetsOf の相補性 ----

        [Fact]
        public void PermitAndNonSubsetsOfCoverTheWholeFamily() =>
            FamilyLaw.Pair(
                "f.Permit(g) | f.NonSubsetsOf(g) == f",
                (manager, f, g) => (f.Permit(g) | f.NonSubsetsOf(g), f),
                _output);

        [Fact]
        public void PermitAndNonSubsetsOfDoNotOverlap() =>
            FamilyLaw.Pair(
                "f.Permit(g) & f.NonSubsetsOf(g) == ∅",
                (manager, f, g) => (f.Permit(g) & f.NonSubsetsOf(g), manager.Empty),
                _output);

        // ---- 絞り込みは何度掛けても同じ ----

        [Fact]
        public void RestrictIsIdempotent() =>
            FamilyLaw.Pair(
                "f.Restrict(g).Restrict(g) == f.Restrict(g)",
                (manager, f, g) => (f.Restrict(g).Restrict(g), f.Restrict(g)),
                _output);

        [Fact]
        public void PermitIsIdempotent() =>
            FamilyLaw.Pair(
                "f.Permit(g).Permit(g) == f.Permit(g)",
                (manager, f, g) => (f.Permit(g).Permit(g), f.Permit(g)),
                _output);

        // ---- 境界 ----

        [Fact]
        public void RestrictByTheBaseFamilyKeepsEverything() =>
            FamilyLaw.Single("f.Restrict({∅}) == f", (manager, f) => (f.Restrict(manager.Base), f), _output);

        [Fact]
        public void RestrictByTheEmptyFamilyKeepsNothing() =>
            FamilyLaw.Single("f.Restrict(∅) == ∅", (manager, f) => (f.Restrict(manager.Empty), manager.Empty), _output);

        [Fact]
        public void PermitByThePowerSetKeepsEverything() =>
            FamilyLaw.Single(
                "f.Permit(2^U) == f",
                (manager, f) => (f.Permit(ZddSets.PowerSet(manager)), f),
                _output);

        [Fact]
        public void EveryFamilyRestrictsItselfToItself() =>
            FamilyLaw.Single("f.Restrict(f) == f", (manager, f) => (f.Restrict(f), f), _output);

        // ---- Meet ----

        [Fact]
        public void MeetIsCommutative() =>
            FamilyLaw.Pair("f.Meet(g) == g.Meet(f)", (manager, f, g) => (f.Meet(g), g.Meet(f)), _output);

        [Fact]
        public void MeetIsAssociative() =>
            FamilyLaw.Triple(
                "f.Meet(g).Meet(h) == f.Meet(g.Meet(h))",
                (manager, f, g, h) => (f.Meet(g).Meet(h), f.Meet(g.Meet(h))),
                _output);

        [Fact]
        public void MeetDistributesOverUnion() =>
            FamilyLaw.Triple(
                "f.Meet(g | h) == f.Meet(g) | f.Meet(h)",
                (manager, f, g, h) => (f.Meet(g | h), f.Meet(g) | f.Meet(h)),
                _output);

        [Fact]
        public void MeetWithThePowerSetIsTheDownwardClosure() =>
            FamilyLaw.Single(
                "f.Meet(2^U) == 2^U.Permit(f)",
                (manager, f) => (f.Meet(ZddSets.PowerSet(manager)), ZddSets.PowerSet(manager).Permit(f)),
                _output);

        [Fact]
        public void MeetWithItselfContainsTheFamily() =>
            FamilyLaw.Single("f <= f.Meet(f)", (manager, f) => (f.Meet(f) | f, f.Meet(f)), _output);
    }
}
