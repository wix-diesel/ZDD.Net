using System;
using System.Collections;
using System.Collections.Generic;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// The set of families a <see cref="ZddManager"/> keeps alive across <see cref="ZddManager.Collect()"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Collection renumbers every surviving node (docs/PLAN.md &#167;4.4), which would invalidate
    /// every <see cref="Zdd"/> handle if nothing were done about it. This set is the exception:
    /// each registered id is remapped in place as part of collection, so re-reading a root from
    /// here afterward (by index, or via <see cref="GetEnumerator"/>) returns a fresh, valid handle
    /// to the same family. A handle obtained before collection and not registered here throws
    /// <see cref="ZddCollectedException"/> if used afterward.
    /// </para>
    /// <para>
    /// Registration order is preserved and <see cref="Add"/> is idempotent (registering the same
    /// family twice keeps one entry), so the family at a given index before a collection is the
    /// same family — under its new id — at that index afterward. Terminals (<see cref="Zdd.IsEmpty"/>
    /// / <see cref="Zdd.IsBase"/>) never need registration: they never move and are always valid,
    /// so <see cref="Add"/> silently ignores them and they never appear in this set.
    /// </para>
    /// <para>Not thread-safe, like the rest of <see cref="ZddManager"/>.</para>
    /// </remarks>
    public sealed class ZddRootSet : IReadOnlyList<Zdd>
    {
        private readonly ZddManager _manager;
        private readonly List<int> _ids = new List<int>();

        internal ZddRootSet(ZddManager manager)
        {
            _manager = manager;
        }

        /// <summary>Number of registered roots (terminals are never counted, since they're never registered).</summary>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public int Count
        {
            get
            {
                EnsureNotDisposed();
                return _ids.Count;
            }
        }

        /// <summary>The root registered at <paramref name="index"/>, in registration order.</summary>
        /// <param name="index">Position, 0 .. <see cref="Count"/> - 1.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public Zdd this[int index]
        {
            get
            {
                EnsureNotDisposed();

                if ((uint)index >= (uint)_ids.Count)
                {
                    ThrowHelper.ThrowArgumentOutOfRangeException(
                        nameof(index),
                        $"'{nameof(index)}' must be in the range 0..{_ids.Count - 1}, but was {index}.");
                }

                return new Zdd(_manager, _ids[index]);
            }
        }

        /// <summary>Registers <paramref name="zdd"/> so it survives the next <see cref="ZddManager.Collect()"/>.</summary>
        /// <param name="zdd">The family to keep alive; must belong to the owning manager and not be stale.</param>
        /// <remarks>A no-op if <paramref name="zdd"/> is already registered, or is a terminal (&#8709; or <c>{&#8709;}</c>), which is always valid regardless of collection.</remarks>
        /// <exception cref="ArgumentException"><paramref name="zdd"/> belongs to a different manager, or is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ZddCollectedException"><paramref name="zdd"/> predates an earlier <see cref="ZddManager.Collect()"/> call and was not kept alive.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public void Add(Zdd zdd)
        {
            EnsureNotDisposed();
            _manager.EnsureOwns(zdd, nameof(zdd));

            if (NodeTable.IsTerminal(zdd.Id) || _ids.Contains(zdd.Id))
            {
                return;
            }

            _ids.Add(zdd.Id);
        }

        /// <summary>Unregisters <paramref name="zdd"/>, if present.</summary>
        /// <param name="zdd">The family to stop keeping alive; must belong to the owning manager.</param>
        /// <returns><see langword="true"/> if it was registered and is now removed.</returns>
        /// <exception cref="ArgumentException"><paramref name="zdd"/> belongs to a different manager, or is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ZddCollectedException"><paramref name="zdd"/> predates an earlier <see cref="ZddManager.Collect()"/> call and was not kept alive.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public bool Remove(Zdd zdd)
        {
            EnsureNotDisposed();
            _manager.EnsureOwns(zdd, nameof(zdd));

            return _ids.Remove(zdd.Id);
        }

        /// <summary>Unregisters every root. The next <see cref="ZddManager.Collect()"/> then keeps nothing alive.</summary>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public void Clear()
        {
            EnsureNotDisposed();
            _ids.Clear();
        }

        /// <summary>Whether <paramref name="zdd"/> is currently registered (always <see langword="true"/> for a terminal belonging to this manager).</summary>
        /// <param name="zdd">The family to check; must belong to the owning manager.</param>
        /// <exception cref="ArgumentException"><paramref name="zdd"/> belongs to a different manager, or is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ZddCollectedException"><paramref name="zdd"/> predates an earlier <see cref="ZddManager.Collect()"/> call and was not kept alive.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public bool Contains(Zdd zdd)
        {
            EnsureNotDisposed();
            _manager.EnsureOwns(zdd, nameof(zdd));

            return NodeTable.IsTerminal(zdd.Id) || _ids.Contains(zdd.Id);
        }

        /// <summary>Enumerates the registered roots in registration order, as fresh (current-generation) handles.</summary>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public IEnumerator<Zdd> GetEnumerator()
        {
            // Checked eagerly here rather than lazily, since an iterator block's body (below)
            // only starts running on the first MoveNext — a disposed manager should fail the call
            // to GetEnumerator() itself, not silently produce an enumerator that fails only once iterated.
            EnsureNotDisposed();
            return Iterate();
        }

        private IEnumerator<Zdd> Iterate()
        {
            foreach (int id in _ids)
            {
                yield return new Zdd(_manager, id);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private void EnsureNotDisposed()
        {
            if (_manager.IsDisposed)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(ZddManager));
            }
        }

        /// <summary>The registered ids, for <see cref="NodeGarbageCollector"/> to mark and remap. Order matches this set's enumeration order.</summary>
        internal List<int> Ids => _ids;

        /// <summary>
        /// Rewrites every registered id through a <see cref="ZddManager.Collect()"/> id map. Every
        /// registered id is, by construction, a mark root and therefore always live, so each lookup
        /// always finds a real new id (never <see cref="NodeTable.DeadId"/>).
        /// </summary>
        internal void Remap(ReadOnlySpan<int> oldToNewId)
        {
            for (int i = 0; i < _ids.Count; i++)
            {
                _ids[i] = oldToNewId[_ids[i]];
            }
        }
    }
}
