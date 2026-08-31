using System;
using System.Numerics;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// 重み最適化（<see cref="Zdd.MaxWeight{TWeight, TOps}"/> / <see cref="Zdd.MinWeight{TWeight, TOps}"/> /
    /// <see cref="Zdd.TopK{TWeight, TOps}"/>）と、確率・期待値・頻度
    /// （<see cref="Zdd.Probability"/> / <see cref="Zdd.ExpectedValue"/> /
    /// <see cref="Zdd.ItemFrequency"/>）の実装。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>「全部並べてから選ぶ」ことをしない</b>のがここの要点である（docs/PLAN.md §5.3）。
    /// ZDD は DAG なので、根から終端 ⊤ までの経路 1 本が集合 1 つに対応する。
    /// すると「重みが最大の集合」は<b>DAG 上の最長路</b>そのもので、ノードを 1 度ずつ見る DP で求まる。
    /// 集合が 10^24 個あっても、見るのはノードの個数ぶんだけである。
    /// </para>
    /// <para>
    /// <b>確率は「族の中身」ではなく「宇宙全体」で考える</b>。<see cref="Probability"/> は
    /// 「各 item が独立に確率 <c>p[i]</c> で選ばれたとき、出来上がった集合が族に属する確率」で、
    /// 属さない item が<b>選ばれなかった</b>確率 <c>1 - p[i]</c> も掛かる。
    /// 辺が確率 p で生きているときの s–t 連結確率（ネットワーク信頼性）がまさにこの形になる。
    /// </para>
    /// <para>
    /// <b>期待値と頻度は「族の上の一様分布」で考える</b>。<see cref="ItemFrequency"/> は
    /// 「族から集合を 1 つ一様に選んだとき、item <c>i</c> がそこに入っている確率」であり、
    /// <see cref="ExpectedValue"/> はその重み付き和である。<see cref="Zdd.Sample(Random)"/> が
    /// 返す集合の統計を、実際にサンプリングせずに求めたものだと読める。
    /// <see cref="Probability"/> とは分布そのものが違うので、値も一致しない。
    /// </para>
    /// <para>
    /// <b>再帰は書かない</b>（docs/PLAN.md §4.5）。走査は <see cref="NodeOrder"/> が作る
    /// 「子が親より先に来る並び」の上の <c>for</c> ループで、深さ 10 万でもスタックを消費しない。
    /// </para>
    /// </remarks>
    internal static class WeightOperations
    {
        /// <summary>
        /// 重みが最大（<paramref name="maximize"/>）または最小の集合を、その重みとともに返す。
        /// </summary>
        /// <typeparam name="TWeight">重みの型。</typeparam>
        /// <typeparam name="TOps">
        /// 重みの演算。<b><c>struct</c> でなければならない</b>（docs/PLAN.md §10-2）。
        /// </typeparam>
        /// <param name="manager">族を所有するマネージャ。</param>
        /// <param name="rootId">族の根ノード ID。</param>
        /// <param name="weights">item ごとの重み。長さはマネージャの変数の個数と等しいこと。</param>
        /// <param name="maximize">最大化なら <see langword="true"/>、最小化なら <see langword="false"/>。</param>
        /// <remarks>
        /// <para>
        /// <b>漸化式</b>: ノード <c>v</c>（item <c>i</c>）以下の部分族の最適値は
        /// 「0-枝側の最適値」と「1-枝側の最適値 ＋ <c>w[i]</c>」の良い方。⊤ は空集合 1 つなので
        /// <c>Zero</c>、⊥ は集合を持たないので候補にならない。どちらを選んだかを覚えておけば、
        /// 根から 1 本降りるだけで最適集合そのものが復元できる。
        /// </para>
        /// <para>
        /// <b>同点のとき</b>は 0-枝側（item を含まない側）を選ぶ。したがって同じ重みの集合が
        /// 複数あるときに返るのは、既定の列挙順（<see cref="ZddEnumerationOrder.Default"/>）で
        /// 最初に来るものである。
        /// </para>
        /// <para>
        /// <b>計算量</b>: 到達できるノード数を <c>m</c>、変数の個数を <c>n</c> として、
        /// 比較と加算が <c>O(m)</c> 回、復元が <c>O(n)</c>。作業メモリは <c>O(m)</c>。
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// <paramref name="weights"/> の長さが変数の個数と違う場合。
        /// </exception>
        /// <exception cref="InvalidOperationException">族が空（集合を 1 つも持たない）の場合。</exception>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="manager"/> が破棄済みの場合。
        /// </exception>
        public static WeightedSet<TWeight> Optimize<TWeight, TOps>(
            ZddManager manager,
            int rootId,
            ReadOnlySpan<TWeight> weights,
            bool maximize)
            where TOps : struct, IWeightOps<TWeight>
        {
            // 破棄済みならここで ObjectDisposedException になる。
            NodeTable nodes = manager.Table.Nodes;
            EnsureWeightCount(manager, weights.Length, nameof(weights));

            if (NodeTable.IsTerminal(rootId))
            {
                EnsureNotEmpty(rootId != NodeTable.Bottom);

                // {∅} の唯一の集合は空集合。重みは加法の単位元。
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
                    // ゼロサプレス削減規則により 1-枝が ⊥ に落ちるノードは存在しない。
                    ThrowHelper.ThrowInvalidOperationException(
                        $"The node {id} has the bottom terminal on its 1-edge, which the zero-suppress rule forbids.");
                }

                // 1-枝の先の集合はどれも item を含むので、その重みが乗る。
                hiValue = TOps.Add(hiValue, weights[item]);

                // 同点は 0-枝側（item を含まない側）を採る。
                takeHi[slot] = !hasLo
                    || (maximize ? TOps.Compare(hiValue, loValue) > 0 : TOps.Compare(hiValue, loValue) < 0);

                best[slot] = takeHi[slot] ? hiValue : loValue;
            }

            return new WeightedSet<TWeight>(
                best[order.SlotOf(rootId)],
                Descend(manager, nodes, order, rootId, takeHi));
        }

        /// <summary>
        /// 重みが大きい順に <paramref name="k"/> 個の集合を返す。
        /// </summary>
        /// <typeparam name="TWeight">重みの型。</typeparam>
        /// <typeparam name="TOps">
        /// 重みの演算。<b><c>struct</c> でなければならない</b>（docs/PLAN.md §10-2）。
        /// </typeparam>
        /// <param name="manager">族を所有するマネージャ。</param>
        /// <param name="rootId">族の根ノード ID。</param>
        /// <param name="weights">item ごとの重み。長さはマネージャの変数の個数と等しいこと。</param>
        /// <param name="k">取り出す個数。0 以上。族の濃度より大きければ、ある分だけ返る。</param>
        /// <remarks>
        /// <para>
        /// <b>漸化式</b>: <see cref="Optimize{TWeight, TOps}"/> の「良い方を 1 つ選ぶ」を
        /// 「良い方から k 個まで残す」に広げたもの。ノードごとに、0-枝側の上位 k 個と
        /// 1-枝側の上位 k 個（それぞれ <c>w[i]</c> を足したもの）を<b>整列済みのまま併合</b>して
        /// 先頭 k 個を採る。どちらの枝の何番目から来たかを覚えておけば、根の <c>j</c> 番目から
        /// 1 本降りるだけで集合が復元できる。
        /// </para>
        /// <para>
        /// <b>計算量</b>: 到達できるノード数を <c>m</c>、変数の個数を <c>n</c> として、
        /// 時間 <c>O(m · k + k · n)</c>、メモリ <c>O(m · k)</c>。
        /// <b><c>k</c> に比例してノード 1 個あたりの費用が増える</b>ので、<c>k</c> が大きいときは
        /// 素直に重い。上位いくつかが要るだけなら小さい <c>k</c> で呼ぶこと。
        /// 全部を重み順に並べたいなら、<see cref="Zdd.Sets(ZddEnumerationOrder)"/> を
        /// 並べ替えるほうが軽い（族が並べ替えられる大きさなら、の話ではある）。
        /// </para>
        /// <para>
        /// <b>同じ重みの集合が複数あるとき</b>、どの集合が何番目に来るかは規定しない。
        /// 規定するのは<b>重みの並び</b>だけで、これは全列挙を降順に並べた先頭 <c>k</c> 個と必ず一致する。
        /// 実装としては、同点なら 0-枝側（item を含まない側）が先に来る。
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// <paramref name="weights"/> の長さが変数の個数と違う場合。
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="k"/> が負の場合。</exception>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="manager"/> が破棄済みの場合。
        /// </exception>
        public static WeightedSet<TWeight>[] TopK<TWeight, TOps>(
            ZddManager manager,
            int rootId,
            ReadOnlySpan<TWeight> weights,
            int k)
            where TOps : struct, IWeightOps<TWeight>
        {
            // 破棄済みならここで ObjectDisposedException になる。
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

            // 終端 ⊤ の「上位 k 個」は空集合 1 つだけ。⊥ は 1 つも持たない。
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

        /// <summary>
        /// 各 item が独立に確率 <paramref name="probabilities"/> で選ばれるとき、
        /// 出来上がる集合が族に属する確率を返す。
        /// </summary>
        /// <param name="manager">族を所有するマネージャ。</param>
        /// <param name="rootId">族の根ノード ID。</param>
        /// <param name="probabilities">item ごとの確率。長さは変数の個数と等しく、各値は 0 以上 1 以下。</param>
        /// <remarks>
        /// <para>
        /// <b>宇宙はマネージャの全変数</b>（docs/OPEN-QUESTIONS.md B8 と同じ立場）。すなわち
        /// <c>Σ_{A ∈ F} Π_{i ∈ A} p[i] · Π_{i ∉ A} (1 - p[i])</c> であり、
        /// 族に一度も現れない item の「選ばれなかった確率」も掛かる。
        /// 族が空なら 0、族が冪集合 2^U なら（どの集合も属するので）1 になる。
        /// </para>
        /// <para>
        /// <b>飛ばされた段を補う必要がある</b>のはこの定義のためである。ZDD は
        /// ゼロサプレス削減規則により「その部分族のどの集合にも属さない item」の段を持たない。
        /// 段が飛んでいるということは<b>その item が必ず選ばれていない</b>ということなので、
        /// 子へ降りるたびに、飛ばされた item の <c>1 - p[j]</c> を掛ける。根より上の段も同じ。
        /// これを忘れると、確率にならない別の量（各経路の確率の和が 1 にならないもの）が出る。
        /// </para>
        /// <para>
        /// <b>計算量</b>: 到達できるノード数を <c>m</c>、変数の個数を <c>n</c> として
        /// <c>O(m + 飛ばされた段の総数)</c>、最悪でも <c>O(m · n)</c>。
        /// 段が飛ぶのは「その部分族が使っていない変数」だけなので、実際の族ではほぼ <c>O(m)</c> である。
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// <paramref name="probabilities"/> の長さが変数の個数と違う場合。
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="probabilities"/> に 0 未満・1 超・<see cref="double.NaN"/> が含まれる場合。
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="manager"/> が破棄済みの場合。
        /// </exception>
        public static double Probability(ZddManager manager, int rootId, ReadOnlySpan<double> probabilities)
        {
            // 破棄済みならここで ObjectDisposedException になる。
            NodeTable nodes = manager.Table.Nodes;
            EnsureWeightCount(manager, probabilities.Length, nameof(probabilities));
            EnsureProbabilities(probabilities);

            if (NodeTable.IsTerminal(rootId))
            {
                // ∅ には集合が無いので 0。{∅} は「どの item も選ばれない」確率そのもの。
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

            // 根より上の段（族のどの集合にも属さない item）も「選ばれなかった」ぶんを掛ける。
            int rootItem = manager.ItemOf(nodes[rootId].Level);

            return probability[order.SlotOf(rootId)] * AbsentProduct(probabilities, 0, rootItem);
        }

        /// <summary>
        /// 族から集合を 1 つ一様に選んだとき、item <c>i</c> がその集合に属する確率を item ごとに返す。
        /// </summary>
        /// <param name="manager">族を所有するマネージャ。</param>
        /// <param name="rootId">族の根ノード ID。</param>
        /// <remarks>
        /// <para>
        /// <b>数え方</b>: item <c>i</c> を含む集合の個数は、item <c>i</c> のノード <c>v</c> ごとに
        /// 「根から <c>v</c> まで降りてくる経路の本数」×「<c>v</c> の 1-枝の先にある集合の個数」を
        /// 足したものである。前者は根から葉へ、後者は葉から根への DP で、どちらもノードを
        /// 1 度ずつ見れば済む（後者は <see cref="CardinalityTable"/> そのもの）。
        /// </para>
        /// <para>
        /// <b>整数で数えてから割る</b>。個数は変数の個数に対して指数的に増えるので、
        /// 途中は <see cref="BigInteger"/> で厳密に数え、最後に <see cref="double"/> の比にする。
        /// 途中で <see cref="double"/> にすると、10^24 個規模の族で下位の桁が失われる。
        /// </para>
        /// <para>
        /// <b>計算量</b>: 到達できるノード数を <c>m</c> として、<see cref="BigInteger"/> の
        /// 加算・乗算が <c>O(m)</c> 回。
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">族が空（集合を 1 つも持たない）の場合。</exception>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="manager"/> が破棄済みの場合。
        /// </exception>
        public static double[] ItemFrequency(ZddManager manager, int rootId)
        {
            // 破棄済みならここで ObjectDisposedException になる。
            NodeTable nodes = manager.Table.Nodes;

            EnsureNotEmpty(rootId != NodeTable.Bottom);

            double[] frequency = new double[manager.VariableCount];

            if (NodeTable.IsTerminal(rootId))
            {
                // {∅} の唯一の集合は空集合なので、どの item も入っていない。
                return frequency;
            }

            NodeOrder order = NodeOrder.Build(manager, rootId);
            CardinalityTable cardinality = CardinalityTable.Build(manager, rootId);

            BigInteger[] paths = new BigInteger[order.Count];
            BigInteger[] containing = new BigInteger[manager.VariableCount];

            paths[order.SlotOf(rootId)] = BigInteger.One;

            // 末尾が根。末尾から先頭へ回すと、親の本数が確定してから子に配れる。
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

                // このノードで 1-枝を選んだ経路は、どれも item を含む集合になる。
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

        /// <summary>
        /// 族から集合を 1 つ一様に選んだときの、その集合の重みの期待値を返す。
        /// </summary>
        /// <param name="manager">族を所有するマネージャ。</param>
        /// <param name="rootId">族の根ノード ID。</param>
        /// <param name="weights">item ごとの重み。長さは変数の個数と等しいこと。</param>
        /// <remarks>
        /// <para>
        /// <b>期待値の線形性</b>そのもの: <c>E[Σ_{i ∈ A} w[i]] = Σ_i w[i] · P(i ∈ A)</c> なので、
        /// <see cref="ItemFrequency"/> との内積で求まる。集合を 1 つずつ数え上げる必要は無い。
        /// </para>
        /// <para>
        /// <b>重みが <see cref="double"/> 固定</b>なのは、期待値には割り算が要るためである。
        /// <see cref="IWeightOps{TWeight}"/> が求めるのは「0・足す・比べる」の 3 つだけで、
        /// 割り算は含まない（含めると有理数や辞書順タプルのような重みが乗らなくなる）。
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// <paramref name="weights"/> の長さが変数の個数と違う場合。
        /// </exception>
        /// <exception cref="InvalidOperationException">族が空（集合を 1 つも持たない）の場合。</exception>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="manager"/> が破棄済みの場合。
        /// </exception>
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

        // ---- 最適集合の復元 ----

        /// <summary>
        /// <see cref="Optimize{TWeight, TOps}"/> が覚えた選択に沿って、根から 1 本降りて集合を組み立てる。
        /// </summary>
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

        /// <summary>
        /// <see cref="TopK{TWeight, TOps}"/> の順位 <paramref name="rank"/> の集合を、
        /// 根から 1 本降りて組み立てる。
        /// </summary>
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

        // ---- 上位 k 個の併合 ----

        /// <summary>
        /// 0-枝側と 1-枝側の「上位 k 個」を、整列を保ったまま併合して先頭 <paramref name="k"/> 個を返す。
        /// </summary>
        /// <remarks>
        /// 1-枝側の重みには <paramref name="itemWeight"/> が乗る（その先の集合はどれも item を含むため）。
        /// 同点なら 0-枝側を先に採る。
        /// </remarks>
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

        /// <summary>子の「上位 k 個」を引く。終端は表に入っていないので、その場で答える。</summary>
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

        // ---- 確率の補助 ----

        /// <summary>
        /// 子の確率を、飛ばされた段（<paramref name="from"/> から子の item の手前まで）の
        /// 「選ばれなかった」確率で持ち上げる。
        /// </summary>
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
                // ⊤ に着いた ＝ 残りの item はどれも選ばれていない。⊥ は起こりえない選び方。
                return childId == NodeTable.Top
                    ? AbsentProduct(probabilities, from, probabilities.Length)
                    : 0.0;
            }

            int childItem = manager.ItemOf(nodes[childId].Level);

            return probability[order.SlotOf(childId)] * AbsentProduct(probabilities, from, childItem);
        }

        /// <summary>
        /// <c>Π_{j = from}^{toExclusive - 1} (1 - p[j])</c>。飛ばされた段を補うための積。
        /// </summary>
        private static double AbsentProduct(ReadOnlySpan<double> probabilities, int from, int toExclusive)
        {
            double product = 1.0;

            for (int item = from; item < toExclusive; item++)
            {
                product *= 1.0 - probabilities[item];
            }

            return product;
        }

        // ---- 頻度の補助 ----

        /// <summary>
        /// <c>numerator / denominator</c> を <see cref="double"/> にする
        /// （<c>0 ≤ numerator ≤ denominator</c>、<c>denominator &gt; 0</c> が前提）。
        /// </summary>
        /// <remarks>
        /// <c>(double)numerator / (double)denominator</c> と書くと、10^308 を超える個数で
        /// 両辺とも <see cref="double.PositiveInfinity"/> になり、比が <see cref="double.NaN"/> に化ける。
        /// 先に整数のまま 2^64 倍して割れば、商は必ず 2^64 以下に収まり、
        /// 仮数部（53bit）より細かい精度で比が得られる。
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

        // ---- 検証 ----

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
                    // NaN もここに落ちる（どの比較も偽になるため）。
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
                // ⊥ に着く経路は集合を 1 つも生まない。DP は集合のある側しか選ばないのでここへは来ない。
                ThrowHelper.ThrowInvalidOperationException(
                    "The descent ended at the bottom terminal, which holds no set; the table and the diagram disagree.");
            }
        }

        private static bool TryValueOf<TWeight, TOps>(NodeOrder order, TWeight[] best, int childId, out TWeight value)
            where TOps : struct, IWeightOps<TWeight>
        {
            if (NodeTable.IsTerminal(childId))
            {
                // ⊤ は空集合 1 つ。⊥ は集合を持たないので、そもそも候補にならない。
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

        /// <summary>
        /// <see cref="TopK{TWeight, TOps}"/> の表に入る 1 件。重みと、どこから来たかを覚える。
        /// </summary>
        private readonly struct TopEntry<TWeight>
        {
            public TopEntry(TWeight weight, bool fromHi, int index)
            {
                Weight = weight;
                FromHi = fromHi;
                Index = index;
            }

            /// <summary>この件が表す集合の重み（このノード以下の部分族としての重み）。</summary>
            public TWeight Weight { get; }

            /// <summary>1-枝側から来たかどうか（＝このノードの item を含むかどうか）。</summary>
            public bool FromHi { get; }

            /// <summary>来た側の子の表での順位。子が終端なら意味を持たない。</summary>
            public int Index { get; }
        }
    }
}
