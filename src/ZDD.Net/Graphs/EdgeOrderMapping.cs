using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ZDD.Net.Graphs
{
    /// <summary>
    /// The permutation between a reordered graph's edge indices and those of the graph it came from.
    /// Reordering renumbers the edges, so a result built over the reordered graph has to be translated
    /// through here before it can be read against the source graph.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the single most error-prone part of edge-order optimization: an edge set that came out of a
    /// build over <c>graph.Optimize()</c> means nothing against the original edge list until every element
    /// has gone through <see cref="ToSourceEdgeIndex"/>. <see cref="ToSourceEdgeSet"/>
    /// translates a whole set at once.
    /// </para>
    /// <para>
    /// <see cref="Source"/> is the graph the reordering was applied to — the immediate predecessor, not
    /// necessarily the graph originally constructed. Reordering an already reordered graph therefore leaves
    /// a chain: follow <c>graph.SourceOrder.Source.SourceOrder</c> to reach the first graph, or reorder from
    /// the original each time.
    /// </para>
    /// </remarks>
    public sealed class EdgeOrderMapping
    {
        private readonly int[] _toSource;
        private readonly int[] _fromSource;
        private readonly ReadOnlyCollection<int> _toSourceView;

        /// <summary>Wraps <paramref name="toSource"/>, which must be a permutation of <c>0 .. Count - 1</c>.</summary>
        /// <remarks>The array is taken over, not copied; callers are the graph reordering methods, which build it fresh.</remarks>
        internal EdgeOrderMapping(Graph source, int[] toSource)
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
        public Graph Source { get; }

        /// <summary>The number of edges, the same in both graphs.</summary>
        public int Count => _toSource.Length;

        /// <summary>
        /// The source edge index of each reordered edge: element <c>i</c> is where the reordered graph's
        /// edge <c>i</c> sat in <see cref="Source"/>.
        /// </summary>
        /// <remarks>A read-only view over the backing storage: it cannot be downcast to mutate the mapping.</remarks>
        public IReadOnlyList<int> ToSourceEdgeIndices => _toSourceView;

        /// <summary>Returns the index in <see cref="Source"/> of the reordered graph's edge <paramref name="edgeIndex"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeIndex"/> is outside <c>0 .. Count - 1</c>.</exception>
        public int ToSourceEdgeIndex(int edgeIndex)
        {
            if ((uint)edgeIndex >= (uint)_toSource.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(edgeIndex), edgeIndex, $"Must be in 0 .. {_toSource.Length - 1}.");
            }

            return _toSource[edgeIndex];
        }

        /// <summary>The inverse of <see cref="ToSourceEdgeIndex"/>: where <see cref="Source"/>'s edge ended up.</summary>
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
        /// Translates a whole edge set — one set enumerated from a ZDD built over the reordered graph —
        /// into <see cref="Source"/>'s edge indices, sorted ascending so sets compare directly.
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
