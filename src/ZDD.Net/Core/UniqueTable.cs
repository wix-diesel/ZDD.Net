using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// ノードの一意化表（unique table）。<c>(level, lo, hi)</c> の三つ組から一意なノード ID を返し、
    /// 同じ形の部分グラフが 2 個以上のノードに分かれないことを保証する。
    /// ゼロサプレス削減規則（<c>hi == ⊥</c> なら新ノードを作らず <c>lo</c> を返す）も
    /// <see cref="GetNode"/> 1 箇所だけで適用する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>正準形の唯一の入口</b>: ZDD のノードは必ずこの型を通して作る。
    /// 一意化と削減規則がここに閉じているので、以降の演算（M1-5 以降）は規則を意識せず
    /// <see cref="GetNode"/> を呼ぶだけで正準形を保てる。<see cref="NodeTable.Add"/> を
    /// 直接呼ぶと同形ノードの重複や削減規則違反が起こりうるため、演算からは呼ばない。
    /// </para>
    /// <para>
    /// <b>構造</b>（docs/PLAN.md §4.2）: オープンアドレス法（線形探索）で、スロット配列は
    /// 2 の冪サイズ。<c>Dictionary&lt;TKey, TValue&gt;</c> は使わない。キーが 3 個の <c>int</c> で
    /// あるためタプル化はボクシングか大きな struct を生み、比較器呼び出しとバケット→エントリの
    /// 2 段間接も加わる。ここは全演算の hot path なので、スロット配列 1 本（<c>int</c> のノード ID）と
    /// ノード表の連続配列だけで引ける形にする。レベルごとに表を分けず<b>全体で 1 表</b>とする
    /// （動的変数順序変更をやらないため、分割の利点が小さい）。
    /// </para>
    /// <para>
    /// <b>空きスロットの表現</b>: スロットには登録済みノードの ID を入れる。実ノードの ID は
    /// <see cref="NodeTable.FirstNodeId"/> (= 2) 以上なので、0（= <see cref="NodeTable.Bottom"/>）を
    /// 「空き」の番兵として使える。削除は無いので tombstone は不要で、線形探索は
    /// 空きスロットに当たった時点で打ち切れる。
    /// </para>
    /// <para>
    /// <b>倍化</b>: 負荷率 <see cref="MaxLoadFactorPercent"/>% を超えるとスロット配列を倍にして
    /// 全エントリを再ハッシュする。再ハッシュはスロット配列だけを作り直し、ノード表には触れないので、
    /// <b>それ以前に得たノード ID は倍化後も同じノードを指し続ける</b>。
    /// </para>
    /// <para>
    /// <b>スレッド安全性</b>: <see cref="NodeTable"/> と同じくスレッドセーフではない。
    /// </para>
    /// </remarks>
    internal sealed class UniqueTable
    {
        /// <summary>スロット配列を倍化する負荷率（%）。docs/PLAN.md §4.2 の 0.7。</summary>
        public const int MaxLoadFactorPercent = 70;

        /// <summary>スロット配列の最小サイズ。2 の冪。</summary>
        public const int MinimumCapacity = 4;

        /// <summary>スロット配列の既定の初期サイズ。2 の冪。</summary>
        public const int DefaultCapacity = 1024;

        /// <summary>
        /// スロット配列の最大サイズ。<see cref="Array.MaxLength"/> 以下で最大の 2 の冪。
        /// この時点で負荷率 70% でも 7.5 億エントリ格納できるため、実際には
        /// <see cref="NodeTable"/> の ID 上限の方が先に来る。
        /// </summary>
        public const int MaxCapacity = 1 << 30;

        /// <summary>「このスロットは空き」を表す番兵。実ノードの ID は 2 以上なので 0 を使える。</summary>
        private const int EmptySlot = NodeTable.Bottom;

        private readonly NodeTable _nodes;

        /// <summary>スロット → ノード ID。長さは常に 2 の冪。</summary>
        private int[] _slots;

        /// <summary>登録済みエントリ数（= <c>_nodes.Count</c>）。</summary>
        private int _count;

        /// <summary>この数を超えた時点で倍化する。<c>_slots.Length * 70 / 100</c>。</summary>
        private int _growThreshold;

        /// <summary>既定の初期容量で、新しいノード表の上に一意化表を作る。</summary>
        public UniqueTable()
            : this(new NodeTable(), DefaultCapacity)
        {
        }

        /// <summary>初期容量を指定して、新しいノード表の上に一意化表を作る。</summary>
        /// <param name="initialCapacity">スロット配列の初期サイズ。2 の冪に切り上げられる。</param>
        public UniqueTable(int initialCapacity)
            : this(new NodeTable(), initialCapacity)
        {
        }

        /// <summary>既存のノード表の上に一意化表を作る。</summary>
        /// <param name="nodes">ノードの格納先。<see langword="null"/> 不可。</param>
        /// <param name="initialCapacity">
        /// スロット配列の初期サイズ。<see cref="MinimumCapacity"/> 以上 <see cref="MaxCapacity"/> 以下の
        /// 2 の冪に切り上げられる。
        /// </param>
        /// <remarks>
        /// <paramref name="nodes"/> は空である必要がある。既にノードを持つ表を渡すと、
        /// それらは一意化表に登録されていないため同形ノードが重複しうる。
        /// </remarks>
        public UniqueTable(NodeTable nodes, int initialCapacity)
        {
            ThrowHelper.ThrowIfNull(nodes, nameof(nodes));
            ThrowHelper.ThrowIfNegativeOrZero(initialCapacity, nameof(initialCapacity));

            if (initialCapacity > MaxCapacity)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(initialCapacity),
                    $"'{nameof(initialCapacity)}' must not exceed {MaxCapacity}, but was {initialCapacity}.");
            }

            if (nodes.Count != 0)
            {
                ThrowHelper.ThrowArgumentException(
                    nameof(nodes),
                    $"The node table must be empty when a unique table is built on top of it, but it already holds {nodes.Count} node(s).");
            }

            int capacity = Math.Max(MinimumCapacity, (int)BitOperations.RoundUpToPowerOf2((uint)initialCapacity));

            _nodes = nodes;
            _slots = new int[capacity];
            _count = 0;
            _growThreshold = ComputeGrowThreshold(capacity);
        }

        /// <summary>この一意化表が使っているノード表。</summary>
        public NodeTable Nodes => _nodes;

        /// <summary>登録済みノード数（終端は含まない）。</summary>
        public int Count => _count;

        /// <summary>現在のスロット配列のサイズ（2 の冪）。</summary>
        public int Capacity => _slots.Length;

        /// <summary>この数を超えたエントリ数になった時点で倍化が走る。</summary>
        public int GrowThreshold => _growThreshold;

        /// <summary>
        /// <c>(level, lo, hi)</c> に対応するノード ID を返す。同じ三つ組に対しては常に同じ ID を返す。
        /// </summary>
        /// <param name="level">変数レベル。1 以上で、<paramref name="lo"/>/<paramref name="hi"/> のレベルより大きい。</param>
        /// <param name="lo">0-枝の子ノード ID。</param>
        /// <param name="hi">1-枝の子ノード ID。</param>
        /// <returns>
        /// <paramref name="hi"/> が <see cref="NodeTable.Bottom"/> なら
        /// <paramref name="lo"/> そのもの（ゼロサプレス削減規則）。
        /// そうでなければ既存の同形ノード、無ければ新しく確保したノードの ID。
        /// </returns>
        public int GetNode(int level, int lo, int hi)
        {
            // ゼロサプレス削減規則: 1-枝が ⊥ を指すノードは「その変数を含む組合せが 1 つも無い」
            // ことを意味し、部分集合族としては 0-枝の側と等しい。ノードを作らずに lo を返す。
            if (hi == NodeTable.Bottom)
            {
                AssertChild(level, lo, nameof(lo));
                return lo;
            }

            AssertChild(level, lo, nameof(lo));
            AssertChild(level, hi, nameof(hi));

            int[] slots = _slots;
            int mask = slots.Length - 1;
            int slot = Hashing.IndexFor(Hashing.Combine(level, lo, hi), slots.Length);

            while (true)
            {
                int id = slots[slot];
                if (id == EmptySlot)
                {
                    break;
                }

                ref ZddNode node = ref _nodes[id];
                if (node.Level == level && node.Lo == lo && node.Hi == hi)
                {
                    return id;
                }

                slot = (slot + 1) & mask;
            }

            // 空きスロットまで来た = 未登録。先に倍化判定を済ませる（倍化するとスロットが変わるため、
            // 探索結果は使えなくなる）。ノード表への確保は倍化の後で行い、
            // 確保に失敗した場合にスロットだけ埋まった状態にならないようにする。
            if (_count + 1 > _growThreshold)
            {
                Grow();
                slot = FindEmptySlot(level, lo, hi);
            }

            int newId = _nodes.Add(level, lo, hi);
            _slots[slot] = newId;
            _count++;
            return newId;
        }

        /// <summary>
        /// <c>(level, lo, hi)</c> が既に登録されていればその ID を返す。ノードの新規確保は行わない。
        /// </summary>
        /// <returns>登録されていれば <see langword="true"/>。</returns>
        public bool TryGetExisting(int level, int lo, int hi, out int id)
        {
            int[] slots = _slots;
            int mask = slots.Length - 1;
            int slot = Hashing.IndexFor(Hashing.Combine(level, lo, hi), slots.Length);

            while (true)
            {
                int candidate = slots[slot];
                if (candidate == EmptySlot)
                {
                    id = NodeTable.Bottom;
                    return false;
                }

                ref ZddNode node = ref _nodes[candidate];
                if (node.Level == level && node.Lo == lo && node.Hi == hi)
                {
                    id = candidate;
                    return true;
                }

                slot = (slot + 1) & mask;
            }
        }

        /// <summary>
        /// <c>(level, lo, hi)</c> が入るべき空きスロットを線形探索で求める。
        /// 未登録であることが確定している場合にのみ呼ぶ（一致判定を行わない）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindEmptySlot(int level, int lo, int hi)
        {
            int[] slots = _slots;
            int mask = slots.Length - 1;
            int slot = Hashing.IndexFor(Hashing.Combine(level, lo, hi), slots.Length);

            while (slots[slot] != EmptySlot)
            {
                slot = (slot + 1) & mask;
            }

            return slot;
        }

        /// <summary>
        /// スロット配列を倍化し、全エントリを再ハッシュする。ノード表には触れないので、
        /// 既に払い出したノード ID は変わらない。
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Grow()
        {
            int[] old = _slots;
            int capacity = old.Length;

            if (capacity >= MaxCapacity)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    $"The unique table cannot grow beyond {MaxCapacity} slots, which is the largest power of two " +
                    $"that fits in an array. It currently holds {_count} node(s).");
            }

            int newCapacity = capacity * 2;
            int[] grown = new int[newCapacity];
            int mask = newCapacity - 1;

            for (int i = 0; i < old.Length; i++)
            {
                int id = old[i];
                if (id == EmptySlot)
                {
                    continue;
                }

                ref ZddNode node = ref _nodes[id];
                int slot = Hashing.IndexFor(Hashing.Combine(node.Level, node.Lo, node.Hi), newCapacity);
                while (grown[slot] != EmptySlot)
                {
                    slot = (slot + 1) & mask;
                }

                grown[slot] = id;
            }

            _slots = grown;
            _growThreshold = ComputeGrowThreshold(newCapacity);
        }

        private static int ComputeGrowThreshold(int capacity) =>
            (int)((long)capacity * MaxLoadFactorPercent / 100);

        /// <summary>
        /// 子ノードの水準が親より真に小さいことを Debug ビルドで表明する。
        /// 変数順序が守られていない呼び出しは、そのままだと正準形が壊れた ZDD として
        /// ずっと後の演算で表面化するため、生成時点で落とす。
        /// </summary>
        [Conditional("DEBUG")]
        private void AssertChild(int level, int child, string name)
        {
            Debug.Assert(level > 0, $"The level must be positive, but was {level}.");
            Debug.Assert(
                (uint)child < (uint)_nodes.NextId,
                $"The {name} child must be an existing node id (0..{_nodes.NextId - 1}), but was {child}.");
            Debug.Assert(
                _nodes[child].Level < level,
                $"The {name} child (id {child}, level {_nodes[child].Level}) must sit strictly below level {level}.");
        }
    }
}
