using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Specs;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Specs
{
    /// <summary>
    /// M4-7 completion criteria for <see cref="DfaSpec"/>: matches brute-force simulation of the DFA over
    /// every input string (a "no run of 3+ consecutive 1s" automaton, and several random small DFAs) across
    /// several lengths, dead-state pruning changes the number of temporary nodes built but not the result,
    /// boundary cases (empty accept set, initial state itself accepting) build the expected family, and
    /// <see cref="DfaSpec.GetChild"/> does not allocate.
    /// </summary>
    public class DfaSpecTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(10)]
        [InlineData(15)]
        public void NoTripleOnesMatchesBruteForceSimulation(int length)
        {
            (int[,] transitions, int initialState, int[] acceptStates) = NoTripleOnesDfa();
            using ZddManager manager = new ZddManager(length);

            Zdd built = FrontierBuilder.Build<DfaSpec, int>(
                manager, new DfaSpec(transitions, initialState, acceptStates, length));

            BruteForceFamily expected = BruteForceDfaAcceptance(transitions, initialState, acceptStates, length);

            FamilyAssert.AssertSameFamily($"length={length}", built, expected);
        }

        [Theory]
        [InlineData(1, 5)]
        [InlineData(2, 8)]
        [InlineData(3, 6)]
        [InlineData(4, 10)]
        [InlineData(5, 12)]
        public void RandomDfaMatchesBruteForceSimulation(int seed, int length)
        {
            (int[,] transitions, int initialState, int[] acceptStates) = RandomDfa(stateCount: 4, seed);
            using ZddManager manager = new ZddManager(length);

            Zdd built = FrontierBuilder.Build<DfaSpec, int>(
                manager, new DfaSpec(transitions, initialState, acceptStates, length));

            BruteForceFamily expected = BruteForceDfaAcceptance(transitions, initialState, acceptStates, length);

            FamilyAssert.AssertSameFamily($"seed={seed} length={length}", built, expected);
        }

        [Fact]
        public void PruningReducesTemporaryNodeCountButNotTheResult()
        {
            (int[,] transitions, int initialState, int[] acceptStates) = NoTripleOnesDfa();
            const int length = 24;

            using ZddManager prunedManager = new ZddManager(length);
            using ZddManager unprunedManager = new ZddManager(length);

            var prunedProgress = new CapturingProgress();
            var unprunedProgress = new CapturingProgress();

            Zdd pruned = FrontierBuilder.Build<DfaSpec, int>(
                prunedManager,
                new DfaSpec(transitions, initialState, acceptStates, length, pruneDeadStates: true),
                new BuildOptions { Progress = prunedProgress });

            Zdd unpruned = FrontierBuilder.Build<DfaSpec, int>(
                unprunedManager,
                new DfaSpec(transitions, initialState, acceptStates, length, pruneDeadStates: false),
                new BuildOptions { Progress = unprunedProgress });

            Assert.Equal(pruned.Count, unpruned.Count);
            FamilyAssert.AssertSameFamily(
                null, pruned, BruteForceDfaAcceptance(transitions, initialState, acceptStates, length));

            Assert.True(
                unprunedProgress.LastNodeCount > prunedProgress.LastNodeCount,
                $"expected pruning to build fewer temporary nodes: pruned={prunedProgress.LastNodeCount}, " +
                $"unpruned={unprunedProgress.LastNodeCount}");
        }

        [Fact]
        public void EmptyAcceptStatesBuildsEmpty()
        {
            var transitions = new int[,] { { 0, 0 } };
            using ZddManager manager = new ZddManager(5);

            Zdd built = FrontierBuilder.Build<DfaSpec, int>(
                manager, new DfaSpec(transitions, 0, Array.Empty<int>(), 5));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void ZeroLengthWithAcceptingInitialStateIsBase()
        {
            var transitions = new int[,] { { 0, 0 } };
            using ZddManager manager = new ZddManager(0);

            Zdd built = FrontierBuilder.Build<DfaSpec, int>(
                manager, new DfaSpec(transitions, 0, new[] { 0 }, 0));

            Assert.Equal(manager.Base, built);
        }

        [Fact]
        public void ZeroLengthWithNonAcceptingInitialStateIsEmpty()
        {
            var transitions = new int[,] { { 0, 0 } };
            using ZddManager manager = new ZddManager(0);

            Zdd built = FrontierBuilder.Build<DfaSpec, int>(
                manager, new DfaSpec(transitions, 0, Array.Empty<int>(), 0));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void InitialStateItselfDeadBuildsEmptyRegardlessOfPruning()
        {
            // State 1 is a trap with no accept state at all: dead from the moment the DFA starts.
            var transitions = new int[,] { { 1, 1 }, { 1, 1 } };
            var acceptStates = new[] { 0 };

            foreach (bool pruneDeadStates in new[] { true, false })
            {
                using ZddManager manager = new ZddManager(4);
                Zdd built = FrontierBuilder.Build<DfaSpec, int>(
                    manager, new DfaSpec(transitions, 1, acceptStates, 4, pruneDeadStates));

                Assert.Equal(manager.Empty, built);
            }
        }

        [Fact]
        public void ConstructorRejectsNullTransitions()
        {
            Assert.Throws<ArgumentNullException>(() => new DfaSpec(null!, 0, new[] { 0 }, 3));
        }

        [Fact]
        public void ConstructorRejectsNullAcceptStates()
        {
            var transitions = new int[,] { { 0, 0 } };
            Assert.Throws<ArgumentNullException>(() => new DfaSpec(transitions, 0, null!, 3));
        }

        [Fact]
        public void ConstructorRejectsWrongColumnCount()
        {
            var transitions = new int[,] { { 0, 0, 0 } };
            Assert.Throws<ArgumentException>(() => new DfaSpec(transitions, 0, new[] { 0 }, 3));
        }

        [Fact]
        public void ConstructorRejectsEmptyTransitions()
        {
            var transitions = new int[0, 2];
            Assert.Throws<ArgumentException>(() => new DfaSpec(transitions, 0, Array.Empty<int>(), 3));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(2)]
        public void ConstructorRejectsOutOfRangeInitialState(int initialState)
        {
            var transitions = new int[,] { { 0, 0 }, { 1, 1 } };
            Assert.Throws<ArgumentOutOfRangeException>(() => new DfaSpec(transitions, initialState, new[] { 0 }, 3));
        }

        [Fact]
        public void ConstructorRejectsOutOfRangeAcceptState()
        {
            var transitions = new int[,] { { 0, 0 }, { 1, 1 } };
            Assert.Throws<ArgumentOutOfRangeException>(() => new DfaSpec(transitions, 0, new[] { 2 }, 3));
        }

        [Fact]
        public void ConstructorRejectsOutOfRangeTransitionTarget()
        {
            var transitions = new int[,] { { 0, 2 } };
            Assert.Throws<ArgumentException>(() => new DfaSpec(transitions, 0, new[] { 0 }, 3));
        }

        [Fact]
        public void ConstructorRejectsNegativeLength()
        {
            var transitions = new int[,] { { 0, 0 } };
            Assert.Throws<ArgumentOutOfRangeException>(() => new DfaSpec(transitions, 0, new[] { 0 }, -1));
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            (int[,] transitions, int initialState, int[] acceptStates) = NoTripleOnesDfa();
            var spec = new DfaSpec(transitions, initialState, acceptStates, 30);
            int state = 0;
            int rootLevel = spec.GetRoot(ref state);

            RunOneSymbolPerLevel(spec, ref state, rootLevel);
            state = 0;
            spec.GetRoot(ref state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneSymbolPerLevel(spec, ref state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneSymbolPerLevel(DfaSpec spec, ref int state, int level)
            {
                while (level > 0)
                {
                    level = spec.GetChild(ref state, level, 0);
                    if (DdResult.IsTerminal(level))
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// "No run of 3 or more consecutive 1s": states 0/1/2 count the current run of 1s, state 3 is the
        /// trap once a run reaches 3 — reachable, but (being non-accepting with no way out) dead from the
        /// moment it is entered, which is exactly what exercises <see cref="DfaSpec"/>'s dead-state pruning.
        /// </summary>
        private static (int[,] Transitions, int InitialState, int[] AcceptStates) NoTripleOnesDfa()
        {
            var transitions = new int[,]
            {
                { 0, 1 }, // state 0 (run of 0): '0' -> 0, '1' -> 1
                { 0, 2 }, // state 1 (run of 1): '0' -> 0, '1' -> 2
                { 0, 3 }, // state 2 (run of 2): '0' -> 0, '1' -> 3 (trap)
                { 3, 3 }, // state 3 (trap): stays forever
            };

            return (transitions, 0, new[] { 0, 1, 2 });
        }

        private static (int[,] Transitions, int InitialState, int[] AcceptStates) RandomDfa(int stateCount, int seed)
        {
            var random = new Random(seed);
            var transitions = new int[stateCount, 2];
            for (int s = 0; s < stateCount; s++)
            {
                transitions[s, 0] = random.Next(stateCount);
                transitions[s, 1] = random.Next(stateCount);
            }

            int initialState = random.Next(stateCount);
            int[] acceptStates = Enumerable.Range(0, stateCount).Where(_ => random.NextDouble() < 0.4).ToArray();

            return (transitions, initialState, acceptStates);
        }

        /// <summary>Simulates the DFA independently on every length-<paramref name="length"/> binary string.</summary>
        private static BruteForceFamily BruteForceDfaAcceptance(
            int[,] transitions, int initialState, int[] acceptStates, int length)
        {
            if (length >= 31)
            {
                throw new ArgumentException(
                    $"BruteForceDfaAcceptance enumerates all 2^length strings and cannot handle length {length}.",
                    nameof(length));
            }

            int stateCount = transitions.GetLength(0);
            var accept = new bool[stateCount];
            foreach (int state in acceptStates)
            {
                accept[state] = true;
            }

            var accepted = new List<int>();
            int bound = 1 << length;

            for (int mask = 0; mask < bound; mask++)
            {
                int state = initialState;
                for (int i = 0; i < length; i++)
                {
                    int symbol = (mask >> i) & 1;
                    state = transitions[state, symbol];
                }

                if (accept[state])
                {
                    accepted.Add(mask);
                }
            }

            return BruteForceFamily.FromMasks(length, accepted);
        }

        private sealed class CapturingProgress : IProgress<BuildProgress>
        {
            public long LastNodeCount { get; private set; }

            public void Report(BuildProgress value) => LastNodeCount = value.NodeCount;
        }
    }
}
