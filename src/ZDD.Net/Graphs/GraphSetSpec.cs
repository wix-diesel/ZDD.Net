using System;
using ZDD.Net.Frontier;

namespace ZDD.Net.Graphs
{
    /// <summary>
    /// A type-erased frontier spec: the runtime counterpart of <see cref="IDdSpec{TState}"/> /
    /// <see cref="IArrayDdSpec"/>, boxing whatever state a wrapped spec needs behind <c>object?</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="GraphSet"/>'s fluent filters (<c>Including</c> / <c>Excluding</c> / <c>Larger</c> /
    /// <c>Smaller</c>) must compose with an arbitrary, unbounded chain of earlier filters
    /// (<c>paths.Including(e).Excluding(f).Smaller(20)</c>), which rules out
    /// <see cref="AndSpec{TSpecA, TStateA, TSpecB, TStateB}"/>'s compile-time generics: each call would
    /// need its own type argument, so the composed type could never be named by a fluent method's
    /// return type. This interface trades that compile-time devirtualization for one virtual call per
    /// sub-spec per level &#8212; the same tradeoff <see cref="ArrayDdSpecAdapter{TSpec}"/> already makes
    /// (cloning arrays on every branch) &#8212; in exchange for filters that are genuinely applied
    /// during the frontier walk (docs/PLAN.md &#167;8's completion criterion) rather than by building the
    /// unfiltered family first and intersecting afterward.
    /// </para>
    /// <para>Implementations: <see cref="ArraySpecErased{TSpec}"/> (wraps an <see cref="IArrayDdSpec"/>),
    /// <see cref="StructSpecErased{TSpec, TState}"/> (wraps any <see cref="IDdSpec{TState}"/>), and
    /// <see cref="AndErasedSpec"/> (conjunction of two erased specs).</para>
    /// </remarks>
    internal interface IErasedGraphSpec
    {
        /// <summary>Initializes the root state and returns its level.</summary>
        int GetRoot(out object? state);

        /// <summary>Moves <paramref name="state"/> along the <paramref name="value"/> branch.</summary>
        int GetChild(object? state, int level, int value, out object? nextState);

        /// <summary>Whether two states at the same level are interchangeable from here on.</summary>
        bool StateEquals(object? left, object? right);

        /// <summary>A hash code consistent with <see cref="StateEquals"/>.</summary>
        int StateHashCode(object? state);
    }

    /// <summary>Wraps an <see cref="IArrayDdSpec"/> as an <see cref="IErasedGraphSpec"/>, boxing its <c>int[]</c> state.</summary>
    /// <typeparam name="TSpec">The array-state spec being erased.</typeparam>
    internal sealed class ArraySpecErased<TSpec> : IErasedGraphSpec
        where TSpec : struct, IArrayDdSpec
    {
        private readonly TSpec _spec;

        public ArraySpecErased(TSpec spec) => _spec = spec;

        public int GetRoot(out object? state)
        {
            int[] array = new int[_spec.ArrayLength];
            int level = _spec.GetRoot(array);
            state = array;
            return level;
        }

        public int GetChild(object? state, int level, int value, out object? nextState)
        {
            int[] clone = (int[])((int[])state!).Clone();
            int result = _spec.GetChild(clone, level, value);
            nextState = clone;
            return result;
        }

        public bool StateEquals(object? left, object? right) =>
            ((int[])left!).AsSpan().SequenceEqual((int[])right!);

        public int StateHashCode(object? state)
        {
            HashCode hash = default;
            foreach (int slot in (int[])state!)
            {
                hash.Add(slot);
            }

            return hash.ToHashCode();
        }
    }

    /// <summary>Wraps any <see cref="IDdSpec{TState}"/> as an <see cref="IErasedGraphSpec"/>, boxing its state.</summary>
    /// <typeparam name="TSpec">The spec being erased.</typeparam>
    /// <typeparam name="TState">The spec's state.</typeparam>
    internal sealed class StructSpecErased<TSpec, TState> : IErasedGraphSpec
        where TSpec : struct, IDdSpec<TState>
    {
        private readonly TSpec _spec;

        public StructSpecErased(TSpec spec) => _spec = spec;

        public int GetRoot(out object? state)
        {
            TState typedState = default!;
            int level = _spec.GetRoot(ref typedState);
            state = typedState;
            return level;
        }

        public int GetChild(object? state, int level, int value, out object? nextState)
        {
            TState typedState = (TState)state!;
            int result = _spec.GetChild(ref typedState, level, value);
            nextState = typedState;
            return result;
        }

        public bool StateEquals(object? left, object? right) => _spec.StateEquals((TState)left!, (TState)right!);

        public int StateHashCode(object? state) => _spec.StateHashCode((TState)state!);
    }

