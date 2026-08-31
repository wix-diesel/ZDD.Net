using System;
using System.Numerics;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Implements weight optimization (<see cref="Zdd.MaxWeight{TWeight, TOps}"/> /
    /// <see cref="Zdd.MinWeight{TWeight, TOps}"/> / <see cref="Zdd.TopK{TWeight, TOps}"/>) and
    /// probability, expected value, and item frequency (<see cref="Zdd.Probability"/> /
    /// <see cref="Zdd.ExpectedValue"/> / <see cref="Zdd.ItemFrequency"/>).
    /// </summary>
    /// <remarks>
    /// None of these enumerate the family's sets. A root-to-⊤ path corresponds to one set, so
    /// e.g. "the maximum-weight set" is the longest path in the DAG, found by a single DP pass
    /// over the nodes regardless of how many sets the family holds.
    /// <see cref="Probability"/> is defined over the whole universe of variables (an absent item
    /// contributes its "not chosen" probability too), while <see cref="ItemFrequency"/> and
    /// <see cref="ExpectedValue"/> are defined over the uniform distribution on the family's sets
    /// — these are different distributions and their outputs are not comparable.
    /// Traversal uses the <see cref="NodeOrder"/> array with a plain <c>for</c> loop, not
    /// recursion, since ZDD depth equals the variable count.
    /// </remarks>
    internal static class WeightOperations
    {
        /// <summary>Returns the set with maximum (<paramref name="maximize"/>) or minimum weight, together with its weight.</summary>
        /// <typeparam name="TWeight">The weight type.</typeparam>
        /// <typeparam name="TOps">The weight operations; must be a <c>struct</c>.</typeparam>
        /// <param name="manager">The manager that owns the family.</param>
        /// <param name="rootId">The family's root node ID.</param>
        /// <param name="weights">Per-item weights; length must equal the manager's variable count.</param>
        /// <param name="maximize"><see langword="true"/> to maximize, <see langword="false"/> to minimize.</param>
        /// <remarks>
        /// DP: the optimum below node <c>v</c> (item <c>i</c>) is the better of the 0-edge
        /// optimum and the 1-edge optimum plus <c>w[i]</c>; the chosen side is recorded to
        /// reconstruct the set by a single descent from the root. Ties favor the 0-edge side.
        /// Cost is O(m) for the DP and O(n) for reconstruction (m = reachable nodes, n = variables).
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="weights"/>'s length does not match the variable count.</exception>
        /// <exception cref="InvalidOperationException">The family is empty.</exception>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> has been disposed.</exception>
        public static WeightedSet<TWeight> Optimize<TWeight, TOps>(
            ZddManager manager,
            int rootId,
            ReadOnlySpan<TWeight> weights,
            bool maximize)
            where TOps : struct, IWeightOps<TWeight>
        {
            NodeTable nodes = manager.Table.Nodes;
            EnsureWeightCount(manager, weights.Length, nameof(weights));

            if (NodeTable.IsTerminal(rootId))
            {
                EnsureNotEmpty(rootId != NodeTable.Bottom);

                // {∅}'s only set is the empty set; its weight is the additive identity.
                return new WeightedSet<TWeight>(TOps.Zero, Array.Empty<int>());
            }

            NodeOrder order = NodeOrder.Build(manager, rootId);

            TWeight[] best = new TWeight[order.Count];
            bool[] takeHi = new bool[order.Count];

            for (int slot = 0; slot < order.Count; slot++)
            {
                int id = order.Ids[slot];
                int lo;
                int hi;
                int item;
                {
                    ref ZddNode node = ref nodes[id];
                    lo = node.Lo;
                    hi = node.Hi;
                    item = manager.ItemOf(node.Level);
                }

                bool hasLo = TryValueOf<TWeight, TOps>(order, best, lo, out TWeight loValue);
                bool hasHi = TryValueOf<TWeight, TOps>(order, best, hi, out TWeight hiValue);

                if (!hasHi)
                {
                    // The zero-suppress rule forbids a node whose 1-edge lands on ⊥.
                    ThrowHelper.ThrowInvalidOperationException(
                        $"The node {id} has the bottom terminal on its 1-edge, which the zero-suppress rule forbids.");
                }

                // Every set past the 1-edge includes this item, so its weight applies.
                hiValue = TOps.Add(hiValue, weights[item]);

                // Ties favor the 0-edge side (item excluded).
                takeHi[slot] = !hasLo
                    || (maximize ? TOps.Compare(hiValue, loValue) > 0 : TOps.Compare(hiValue, loValue) < 0);

                best[slot] = takeHi[slot] ? hiValue : loValue;
            }

            return new WeightedSet<TWeight>(
                best[order.SlotOf(rootId)],
                Descend(manager, nodes, order, rootId, takeHi));
        }

        /// <summary>Returns the <paramref name="k"/> sets with the largest weight, in descending order.</summary>
        /// <typeparam name="TWeight">The weight type.</typeparam>
        /// <typeparam name="TOps">The weight operations; must be a <c>struct</c>.</typeparam>
        /// <param name="manager">The manager that owns the family.</param>
        /// <param name="rootId">The family's root node ID.</param>
        /// <param name="weights">Per-item weights; length must equal the manager's variable count.</param>
        /// <param name="k">Number of sets to return; if larger than the family's cardinality, only that many come back.</param>
        /// <remarks>
        /// Generalizes <see cref="Optimize{TWeight, TOps}"/>'s "keep the better side" to "keep the
        /// top k from each side, merged". Cost is O(m·k + k·n) time and O(m·k) memory, so this
        /// gets expensive for large k; for large k prefer sorting <see cref="Zdd.Sets(ZddEnumerationOrder)"/>
        /// instead. Ties favor the 0-edge side; only the weight ordering is guaranteed, matching
        /// a full descending enumeration's first k entries.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="weights"/>'s length does not match the variable count.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="k"/> is negative.</exception>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> has been disposed.</exception>
        public static WeightedSet<TWeight>[] TopK<TWeight, TOps>(
            ZddManager manager,
            int rootId,
            ReadOnlySpan<TWeight> weights,
            int k)
            where TOps : struct, IWeightOps<TWeight>
        {
            NodeTable nodes = manager.Table.Nodes;
            EnsureWeightCount(manager, weights.Length, nameof(weights));
            ThrowHelper.ThrowIfNegative(k, nameof(k));

            if (k == 0)
            {
                return Array.Empty<WeightedSet<TWeight>>();
            }

            if (NodeTable.IsTerminal(rootId))
            {
                return rootId == NodeTable.Bottom
                    ? Array.Empty<WeightedSet<TWeight>>()
                    : new[] { new WeightedSet<TWeight>(TOps.Zero, Array.Empty<int>()) };
            }

            NodeOrder order = NodeOrder.Build(manager, rootId);

            // ⊤'s "top k" is just the empty set; ⊥ has none.
            TopEntry<TWeight>[] top = { new TopEntry<TWeight>(TOps.Zero, fromHi: false, index: 0) };
            TopEntry<TWeight>[] bottom = Array.Empty<TopEntry<TWeight>>();
            TopEntry<TWeight>[][] lists = new TopEntry<TWeight>[order.Count][];

            for (int slot = 0; slot < order.Count; slot++)
            {
                int id = order.Ids[slot];
                int lo;
                int hi;
                int item;
                {
                    ref ZddNode node = ref nodes[id];
                    lo = node.Lo;
                    hi = node.Hi;
                    item = manager.ItemOf(node.Level);
                }

                TopEntry<TWeight>[] loList = ListOf(order, lists, top, bottom, lo);
                TopEntry<TWeight>[] hiList = ListOf(order, lists, top, bottom, hi);

                lists[slot] = Merge<TWeight, TOps>(loList, hiList, weights[item], k);
            }

            TopEntry<TWeight>[] rootList = lists[order.SlotOf(rootId)];
            WeightedSet<TWeight>[] result = new WeightedSet<TWeight>[rootList.Length];

            for (int rank = 0; rank < rootList.Length; rank++)
            {
                result[rank] = new WeightedSet<TWeight>(
                    rootList[rank].Weight,
                    Descend(manager, nodes, order, lists, rootId, rank));
            }

            return result;
        }

        /// <summary>Returns the probability that a set formed by choosing each item independently with probability <paramref name="probabilities"/> belongs to the family.</summary>
        /// <param name="manager">The manager that owns the family.</param>
        /// <param name="rootId">The family's root node ID.</param>
        /// <param name="probabilities">Per-item probability, length equal to the variable count, each in [0, 1].</param>
        /// <remarks>
        /// The universe is all of the manager's variables: <c>Σ_{A ∈ F} Π_{i ∈ A} p[i] · Π_{i ∉ A} (1 - p[i])</c>,
        /// so items never appearing in the family still contribute their "not chosen" factor.
        /// Because zero-suppression skips levels a subfamily never uses, each skipped level's
        /// "not chosen" factor is folded in explicitly while descending (and above the root too).
        /// Cost is O(m + skipped levels), worst case O(m·n), typically close to O(m).
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="probabilities"/>'s length does not match the variable count.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="probabilities"/> contains a value outside [0, 1] or <see cref="double.NaN"/>.</exception>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> has been disposed.</exception>
        public static double Probability(ZddManager manager, int rootId, ReadOnlySpan<double> probabilities)
        {
            NodeTable nodes = manager.Table.Nodes;
            EnsureWeightCount(manager, probabilities.Length, nameof(probabilities));
            EnsureProbabilities(probabilities);

            if (NodeTable.IsTerminal(rootId))
            {
                // ∅ has no sets, so 0. {∅} is exactly "no item is chosen".
                return rootId == NodeTable.Bottom
                    ? 0.0
                    : AbsentProduct(probabilities, 0, probabilities.Length);
            }

            NodeOrder order = NodeOrder.Build(manager, rootId);
            double[] probability = new double[order.Count];

            for (int slot = 0; slot < order.Count; slot++)
            {
                int id = order.Ids[slot];
                int lo;
                int hi;
                int item;
                {
                    ref ZddNode node = ref nodes[id];
                    lo = node.Lo;
                    hi = node.Hi;
                    item = manager.ItemOf(node.Level);
                }

                probability[slot] =
                    ((1.0 - probabilities[item]) * Lift(manager, nodes, order, probability, probabilities, lo, item + 1))
                    + (probabilities[item] * Lift(manager, nodes, order, probability, probabilities, hi, item + 1));
            }

            // Levels above the root (items no set in the family ever uses) also contribute
            // their "not chosen" factor.
            int rootItem = manager.ItemOf(nodes[rootId].Level);

            return probability[order.SlotOf(rootId)] * AbsentProduct(probabilities, 0, rootItem);
        }

        /// <summary>Returns, per item, the probability that a set chosen uniformly at random from the family contains it.</summary>
        /// <param name="manager">The manager that owns the family.</param>
        /// <param name="rootId">The family's root node ID.</param>
        /// <remarks>
        /// For each node on item <c>i</c>, the number of sets containing item <c>i</c> is
        /// (paths from the root to that node) times (sets past its 1-edge), summed over all
        /// such nodes; both factors are computed with a single DP pass each (the latter is
        /// exactly <see cref="CardinalityTable"/>). Counting is done in <see cref="BigInteger"/>
        /// throughout and only converted to <see cref="double"/> at the end, since these counts
        /// can be astronomically large and a premature cast to <c>double</c> loses precision.
        /// </remarks>
        /// <exception cref="InvalidOperationException">The family is empty.</exception>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> has been disposed.</exception>
        public static double[] ItemFrequency(ZddManager manager, int rootId)
        {
            NodeTable nodes = manager.Table.Nodes;

            EnsureNotEmpty(rootId != NodeTable.Bottom);

            double[] frequency = new double[manager.VariableCount];

            if (NodeTable.IsTerminal(rootId))
            {
                // {∅}'s only set is the empty set, so no item is present.
                return frequency;
            }

            NodeOrder order = NodeOrder.Build(manager, rootId);
            CardinalityTable cardinality = CardinalityTable.Build(manager, rootId);

            BigInteger[] paths = new BigInteger[order.Count];
            BigInteger[] containing = new BigInteger[manager.VariableCount];

            paths[order.SlotOf(rootId)] = BigInteger.One;

            // The root is last; walking back-to-front means a node's path count is finalized
            // before it's distributed to its children.
            for (int slot = order.Count - 1; slot >= 0; slot--)
            {
                int id = order.Ids[slot];
                int lo;
                int hi;
                int item;
                {
                    ref ZddNode node = ref nodes[id];
                    lo = node.Lo;
                    hi = node.Hi;
                    item = manager.ItemOf(node.Level);
                }

                BigInteger incoming = paths[slot];

                // Every path taking the 1-edge here yields a set that contains this item.
                containing[item] += incoming * cardinality.CountOf(hi);

                if (!NodeTable.IsTerminal(lo))
                {
                    paths[order.SlotOf(lo)] += incoming;
                }

                if (!NodeTable.IsTerminal(hi))
                {
                    paths[order.SlotOf(hi)] += incoming;
                }
            }

            BigInteger total = cardinality.CountOf(rootId);

            for (int item = 0; item < frequency.Length; item++)
            {
                frequency[item] = Ratio(containing[item], total);
            }

            return frequency;
        }

        /// <summary>Returns the expected weight of a set chosen uniformly at random from the family.</summary>
        /// <param name="manager">The manager that owns the family.</param>
        /// <param name="rootId">The family's root node ID.</param>
        /// <param name="weights">Per-item weights, length equal to the variable count.</param>
        /// <remarks>
        /// Linearity of expectation: <c>E[Σ_{i ∈ A} w[i]] = Σ_i w[i] · P(i ∈ A)</c>, so this is
        /// just the dot product with <see cref="ItemFrequency"/> — no per-set enumeration needed.
        /// Weight is fixed to <see cref="double"/> because computing an expectation requires
        /// division, which <see cref="IWeightOps{TWeight}"/> deliberately does not require.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="weights"/>'s length does not match the variable count.</exception>
        /// <exception cref="InvalidOperationException">The family is empty.</exception>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> has been disposed.</exception>
        public static double ExpectedValue(ZddManager manager, int rootId, ReadOnlySpan<double> weights)
        {
            EnsureWeightCount(manager, weights.Length, nameof(weights));

            double[] frequency = ItemFrequency(manager, rootId);
            double expected = 0.0;

            for (int item = 0; item < frequency.Length; item++)
            {
                expected += weights[item] * frequency[item];
            }

            return expected;
        }

        // ---- Reconstructing the optimal set ----

        /// <summary>Rebuilds the chosen set by descending from the root, following the choices <see cref="Optimize{TWeight, TOps}"/> recorded.</summary>
        private static int[] Descend(
            ZddManager manager,
            NodeTable nodes,
            NodeOrder order,
            int rootId,
            bool[] takeHi)
        {
            int[] path = new int[16];
            int length = 0;
            int id = rootId;

            while (!NodeTable.IsTerminal(id))
            {
                ZddNode node = nodes[id];

                if (takeHi[order.SlotOf(id)])
                {
                    Append(ref path, ref length, manager.ItemOf(node.Level));
                    id = node.Hi;
                    continue;
                }

                id = node.Lo;
            }

            EnsureLandedOnTop(id);

            return path.AsSpan(0, length).ToArray();
        }

        /// <summary>Rebuilds the set at rank <paramref name="rank"/> from <see cref="TopK{TWeight, TOps}"/> by descending from the root.</summary>
        private static int[] Descend<TWeight>(
            ZddManager manager,
            NodeTable nodes,
            NodeOrder order,
            TopEntry<TWeight>[][] lists,
            int rootId,
            int rank)
        {
            int[] path = new int[16];
            int length = 0;
            int id = rootId;
            int index = rank;

            while (!NodeTable.IsTerminal(id))
            {
                ZddNode node = nodes[id];
                TopEntry<TWeight> entry = lists[order.SlotOf(id)][index];
                index = entry.Index;

                if (entry.FromHi)
                {
                    Append(ref path, ref length, manager.ItemOf(node.Level));
                    id = node.Hi;
                    continue;
                }

                id = node.Lo;
            }

            EnsureLandedOnTop(id);

            return path.AsSpan(0, length).ToArray();
        }

        // ---- Merging top-k lists ----

        /// <summary>Merges the 0-edge and 1-edge "top k" lists, preserving order, and keeps the first <paramref name="k"/>.</summary>
        /// <remarks>The 1-edge side's weight gains <paramref name="itemWeight"/> (every set past it includes the item); ties favor the 0-edge side.</remarks>
        private static TopEntry<TWeight>[] Merge<TWeight, TOps>(
            TopEntry<TWeight>[] loList,
            TopEntry<TWeight>[] hiList,
            TWeight itemWeight,
            int k)
            where TOps : struct, IWeightOps<TWeight>
        {
            int limit = Math.Min(k, loList.Length + hiList.Length);
            TopEntry<TWeight>[] merged = new TopEntry<TWeight>[limit];

            int loNext = 0;
            int hiNext = 0;

            for (int filled = 0; filled < limit; filled++)
            {
                if (hiNext == hiList.Length)
                {
                    merged[filled] = new TopEntry<TWeight>(loList[loNext].Weight, fromHi: false, index: loNext);
                    loNext++;
                    continue;
                }

                TWeight hiWeight = TOps.Add(hiList[hiNext].Weight, itemWeight);

                if (loNext < loList.Length && TOps.Compare(loList[loNext].Weight, hiWeight) >= 0)
                {
                    merged[filled] = new TopEntry<TWeight>(loList[loNext].Weight, fromHi: false, index: loNext);
                    loNext++;
                    continue;
                }

                merged[filled] = new TopEntry<TWeight>(hiWeight, fromHi: true, index: hiNext);
                hiNext++;
            }

            return merged;
        }

        /// <summary>Looks up a child's "top k" list; terminals aren't in the table, so answer directly.</summary>
        private static TopEntry<TWeight>[] ListOf<TWeight>(
            NodeOrder order,
            TopEntry<TWeight>[][] lists,
            TopEntry<TWeight>[] top,
            TopEntry<TWeight>[] bottom,
            int childId)
        {
            if (NodeTable.IsTerminal(childId))
            {
                return childId == NodeTable.Top ? top : bottom;
            }

            return lists[order.SlotOf(childId)];
        }

        // ---- Probability helpers ----

        /// <summary>Scales a child's probability by the "not chosen" factor for levels skipped between <paramref name="from"/> and the child's item.</summary>
        private static double Lift(
            ZddManager manager,
            NodeTable nodes,
            NodeOrder order,
            double[] probability,
            ReadOnlySpan<double> probabilities,
            int childId,
            int from)
        {
            if (NodeTable.IsTerminal(childId))
            {
                // Landing on ⊤ means every remaining item was not chosen; ⊥ can't happen here.
                return childId == NodeTable.Top
                    ? AbsentProduct(probabilities, from, probabilities.Length)
                    : 0.0;
            }

            int childItem = manager.ItemOf(nodes[childId].Level);

            return probability[order.SlotOf(childId)] * AbsentProduct(probabilities, from, childItem);
        }

        /// <summary><c>Π_{j = from}^{toExclusive - 1} (1 - p[j])</c>, the "not chosen" product for skipped levels.</summary>
        private static double AbsentProduct(ReadOnlySpan<double> probabilities, int from, int toExclusive)
        {
            double product = 1.0;

            for (int item = from; item < toExclusive; item++)
            {
                product *= 1.0 - probabilities[item];
            }

            return product;
        }

        // ---- Frequency helpers ----

        /// <summary><c>numerator / denominator</c> as a <see cref="double"/> (requires <c>0 &lt;= numerator &lt;= denominator</c>, <c>denominator &gt; 0</c>).</summary>
        /// <remarks>
        /// A naive <c>(double)numerator / (double)denominator</c> can turn both sides into
        /// <see cref="double.PositiveInfinity"/> for counts beyond ~10^308, yielding
        /// <see cref="double.NaN"/>. Scaling by 2^64 before converting avoids that.
        /// </remarks>
        private static double Ratio(BigInteger numerator, BigInteger denominator)
        {
            if (numerator.IsZero)
            {
                return 0.0;
            }

            if (numerator == denominator)
            {
                return 1.0;
            }

            const int Scale = 64;

            return Math.ScaleB((double)((numerator << Scale) / denominator), -Scale);
        }

        // ---- Validation ----

        private static void EnsureWeightCount(ZddManager manager, int length, string paramName)
        {
            if (length != manager.VariableCount)
            {
                ThrowHelper.ThrowArgumentException(
                    paramName,
                    $"'{paramName}' must have one entry per variable ({manager.VariableCount}), but had {length}.");
            }
        }

        private static void EnsureProbabilities(ReadOnlySpan<double> probabilities)
        {
            for (int item = 0; item < probabilities.Length; item++)
            {
                double probability = probabilities[item];

                if (!(probability >= 0.0 && probability <= 1.0))
                {
                    // NaN also lands here, since every comparison with it is false.
                    ThrowHelper.ThrowArgumentOutOfRangeException(
                        nameof(probabilities),
                        $"'{nameof(probabilities)}[{item}]' must be in the range 0..1, but was {probability}.");
                }
            }
        }

        private static void EnsureNotEmpty(bool hasSet)
        {
            if (!hasSet)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    "The family holds no set, so there is nothing to optimize; check IsEmpty first.");
            }
        }

        private static void EnsureLandedOnTop(int id)
        {
            if (id != NodeTable.Top)
            {
                // A path landing on ⊥ yields no set; the DP only ever selects sides that have one.
                ThrowHelper.ThrowInvalidOperationException(
                    "The descent ended at the bottom terminal, which holds no set; the table and the diagram disagree.");
            }
        }

        private static bool TryValueOf<TWeight, TOps>(NodeOrder order, TWeight[] best, int childId, out TWeight value)
            where TOps : struct, IWeightOps<TWeight>
        {
            if (NodeTable.IsTerminal(childId))
            {
                // ⊤ is the empty set; ⊥ has no sets and so is never a candidate.
                value = TOps.Zero;
                return childId == NodeTable.Top;
            }

            value = best[order.SlotOf(childId)];
            return true;
        }

        private static void Append(ref int[] buffer, ref int length, int value)
        {
            if (length == buffer.Length)
            {
                Array.Resize(ref buffer, buffer.Length * 2);
            }

            buffer[length++] = value;
        }

        /// <summary>One entry in <see cref="TopK{TWeight, TOps}"/>'s table: a weight, plus where it came from.</summary>
        private readonly struct TopEntry<TWeight>
        {
            public TopEntry(TWeight weight, bool fromHi, int index)
            {
                Weight = weight;
                FromHi = fromHi;
                Index = index;
            }

            /// <summary>The weight of the set this entry represents (relative to this node's subfamily).</summary>
            public TWeight Weight { get; }

            /// <summary>Whether this entry came from the 1-edge side (i.e. includes this node's item).</summary>
            public bool FromHi { get; }

            /// <summary>This entry's rank within the child list it came from; meaningless if the child is a terminal.</summary>
            public int Index { get; }
        }
    }
}
