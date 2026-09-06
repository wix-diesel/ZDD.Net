using ZDD.Net.Frontier;

namespace ZDD.Net.Graphs
{
    /// <summary>
    /// The family of arc sets that include (<c>require</c> <see langword="true"/>) or exclude
    /// (<see langword="false"/>) one specific arc &#8212; <see cref="DirectedGraphSet.Including(DirectedEdge)"/> /
    /// <see cref="DirectedGraphSet.Excluding(DirectedEdge)"/>'s building block. The directed counterpart of
    /// <see cref="EdgeMembershipSpec"/>: needs no state, since the decision depends only on which level is
    /// being decided, not on anything chosen earlier.
    /// </summary>
    internal readonly struct DirectedEdgeMembershipSpec : IArrayDdSpec
    {
        private readonly DirectedGraph _graph;
        private readonly int _edgeIndex;
        private readonly bool _require;

        public DirectedEdgeMembershipSpec(DirectedGraph graph, int edgeIndex, bool require)
        {
            _graph = graph;
            _edgeIndex = edgeIndex;
            _require = require;
        }

        public int ArrayLength => 0;

        public int GetRoot(System.Span<int> state) =>
            _graph.EdgeCount == 0 ? (_require ? DdResult.False : DdResult.True) : _graph.EdgeCount;

        public int GetChild(System.Span<int> state, int level, int value)
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
    /// The family of arc sets that touch (<c>require</c> <see langword="true"/>: at least one incident arc,
    /// regardless of direction, chosen) or avoid (<see langword="false"/>: no incident arc chosen) one
    /// specific vertex &#8212; <see cref="DirectedGraphSet.Including(int)"/> /
    /// <see cref="DirectedGraphSet.Excluding(int)"/>'s building block. The directed counterpart of
    /// <see cref="VertexTouchSpec"/>.
    /// </summary>
    /// <remarks>State: a single "touched yet" flag, checked only once the vertex's last incident arc is decided.</remarks>
    internal readonly struct DirectedVertexTouchSpec : IArrayDdSpec
    {
        private const int NotTouched = 0;
        private const int Touched = 1;

        private readonly DirectedGraph _graph;
        private readonly int _vertex;
        private readonly bool _require;
        private readonly int _lastIncidentEdgeIndex;

        public DirectedVertexTouchSpec(DirectedGraph graph, int vertex, bool require)
        {
            _graph = graph;
            _vertex = vertex;
            _require = require;

            System.Collections.Generic.IReadOnlyList<int> incident = graph.IncidentEdges(vertex);
            _lastIncidentEdgeIndex = incident.Count == 0 ? -1 : incident[incident.Count - 1];
        }

        public int ArrayLength => 1;

        public int GetRoot(System.Span<int> state)
        {
            if (_lastIncidentEdgeIndex < 0)
            {
                // An isolated vertex is never touched by any arc set.
                return _require ? DdResult.False : DdResult.True;
            }

            // state is zero-filled by the caller: the flag slot already reads NotTouched.
            return _graph.EdgeCount;
        }

        public int GetChild(System.Span<int> state, int level, int value)
        {
            int edgeIndex = _graph.LevelToEdgeIndex(level);
            DirectedEdge arc = _graph.GetEdge(edgeIndex);
            bool incident = arc.From == _vertex || arc.To == _vertex;

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