    /// <summary>
    /// The conjunction of two erased specs, applying the same level-synchronization and
    /// implicit-exclusion rules as <see cref="AndSpec{TSpecA, TStateA, TSpecB, TStateB}"/> (see its
    /// remarks), but over boxed <c>object?</c> sub-states rather than compile-time generic ones.
    /// </summary>
    internal sealed class AndErasedSpec : IErasedGraphSpec
    {
        private readonly IErasedGraphSpec _a;
        private readonly IErasedGraphSpec _b;

        public AndErasedSpec(IErasedGraphSpec a, IErasedGraphSpec b)
        {
            _a = a;
            _b = b;
        }

        public int GetRoot(out object? state)
        {
            int levelA = _a.GetRoot(out object? stateA);
            if (levelA == DdResult.False)
            {
                state = null;
                return DdResult.False;
            }

            int levelB = _b.GetRoot(out object? stateB);
            if (levelB == DdResult.False)
            {
                state = null;
                return DdResult.False;
            }

            var combined = new State(levelA == DdResult.True ? null : stateA, levelA, levelB == DdResult.True ? null : stateB, levelB);
            state = combined;
            return Combine(levelA, levelB);
        }

        public int GetChild(object? state, int level, int value, out object? nextState)
        {
            var current = (State)state!;

            int levelA = StepA(current, level, value, out object? stateA);
            if (levelA == DdResult.False)
            {
                nextState = null;
                return DdResult.False;
            }

            int levelB = StepB(current, level, value, out object? stateB);
            if (levelB == DdResult.False)
            {
                nextState = null;
                return DdResult.False;
            }

            var combined = new State(levelA == DdResult.True ? null : stateA, levelA, levelB == DdResult.True ? null : stateB, levelB);
            nextState = combined;
            return Combine(levelA, levelB);
        }

        public bool StateEquals(object? left, object? right)
        {
            var l = (State)left!;
            var r = (State)right!;

            if (l.LevelA != r.LevelA || l.LevelB != r.LevelB)
            {
                return false;
            }

            bool equalA = l.LevelA == DdResult.True || _a.StateEquals(l.StateA, r.StateA);
            bool equalB = l.LevelB == DdResult.True || _b.StateEquals(l.StateB, r.StateB);
            return equalA && equalB;
        }

        public int StateHashCode(object? state)
        {
            var s = (State)state!;
            int hashA = s.LevelA == DdResult.True ? 0 : _a.StateHashCode(s.StateA);
            int hashB = s.LevelB == DdResult.True ? 0 : _b.StateHashCode(s.StateB);
            return HashCode.Combine(s.LevelA, hashA, s.LevelB, hashB);
        }

        private int StepA(State current, int level, int value, out object? nextState)
        {
            if (current.LevelA == level)
            {
                return _a.GetChild(current.StateA, level, value, out nextState);
            }

            nextState = current.StateA;
            return value == 0 ? current.LevelA : DdResult.False;
        }

        private int StepB(State current, int level, int value, out object? nextState)
        {
            if (current.LevelB == level)
            {
                return _b.GetChild(current.StateB, level, value, out nextState);
            }

            nextState = current.StateB;
            return value == 0 ? current.LevelB : DdResult.False;
        }

        private static int Combine(int levelA, int levelB)
        {
            if (levelA == DdResult.True && levelB == DdResult.True)
            {
                return DdResult.True;
            }

            return levelA > levelB ? levelA : levelB;
        }

        private sealed class State
        {
            public State(object? stateA, int levelA, object? stateB, int levelB)
            {
                StateA = stateA;
                LevelA = levelA;
                StateB = stateB;
                LevelB = levelB;
            }

            public object? StateA { get; }

            public int LevelA { get; }

            public object? StateB { get; }

            public int LevelB { get; }
        }
    }

