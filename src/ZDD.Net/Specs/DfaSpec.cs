using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of length-<c>Length</c> binary strings a deterministic finite automaton accepts, read as
    /// sets: string position <c>i</c> is variable <c>i</c>, "included" means the bit at <c>i</c> is
    /// <c>1</c>. This is the general-purpose entry point PLAN.md &#167;7.2 calls out — any constraint a
    /// user can phrase as "read the variables in order, track a state, accept or reject at the end" becomes
    /// a <see cref="DfaSpec"/> by writing down its transition table, with no frontier reasoning of its own
    /// required.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>State</b>: the DFA's current state (an <c>int</c>) — nothing else. <see cref="GetChild"/> is
    /// exactly the transition function: <c>state' = Transitions[state, value]</c>.
    /// </para>
    /// <para>
    /// <b>Variable order</b>: input-symbol order. Position <c>0</c> is read first (decided at the root,
    /// the highest level), position <c>Length - 1</c> last, matching how a DFA reads its input left to right.
    /// </para>
    /// <para>
    /// <b>Dead-state pruning</b> (<see cref="PruneDeadStates"/>, on by default): the constructor
    /// precomputes, for every state, whether any accept state is reachable from it at all (a plain
    /// worklist search over the reverse transition graph, from the accept states backward — a state is
    /// live if it is an accept state or either transition out of it lands on a live state). Once
    /// <see cref="GetChild"/> lands on a dead state it returns <see cref="DdResult.False"/> immediately
    /// instead of continuing to expand a subtree that can only ever reject. The final family is identical
    /// either way — a subtree that can never accept always reduces away to nothing once the top-down
    /// expansion is fully reduced bottom-up — so the flag exists to trade that reduction for less work
    /// during the build (fewer temporary nodes: see <see cref="Frontier.BuildOptions.Progress"/>'s
    /// <see cref="Frontier.BuildProgress.NodeCount"/>), not to change what gets built. There is no separate
    /// handling for a state unreachable from <see cref="InitialState"/>: it is simply never visited by
    /// <see cref="GetChild"/>, dead or not.
    /// </para>
    /// </remarks>
    public readonly struct DfaSpec : IDdSpec<int>
    {
        private readonly int[,] _transitions;
        private readonly int _initialState;
        private readonly bool[] _acceptStates;
        private readonly bool[] _canReachAccept;
        private readonly int _length;
        private readonly bool _pruneDeadStates;

        /// <summary>Creates a spec for the length-<paramref name="length"/> binary strings a DFA accepts.</summary>
        /// <param name="transitions">
        /// The transition table, <c>transitions[state, symbol]</c> for <c>symbol</c> in <c>{0, 1}</c> —
        /// so its shape must be <c>[stateCount, 2]</c> with at least one state. Copied, so later mutating
        /// the array passed in has no effect on the spec.
        /// </param>
        /// <param name="initialState">The DFA's start state.</param>
        /// <param name="acceptStates">The accepting states. May be empty (then the family is empty).</param>
        /// <param name="length">The fixed string length; must be non-negative.</param>
        /// <param name="pruneDeadStates">
        /// Whether to prune subtrees rooted at a state that can never reach an accept state. Defaults to
        /// <see langword="true"/>; see the type remarks for what turning it off costs and does not change.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="transitions"/> or <paramref name="acceptStates"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="transitions"/> is not shaped <c>[stateCount, 2]</c> with <c>stateCount &gt;= 1</c>,
        /// or one of its entries is outside <c>0 .. stateCount - 1</c>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="initialState"/> or a value in <paramref name="acceptStates"/> is outside
        /// <c>0 .. stateCount - 1</c>, or <paramref name="length"/> is negative.
        /// </exception>
        public DfaSpec(int[,] transitions, int initialState, IEnumerable<int> acceptStates, int length, bool pruneDeadStates = true)
        {
            ArgumentNullException.ThrowIfNull(transitions);
            ArgumentNullException.ThrowIfNull(acceptStates);

            if (transitions.GetLength(1) != 2)
            {
                throw new ArgumentException(
                    $"Transitions must have exactly 2 columns (one per symbol), but has {transitions.GetLength(1)}.",
                    nameof(transitions));
            }

            int stateCount = transitions.GetLength(0);
            if (stateCount == 0)
            {
                throw new ArgumentException("Transitions must describe at least one state.", nameof(transitions));
            }

            if ((uint)initialState >= (uint)stateCount)
            {
                throw new ArgumentOutOfRangeException(nameof(initialState), initialState, $"Must be in 0 .. {stateCount - 1}.");
            }

            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length), length, "Must be non-negative.");
            }

            for (int s = 0; s < stateCount; s++)
            {
                for (int symbol = 0; symbol < 2; symbol++)
                {
                    int next = transitions[s, symbol];
                    if ((uint)next >= (uint)stateCount)
                    {
                        throw new ArgumentException(
                            $"transitions[{s}, {symbol}] = {next} is outside 0 .. {stateCount - 1}.",
                            nameof(transitions));
                    }
                }
            }

            var accept = new bool[stateCount];
            foreach (int state in acceptStates)
            {
                if ((uint)state >= (uint)stateCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(acceptStates), state, $"Must be in 0 .. {stateCount - 1}.");
                }

                accept[state] = true;
            }

            _transitions = (int[,])transitions.Clone();
            _initialState = initialState;
            _acceptStates = accept;
            _length = length;
            _pruneDeadStates = pruneDeadStates;
            _canReachAccept = ComputeCanReachAccept(_transitions, accept, stateCount);
        }

        /// <summary>The number of DFA states.</summary>
        public int StateCount => _transitions.GetLength(0);

        /// <summary>The DFA's start state.</summary>
        public int InitialState => _initialState;

        /// <summary>The fixed length of the binary strings this spec accepts or rejects.</summary>
        public int Length => _length;

        /// <summary>Whether unreachable-to-accept subtrees are pruned during the build.</summary>
        public bool PruneDeadStates => _pruneDeadStates;

        /// <summary>Whether <paramref name="state"/> is one of the DFA's accept states.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="state"/> is outside <c>0 .. StateCount - 1</c>.</exception>
        public bool IsAccepting(int state) => _acceptStates[state];

        /// <summary>The transition function: the state reached from <paramref name="state"/> on <paramref name="symbol"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="state"/> or <paramref name="symbol"/> is out of range.</exception>
        public int Transition(int state, int symbol) => _transitions[state, symbol];

        /// <inheritdoc/>
        public int GetRoot(ref int state)
        {
            state = _initialState;

            if (_pruneDeadStates && !_canReachAccept[_initialState])
            {
                return DdResult.False;
            }

            if (_length == 0)
            {
                return _acceptStates[_initialState] ? DdResult.True : DdResult.False;
            }

            return _length;
        }

        /// <inheritdoc/>
        public int GetChild(ref int state, int level, int value)
        {
            int next = _transitions[state, value];
            state = next;

            if (_pruneDeadStates && !_canReachAccept[next])
            {
                return DdResult.False;
            }

            int remaining = level - 1;
            if (remaining == 0)
            {
                return _acceptStates[next] ? DdResult.True : DdResult.False;
            }

            return remaining;
        }

        /// <inheritdoc/>
        public bool StateEquals(in int left, in int right) => left == right;

        /// <inheritdoc/>
        public int StateHashCode(in int state) => state;

        /// <summary>
        /// A worklist search over the reverse transition graph, starting from every accept state: state
        /// <c>s</c> is live (can reach an accept state) the moment it is discovered either to be one itself
        /// or to have an edge into an already-live state.
        /// </summary>
        private static bool[] ComputeCanReachAccept(int[,] transitions, bool[] acceptStates, int stateCount)
        {
            var reverseEdgeCounts = new int[stateCount];
            for (int s = 0; s < stateCount; s++)
            {
                for (int symbol = 0; symbol < 2; symbol++)
                {
                    reverseEdgeCounts[transitions[s, symbol]]++;
                }
            }

            var reverseEdgeStart = new int[stateCount + 1];
            for (int s = 0; s < stateCount; s++)
            {
                reverseEdgeStart[s + 1] = reverseEdgeStart[s] + reverseEdgeCounts[s];
            }

            var reverseEdges = new int[reverseEdgeStart[stateCount]];
            var fillIndex = (int[])reverseEdgeStart.Clone();
            for (int s = 0; s < stateCount; s++)
            {
                for (int symbol = 0; symbol < 2; symbol++)
                {
                    int target = transitions[s, symbol];
                    reverseEdges[fillIndex[target]++] = s;
                }
            }

            var canReach = (bool[])acceptStates.Clone();
            var queue = new Queue<int>();
            for (int s = 0; s < stateCount; s++)
            {
                if (canReach[s])
                {
                    queue.Enqueue(s);
                }
            }

            while (queue.Count > 0)
            {
                int s = queue.Dequeue();
                for (int i = reverseEdgeStart[s]; i < reverseEdgeStart[s + 1]; i++)
                {
                    int predecessor = reverseEdges[i];
                    if (!canReach[predecessor])
                    {
                        canReach[predecessor] = true;
                        queue.Enqueue(predecessor);
                    }
                }
            }

            return canReach;
        }
    }
}
