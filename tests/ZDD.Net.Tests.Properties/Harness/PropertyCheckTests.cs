using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CsCheck;
using Xunit;
using Xunit.Abstractions;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Properties.Harness
{
    /// <summary>
    /// プロパティを走らせる仕掛けそのものの検証。
    /// </summary>
    /// <remarks>
    /// プロパティテストは「落ちなければ何も分からない」道具なので、道具の側を確かめておく。
    /// ここで見るのは 3 つ: 種を固定すると同じ入力列が出ること、失敗した回の種がログに出て
    /// その種で再現できること、そして<b>シュリンクが効く</b>こと
    /// （わざと壊した実装で、反例が小さくなること）。
    /// </remarks>
    public class PropertyCheckTests
    {
        /// <summary>失敗の報告から種を拾うための形（CsCheck が出す <c>Set seed: "…"</c>）。</summary>
        private static readonly Regex SeedPattern = new Regex("Set seed: \"([^\"]+)\"", RegexOptions.CultureInvariant);

        private readonly ITestOutputHelper _output;

        public PropertyCheckTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void TheSameSeedReplaysTheSameInputSequence()
        {
            const string Seed = "zdd-net-0001";

            List<string> first = new List<string>();
            List<string> second = new List<string>();

            PropertyCheck.Sample(FamilyGen.Pair, input => first.Add(input.ToString()!), seed: Seed, iterations: 25);
            PropertyCheck.Sample(FamilyGen.Pair, input => second.Add(input.ToString()!), seed: Seed, iterations: 25);

            Assert.Equal(25, first.Count);
            Assert.Equal(first, second);

            // 種が違えば別の列になる（＝上の一致が「常に同じ 1 件」なだけではない）。
            List<string> other = new List<string>();
            PropertyCheck.Sample(FamilyGen.Pair, input => other.Add(input.ToString()!), seed: "zdd-net-0002", iterations: 25);

            Assert.NotEqual(first, other);
        }

        [Fact]
        public void EveryPropertyGetsItsOwnStableSeed()
        {
            Assert.Equal(PropertyCheck.SeedFor("SomeProperty"), PropertyCheck.SeedFor("SomeProperty"));
            Assert.NotEqual(PropertyCheck.SeedFor("SomeProperty"), PropertyCheck.SeedFor("SomeOtherProperty"));

            // 作った種はそのまま CsCheck に渡せる形でなければ意味がない。
            _ = PropertyCheck.ParseSeed(PropertyCheck.SeedFor("SomeProperty"));
        }

        [Fact]
        public void AMalformedSeedIsReportedAsSuch()
        {
            ArgumentException failure = Assert.Throws<ArgumentException>(
                () => PropertyCheck.Sample(FamilyGen.Family, _ => { }, seed: "nope"));

            Assert.Contains("is not a CsCheck seed", failure.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void TheIterationCountComesFromTheEnvironment()
        {
            Assert.Equal(PropertyCheck.DefaultIterations, PropertyCheck.ResolveIterations(null));
            Assert.Equal(PropertyCheck.DefaultIterations, PropertyCheck.ResolveIterations("   "));
            Assert.Equal(5000, PropertyCheck.ResolveIterations("5000"));
            Assert.Throws<ArgumentException>(() => PropertyCheck.ResolveIterations("many"));
            Assert.Throws<ArgumentException>(() => PropertyCheck.ResolveIterations("0"));
        }

        [Fact]
        public void TheSeedComesFromTheEnvironmentWhenItIsSet()
        {
            Assert.Equal(PropertyCheck.SeedFor("Property"), PropertyCheck.ResolveSeed(null, "Property"));
            Assert.Equal("zdd-net-0001", PropertyCheck.ResolveSeed("zdd-net-0001", "Property"));
        }

        [Fact]
        public void AFailureReportsASeedThatReplaysTheSameCase()
        {
            List<FamilyPair> failures = new List<FamilyPair>();
            RecordingOutput recorded = new RecordingOutput();

            CsCheckException failure = Assert.Throws<CsCheckException>(() =>
                PropertyCheck.Sample(
                    FamilyGen.Pair,
                    input => AssertBrokenDifferenceRebuildsTheFamily(input, failures),
                    recorded,
                    seed: "zdd-net-0001"));

            Assert.NotEmpty(failures);

            // 失敗した回の種はテスト出力に出ている（CI のログから手元で再生できる）。
            Assert.Contains("failed on iteration", recorded.Text, StringComparison.Ordinal);
            Assert.Contains("Set seed:", recorded.Text, StringComparison.Ordinal);

            // 報告された種で、報告された反例がそのまま出る。
            string seed = SeedPattern.Match(failure.Message).Groups[1].Value;
            Assert.NotEmpty(seed);

            List<FamilyPair> replayed = new List<FamilyPair>();
            FamilyGen.Pair.Sample(input => replayed.Add(input), iter: 1, threads: 1, seed: seed);

            Assert.Equal(failures[^1].ToString(), replayed[0].ToString());
        }

        [Fact]
        public void ShrinkingMakesTheCounterexampleSmaller()
        {
            List<FamilyPair> failures = new List<FamilyPair>();

            Assert.Throws<CsCheckException>(() =>
                PropertyCheck.Sample(
                    FamilyGen.Pair,
                    input => AssertBrokenDifferenceRebuildsTheFamily(input, failures),
                    _output,
                    seed: "zdd-net-0001"));

            FamilyPair first = failures[0];
            FamilyPair smallest = failures[^1];

            _output.WriteLine($"first counterexample  : {first}");
            _output.WriteLine($"smallest counterexample: {smallest}");

            int firstSize = first.First.Count + first.Second.Count;
            int smallestSize = smallest.First.Count + smallest.Second.Count;

            // 縮まなければ、変数 6 個・集合 16 個ぶんの反例を人間が読む羽目になる。
            // 実際には「変数 1 個、f = {{0}}, g = {∅}」あたりまで落ちる。
            Assert.True(
                smallestSize < firstSize,
                $"shrinking did not reduce the counterexample: {first} -> {smallest}");
            Assert.True(
                smallest.First.VariableCount <= 2,
                $"the counterexample still has {smallest.First.VariableCount} variable(s): {smallest}");
            Assert.True(
                smallestSize <= 4,
                $"the counterexample still has {smallestSize} set(s): {smallest}");
        }

        /// <summary>
        /// <b>わざと壊した</b> 差の実装で「(f - g) | (f &amp; g) == f」を検査する。
        /// </summary>
        /// <remarks>
        /// 差を <see cref="Zdd.NonSupersetsOf"/> で書くと、g のどれかを含む集合まで落ちてしまう。
        /// 法則は f が g の要素の真の上位集合を含むときに破れる。シュリンクの効きを見るための的。
        /// </remarks>
        private static void AssertBrokenDifferenceRebuildsTheFamily(FamilyPair input, List<FamilyPair> failures)
        {
            using ZddManager manager = new ZddManager(input.First.VariableCount);

            Zdd f = input.First.Build(manager);
            Zdd g = input.Second.Build(manager);
            Zdd brokenDifference = f.NonSupersetsOf(g);

            if (!(brokenDifference | (f & g)).Equals(f))
            {
                failures.Add(input);
                throw new InvalidOperationException($"(f - g) | (f & g) != f for {input}");
            }
        }
    }
}
