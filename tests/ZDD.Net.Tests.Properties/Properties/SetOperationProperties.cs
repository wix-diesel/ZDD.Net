using Xunit;
using Xunit.Abstractions;
using ZDD.Net.Tests.Properties.Harness;

namespace ZDD.Net.Tests.Properties.Properties
{
    /// <summary>
    /// 集合演算（<c>Union</c> / <c>Intersect</c> / <c>Difference</c> /
    /// <c>SymmetricDifference</c> / <c>Complement</c>）が満たすべき代数法則。
    /// </summary>
    /// <remarks>
    /// 族の集まりは集合演算について<b>ブール束</b>を成す。ここで確かめるのはその公理そのもので、
    /// 素朴実装との照合（M1-6〜M1-10）とは独立に効く。たとえば「どちらも同じように間違えている」
    /// 場合は照合を通ってしまうが、分配則や補元の法則までは通らない。
    /// </remarks>
    public class SetOperationProperties
    {
        private readonly ITestOutputHelper _output;

        public SetOperationProperties(ITestOutputHelper output) => _output = output;

        // ---- 交換則 ----

        [Fact]
        public void UnionIsCommutative() =>
            FamilyLaw.Pair("f | g == g | f", (manager, f, g) => (f | g, g | f), _output);

        [Fact]
        public void IntersectIsCommutative() =>
            FamilyLaw.Pair("f & g == g & f", (manager, f, g) => (f & g, g & f), _output);

        [Fact]
        public void SymmetricDifferenceIsCommutative() =>
            FamilyLaw.Pair("f ^ g == g ^ f", (manager, f, g) => (f ^ g, g ^ f), _output);

        // ---- 結合則 ----

        [Fact]
        public void UnionIsAssociative() =>
            FamilyLaw.Triple("(f | g) | h == f | (g | h)", (manager, f, g, h) => ((f | g) | h, f | (g | h)), _output);

        [Fact]
        public void IntersectIsAssociative() =>
            FamilyLaw.Triple("(f & g) & h == f & (g & h)", (manager, f, g, h) => ((f & g) & h, f & (g & h)), _output);

        [Fact]
        public void SymmetricDifferenceIsAssociative() =>
            FamilyLaw.Triple("(f ^ g) ^ h == f ^ (g ^ h)", (manager, f, g, h) => ((f ^ g) ^ h, f ^ (g ^ h)), _output);

        // ---- 分配則 ----

        [Fact]
        public void UnionDistributesOverIntersect() =>
            FamilyLaw.Triple(
                "f | (g & h) == (f | g) & (f | h)",
                (manager, f, g, h) => (f | (g & h), (f | g) & (f | h)),
                _output);

        [Fact]
        public void IntersectDistributesOverUnion() =>
            FamilyLaw.Triple(
                "f & (g | h) == (f & g) | (f & h)",
                (manager, f, g, h) => (f & (g | h), (f & g) | (f & h)),
                _output);

        // ---- ド・モルガン則と補元 ----

        [Fact]
        public void ComplementTurnsUnionIntoIntersect() =>
            FamilyLaw.Pair("~(f | g) == ~f & ~g", (manager, f, g) => (~(f | g), ~f & ~g), _output);

        [Fact]
        public void ComplementTurnsIntersectIntoUnion() =>
            FamilyLaw.Pair("~(f & g) == ~f | ~g", (manager, f, g) => (~(f & g), ~f | ~g), _output);

        [Fact]
        public void ComplementIsAnInvolution() =>
            FamilyLaw.Single("~~f == f", (manager, f) => (~~f, f), _output);

        [Fact]
        public void AFamilyAndItsComplementPartitionThePowerSet() =>
            FamilyLaw.Single(
                "f | ~f == 2^U",
                (manager, f) => (f | ~f, ZddSets.PowerSet(manager)),
                _output);

        [Fact]
        public void AFamilyAndItsComplementDoNotOverlap() =>
            FamilyLaw.Single("f & ~f == empty", (manager, f) => (f & ~f, manager.Empty), _output);

        // ---- 吸収則・冪等則 ----

        [Fact]
        public void UnionAbsorbsIntersect() =>
            FamilyLaw.Pair("f | (f & g) == f", (manager, f, g) => (f | (f & g), f), _output);

        [Fact]
        public void IntersectAbsorbsUnion() =>
            FamilyLaw.Pair("f & (f | g) == f", (manager, f, g) => (f & (f | g), f), _output);

        [Fact]
        public void UnionIsIdempotent() =>
            FamilyLaw.Single("f | f == f", (manager, f) => (f | f, f), _output);

        [Fact]
        public void IntersectIsIdempotent() =>
            FamilyLaw.Single("f & f == f", (manager, f) => (f & f, f), _output);

        // ---- 差と対称差 ----

        [Fact]
        public void DifferenceIsIntersectionWithTheComplement() =>
            FamilyLaw.Pair("f - g == f & ~g", (manager, f, g) => (f - g, f & ~g), _output);

        [Fact]
        public void SymmetricDifferenceIsTheUnionOfBothDifferences() =>
            FamilyLaw.Pair("f ^ g == (f - g) | (g - f)", (manager, f, g) => (f ^ g, (f - g) | (g - f)), _output);

        [Fact]
        public void SymmetricDifferenceIsTheUnionWithoutTheIntersection() =>
            FamilyLaw.Pair("f ^ g == (f | g) - (f & g)", (manager, f, g) => (f ^ g, (f | g) - (f & g)), _output);

        [Fact]
        public void SymmetricDifferenceWithItselfIsEmpty() =>
            FamilyLaw.Single("f ^ f == empty", (manager, f) => (f ^ f, manager.Empty), _output);

        // ---- 単位元・零元 ----

        [Fact]
        public void TheEmptyFamilyIsTheUnitOfUnion() =>
            FamilyLaw.Single("f | empty == f", (manager, f) => (f | manager.Empty, f), _output);

        [Fact]
        public void ThePowerSetIsTheUnitOfIntersect() =>
            FamilyLaw.Single("f & 2^U == f", (manager, f) => (f & ZddSets.PowerSet(manager), f), _output);
    }
}
