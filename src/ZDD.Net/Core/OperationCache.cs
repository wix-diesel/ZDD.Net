using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// 演算結果のメモ表。<c>(演算, オペランド)</c> から結果ノード ID を引く
    /// direct-mapped lossy cache（CUDD 流）で、衝突したら無条件に上書きする。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>速度のためではなく計算量のための部品</b>: ZDD の二項演算は「同じ部分問題に何度も到達する」
    /// 再帰なので、結果を使い回さないと DAG のサイズではなく<b>パスの本数</b>に比例した時間になり、
    /// 指数的に退化する。この表はその再訪を定数時間で潰すためのものであって、
    /// 外すと遅くなるだけの高速化オプションではない。
    /// </para>
    /// <para>
    /// <b>lossy で良い理由</b>: エントリを失っても失われるのは「計算済み」という事実だけで、
    /// 呼び出し側は同じ部分問題をもう一度計算して同じ答えを得る。したがって
    /// <b>キャッシュの中身は結果の正しさに一切影響しない</b>。この性質があるので、
    /// チェーンも探索も追い出し方針も持たず、スロット 1 個だけを見て終わりにできる
    /// （docs/PLAN.md §4.3）。サイズ変更時に中身を捨てられるのも同じ理由による。
    /// </para>
    /// <para>
    /// <b>誤ヒットが起きない</b>: 添字は <c>(op, a, b)</c> の 64bit ハッシュから作るが、
    /// エントリには <see cref="Entry.Op"/> と、2 つのオペランドを 32bit ずつ<b>そのまま</b>
    /// 詰めた <see cref="Entry.Key"/> を保持し、ヒット判定はこの 2 つの完全一致で行う。
    /// キーは切り詰められていないので、ハッシュが衝突しても別の部分問題の結果を返すことはない
    /// （ヒットしないだけ）。
    /// </para>
    /// <para>
    /// <b>可換演算の正規化</b>: <see cref="ZddOperations.IsCommutative"/> が真の演算では
    /// <c>a &gt; b</c> のときオペランドを入れ替えてからキーにする。<c>f ∪ g</c> と <c>g ∪ f</c> が
    /// 同じエントリを共有するので、実効的なヒット率が上がる。
    /// </para>
    /// <para>
    /// <b>アロケーション</b>: エントリは 16 バイトの struct で、表は 1 本の配列。
    /// 引きも書き込みも <c>ref</c> 経由で行い、hot path では一切ヒープを触らない。
    /// <c>ArrayPool</c> は使わない（長寿命の固定配列なのでプールの利点が無い／docs/PLAN.md §4.3）。
    /// </para>
    /// <para>
    /// <b>スレッド安全性</b>: <see cref="NodeTable"/> / <see cref="UniqueTable"/> と同じくスレッドセーフではない。
    /// </para>
    /// </remarks>
    internal sealed class OperationCache
    {
        /// <summary>エントリ 1 個あたりが受け持つノード数。既定サイズ = ノード数 / この値。</summary>
        public const int NodesPerEntry = 4;

        /// <summary>初期サイズの既定値（16 バイト × 1024 = 16 KB）。</summary>
        public const int DefaultInitialCapacity = 1024;

        /// <summary>サイズ上限の既定値（16 バイト × 約 419 万 = 64 MB）。</summary>
        public const int DefaultMaxCapacity = 1 << 22;

        /// <summary>
        /// <see cref="MaxCapacity"/> に指定できる最大値（16 バイト × 約 1.34 億 = 2 GB）。
        /// 自動調整はノード数の 1/4 を狙うので、ここへ届くにはノードだけで 8 GB 以上必要になる。
        /// </summary>
        public const int CapacityLimit = 1 << 27;

        /// <summary>「このエントリは空き」を表す番兵。</summary>
        private const int EmptyOp = (int)ZddOperation.None;

        private readonly int _maxCapacity;

        /// <summary>長さは常に 0 か 2 の冪。0 なら常にミスする（キャッシュ無効）。</summary>
        private Entry[] _entries;

        private long _lookups;
        private long _hits;
        private long _collisions;

        /// <summary>既定のサイズでキャッシュを作る。</summary>
        public OperationCache()
            : this(DefaultInitialCapacity, DefaultMaxCapacity)
        {
        }

        /// <summary>サイズを指定してキャッシュを作る。</summary>
        /// <param name="initialCapacity">
        /// 初期エントリ数。2 の冪に切り上げたうえで <paramref name="maxCapacity"/> に丸め込まれる。
        /// 0 なら表を確保せず、<see cref="Tune"/> が呼ばれるまで無効のまま。
        /// </param>
        /// <param name="maxCapacity">
        /// エントリ数の上限。2 の冪に<b>切り下げ</b>られる（指定値を超えないため）。
        /// 0 ならキャッシュを完全に無効化する。
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// いずれかが負、または <paramref name="maxCapacity"/> が <see cref="CapacityLimit"/> を超える場合。
        /// </exception>
        public OperationCache(int initialCapacity, int maxCapacity)
        {
            ThrowHelper.ThrowIfNegative(initialCapacity, nameof(initialCapacity));
            ThrowHelper.ThrowIfNegative(maxCapacity, nameof(maxCapacity));

            if (maxCapacity > CapacityLimit)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(maxCapacity),
                    $"'{nameof(maxCapacity)}' must not exceed {CapacityLimit}, but was {maxCapacity}.");
            }

            // 上限は切り下げる。切り上げると「上限」として指定した値を超えてしまう。
            _maxCapacity = maxCapacity == 0 ? 0 : 1 << BitOperations.Log2((uint)maxCapacity);

            uint capacity = initialCapacity == 0
                ? 0
                : Math.Min((uint)_maxCapacity, BitOperations.RoundUpToPowerOf2((uint)initialCapacity));

            _entries = capacity == 0 ? Array.Empty<Entry>() : new Entry[capacity];
        }

        /// <summary>現在のエントリ数（0 か 2 の冪）。</summary>
        public int Capacity => _entries.Length;

        /// <summary>自動調整が広げられるエントリ数の上限。0 ならキャッシュは無効。</summary>
        public int MaxCapacity => _maxCapacity;

        /// <summary>引きに応えられる状態か（サイズが 0 でないか）。</summary>
        public bool IsEnabled => _entries.Length != 0;

        /// <summary>これまでの参照回数。</summary>
        public long Lookups => _lookups;

        /// <summary>そのうちヒットした回数。</summary>
        public long Hits => _hits;

        /// <summary>そのうち外れた回数。</summary>
        public long Misses => _lookups - _hits;

        /// <summary>
        /// 書き込み時に、別の <c>(演算, オペランド)</c> のエントリを上書きした回数。
        /// ヒット率と併せて見ると、サイズが足りているかの目安になる。
        /// </summary>
        public long Collisions => _collisions;

        /// <summary>ヒット率（0.0 〜 1.0）。一度も引いていなければ 0。</summary>
        public double HitRate => _lookups == 0 ? 0.0 : (double)_hits / _lookups;

        /// <summary>
        /// 二項演算の結果を引く。可換演算ならオペランドの順序は問わない。
        /// </summary>
        /// <param name="op">演算の種別。<see cref="ZddOperation.None"/> 不可。</param>
        /// <param name="f">左オペランドのノード ID。</param>
        /// <param name="g">右オペランドのノード ID。</param>
        /// <param name="result">見つかった結果ノード ID。見つからなければ <see cref="NodeTable.Bottom"/>。</param>
        /// <returns>エントリが見つかれば <see langword="true"/>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetBinary(ZddOperation op, int f, int g, out int result)
        {
            AssertBinary(op);
            Normalize(op, ref f, ref g);
            return TryGet(op, f, g, out result);
        }

        /// <summary>
        /// 二項演算の結果を書き込む。同じスロットの先客は無条件に捨てられる。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PutBinary(ZddOperation op, int f, int g, int result)
        {
            AssertBinary(op);
            Normalize(op, ref f, ref g);
            Put(op, f, g, result);
        }

        /// <summary>
        /// 単項演算の結果を引く。
        /// </summary>
        /// <param name="op">演算の種別。<see cref="ZddOperation.None"/> 不可。</param>
        /// <param name="f">オペランドのノード ID。</param>
        /// <param name="item">
        /// 演算のパラメータ（<see cref="ZddOperation.Change"/> などの item index）。
        /// item を取らない演算では 0 を渡す。
        /// </param>
        /// <param name="result">見つかった結果ノード ID。見つからなければ <see cref="NodeTable.Bottom"/>。</param>
        /// <returns>エントリが見つかれば <see langword="true"/>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetUnary(ZddOperation op, int f, int item, out int result)
        {
            AssertUnary(op);
            return TryGet(op, f, item, out result);
        }

        /// <summary>
        /// 単項演算の結果を書き込む。同じスロットの先客は無条件に捨てられる。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PutUnary(ZddOperation op, int f, int item, int result)
        {
            AssertUnary(op);
            Put(op, f, item, result);
        }

        /// <summary>
        /// 全エントリを捨てる。ノード ID の意味が変わる操作（将来の M5-3 GC による
        /// ノード表の再構成）の直後には、必ずこれを呼ばなければならない。
        /// </summary>
        /// <remarks>統計（<see cref="Lookups"/> など）は積算値なので消さない。</remarks>
        public void Clear() => Array.Clear(_entries);

        /// <summary>統計カウンタだけを 0 に戻す。エントリには触れない。</summary>
        public void ResetStatistics()
        {
            _lookups = 0;
            _hits = 0;
            _collisions = 0;
        }

        /// <summary>
        /// ノード数に見合うサイズへ表を広げる。演算の入口（M1-5 以降）から呼ぶ。
        /// </summary>
        /// <param name="nodeCount">現在のノード数。</param>
        /// <returns>実際に広げたなら <see langword="true"/>。</returns>
        /// <remarks>
        /// 狙いは <c>nodeCount / <see cref="NodesPerEntry"/></c> エントリ（docs/PLAN.md §4.3 の「ノード数の 1/4 程度」）で、
        /// <see cref="MaxCapacity"/> で頭打ちにする。<b>縮めることはしない</b>。
        /// 広げるときは古い表を捨てて作り直す。direct-mapped なので添字はサイズが変わると
        /// 総入れ替えになり、再ハッシュしても大半は上書きで消えるうえ、失っても
        /// 再計算されるだけだからである。
        /// </remarks>
        public bool Tune(long nodeCount)
        {
            int capacity = _entries.Length;
            if (capacity >= _maxCapacity)
            {
                return false;
            }

            long desired = nodeCount <= 0 ? 0 : nodeCount / NodesPerEntry;
            if (desired <= capacity)
            {
                return false;
            }

            // desired < _maxCapacity <= CapacityLimit なので、切り上げても uint に収まる。
            int grown = desired >= _maxCapacity
                ? _maxCapacity
                : (int)Math.Min((uint)_maxCapacity, BitOperations.RoundUpToPowerOf2((uint)desired));

            _entries = new Entry[grown];
            return true;
        }

        /// <summary>可換演算なら <c>a &lt;= b</c> になるよう入れ替える。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Normalize(ZddOperation op, ref int a, ref int b)
        {
            if (a > b && ZddOperations.IsCommutative(op))
            {
                (a, b) = (b, a);
            }
        }

        /// <summary>2 つのオペランドを 64bit にそのまま詰める。情報を落とさないので照合に使える。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long KeyOf(int a, int b) => (long)(((ulong)(uint)a << 32) | (uint)b);

        /// <summary>
        /// <c>(op, a, b)</c> からスロット添字を求める。<see cref="Hashing.Combine"/> は
        /// 3 つの <c>int</c> を混ぜる汎用の混合関数で、出力は下位ビットまで撹拌済みなので、
        /// ここでは Fibonacci hashing ではなく素直なマスクで足りる
        /// （サイズ 1 の表も特別扱いせずに扱える）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SlotOf(ZddOperation op, int a, int b, int capacity) =>
            (int)(Hashing.Combine((int)op, a, b) & (ulong)(capacity - 1));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGet(ZddOperation op, int a, int b, out int result)
        {
            _lookups++;

            Entry[] entries = _entries;
            if (entries.Length == 0)
            {
                result = NodeTable.Bottom;
                return false;
            }

            ref Entry entry = ref entries[SlotOf(op, a, b, entries.Length)];

            // Op とキー全体を照合する。空きエントリは Op == EmptyOp なので、
            // 呼び出し側の op が None でない限りここで弾かれる。
            if (entry.Op == (int)op && entry.Key == KeyOf(a, b))
            {
                _hits++;
                result = entry.Result;
                return true;
            }

            result = NodeTable.Bottom;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Put(ZddOperation op, int a, int b, int result)
        {
            Entry[] entries = _entries;
            if (entries.Length == 0)
            {
                return;
            }

            long key = KeyOf(a, b);
            ref Entry entry = ref entries[SlotOf(op, a, b, entries.Length)];

            if (entry.Op != EmptyOp && (entry.Op != (int)op || entry.Key != key))
            {
                _collisions++;
            }

            entry.Key = key;
            entry.Op = (int)op;
            entry.Result = result;
        }

        [Conditional("DEBUG")]
        private static void AssertBinary(ZddOperation op) =>
            Debug.Assert(
                op != ZddOperation.None && !ZddOperations.IsUnary(op),
                $"'{op}' is not a binary operation; use the unary entry points for it.");

        [Conditional("DEBUG")]
        private static void AssertUnary(ZddOperation op) =>
            Debug.Assert(
                ZddOperations.IsUnary(op),
                $"'{op}' is not a unary operation; use the binary entry points for it.");

        /// <summary>
        /// キャッシュの 1 エントリ。16 バイト固定（docs/PLAN.md §4.3）。
        /// <see cref="Op"/> が <see cref="EmptyOp"/> なら未使用のスロット。
        /// </summary>
        internal struct Entry
        {
            /// <summary>2 つのオペランドを 32bit ずつ詰めたもの。切り詰めていない。</summary>
            public long Key;

            /// <summary><see cref="ZddOperation"/> の値。</summary>
            public int Op;

            /// <summary>結果のノード ID。<see cref="NodeTable.Bottom"/> も正当な値である。</summary>
            public int Result;
        }
    }
}
