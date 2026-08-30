using Xunit;
using Xunit.Abstractions;
using ZDD.Net.Tests.Properties.Harness;

namespace ZDD.Net.Tests.Properties.Properties
{
    /// <summary>
    /// 極大・極小・ヒッティング集合（<c>Maximal</c> / <c>Minimal</c> / <c>HittingSets</c>）の法則。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>HittingSets</c> は<b>極小とは限らない</b>横断すべて、すなわち
    /// <c>H(f) = { a ⊆ U : ∀ b ∈ f, a ∩ b ≠ ∅ }</c> を返す。よって二重適用は元の族には戻らず、
    /// <c>H(H(f))</c> は f の<b>上方閉包</b>（f のどれかを含む集合すべて）になる。
    /// 上方閉包は <c>2^U.Restrict(f)</c> と書ける。
    /// </para>
    /// <para>
    /// 証明の筋: a が f のどれかを含むなら、どの横断も a と交わる。逆に a が f のどれも含まないなら、
    /// U \ a は f の横断なのに a と交わらないので、a は H(H(f)) に入らない。
    /// </para>
    /// </remarks>
    public class ExtremalProperties
    {
        private readonly ITestOutputHelper _output;

        public ExtremalProperties(ITestOutputHelper output) => _output = output;

        // ---- 極大・極小 ----

        [Fact]
        public void MinimalIsIdempotent() =>
            FamilyLaw.Single("f.Minimal().Minimal() == f.Minimal()", (manager, f) => (f.Minimal().Minimal(), f.Minimal()), _output);

        [Fact]
        public void MaximalIsIdempotent() =>
            FamilyLaw.Single("f.Maximal().Maximal() == f.Maximal()", (manager, f) => (f.Maximal().Maximal(), f.Maximal()), _output);

        [Fact]
        public void MinimalKeepsPartOfTheFamily() =>
            FamilyLaw.Single("f.Minimal() <= f", (manager, f) => (f.Minimal() | f, f), _output);

        [Fact]
        public void MaximalKeepsPartOfTheFamily() =>
            FamilyLaw.Single("f.Maximal() <= f", (manager, f) => (f.Maximal() | f, f), _output);

        [Fact]
        public void MaximalSetsFormAnAntichain() =>
            FamilyLaw.Single(
                "f.Maximal().Minimal() == f.Maximal()",
                (manager, f) => (f.Maximal().Minimal(), f.Maximal()),
                _output);

        [Fact]
        public void MinimalSetsFormAnAntichain() =>
            FamilyLaw.Single(
                "f.Minimal().Maximal() == f.Minimal()",
                (manager, f) => (f.Minimal().Maximal(), f.Minimal()),
                _output);

        [Fact]
        public void TheUpwardClosureHasTheSameMinimalSets() =>
            FamilyLaw.Single(
                "2^U.Restrict(f).Minimal() == f.Minimal()",
                (manager, f) => (ZddSets.PowerSet(manager).Restrict(f).Minimal(), f.Minimal()),
                _output);

        [Fact]
        public void TheDownwardClosureHasTheSameMaximalSets() =>
            FamilyLaw.Single(
                "2^U.Permit(f).Maximal() == f.Maximal()",
                (manager, f) => (ZddSets.PowerSet(manager).Permit(f).Maximal(), f.Maximal()),
                _output);

        // ---- ヒッティング集合 ----

        [Fact]
        public void HittingSetsAppliedTwiceGiveTheUpwardClosure() =>
            FamilyLaw.Single(
                "f.HittingSets().HittingSets() == 2^U.Restrict(f)",
                (manager, f) => (f.HittingSets().HittingSets(), ZddSets.PowerSet(manager).Restrict(f)),
                _output);

        [Fact]
        public void HittingSetsOnlyDependOnTheMinimalSets() =>
            FamilyLaw.Single(
                "f.HittingSets() == f.Minimal().HittingSets()",
                (manager, f) => (f.HittingSets(), f.Minimal().HittingSets()),
                _output);

        [Fact]
        public void HittingSetsAreUpwardClosed() =>
            FamilyLaw.Single(
                "2^U.Restrict(f.HittingSets()) == f.HittingSets()",
                (manager, f) => (ZddSets.PowerSet(manager).Restrict(f.HittingSets()), f.HittingSets()),
                _output);

        [Fact]
        public void HittingSetsOfAUnionAreTheIntersectionOfBoth() =>
            FamilyLaw.Pair(
                "(f | g).HittingSets() == f.HittingSets() & g.HittingSets()",
                (manager, f, g) => ((f | g).HittingSets(), f.HittingSets() & g.HittingSets()),
                _output);

        [Fact]
        public void NothingHitsAFamilyThatContainsTheEmptySet() =>
            FamilyLaw.Single(
                "(f | {∅}).HittingSets() == ∅",
                (manager, f) => ((f | manager.Base).HittingSets(), manager.Empty),
                _output);

        [Fact]
        public void EverythingHitsTheEmptyFamily() =>
            FamilyLaw.Single(
                "∅.HittingSets() == 2^U",
                (manager, f) => (manager.Empty.HittingSets(), ZddSets.PowerSet(manager)),
                _output);
    }
}
