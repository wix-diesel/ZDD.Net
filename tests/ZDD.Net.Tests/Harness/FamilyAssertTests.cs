using System;
using System.Linq;
using Xunit;
using Xunit.Sdk;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Harness
{
    /// <summary>
    /// <see cref="FamilyAssert"/> 自身の検証。
    /// </summary>
    /// <remarks>
    /// 通るときに通ることより、<b>落ちたときに何が書いてあるか</b>のほうが大事なので、
    /// 意図的に食い違わせて、メッセージが差分の中身を示すことを確かめる。
    /// </remarks>
    public class FamilyAssertTests
    {
        [Fact]
        public void AMatchingFamilyPasses()
        {
            using ZddManager manager = new ZddManager(4);

            BruteForceFamily family = BruteForceFamily.FromSets(4, [], [0], [1, 2]);

            FamilyAssert.AssertSameFamily(ZddFamilies.Build(manager, family), family);
            FamilyAssert.AssertSameFamily("Union", ZddFamilies.Build(manager, family), family);
        }

        [Fact]
        public void AMismatchNamesTheMissingAndTheUnexpectedSets()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd actual = ZddFamilies.Build(manager, [], [0]);
            BruteForceFamily expected = BruteForceFamily.FromSets(4, [], [1, 2]);

            XunitException error = Assert.Throws<FailException>(
                () => FamilyAssert.AssertSameFamily("Change(0)", actual, expected));

            Assert.Contains("[Change(0)]", error.Message, StringComparison.Ordinal);
            Assert.Contains("expected : 2 set(s) {∅, {1, 2}}", error.Message, StringComparison.Ordinal);
            Assert.Contains("actual   : 2 set(s) {∅, {0}}", error.Message, StringComparison.Ordinal);
            Assert.Contains("missing (1): {1, 2}", error.Message, StringComparison.Ordinal);
            Assert.Contains("unexpected (1): {0}", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AMismatchWithoutAContextStillReadsAsASentence()
        {
            using ZddManager manager = new ZddManager(3);

            XunitException error = Assert.Throws<FailException>(
                () => FamilyAssert.AssertSameFamily(manager.Base, BruteForceFamily.Empty(3)));

            Assert.StartsWith("the family does not match", error.Message, StringComparison.Ordinal);
            Assert.Contains("missing (0): none", error.Message, StringComparison.Ordinal);
            Assert.Contains("unexpected (1): ∅", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void TheInputFamilyIsShownWhenItIsGiven()
        {
            using ZddManager manager = new ZddManager(3);

            BruteForceFamily source = BruteForceFamily.FromSets(3, [0], [1]);

            XunitException error = Assert.Throws<FailException>(
                () => FamilyAssert.AssertSameFamily(
                    "OnSet(0)",
                    manager.Base,
                    BruteForceFamily.Empty(3),
                    source));

            Assert.Contains("input    : 2 set(s) {{0}, {1}}", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ALongDiffIsCutOffWithACount()
        {
            BruteForceFamily expected = BruteForceFamily.PowerSet(4);
            BruteForceFamily actual = BruteForceFamily.Empty(4);

            string message = FamilyAssert.DescribeMismatch(null, expected, actual);

            Assert.Contains(
                $"missing (16): {string.Join(", ", expected.Masks.Take(FamilyAssert.MaxReportedSets).Select(BruteForceFamily.FormatSet))}",
                message,
                StringComparison.Ordinal);
            Assert.Contains($"… (+{16 - FamilyAssert.MaxReportedSets} more)", message, StringComparison.Ordinal);
            Assert.Contains("expected : 16 set(s)", message, StringComparison.Ordinal);
        }

        [Fact]
        public void TwoNaiveFamiliesCanBeComparedTheSameWay()
        {
            BruteForceFamily left = BruteForceFamily.FromSets(3, [0]);
            BruteForceFamily right = BruteForceFamily.FromSets(3, [1]);

            FamilyAssert.AssertSameFamily("same", left, left);

            XunitException error = Assert.Throws<FailException>(
                () => FamilyAssert.AssertSameFamily("Meet", left, right));

            Assert.Contains("missing (1): {1}", error.Message, StringComparison.Ordinal);
            Assert.Contains("unexpected (1): {0}", error.Message, StringComparison.Ordinal);
        }
    }
}
