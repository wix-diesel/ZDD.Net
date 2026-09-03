using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Specs;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Frontier
{
    /// <summary>
    /// The bottom-up reduction and its wiring into <see cref="FrontierBuilder"/> (M2-4).
    /// </summary>
    /// <remarks>
    /// This is where the two frontier passes and Core finally connect end to end: write a spec,
    /// call <c>FrontierBuilder.Build</c>, get back a <see cref="Zdd"/> that every M1 operation
    /// works on. <see cref="FamilyAssert.AssertSameFamily(in Zdd, BruteForceFamily)"/> checks both
    /// that the accepted family matches (correctness) and that the handle equals what
    /// <see cref="ZddFamilies.Build"/> would produce from the same family via
    /// <see cref="ZddManager.CreateNode"/> alone (canonicity) — a mismatch there means reduction
    /// rule A or B, not the family itself, is broken.
    /// </remarks>
    public class FrontierBuilderTests
    {
        /// <summary>
        /// The minimal spec the issue's completion criteria name explicitly: no state, so every
        /// subset is accepted and every level merges down to width 1 — the power set.
        /// </summary>
        private readonly struct PowerSetSpec : IDdSpec<int>
        {
            private readonly int _itemCount;

            public PowerSetSpec(int itemCount) => _itemCount = itemCount;

            public int GetRoot(ref int state)
            {
                state = 0;
                return _itemCount == 0 ? DdResult.True : _itemCount;
            }

            public int GetChild(ref int state, int level, int value) =>
                level == 1 ? DdResult.True : level - 1;

            public bool StateEquals(in int left, in int right) => true;

            public int StateHashCode(in int state) => 0;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(5)]
        [InlineData(8)]
        public void PowerSetSpecBuildsThePowerSetWithCardinality2ToTheN(int itemCount)
        {
            using ZddManager manager = new ZddManager(itemCount);

            Zdd built = FrontierBuilder.Build<PowerSetSpec, int>(manager, new PowerSetSpec(itemCount));

            Assert.Equal(BigInteger.Pow(2, itemCount), built.Count);
            FamilyAssert.AssertSameFamily(built, BruteForceFamily.PowerSet(itemCount));
        }

        /// <summary>
        /// A spec whose family is exactly as the top-down pass alone (<see cref="SpecWalker"/>)
        /// says, built through a manager that already holds unrelated nodes — the general
        /// correctness + canonicity check, not tied to the power set's trivial structure.
        /// </summary>
        [Theory]
        [InlineData(6, 0)]
        [InlineData(6, 2)]
        [InlineData(6, 4)]
        [InlineData(6, 6)]
        [InlineData(7, 8)]
        public void BuildProducesExactlyWhatTheSpecAcceptsAndItIsCanonical(int itemCount, int k)
        {
            using ZddManager manager = new ZddManager(itemCount);
            ExactlyKSpec spec = new ExactlyKSpec(itemCount, k);

            Zdd built = FrontierBuilder.Build<ExactlyKSpec, int>(manager, spec);

            var accepted = SpecWalker.Accepted<ExactlyKSpec, int>(spec, itemCount);
            FamilyAssert.AssertSameFamily(built, BruteForceFamily.FromSets(itemCount, accepted.ToArray()));
        }

        /// <summary>A single item that <c>GetChild</c> always excludes: its <c>Hi</c> branch is bottom.</summary>
        private readonly struct NeverIncludedSpec : IDdSpec<int>
        {
            public int GetRoot(ref int state)
            {
                state = 0;
                return 1;
            }

            public int GetChild(ref int state, int level, int value) =>
                value == 1 ? DdResult.False : DdResult.True;

            public bool StateEquals(in int left, in int right) => true;

            public int StateHashCode(in int state) => 0;
        }

        /// <summary>Rule A: a node with <c>Hi == &#8869;</c> is replaced by its <c>Lo</c> child, so no node survives it.</summary>
        [Fact]
        public void ReductionRuleADropsTheNodeWhoseHiBranchIsBottom()
        {
            using ZddManager manager = new ZddManager(1);

            Zdd built = FrontierBuilder.Build<NeverIncludedSpec, int>(manager, new NeverIncludedSpec());

            Assert.Equal(0, built.NodeCount);
            Assert.Equal(manager.Base, built);
        }

        /// <summary>
        /// A state that tags which branch was last taken but never lets that tag affect a later
        /// decision. Top-down merging (which goes by <c>StateEquals</c>) keeps the two tags apart,
        /// so every level ends up twice as wide as it needs to be — the family is still the power
        /// set, and its two nodes per level are structurally identical once translated to Core ids.
        /// </summary>
        private readonly struct TaggedPowerSetSpec : IDdSpec<int>
        {
            private readonly int _itemCount;

            public TaggedPowerSetSpec(int itemCount) => _itemCount = itemCount;

            public int GetRoot(ref int tag)
            {
                tag = 0;
                return _itemCount;
            }

            public int GetChild(ref int tag, int level, int value)
            {
                tag = value;
                return level == 1 ? DdResult.True : level - 1;
            }

            // Distinguishes the tag on purpose, so top-down merging never collapses the two states
            // that are, from here on, actually interchangeable — leaving rule B to do it instead.
            public bool StateEquals(in int left, in int right) => left == right;

            public int StateHashCode(in int state) => state;
        }

        /// <summary>Rule B: nodes with the same <c>(Level, Lo, Hi)</c> triple share one Core node.</summary>
        [Fact]
        public void ReductionRuleBSharesNodesThatTopDownMergingLeftDuplicated()
        {
            const int ItemCount = 6;
            TaggedPowerSetSpec spec = new TaggedPowerSetSpec(ItemCount);

            TemporaryNodeTable unreduced = TopDownExpander<TaggedPowerSetSpec, int>.Expand(spec);

            using ZddManager manager = new ZddManager(ItemCount);
            Zdd built = FrontierBuilder.Build<TaggedPowerSetSpec, int>(manager, spec);

            // Un-merged, every level but the root holds both tags: about twice as many temporary
            // nodes as the power set actually needs (one per level).
            Assert.True(unreduced.NodeCount > ItemCount);

            // The reduction collapses that duplication down to the canonical power set shape.
            Assert.Equal(ItemCount, built.NodeCount);
            Assert.Equal(BigInteger.Pow(2, ItemCount), built.Count);
            FamilyAssert.AssertSameFamily(built, BruteForceFamily.PowerSet(ItemCount));
        }

        [Theory]
        [InlineData(DdResult.False)]
        [InlineData(DdResult.True)]
        public void ATerminalRootBuildsTheMatchingTrivialFamily(int rootResult)
        {
            using ZddManager manager = new ZddManager(3);

            Zdd built = FrontierBuilder.Build<FixedRootSpec, int>(manager, new FixedRootSpec(rootResult));

            Assert.Equal(rootResult == DdResult.True, built.IsBase);
            Assert.Equal(rootResult == DdResult.False, built.IsEmpty);
        }

        /// <summary>Every M1 capability keeps working on a family that came from a frontier build.</summary>
        [Fact]
        public void EveryM1CapabilityWorksOnABuiltFamily()
        {
            const int ItemCount = 6;
            const int K = 3;
            ExactlyKSpec spec = new ExactlyKSpec(ItemCount, K);

            using ZddManager manager = new ZddManager(ItemCount);
            Zdd built = FrontierBuilder.Build<ExactlyKSpec, int>(manager, spec);

            BigInteger expectedCount = Binomial(ItemCount, K);
            Assert.Equal(expectedCount, built.Count);

            int enumerated = 0;
            foreach (int[] set in built.Sets())
            {
                Assert.Equal(K, set.Length);
                enumerated++;
            }

            Assert.Equal((int)expectedCount, enumerated);

            int[] first = built.ElementAt(BigInteger.Zero);
            Assert.True(built.Contains(first));
            Assert.Equal(BigInteger.Zero, built.IndexOf(first));

            int[] sampled = built.Sample(new Random(12345));
            Assert.True(built.Contains(sampled));

            int[] weights = new int[ItemCount];
            for (int item = 0; item < ItemCount; item++)
            {
                weights[item] = item;
            }

            WeightedSet<int> best = built.MaxWeight(weights);
            Assert.True(built.Contains(best.Items));
        }

        /// <summary>A state that keeps one slot per pair, used only via <see cref="Span{T}"/> (<see cref="IArrayDdSpec"/>).</summary>
        private readonly struct AtMostOnePerPairSpec : IArrayDdSpec
        {
            private readonly int _pairCount;

            public AtMostOnePerPairSpec(int pairCount) => _pairCount = pairCount;

            public int ArrayLength => _pairCount;

            public int GetRoot(Span<int> state)
            {
                state.Clear();
                return _pairCount == 0 ? DdResult.True : 2 * _pairCount;
            }

            public int GetChild(Span<int> state, int level, int value)
            {
                int item = (2 * _pairCount) - level;
                int pair = item / 2;

                if (value == 1)
                {
                    if (state[pair] != 0)
                    {
                        return DdResult.False;
                    }

                    state[pair] = 1;
                }

                return level == 1 ? DdResult.True : level - 1;
            }
        }

        [Fact]
        public void TheArraySpecOverloadBuildsAndReducesCorrectly()
        {
            const int PairCount = 3;
            const int ItemCount = 2 * PairCount;
            AtMostOnePerPairSpec spec = new AtMostOnePerPairSpec(PairCount);

            using ZddManager manager = new ZddManager(ItemCount);
            Zdd built = FrontierBuilder.Build(manager, spec);

            // Every subset that never picks both members of a pair: 3 choices (neither, first, second) per pair.
            Assert.Equal(BigInteger.Pow(3, PairCount), built.Count);

            foreach (int[] set in built.Sets())
            {
                for (int pair = 0; pair < PairCount; pair++)
                {
                    bool hasFirst = Array.IndexOf(set, 2 * pair) >= 0;
                    bool hasSecond = Array.IndexOf(set, (2 * pair) + 1) >= 0;
                    Assert.False(hasFirst && hasSecond);
                }
            }
        }

        /// <summary>Building the same family repeatedly must not grow the manager's node table past what the family needs.</summary>
        /// <remarks>
        /// The temporary tables of each build (level state tables and the unreduced node table)
        /// go out of scope once <c>Build</c> returns, so nothing but the reduced, shared nodes
        /// survives a build; repeating it should keep finding the same Core nodes rather than
        /// piling up new ones.
        /// </remarks>
        [Fact]
        public void BuildingTheSameSpecRepeatedlyDoesNotGrowTheNodeTable()
        {
            const int ItemCount = 8;
            using ZddManager manager = new ZddManager(ItemCount);
            ExactlyKSpec spec = new ExactlyKSpec(ItemCount, 4);

            Zdd first = FrontierBuilder.Build<ExactlyKSpec, int>(manager, spec);
            long afterFirst = manager.NodeCount;

            for (int i = 0; i < 200; i++)
            {
                Zdd repeated = FrontierBuilder.Build<ExactlyKSpec, int>(manager, spec);
                Assert.Equal(first, repeated);
            }

            Assert.Equal(afterFirst, manager.NodeCount);
        }

        [Fact]
        public void BuildThrowsWhenTheManagerIsNull()
        {
            Assert.Throws<ArgumentNullException>(
                () => FrontierBuilder.Build<PowerSetSpec, int>(null!, new PowerSetSpec(3)));
        }

        /// <summary>A spec whose root level ignores the manager entirely, always returning a fixed level.</summary>
        private readonly struct FixedLevelSpec : IDdSpec<int>
        {
            private readonly int _level;

            public FixedLevelSpec(int level) => _level = level;

            public int GetRoot(ref int state)
            {
                state = 0;
                return _level;
            }

            public int GetChild(ref int state, int level, int value) =>
                level == 1 ? DdResult.True : level - 1;

            public bool StateEquals(in int left, in int right) => true;

            public int StateHashCode(in int state) => 0;
        }

        /// <summary>
        /// A spec's levels are decided independently of the manager it will be built into; a root
        /// level above <see cref="ZddManager.VariableCount"/> must be rejected up front rather than
        /// producing a handle that later fails deep inside an unrelated operation.
        /// </summary>
        [Fact]
        public void BuildThrowsWhenTheSpecsRootLevelExceedsTheManagersVariableCount()
        {
            using ZddManager manager = new ZddManager(3);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => FrontierBuilder.Build<FixedLevelSpec, int>(manager, new FixedLevelSpec(5)));

            Assert.Contains("5", error.Message, StringComparison.Ordinal);
            Assert.Contains("3", error.Message, StringComparison.Ordinal);
        }

        /// <summary>The array-spec overload guards the same way as the struct-state one.</summary>
        private readonly struct FixedLevelArraySpec : IArrayDdSpec
        {
            private readonly int _level;

            public FixedLevelArraySpec(int level) => _level = level;

            public int ArrayLength => 1;

            public int GetRoot(Span<int> state)
            {
                state.Clear();
                return _level;
            }

            public int GetChild(Span<int> state, int level, int value) =>
                level == 1 ? DdResult.True : level - 1;
        }

        [Fact]
        public void TheArrayOverloadAlsoThrowsWhenTheRootLevelExceedsTheManagersVariableCount()
        {
            using ZddManager manager = new ZddManager(2);

            Assert.Throws<InvalidOperationException>(
                () => FrontierBuilder.Build(manager, new FixedLevelArraySpec(4)));
        }

        /// <summary>An array spec that reports a negative <see cref="IArrayDdSpec.ArrayLength"/>.</summary>
        private readonly struct NegativeArrayLengthSpec : IArrayDdSpec
        {
            public int ArrayLength => -1;

            public int GetRoot(Span<int> state) => DdResult.True;

            public int GetChild(Span<int> state, int level, int value) => DdResult.True;
        }

        [Fact]
        public void TheArrayOverloadRejectsANegativeArrayLength()
        {
            using ZddManager manager = new ZddManager(1);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => FrontierBuilder.Build(manager, new NegativeArrayLengthSpec()));

            Assert.Contains("ArrayLength", error.Message, StringComparison.Ordinal);
        }

        /// <summary>An array spec with no slots at all, whose root nonetheless asks for a real level.</summary>
        private readonly struct ZeroArrayLengthNonTerminalSpec : IArrayDdSpec
        {
            public int ArrayLength => 0;

            public int GetRoot(Span<int> state) => 1;

            public int GetChild(Span<int> state, int level, int value) => DdResult.True;
        }

        [Fact]
        public void TheArrayOverloadRejectsAZeroArrayLengthWithANonTerminalRoot()
        {
            using ZddManager manager = new ZddManager(1);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => FrontierBuilder.Build(manager, new ZeroArrayLengthNonTerminalSpec()));

            Assert.Contains("ArrayLength", error.Message, StringComparison.Ordinal);
        }

        // ---- State recording (M5-4, issue #56) ----

        [Fact]
        public void RecordStatesDefaultsToFalseAndStateLabelsComesBackEmpty()
        {
            using ZddManager manager = new ZddManager(5);
            BuildOptions options = new BuildOptions();

            FrontierBuilder.Build<CardinalitySpec, int>(
                manager, new CardinalitySpec(5, 1, 3), options, out IReadOnlyDictionary<int, string> stateLabels);

            Assert.False(options.RecordStates);
            Assert.Empty(stateLabels);
        }

        /// <summary>
        /// 記録無効時の空辞書は複数回の呼び出しで共有される 1 個のインスタンスだが、
        /// <see cref="IDictionary{TKey, TValue}"/> へダウンキャストしても書き換えられない
        /// ——読み取り専用ラッパーであって、単に読み取り専用インタフェース越しに見せているだけではない。
        /// </summary>
        [Fact]
        public void TheSharedEmptyStateLabelsCannotBeMutatedThroughADowncast()
        {
            using ZddManager manager = new ZddManager(3);
            BuildOptions options = new BuildOptions();

            FrontierBuilder.Build<CardinalitySpec, int>(
                manager, new CardinalitySpec(3, 0, 3), options, out IReadOnlyDictionary<int, string> stateLabels);

            Assert.Throws<NotSupportedException>(() => ((IDictionary<int, string>)stateLabels)[0] = "x");
        }

        [Fact]
        public void RecordStatesLabelsEveryNodeWithTheStatesDefaultToString()
        {
            using ZddManager manager = new ZddManager(5);
            BuildOptions options = new BuildOptions { RecordStates = true };

            Zdd built = FrontierBuilder.Build<CardinalitySpec, int>(
                manager, new CardinalitySpec(5, 1, 3), options, out IReadOnlyDictionary<int, string> stateLabels);

            Assert.Equal((int)built.NodeCount, stateLabels.Count);

            // CardinalitySpec の状態はそのまま int (int.ToString()) なので、ラベルは "0".."3" のいずれか。
            foreach (string label in stateLabels.Values)
            {
                int count = int.Parse(label, System.Globalization.CultureInfo.InvariantCulture);
                Assert.InRange(count, 0, 3);
            }
        }

        [Fact]
        public void RecordStatesUsesTheSuppliedDescribeStateDelegateOverTheDefaultToString()
        {
            using ZddManager manager = new ZddManager(5);
            BuildOptions options = new BuildOptions { RecordStates = true };

            FrontierBuilder.Build<CardinalitySpec, int>(
                manager,
                new CardinalitySpec(5, 1, 3),
                options,
                out IReadOnlyDictionary<int, string> stateLabels,
                describeState: count => $"taken={count}");

            Assert.NotEmpty(stateLabels);
            Assert.All(stateLabels.Values, label => Assert.StartsWith("taken=", label, StringComparison.Ordinal));
        }

        [Fact]
        public void RecordingStatesDoesNotChangeTheBuiltFamily()
        {
            using ZddManager manager = new ZddManager(6);
            CardinalitySpec spec = new CardinalitySpec(6, 2, 4);

            Zdd withoutRecording = FrontierBuilder.Build<CardinalitySpec, int>(manager, spec);

            Zdd withRecording = FrontierBuilder.Build<CardinalitySpec, int>(
                manager, spec, new BuildOptions { RecordStates = true }, out IReadOnlyDictionary<int, string> _);

            // 別々の呼び出しでも、正準化により同じマネージャ内では同じノード ID に落ちる。
            Assert.Equal(withoutRecording, withRecording);
        }

        [Fact]
        public void TheStateLabelOverloadRejectsANullManager()
        {
            Assert.Throws<ArgumentNullException>(
                () => FrontierBuilder.Build<CardinalitySpec, int>(
                    null!, new CardinalitySpec(3, 0, 3), new BuildOptions(), out IReadOnlyDictionary<int, string> _));
        }

        [Fact]
        public void TheStateLabelOverloadRejectsNullOptions()
        {
            using ZddManager manager = new ZddManager(3);

            Assert.Throws<ArgumentNullException>(
                () => FrontierBuilder.Build<CardinalitySpec, int>(
                    manager, new CardinalitySpec(3, 0, 3), null!, out IReadOnlyDictionary<int, string> _));
        }

        private static BigInteger Binomial(int n, int k)
        {
            if (k < 0 || k > n)
            {
                return BigInteger.Zero;
            }

            BigInteger result = BigInteger.One;
            for (int i = 0; i < k; i++)
            {
                result = result * (n - i) / (i + 1);
            }

            return result;
        }
    }
}
