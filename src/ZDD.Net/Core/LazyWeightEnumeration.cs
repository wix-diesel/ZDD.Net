using System;
using System.Collections.Generic;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Lazily enumerates a family's sets in strictly descending (or ascending) weight order, without
    /// ever sorting the whole family: <c>GraphSet.MinIter</c> and <c>GraphSet.MaxIter</c>
    /// (<c>Graphs/GraphSet.cs</c>) are the public entry points.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Algorithm</b>: a priority search over partial root-to-&#8868; paths, using the same per-node
    /// "best achievable completion weight" table <see cref="WeightOperations.Optimize{TWeight, TOps}"/>
    /// computes for <c>MaxWeight</c>/<c>MinWeight</c> (here called <c>bound</c>). Each open candidate's
    /// priority is <c>accumulated weight so far + bound(current node)</c> &#8212; since <c>bound</c> is
    /// the <i>exact</i> best completion (not merely an upper bound), this priority is the exact best
    /// total any completion of that candidate could ever reach. A candidate landing on &#8868; has
    /// priority equal to its own final weight, so the moment such a candidate reaches the head of the
    /// priority queue, no still-open candidate can ever beat it (its own priority already bounds its
    /// best case) or tie it earlier &#8212; the classic uniform-cost argument, specialized to a heuristic
    /// that happens to be exact rather than merely admissible.
    /// </para>
    /// <para>
    /// <b>Laziness</b>: only <see cref="PriorityQueue{TElement, TPriority}"/> pops touch new nodes, and
    /// each pop expands at most two children, so enumerating the first <c>k</c> sets costs work
    /// proportional to <c>k</c> (times variable count for path reconstruction), never the full family
    /// &#8212; <c>Take(10)</c> returns immediately regardless of how many sets the family holds.
    /// </para>
    /// </remarks>
    internal static class LazyWeightEnumeration
    {
        /// <summary>Enumerates every set of the family rooted at <paramref name="rootId"/>, in descending (<paramref name="maximize"/>) or ascending weight order.</summary>
        /// <typeparam name="TWeight">The weight type.</typeparam>
        /// <typeparam name="TOps">Weight operations; must be a <c>struct</c>.</typeparam>
        /// <param name="manager">The manager that owns the family.</param>
        /// <param name="rootId">The family's root node ID.</param>
        /// <param name="weights">Per-item weights; length must equal the manager's variable count.</param>
        /// <param name="maximize"><see langword="true"/> for descending (max first), <see langword="false"/> for ascending (min first).</param>
        /// <exception cref="ArgumentException"><paramref name="weights"/>'s length does not match the variable count.</exception>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> has been disposed.</exception>
        public static IEnumerable<WeightedSet<TWeight>> Enumerate<TWeight, TOps>(
            ZddManager manager,
            int rootId,
            TWeight[] weights,
            bool maximize)
            where TOps : struct, IWeightOps<TWeight>
        {
            if (weights.Length != manager.VariableCount)
            {
                ThrowHelper.ThrowArgumentException(
                    nameof(weights),
                    $"'{nameof(weights)}' must have one entry per variable ({manager.VariableCount}), but had {weights.Length}.");
            }

            if (NodeTable.IsTerminal(rootId))
            {
                if (rootId == NodeTable.Top)
                {
                    return new[] { new WeightedSet<TWeight>(TOps.Zero, Array.Empty<int>()) };
                }

                return Array.Empty<WeightedSet<TWeight>>();
            }

            return EnumerateCore<TWeight, TOps>(manager, rootId, weights, maximize);
        }

        private static IEnumerable<WeightedSet<TWeight>> EnumerateCore<TWeight, TOps>(
            ZddManager manager,
            int rootId,
            TWeight[] weights,
            bool maximize)
            where TOps : struct, IWeightOps<TWeight>
        {
            NodeTable nodes = manager.Table.Nodes;
            NodeOrder order = NodeOrder.Build(manager, rootId);
            TWeight[] bound = ComputeBound<TWeight, TOps>(manager, nodes, order, weights, maximize);

            TWeight BoundOf(int id) =>
                NodeTable.IsTerminal(id) ? TOps.Zero : bound[order.SlotOf(id)];

            // PriorityQueue is always a min-heap; for descending (maximize) order, invert the comparison.
            IComparer<TWeight> priorityComparer = maximize
                ? Comparer<TWeight>.Create((a, b) => TOps.Compare(b, a))
                : Comparer<TWeight>.Create((a, b) => TOps.Compare(a, b));

            var heap = new PriorityQueue<Candidate<TWeight>, TWeight>(priorityComparer);
            var rootCandidate = new Candidate<TWeight>(rootId, TOps.Zero, Array.Empty<int>());
            heap.Enqueue(rootCandidate, TOps.Add(TOps.Zero, BoundOf(rootId)));

            while (heap.TryDequeue(out Candidate<TWeight> candidate, out _))
            {
                if (candidate.NodeId == NodeTable.Top)
                {
                    yield return new WeightedSet<TWeight>(candidate.Accumulated, candidate.Path);
                    continue;
                }

                ZddNode node = nodes[candidate.NodeId];
                int item = manager.ItemOf(node.Level);

                EnqueueChild<TWeight, TOps>(heap, node.Lo, candidate.Accumulated, candidate.Path, BoundOf);

                TWeight hiAccumulated = TOps.Add(candidate.Accumulated, weights[item]);
                int[] hiPath = Append(candidate.Path, item);
                EnqueueChild<TWeight, TOps>(heap, node.Hi, hiAccumulated, hiPath, BoundOf);
            }
        }

        private static void EnqueueChild<TWeight, TOps>(
            PriorityQueue<Candidate<TWeight>, TWeight> heap,
            int childId,
            TWeight accumulated,
            int[] path,
            Func<int, TWeight> boundOf)
            where TOps : struct, IWeightOps<TWeight>
        {
            if (childId == NodeTable.Bottom)
            {
                return; // no set completes through here
            }

            var candidate = new Candidate<TWeight>(childId, accumulated, path);
            heap.Enqueue(candidate, TOps.Add(accumulated, boundOf(childId)));
        }

        private static int[] Append(int[] path, int item)
        {
            int[] extended = new int[path.Length + 1];
            Array.Copy(path, extended, path.Length);
            extended[path.Length] = item;
            return extended;
        }

        /// <summary>Per-node exact best completion weight, the same DP <see cref="WeightOperations.Optimize{TWeight, TOps}"/> uses, without the reconstruction step.</summary>
        private static TWeight[] ComputeBound<TWeight, TOps>(
            ZddManager manager,
            NodeTable nodes,
            NodeOrder order,
            TWeight[] weights,
            bool maximize)
            where TOps : struct, IWeightOps<TWeight>
        {
            TWeight[] best = new TWeight[order.Count];

            for (int slot = 0; slot < order.Count; slot++)
            {
                int id = order.Ids[slot];
                ZddNode node = nodes[id];
                int item = manager.ItemOf(node.Level);

                bool hasLo = TryValueOf(order, best, node.Lo, out TWeight loValue);

                // The zero-suppress rule guarantees a node's 1-edge never lands on bottom, so the
                // hi side always has a value (top counts as the empty completion, weight zero).
                TryValueOf(order, best, node.Hi, out TWeight hiValue);
                hiValue = TOps.Add(hiValue, weights[item]);

                bool takeHi = !hasLo || (maximize ? TOps.Compare(hiValue, loValue) >= 0 : TOps.Compare(hiValue, loValue) <= 0);
                best[slot] = takeHi ? hiValue : loValue;
            }

            return best;
        }

        private static bool TryValueOf<TWeight>(NodeOrder order, TWeight[] best, int childId, out TWeight value)
        {
            if (NodeTable.IsTerminal(childId))
            {
                value = default!;
                return childId == NodeTable.Top;
            }

            value = best[order.SlotOf(childId)];
            return true;
        }

        private readonly struct Candidate<TWeight>
        {
            public Candidate(int nodeId, TWeight accumulated, int[] path)
            {
                NodeId = nodeId;
                Accumulated = accumulated;
                Path = path;
            }

            public int NodeId { get; }

            public TWeight Accumulated { get; }

            public int[] Path { get; }
        }
    }
}
