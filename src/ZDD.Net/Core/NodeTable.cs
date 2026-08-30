using System;
using System.Runtime.CompilerServices;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// ZDD ノードの物理的な格納庫。<see cref="ZddNode"/> を 1 本の配列に連続確保し、
    /// 満杯になったら容量を倍化する。ノードは配列 index と一致する <c>int</c> の ID で参照される。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ID <see cref="Bottom"/> (= 0) と <see cref="Top"/> (= 1) は終端として予約済みで、
    /// 実ノードの ID は <see cref="FirstNodeId"/> (= 2) から始まる。終端も配列上に
    /// 実体を持たせることで「ID = 配列 index」が常に成立し、参照時の減算が不要になる。
    /// </para>
    /// <para>
    /// この型は一意化（同じ <c>(level, lo, hi)</c> の共有）もゼロサプレス削減規則の適用も行わない。
    /// それらは一意化表（M1-2）の責務で、本型はその下に敷く「表」だけを提供する。
    /// </para>
    /// <para>
    /// 設計方針（docs/PLAN.md §4.1・§10）: <c>List&lt;T&gt;</c> や <c>Dictionary</c> を使わず生の配列で持つ、
    /// レベルごとに表を分割せず全体で 1 本にする、ID は <c>int</c> のままとし 64bit ID 版は作らない。
    /// </para>
    /// <para>
    /// <b>不変条件</b>: <c>FirstNodeId &lt;= _count &lt;= _nodes.Length</c> が常に成り立つ。
    /// <see cref="Grow"/> は配列を先に差し替えてから <c>_count</c> を進めるため、
    /// 途中でこれが破れる瞬間は無い。
    /// </para>
    /// <para>
    /// <b>メモリ安全性</b>: ノードへのアクセスは通常の配列インデクサで行い、
    /// <c>Unsafe.Add</c> による境界チェックの省略は<b>採らない</b>。
    /// <c>id &lt; _count</c> の確認（意味的な契約）と CLR の境界チェック（メモリ安全性の最後の砦）が
    /// 二重になるが、実測では最も差の出た走査パターンでも 3〜4%、書き込み経路と逐次走査では
    /// 誤差（≦2%）でしかなく、不変条件を 1 箇所壊しただけでヒープ破壊になる設計を
    /// ライブラリの土台に置く価値は無いと判断した。ここを詰めるのは
    /// BenchmarkDotNet でプロファイルを取ってから（docs/ROADMAP.md v0.4）で十分間に合う。
    /// </para>
    /// <para>
    /// <b>スレッド安全性</b>: この型は<b>スレッドセーフではない</b>。同一インスタンスへの
    /// <see cref="Add"/> と読み出しを複数スレッドから並行に行ってはならない。
    /// ただし境界チェックを省いていないため、誤って並行アクセスした場合の最悪ケースは
    /// 例外か読み取り値の不整合であって、ヒープ破壊ではない。
    /// 並列フロンティア構築（docs/PLAN.md §10-8, v0.4）ではスレッドごとに別インスタンスを持たせる。
    /// </para>
    /// </remarks>
    internal sealed class NodeTable
    {
        /// <summary>終端 ⊥（空集合族 ∅）を表す予約 ID。</summary>
        public const int Bottom = 0;

        /// <summary>終端 ⊤（<c>{∅}</c>）を表す予約 ID。</summary>
        public const int Top = 1;

        /// <summary>最初の実ノードに割り当てられる ID。予約済み終端の個数でもある。</summary>
        public const int FirstNodeId = 2;

        /// <summary><see cref="ZddNode.Next"/> が「次が無い」ことを表す番兵。</summary>
        public const int NoNext = -1;

        /// <summary>容量を明示しない場合の初期容量（終端 2 個分を含む）。</summary>
        public const int DefaultCapacity = 1024;

        /// <summary>
        /// ノード表が確保できる ID の上限。ID は <c>int</c> なので理論上限は 2^31 だが、
        /// 実際には配列長の上限（<see cref="Array.MaxLength"/>）の方が先に来るため、そちらを採る。
        /// 16 バイト × この個数 ≒ 32 GB。
        /// </summary>
        public static readonly int MaxCapacity = Array.MaxLength;

        /// <summary>
        /// 容量の上限。既定は <see cref="MaxCapacity"/>。上限到達時の例外をテストするために、
        /// internal なコンストラクタで小さい値へ差し替えられるようにしてある
        /// （実際に 2^31 個確保して検証することはできないため）。
        /// </summary>
        private readonly int _capacityLimit;

        private ZddNode[] _nodes;

        /// <summary>使用済みスロット数。終端 2 個を含むので、次に払い出す ID と一致する。</summary>
        private int _count;

        /// <summary>既定の初期容量でノード表を作る。</summary>
        public NodeTable()
            : this(DefaultCapacity, MaxCapacity)
        {
        }

        /// <summary>初期容量を指定してノード表を作る。</summary>
        /// <param name="initialCapacity">
        /// 初期容量。終端 2 個分を含むため <see cref="FirstNodeId"/> 以上でなければならない。
        /// </param>
        public NodeTable(int initialCapacity)
            : this(initialCapacity, MaxCapacity)
        {
        }

        /// <summary>初期容量と容量上限を指定してノード表を作る（上限到達時の挙動を試験するための入口）。</summary>
        /// <param name="initialCapacity">初期容量。<see cref="FirstNodeId"/> 以上。</param>
        /// <param name="capacityLimit">
        /// 容量の上限。<paramref name="initialCapacity"/> 以上かつ <see cref="MaxCapacity"/> 以下。
        /// </param>
        public NodeTable(int initialCapacity, int capacityLimit)
        {
            if (initialCapacity < FirstNodeId)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(initialCapacity),
                    $"'{nameof(initialCapacity)}' must be at least {FirstNodeId} to hold the reserved terminals, but was {initialCapacity}.");
            }

            if (capacityLimit < initialCapacity || capacityLimit > MaxCapacity)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(capacityLimit),
                    $"'{nameof(capacityLimit)}' must be between {initialCapacity} and {MaxCapacity}, but was {capacityLimit}.");
            }

            _capacityLimit = capacityLimit;
            _nodes = GC.AllocateUninitializedArray<ZddNode>(initialCapacity);
            _count = FirstNodeId;

            // 終端はゼロ初期化されない配列上に置かれるので、明示的に書き込む。
            // 終端はどの変数にも属さないのでレベル 0、枝は自分自身を指さず 0 のままにする。
            _nodes[Bottom] = new ZddNode { Level = 0, Lo = Bottom, Hi = Bottom, Next = NoNext };
            _nodes[Top] = new ZddNode { Level = 0, Lo = Bottom, Hi = Bottom, Next = NoNext };
        }

        /// <summary>確保済みの実ノード数（予約された終端 2 個は含まない）。</summary>
        public int Count => _count - FirstNodeId;

        /// <summary>次の <see cref="Add"/> が返す ID。終端を含めた使用済みスロット数でもある。</summary>
        public int NextId => _count;

        /// <summary>現在の容量（終端 2 個分を含む）。</summary>
        public int Capacity => _nodes.Length;

        /// <summary>この表が確保できる ID の上限。</summary>
        public int CapacityLimit => _capacityLimit;

        /// <summary>ID が予約済みの終端（⊥ または ⊤）かどうか。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsTerminal(int id) => (uint)id < FirstNodeId;

        /// <summary>
        /// ID からノードへの参照を得る。返り値は表の実体への <c>ref</c> なので、
        /// 経由して書き換えるとノード表に反映される。
        /// </summary>
        /// <remarks>
        /// リサイズで配列が差し替わると、それ以前に取得した <c>ref</c> は古い配列を指したままになる。
        /// <see cref="Add"/> を挟んで <c>ref</c> を保持しないこと。
        /// </remarks>
        /// <param name="id">0 以上 <see cref="NextId"/> 未満のノード ID。</param>
        public ref ZddNode this[int id]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if ((uint)id >= (uint)_count)
                {
                    ThrowHelper.ThrowArgumentOutOfRangeException(
                        nameof(id),
                        $"Node id {id} is out of range; the table currently holds ids 0..{_count - 1}.");
                }

                // 上のチェックは「まだ追加されていないスロット」を弾くための意味的な検査で、
                // メモリ安全性は配列インデクサ自身の境界チェックが担保する（型の remarks 参照）。
                return ref _nodes[id];
            }
        }

        /// <summary>
        /// ノードを 1 個追加し、その ID を返す。容量が尽きていれば倍化してから書き込む。
        /// </summary>
        /// <param name="level">変数レベル。1 以上（0 は終端の予約値）。</param>
        /// <param name="lo">0-枝の子 ID。既に存在する ID でなければならない。</param>
        /// <param name="hi">
        /// 1-枝の子 ID。既に存在する ID で、かつゼロサプレス削減規則より
        /// <see cref="Bottom"/> であってはならない。
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// ID 空間を使い切っている場合。上限は <see cref="CapacityLimit"/> だが、これは
        /// 予約済み終端 2 個を含めた ID の個数なので、実ノード数（<see cref="Count"/>）の上限は
        /// <c><see cref="CapacityLimit"/> - <see cref="FirstNodeId"/></c> になる。
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Add(int level, int lo, int hi)
        {
            ValidateNewNode(level, lo, hi);

            int id = _count;

            // 不変条件より等号でしか成立しないが、万一 _count が先走った場合でも
            // 黙って書き潰さないよう >= で受ける。
            if (id >= _nodes.Length)
            {
                Grow();
            }

            ref ZddNode node = ref _nodes[id];
            node.Level = level;
            node.Lo = lo;
            node.Hi = hi;
            node.Next = NoNext;

            _count = id + 1;
            return id;
        }

        private void ValidateNewNode(int level, int lo, int hi)
        {
            ThrowHelper.ThrowIfNegativeOrZero(level, nameof(level));

            if ((uint)lo >= (uint)_count)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(lo),
                    $"The lo child must be an existing node id (0..{_count - 1}), but was {lo}.");
            }

            if ((uint)hi >= (uint)_count)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(hi),
                    $"The hi child must be an existing node id (0..{_count - 1}), but was {hi}.");
            }

            if (hi == Bottom)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(hi),
                    "The hi child must not be the bottom terminal: a node whose 1-edge points to bottom is removed by the zero-suppressed reduction rule.");
            }
        }

        /// <summary>
        /// 容量を倍化する（上限に近ければ上限まで）。<c>Array.Resize</c> は内部で新配列をゼロ初期化するため、
        /// 未初期化確保 + コピーで済ませる。書き込むのは <see cref="Add"/> が使う分だけなので、
        /// 末尾のゴミが読まれることはない（<see cref="this[int]"/> が <see cref="NextId"/> で弾く）。
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Grow()
        {
            int capacity = _nodes.Length;
            if (capacity >= _capacityLimit)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    $"The node table has run out of ids: its limit of {_capacityLimit} ids " +
                    $"(including the {FirstNodeId} reserved terminals) is exhausted. " +
                    "Node ids are 32bit by design (docs/PLAN.md §4.1), so the diagram cannot grow any further.");
            }

            int newCapacity = capacity <= _capacityLimit / 2 ? capacity * 2 : _capacityLimit;

            // ZddNode は参照型フィールドを持たないため、未初期化のまま確保しても
            // GC が追跡すべき値は生じない（参照を含む型なら、この API でもゼロ初期化される）。
            ZddNode[] grown = GC.AllocateUninitializedArray<ZddNode>(newCapacity);
            Array.Copy(_nodes, grown, capacity);
            _nodes = grown;
        }
    }
}
