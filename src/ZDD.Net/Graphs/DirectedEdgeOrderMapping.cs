using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ZDD.Net.Graphs
{
    /// <summary>
    /// The permutation between a reordered <see cref="DirectedGraph"/>'s arc indices and those of the
    /// graph it came from. <see cref="DirectedGraph"/>'s counterpart to <see cref="EdgeOrderMapping"/>
    /// (kept as a separate type, not a shared base, so <see cref="EdgeOrderMapping.Source"/> can stay
    /// <see cref="Graph"/>-typed without a breaking change — see
    /// docs/design/m7-directed-graphs.md §3.5 for the same reasoning applied to <c>GraphSet</c>/
    /// <c>DirectedGraphSet</c>).
    /// </summary>
    /// <remarks>
    /// Reordering renumbers the arcs, so a result built over the reordered graph has to be translated
    /// through here before it can be read against the source graph — see <see cref="EdgeOrderMapping"/>'s
    /// remarks, which apply identically.
    /// </remarks>
    public sealed class DirectedEdgeOrderMapping
    {
        private readonly int[] _toSource;
        private readonly int[] _fromSource;
        private readonly ReadOnlyCollection<int> _toSourceView;

        /// <summary>Wraps <paramref name="toSource"/>, which must be a permutation of <c>0 .. Count - 1</c>.</summary>
        /// <remarks>The array is taken over, not copied; callers are the graph reordering methods, which build it fresh.</remarks>
        internal DirectedEdgeOrderMapping(DirectedGraph source, int[] toSource)
        {
            Source = source;
            _toSource = toSource;
            _toSourceView = new ReadOnlyCollection<int>(toSource);

            _fromSource = new int[toSource.Length];
            for (int i = 0; i < toSource.Length; i++)
            {
                _fromSource[toSource[i]] = i;
            }
        }

        /// <summary>The graph the reordering was applied to.</summary>
        public DirectedGraph Source { get; }

        /// <summary>The number of arcs, the same in both graphs.</summary>
        public int Count => _toSource.Length;

        /// <summary>
        /// The source arc index of each reordered arc: element <c>i</c> is where the reordered graph's
        /// arc <c>i</c> sat in <see cref="Source"/>.
        /// </summary>
        /// <remarks>A read-only view over the backing storage: it cannot be downcast to mutate the mapping.</remarks>
        public IReadOnlyList<int> ToSourceEdgeIndices => _toSourceView;

        /// <summary>Returns the index in <see cref="Source"/> of the reordered graph's arc <paramref name="edgeIndex"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeIndex"/> is outside <c>0 .. Count - 1</c>.</exception>
        public int ToSourceEdgeIndex(int edgeIndex)
        {
            if ((uint)edgeIndex >= (uint)_toSource.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(edgeIndex), edgeIndex, $"Must be in 0 .. {_toSource.Length - 1}.");
            }

            return _toSource[edgeIndex];
        }

        /// <summary>The inverse of <see cref="ToSourceEdgeIndex"/>: where <see cref="Source"/>'s arc ended up.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="sourceEdgeIndex"/> is outside <c>0 .. Count - 1</c>.</exception>
        public int FromSourceEdgeIndex(int sourceEdgeIndex)
        {
            if ((uint)sourceEdgeIndex >= (uint)_fromSource.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceEdgeIndex), sourceEdgeIndex, $"Must be in 0 .. {_fromSource.Length - 1}.");
            }

            return _fromSource[sourceEdgeIndex];
        }

        /// <summary>
        /// Translates a whole arc set — one set enumerated from a ZDD built over the reordered graph —
        /// into <see cref="Source"/>'s arc indices, sorted ascending so sets compare directly.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="edgeIndices"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An element is outside <c>0 .. Count - 1</c>.</exception>
        public int[] ToSourceEdgeSet(IEnumerable<int> edgeIndices)
        {
            ArgumentNullException.ThrowIfNull(edgeIndices);

            var translated = new List<int>();
            foreach (int edgeIndex in edgeIndices)
            {
                translated.Add(ToSourceEdgeIndex(edgeIndex));
            }

            int[] result = translated.ToArray();
            Array.Sort(result);
            return result;
        }
    }
}
