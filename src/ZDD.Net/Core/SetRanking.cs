using System;
using System.Numerics;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// 順位づけ（<see cref="Zdd.ElementAt(BigInteger, ZddEnumerationOrder)"/> /
    /// <see cref="Zdd.IndexOf(System.Collections.Generic.IEnumerable{int}, ZddEnumerationOrder)"/>）と
    /// 一様サンプリング（<see cref="Zdd.Sample(Random)"/>）の実装。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>これが ZDD の目玉である</b>（docs/PLAN.md §5.3）。列挙（<see cref="SetEnumeration"/>）は
    /// 先頭から順に舐めるしかないので、10^20 番目の集合を取り出すには 10^20 回ぶん辿ることになる。
    /// 一方、各ノードの部分濃度（<see cref="CardinalityTable"/>）を先に求めておけば、
    /// 根から 1 本の経路を降りるだけで <c>k</c> 番目の集合が出る: ノード <c>v</c> で
    /// 「0-枝の先に集合が <c>c</c> 個ある」と分かっていれば、<c>k &lt; c</c> なら 0-枝、
    /// そうでなければ <c>k</c> から <c>c</c> を引いて 1-枝、と決められるからである。
    /// 一様サンプリングはこの unranking に一様乱数を食わせるだけで済む。
    /// </para>
    /// <para>
    /// <b>順序は列挙と同じ</b>。<see cref="Zdd.ElementAt(BigInteger, ZddEnumerationOrder)"/> の
    /// <c>k</c> 番目は <see cref="Zdd.Sets(ZddEnumerationOrder)"/> の <c>k</c> 番目とぴったり一致する（同じ
    /// <see cref="ZddEnumerationOrder"/> を渡した場合）。一致しないと
    /// 「列挙で見た並びと ElementAt の番号が食い違う」ことになり、利用者は順位づけを信用できない。
    /// </para>
    /// <para>
    /// <b>2 つの順序で降り方が違う</b>。<see cref="ZddEnumerationOrder.Default"/>（0-枝優先）では
    /// ノードごとに 0-枝と 1-枝のどちらへ行くかを選ぶだけである。
    /// <see cref="ZddEnumerationOrder.Lexicographic"/>（列としての辞書順）では、
    /// 空集合が最小・以降は先頭要素の小さい順なので、ノード <c>n</c> の部分族は
    /// 「∅（あれば）」→「<c>n</c> の 1-枝から始まる集合たち」→「<c>n</c> の 0-枝の連なり上、
    /// 次のノードの 1-枝から始まる集合たち」→ … という並びになる。降りるときは
    /// 0-枝の連なりを根側から順に見て、順位が入るブロックを選ぶ。
    /// </para>
    /// <para>
    /// <b>計算量</b>: 表を作るのにノード数ぶんの足し算 1 回、そこから 1 つ取り出すのに
    /// O(変数の個数)。連なりを見て回る手間も、辿るたびに必ず 1 段下がるので変数の個数で抑えられる。
    /// <see cref="Sample(ZddManager, int, int, Random)"/> は表を 1 本だけ作って <c>n</c> 回引く。
    /// </para>
    /// <para>
    /// <b>再帰は書かない</b>（docs/PLAN.md §4.5）。もっとも、ここでの走査はそもそも
    /// 根から終端までの 1 本道であり、分岐を溜めるスタック自体が要らない。
    /// </para>
    /// </remarks>
    internal static class SetRanking
    {
        /// <summary>経路を溜める作業配列の初期の大きさ。足りなくなれば倍化する。</summary>
        private const int InitialPathCapacity = 16;

        /// <summary>
        /// 族の <paramref name="index"/> 番目（0 始まり）の集合を返す（unranking）。
        /// </summary>
        /// <param name="manager">族を所有するマネージャ。</param>
        /// <param name="rootId">族の根ノード ID。</param>
        /// <param name="index">取り出す集合の順位。0 以上、族の濃度未満。</param>
        /// <param name="order">順位の数え方（列挙の順序と同じ）。</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="order"/> が定義されていない値の場合、または <paramref name="index"/> が
        /// 範囲外の場合（空の族ではどんな値も範囲外になる）。
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="manager"/> が破棄済みの場合。
        /// </exception>
        public static int[] ElementAt(ZddManager manager, int rootId, BigInteger index, ZddEnumerationOrder order)
        {
            SetEnumeration.EnsureDefinedOrder(order);

            CardinalityTable table = CardinalityTable.Build(manager, rootId);
            BigInteger count = table.CountOf(rootId);

            if (index < BigInteger.Zero || index >= count)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(index),
                    count.IsZero
                        ? $"The family is empty, so no '{nameof(index)}' is valid; it was {index}."
                        : $"'{nameof(index)}' must be in the range 0..{count - BigInteger.One}, but was {index}.");
            }

            int[] path = new int[InitialPathCapacity];
            return Unrank(manager, table, rootId, index, order == ZddEnumerationOrder.Lexicographic, ref path);
        }

        /// <summary>
        /// <paramref name="items"/> が表す集合の順位を返す（ranking）。族に属さなければ -1。
        /// </summary>
        /// <param name="manager">族を所有するマネージャ。</param>
        /// <param name="rootId">族の根ノード ID。</param>
        /// <param name="items">調べる集合の item index。順不同で、同じ item が重なっていてもよい。</param>
        /// <param name="order">順位の数え方（列挙の順序と同じ）。</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="order"/> が定義されていない値の場合、または <paramref name="items"/> に
        /// 範囲外の item index が含まれる場合。
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="manager"/> が破棄済みの場合。
        /// </exception>
        public static BigInteger IndexOf(
            ZddManager manager,
            int rootId,
            ReadOnlySpan<int> items,
            ZddEnumerationOrder order)
        {
            SetEnumeration.EnsureDefinedOrder(order);

            // 破棄済みならここで ObjectDisposedException になる。
            NodeTable nodes = manager.Table.Nodes;

            int[] wanted = SortedDistinct(items);

            // 属するかどうかだけなら表は要らない。属さない集合に順位は無いので先に弾く。
            // 範囲外の item もここで（降り始める前に）弾かれる。この一手が
            // 「順位を数える側は必ず経路に乗っている」ことを保証し、下の 2 つを短く保つ。
            if (!QueryOperations.Contains(manager, rootId, wanted))
            {
                return BigInteger.MinusOne;
            }

            CardinalityTable table = CardinalityTable.Build(manager, rootId);

            return order == ZddEnumerationOrder.Lexicographic
                ? LexicographicRank(manager, nodes, table, rootId, wanted)
                : DefaultRank(manager, nodes, table, rootId, wanted);
        }

        /// <summary>族から集合を 1 つ、一様ランダムに選んで返す。</summary>
        /// <param name="manager">族を所有するマネージャ。</param>
        /// <param name="rootId">族の根ノード ID。</param>
        /// <param name="random">乱数の供給元。</param>
        /// <exception cref="ArgumentNullException"><paramref name="random"/> が <see langword="null"/> の場合。</exception>
        /// <exception cref="InvalidOperationException">族が空（集合を 1 つも持たない）の場合。</exception>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="manager"/> が破棄済みの場合。
        /// </exception>
        public static int[] Sample(ZddManager manager, int rootId, Random random)
        {
            ThrowHelper.ThrowIfNull(random, nameof(random));

            CardinalityTable table = CardinalityTable.Build(manager, rootId);
            BigInteger count = table.CountOf(rootId);
            EnsureNotEmpty(count);

            UniformBigInteger uniform = new UniformBigInteger(count);
            int[] path = new int[InitialPathCapacity];

            return Unrank(manager, table, rootId, uniform.Next(random), lexicographic: false, ref path);
        }

        /// <summary>族から集合を <paramref name="count"/> 個、一様ランダムに選んで返す（重複あり）。</summary>
        /// <param name="manager">族を所有するマネージャ。</param>
        /// <param name="rootId">族の根ノード ID。</param>
        /// <param name="count">取り出す個数。0 以上。</param>
        /// <param name="random">乱数の供給元。</param>
        /// <remarks>
        /// 1 回ずつ独立に引くので<b>同じ集合が 2 度出ることがある</b>（復元抽出）。
        /// 濃度の表は 1 本だけ作って使い回すので、<c>n</c> 回引く手間は
        /// 「表 1 本 ＋ n × O(変数の個数)」で済む。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="random"/> が <see langword="null"/> の場合。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> が負の場合。</exception>
        /// <exception cref="InvalidOperationException">族が空（集合を 1 つも持たない）の場合。</exception>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="manager"/> が破棄済みの場合。
        /// </exception>
        public static int[][] Sample(ZddManager manager, int rootId, int count, Random random)
        {
            ThrowHelper.ThrowIfNegative(count, nameof(count));
            ThrowHelper.ThrowIfNull(random, nameof(random));

            CardinalityTable table = CardinalityTable.Build(manager, rootId);
            BigInteger cardinality = table.CountOf(rootId);
            EnsureNotEmpty(cardinality);

            UniformBigInteger uniform = new UniformBigInteger(cardinality);
            int[] path = new int[InitialPathCapacity];
            int[][] result = new int[count][];

            for (int i = 0; i < count; i++)
            {
                result[i] = Unrank(manager, table, rootId, uniform.Next(random), lexicographic: false, ref path);
            }

            return result;
        }

        /// <summary>
        /// <paramref name="index"/> 番目の集合を、根から 1 本の経路を降りて組み立てる。
        /// </summary>
        /// <remarks>
        /// <paramref name="path"/> は経路を溜める作業配列で、足りなければ倍化して返す
        /// （何度も呼ぶ側が使い回せるように <c>ref</c> で受ける）。返す配列は毎回新しい。
        /// </remarks>
        private static int[] Unrank(
            ZddManager manager,
            CardinalityTable table,
            int rootId,
            BigInteger index,
            bool lexicographic,
            ref int[] path)
        {
            NodeTable nodes = manager.Table.Nodes;

            int length = 0;
            int id = rootId;

            while (!NodeTable.IsTerminal(id))
            {
                if (!lexicographic)
                {
                    // 0-枝優先の順では、0-枝の先の集合がすべて先に来る。
                    ZddNode node = nodes[id];
                    BigInteger loCount = table.CountOf(node.Lo);

                    if (index < loCount)
                    {
                        id = node.Lo;
                        continue;
                    }

                    index -= loCount;
                    Append(ref path, ref length, manager.ItemOf(node.Level));
                    id = node.Hi;
                    continue;
                }

                // 列としての辞書順では空列が最小なので、この部分族が空集合を持つならそれが先頭。
                if (table.HasEmptySet(id))
                {
                    if (index.IsZero)
                    {
                        return path.AsSpan(0, length).ToArray();
                    }

                    index -= BigInteger.One;
                }

                // 残りは「先頭要素が何か」で分かれる。0-枝の連なりを根側から順に見て、
                // 順位の入るブロック（その節の 1-枝）を選ぶ。
                while (true)
                {
                    if (NodeTable.IsTerminal(id))
                    {
                        // 順位は部分族の濃度未満なので、必ずどれかのブロックに入る。
                        ThrowHelper.ThrowInvalidOperationException(
                            $"The rank {index} ran past the end of the family while descending; the cardinality table and the diagram disagree.");
                    }

                    ZddNode node = nodes[id];
                    BigInteger hiCount = table.CountOf(node.Hi);

                    if (index < hiCount)
                    {
                        Append(ref path, ref length, manager.ItemOf(node.Level));
                        id = node.Hi;
                        break;
                    }

                    index -= hiCount;
                    id = node.Lo;
                }
            }

            if (id != NodeTable.Top)
            {
                // ⊥ に着く経路は集合を 1 つも生まない。順位を範囲内に限っている以上ここへは来ない。
                ThrowHelper.ThrowInvalidOperationException(
                    "The descent ended at the bottom terminal, which holds no set; the cardinality table and the diagram disagree.");
            }

            return path.AsSpan(0, length).ToArray();
        }

        /// <summary>
        /// 0-枝優先の順（<see cref="ZddEnumerationOrder.Default"/>）での順位を数える。
        /// </summary>
        /// <remarks>
        /// 降り方は <see cref="QueryOperations.Contains"/> と同じで、1-枝を選ぶたびに
        /// 「先に出てしまう集合の個数」＝ 0-枝の先の濃度を足していくだけである。
        /// 集合が族に属することは呼び出し側が確かめているので、ここでは経路を外れることはない。
        /// </remarks>
        private static BigInteger DefaultRank(
            ZddManager manager,
            NodeTable nodes,
            CardinalityTable table,
            int rootId,
            int[] wanted)
        {
            BigInteger rank = BigInteger.Zero;
            int next = 0;
            int id = rootId;

            while (!NodeTable.IsTerminal(id))
            {
                ZddNode node = nodes[id];
                int item = manager.ItemOf(node.Level);

                if (next < wanted.Length && wanted[next] == item)
                {
                    rank += table.CountOf(node.Lo);
                    next++;
                    id = node.Hi;
                    continue;
                }

                id = node.Lo;
            }

            return rank;
        }

        /// <summary>
        /// 列としての辞書順（<see cref="ZddEnumerationOrder.Lexicographic"/>）での順位を数える。
        /// </summary>
        /// <remarks>
        /// <see cref="Unrank"/> の逆をなぞる。ノード <c>n</c> では、空集合（あれば）が 1 つ先に出て、
        /// 続いて 0-枝の連なりの節ごとに「その item から始まる集合たち」が並ぶ。
        /// 欲しい集合の先頭要素と同じ item の節に着くまで、その手前のブロックの濃度を足していく。
        /// </remarks>
        private static BigInteger LexicographicRank(
            ZddManager manager,
            NodeTable nodes,
            CardinalityTable table,
            int rootId,
            int[] wanted)
        {
            BigInteger rank = BigInteger.Zero;
            int next = 0;
            int id = rootId;

            while (!NodeTable.IsTerminal(id))
            {
                if (next == wanted.Length)
                {
                    // 残りが空列なら、それがこの部分族の先頭。足すものは無い。
                    return rank;
                }

                if (table.HasEmptySet(id))
                {
                    rank += BigInteger.One;
                }

                while (true)
                {
                    ZddNode node = nodes[id];
                    int item = manager.ItemOf(node.Level);

                    if (item == wanted[next])
                    {
                        next++;
                        id = node.Hi;
                        break;
                    }

                    // この item から始まる集合たちは、欲しい集合より先に出る。
                    rank += table.CountOf(node.Hi);
                    id = node.Lo;
                }
            }

            return rank;
        }

        /// <summary>item を昇順に並べ、重なりを 1 つに潰す。</summary>
        /// <remarks>
        /// 集合としては同じ item が何度渡されても 1 つなので、先に均しておく。
        /// 昇順に並べておけば、根から葉へ向かって item が増える ZDD と同時に前進できる。
        /// </remarks>
        private static int[] SortedDistinct(ReadOnlySpan<int> items)
        {
            if (items.Length == 0)
            {
                return Array.Empty<int>();
            }

            int[] sorted = items.ToArray();
            Array.Sort(sorted);

            int length = 1;
            for (int i = 1; i < sorted.Length; i++)
            {
                if (sorted[i] != sorted[length - 1])
                {
                    sorted[length++] = sorted[i];
                }
            }

            return length == sorted.Length ? sorted : sorted.AsSpan(0, length).ToArray();
        }

        private static void EnsureNotEmpty(BigInteger count)
        {
            if (count.IsZero)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    "The family holds no set, so there is nothing to sample; check IsEmpty before sampling.");
            }
        }

        private static void Append(ref int[] buffer, ref int length, int value)
        {
            if (length == buffer.Length)
            {
                Array.Resize(ref buffer, buffer.Length * 2);
            }

            buffer[length++] = value;
        }
    }
}
