using System;
using ZDD.Net.Internal;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// The two state tables a top-down frontier build needs at any moment: the level being read
    /// (<see cref="Current"/>) and the level being filled (<see cref="Next"/>).
    /// </summary>
    /// <typeparam name="TTable">The table kind, one of the <see cref="LevelStateTable"/> subclasses.</typeparam>
    /// <remarks>
    /// <see cref="Advance"/> rotates the two rather than allocating a table per level, so peak
    /// memory is two levels wide however deep the diagram is, and a level's buffers are already
    /// grown to roughly the width the next level needs.
    /// </remarks>
    internal sealed class LevelStateTablePair<TTable> : IDisposable
        where TTable : LevelStateTable
    {
        private TTable _current;
        private TTable _next;

        /// <summary>Takes ownership of two distinct, empty tables.</summary>
        /// <param name="current">The table for the level being read.</param>
        /// <param name="next">The table for the level being filled.</param>
        public LevelStateTablePair(TTable current, TTable next)
        {
            ThrowHelper.ThrowIfNull(current, nameof(current));
            ThrowHelper.ThrowIfNull(next, nameof(next));

            if (ReferenceEquals(current, next))
            {
                ThrowHelper.ThrowArgumentException(
                    nameof(next),
                    "The two levels must be held by two different tables; rotating one table over itself would drop the level being read.");
            }

            _current = current;
            _next = next;
        }

        /// <summary>The table holding the level currently being expanded.</summary>
        public TTable Current => _current;

        /// <summary>The table collecting the child states of <see cref="Current"/>.</summary>
        public TTable Next => _next;

        /// <summary>The largest width either level reached: the peak frontier width of the build.</summary>
        public int PeakWidth => Math.Max(_current.PeakWidth, _next.PeakWidth);

        /// <summary>States registered across every level so far.</summary>
        public long TotalRegistered => _current.TotalRegistered + _next.TotalRegistered;

        /// <summary>Probe collisions across every level so far.</summary>
        public long Collisions => _current.Collisions + _next.Collisions;

        /// <summary>
        /// Moves down one level: the table just filled becomes <see cref="Current"/>, and the
        /// finished one is cleared and reused as <see cref="Next"/>.
        /// </summary>
        /// <remarks>Indices handed out for the level that is dropped stop being meaningful here.</remarks>
        public void Advance()
        {
            TTable finished = _current;
            _current = _next;
            _next = finished;
            _next.Clear();
        }

        /// <summary>Disposes both tables, returning their pooled buffers.</summary>
        public void Dispose()
        {
            _current.Dispose();
            _next.Dispose();
        }
    }
}