    /// <summary>Bridges one <see cref="IErasedGraphSpec"/> back into <see cref="IDdSpec{TState}"/> so <see cref="FrontierBuilder"/> can build it.</summary>
    internal readonly struct ErasedGraphDdSpec : IDdSpec<object?>
    {
        private readonly IErasedGraphSpec _spec;

        public ErasedGraphDdSpec(IErasedGraphSpec spec) => _spec = spec;

        public int GetRoot(ref object? state) => _spec.GetRoot(out state);

        public int GetChild(ref object? state, int level, int value)
        {
            int result = _spec.GetChild(state, level, value, out object? nextState);
            state = nextState;
            return result;
        }

        public bool StateEquals(in object? left, in object? right) => _spec.StateEquals(left, right);

        public int StateHashCode(in object? state) => _spec.StateHashCode(state);
    }

    /// <summary>
    /// The family of edge sets that include (<c>require</c> <see langword="true"/>) or exclude
    /// (<see langword="false"/>) one specific edge &#8212; <see cref="GraphSet.Including(Edge)"/> /
    /// <see cref="GraphSet.Excluding(Edge)"/>'s building block. Needs no state: the decision depends
    /// only on which level is being decided, not on anything chosen earlier.
    /// </summary>
    internal readonly struct EdgeMembershipSpec : IArrayDdSpec
    {
        private readonly Graph _graph;
        private readonly int _edgeIndex;
        private readonly bool _require;

        public EdgeMembershipSpec(Graph graph, int edgeIndex, bool require)
        {
            _graph = graph;
            _edgeIndex = edgeIndex;
            _require = require;
        }

        public int ArrayLength => 0;

        public int GetRoot(Span<int> state) =>
            _graph.EdgeCount == 0 ? (_require ? DdResult.False : DdResult.True) : _graph.EdgeCount;

        public int GetChild(Span<int> state, int level, int value)
        {
            int edgeIndex = _graph.LevelToEdgeIndex(level);

            if (edgeIndex == _edgeIndex && (value == 1) != _require)
            {
                return DdResult.False;
            }

            int remaining = level - 1;
            return remaining > 0 ? remaining : DdResult.True;
        }
    }

    /// <summary>
    /// The family of edge sets that touch (<c>require</c> <see langword="true"/>: at least one
    /// incident edge chosen) or avoid (<see langword="false"/>: no incident edge chosen) one specific
    /// vertex &#8212; <see cref="GraphSet.Including(int)"/> / <see cref="GraphSet.Excluding(int)"/>'s
    /// building block.
    /// </summary>
    /// <remarks>State: a single "touched yet" flag, checked only once the vertex's last incident edge is decided.</remarks>
    internal readonly struct VertexTouchSpec : IArrayDdSpec
    {
        private const int NotTouched = 0;
        private const int Touched = 1;

        private readonly Graph _graph;
        private readonly int _vertex;
        private readonly bool _require;
        private readonly int _lastIncidentEdgeIndex;

        public VertexTouchSpec(Graph graph, int vertex, bool require)
        {
            _graph = graph;
            _vertex = vertex;
            _require = require;

            System.Collections.Generic.IReadOnlyList<int> incident = graph.IncidentEdges(vertex);
            _lastIncidentEdgeIndex = incident.Count == 0 ? -1 : incident[incident.Count - 1];
        }

        public int ArrayLength => 1;

        public int GetRoot(Span<int> state)
        {
            if (_lastIncidentEdgeIndex < 0)
            {
                // An isolated vertex is never touched by any edge set.
                return _require ? DdResult.False : DdResult.True;
            }

            // state is zero-filled by the caller: the flag slot already reads NotTouched.
            return _graph.EdgeCount;
        }

        public int GetChild(Span<int> state, int level, int value)
        {
            int edgeIndex = _graph.LevelToEdgeIndex(level);
            Edge edge = _graph.GetEdge(edgeIndex);
            bool incident = edge.U == _vertex || edge.V == _vertex;

            if (incident && value == 1)
            {
                if (!_require)
                {
                    return DdResult.False; // must-not-touch violated
                }

                state[0] = Touched;
            }

            if (incident && edgeIndex == _lastIncidentEdgeIndex && _require && state[0] == NotTouched)
            {
                return DdResult.False; // last chance to touch the vertex, and it was not taken
            }

            int remaining = level - 1;
            return remaining > 0 ? remaining : DdResult.True;
        }
    }
}
