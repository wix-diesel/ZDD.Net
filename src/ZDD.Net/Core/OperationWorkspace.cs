using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// 反復（明示スタック）実装の作業領域。<b>作業スタック</b>と<b>途中結果表</b>の 2 つを持ち、
    /// 演算 1 回ぶんの「再帰の代わり」を提供する。ZDD の演算はすべてこの型を使って書く。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>なぜ必要か</b>（docs/PLAN.md §4.5）: 家族代数の演算は自然に書けば再帰になるが、
    /// ZDD の深さは変数の個数そのもので、10 万規模になると <c>StackOverflowException</c> が起きる。
    /// .NET ではこれを catch できず<b>プロセスが即死する</b>ため、本ライブラリは
    /// <b>全演算を反復で書く</b>。その共通部分がこの型である。
    /// </para>
    /// <para>
    /// <b>後続の演算はこの形に倣って書く</b>（M1-7 の集合演算 〜 M1-10 の極大・極小）。雛形は
    /// <see cref="UnaryOperations.Apply"/> にあり、骨格は次の 5 段だけである。
    /// </para>
    /// <list type="number">
    /// <item><description>根を <see cref="PushVisit"/> でスタックに積む。</description></item>
    /// <item><description>
    /// <see cref="TryPop"/> で 1 件取り出す。<see cref="IsCombine"/> が真なら 6 へ。
    /// </description></item>
    /// <item><description>
    /// 途中結果表（<see cref="TryGetResult"/>）に答があれば何もしない。次に終端などの基底ケース、
    /// 最後に演算キャッシュ（<see cref="OperationCache"/>）を見て、決まれば
    /// <see cref="SetResult"/> して次へ。
    /// </description></item>
    /// <item><description>
    /// どれでも決まらなければ、<b>自分を <see cref="PushCombine"/> で積み直してから</b>
    /// 未計算の子を <see cref="PushVisit"/> で積む。スタックは LIFO なので、
    /// 子は必ず自分の合成より先に片付く。
    /// </description></item>
    /// <item><description>
    /// 合成として取り出されたら、子の結果を <see cref="TryGetResult"/> で引き、
    /// <see cref="UniqueTable.GetNode"/> で 1 個のノードに合成して <see cref="SetResult"/> し、
    /// 演算キャッシュにも書く。
    /// </description></item>
    /// <item><description>スタックが空になったら、根の途中結果がそのまま答。</description></item>
    /// </list>
    /// <para>
    /// <b>キー</b>: スタックにも途中結果表にも <c>long</c> の「キー」を入れる。単項演算では
    /// ノード ID をそのまま使い、二項演算では 2 つのノード ID を 32bit ずつ詰めた値を使う
    /// （どちらも非負になる）。キーの負値は「合成として積み直したもの」を表す印
    /// （<see cref="PushCombine"/> はビット反転して積む）に予約されている。
    /// </para>
    /// <para>
    /// <b>途中結果表と演算キャッシュは役割が違う</b>: <see cref="OperationCache"/> は
    /// 衝突したエントリを捨てる lossy な表で、<b>演算をまたいで</b>結果を使い回すためのもの。
    /// 対してこちらは<b>取りこぼさない</b>表で、演算 1 回の中で「子の結果はもう出ている」ことを
    /// 保証する。再帰なら呼び出し元のローカル変数が担っていた役割で、ここを lossy な表で代用すると、
    /// 2 つの子が同じスロットを奪い合ったときに互いを追い出し続けて<b>停止しなくなる</b>。
    /// </para>
    /// <para>
    /// <b>アロケーション</b>: <see cref="ZddManager"/> が 1 個を持ち回り、演算のたびに
    /// <see cref="Reset"/> して使い回す。したがって配列の確保は「これまでで最大の演算」の
    /// 分だけしか起きない。ノード 1 個ごとの割り当ては無い。後始末も表を舐めずに済むよう、
    /// 途中結果表のスロットには<b>世代</b>を持たせてある（<see cref="Reset"/> は世代を進めるだけ）。
    /// これがないと、一度きりの巨大な演算のあとに小さな演算を何度も回すたび、
    /// 大きくなった表を消して回る手間を毎回払うことになる。
    /// </para>
    /// <para>
    /// <b>スレッド安全性</b>: 他の内部表と同じくスレッドセーフではない。
    /// </para>
    /// </remarks>
    internal sealed class OperationWorkspace
    {
        /// <summary>作業スタックの既定の初期段数。</summary>
        public const int DefaultStackCapacity = 64;

        /// <summary>途中結果表の既定の初期エントリ数。2 の冪。</summary>
        public const int DefaultResultCapacity = 64;

        /// <summary>途中結果表の最小エントリ数。2 の冪で、2 より大きい（Fibonacci hashing の前提）。</summary>
        public const int MinimumResultCapacity = 4;

        /// <summary>途中結果表を倍化する負荷率（%）。<see cref="UniqueTable"/> と揃えてある。</summary>
        public const int MaxLoadFactorPercent = 70;

        /// <summary>途中結果表のエントリ数の上限。2 の冪。</summary>
        public const int MaxResultCapacity = 1 << 30;

        /// <summary>まだ一度も使われていないスロットの世代。<see cref="_generation"/> は 1 から始まる。</summary>
        private const int UnusedGeneration = 0;

        /// <summary>作業スタック。負の値は「合成として積み直したもの」（<see cref="PushCombine"/>）。</summary>
        private long[] _stack;

        /// <summary>スタックに積まれている段数。</summary>
        private int _top;

        /// <summary>途中結果表のキー。<see cref="_generations"/> が現世代のスロットだけが有効。</summary>
        private long[] _keys;

        /// <summary><see cref="_keys"/> と同じ添字で対応する結果ノード ID。</summary>
        private int[] _values;

        /// <summary>
        /// スロットが最後に書かれた世代。<see cref="_generation"/> と一致しないスロットは空きとみなす。
        /// </summary>
        private int[] _generations;

        /// <summary>
        /// 現在の世代。<see cref="Reset"/> はこれを 1 つ進めるだけで、表を舐めない。
        /// </summary>
        private int _generation;

        /// <summary>途中結果表に入っているエントリ数。</summary>
        private int _count;

        /// <summary>この数を超えた時点で途中結果表を倍化する。</summary>
        private int _growThreshold;

        /// <summary>既定の大きさで作業領域を作る。</summary>
        public OperationWorkspace()
            : this(DefaultStackCapacity, DefaultResultCapacity)
        {
        }

        /// <summary>大きさを指定して作業領域を作る。どちらも足りなくなれば自動で倍化される。</summary>
        /// <param name="stackCapacity">作業スタックの初期段数。1 以上。</param>
        /// <param name="resultCapacity">
        /// 途中結果表の初期エントリ数。<see cref="MinimumResultCapacity"/> 以上の 2 の冪に切り上げられる。
        /// </param>
        public OperationWorkspace(int stackCapacity, int resultCapacity)
        {
            ThrowHelper.ThrowIfNegativeOrZero(stackCapacity, nameof(stackCapacity));
            ThrowHelper.ThrowIfNegativeOrZero(resultCapacity, nameof(resultCapacity));

            if (resultCapacity > MaxResultCapacity)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(resultCapacity),
                    $"'{nameof(resultCapacity)}' must not exceed {MaxResultCapacity}, but was {resultCapacity}.");
            }

            int capacity = Math.Max(MinimumResultCapacity, (int)BitOperations.RoundUpToPowerOf2((uint)resultCapacity));

            _stack = new long[stackCapacity];
            _top = 0;
            _keys = new long[capacity];
            _values = new int[capacity];
            _generations = new int[capacity];
            _generation = UnusedGeneration + 1;
            _count = 0;
            _growThreshold = ComputeGrowThreshold(capacity);
        }

        /// <summary>いま作業スタックに積まれている段数。</summary>
        public int Depth => _top;

        /// <summary>作業スタックが空かどうか。</summary>
        public bool IsEmpty => _top == 0;

        /// <summary>途中結果表に入っているエントリ数。</summary>
        public int ResultCount => _count;

        /// <summary>作業スタックの現在の段数上限（倍化で増える）。</summary>
        public int StackCapacity => _stack.Length;

        /// <summary>途中結果表の現在のエントリ数上限（倍化で増える）。</summary>
        public int ResultCapacity => _keys.Length;

        /// <summary>スタックから取り出した項目が「合成」（子を積んだ後の積み直し）かどうか。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCombine(long entry) => entry < 0;

        /// <summary>スタックから取り出した項目の、印を外した元のキー。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long KeyOf(long entry) => entry < 0 ? ~entry : entry;

        /// <summary>これから計算する部分問題としてキーを積む。</summary>
        /// <param name="key">部分問題のキー（非負）。</param>
        public void PushVisit(long key)
        {
            AssertKey(key);
            Push(key);
        }

        /// <summary>
        /// 子の結果が揃った後にもう一度取り出すために、キーを「合成」の印つきで積む。
        /// <b>子を積むより先に</b>呼ぶこと（スタックは LIFO なので、後から積んだ子が先に片付く）。
        /// </summary>
        /// <param name="key">部分問題のキー（非負）。</param>
        public void PushCombine(long key)
        {
            AssertKey(key);
            Push(~key);
        }

        /// <summary>スタックから 1 件取り出す。</summary>
        /// <param name="entry">
        /// 取り出した項目。<see cref="IsCombine"/> と <see cref="KeyOf"/> で読み解く。
        /// </param>
        /// <returns>取り出せたら <see langword="true"/>、スタックが空なら <see langword="false"/>。</returns>
        public bool TryPop(out long entry)
        {
            if (_top == 0)
            {
                entry = 0;
                return false;
            }

            entry = _stack[--_top];
            return true;
        }

        /// <summary>途中結果表からキーに対する結果を引く。</summary>
        /// <param name="key">部分問題のキー（非負）。</param>
        /// <param name="result">
        /// 見つかった結果ノード ID。見つからなければ <see cref="NodeTable.Bottom"/>。
        /// </param>
        /// <returns>計算済みなら <see langword="true"/>。</returns>
        public bool TryGetResult(long key, out int result)
        {
            AssertKey(key);

            long[] keys = _keys;
            int[] generations = _generations;
            int generation = _generation;
            int mask = keys.Length - 1;
            int slot = SlotOf(key, keys.Length);

            while (true)
            {
                if (generations[slot] != generation)
                {
                    // 前の演算の名残か、一度も使われていないスロット。どちらも「空き」。
                    result = NodeTable.Bottom;
                    return false;
                }

                if (keys[slot] == key)
                {
                    result = _values[slot];
                    return true;
                }

                slot = (slot + 1) & mask;
            }
        }

        /// <summary>キーに対する結果が既に出ているかどうか。</summary>
        /// <param name="key">部分問題のキー（非負）。</param>
        public bool HasResult(long key) => TryGetResult(key, out _);

        /// <summary>
        /// 途中結果表にキーと結果を記録する。同じキーへの再登録は上書きになる
        /// （反復実装では同じ部分問題を 2 度計算しても同じ答になるので、上書きは無害）。
        /// </summary>
        /// <param name="key">部分問題のキー（非負）。</param>
        /// <param name="result">結果ノード ID。</param>
        public void SetResult(long key, int result)
        {
            AssertKey(key);

            if (_count + 1 > _growThreshold)
            {
                Grow();
            }

            int slot = FindSlot(_keys, _generations, _generation, key);
            if (_generations[slot] != _generation)
            {
                _generations[slot] = _generation;
                _keys[slot] = key;
                _count++;
            }

            _values[slot] = result;
        }

        /// <summary>
        /// 次の演算のために中身を空にする。確保済みの配列は手放さないので、
        /// 使い回すぶんには追加のアロケーションが起きない。
        /// </summary>
        /// <remarks>
        /// 表を舐めて消すのではなく<b>世代を 1 つ進める</b>だけなので、直前の演算が
        /// どれだけ大きくても後始末は一定時間で済む。大きな演算を 1 回やったあとに
        /// 小さな演算を何度も回す、という使い方でも、毎回その大きさぶんを払うことにならない。
        /// </remarks>
        public void Reset()
        {
            _top = 0;
            _count = 0;

            if (_generation == int.MaxValue)
            {
                // 演算 21 億回に 1 度。ここでだけ本当に表を消して、世代を最初に戻す。
                Array.Clear(_generations);
                _generation = UnusedGeneration + 1;
                return;
            }

            _generation++;
        }

        private static int ComputeGrowThreshold(int capacity) =>
            (int)((long)capacity * MaxLoadFactorPercent / 100);

        /// <summary>
        /// キーからスロット添字を求める。キーは連番のノード ID（や、その組）なので下位ビットに
        /// 強い規則性がある。<see cref="Hashing.Mix64"/> で撹拌してから Fibonacci hashing にかける。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SlotOf(long key, int capacity) =>
            Hashing.IndexForPowerOfTwo(Hashing.Mix64((ulong)key), capacity);

        /// <summary><paramref name="key"/> が入っているスロット、無ければ入れるべき空きスロット。</summary>
        private static int FindSlot(long[] keys, int[] generations, int generation, long key)
        {
            int mask = keys.Length - 1;
            int slot = SlotOf(key, keys.Length);

            while (true)
            {
                if (generations[slot] != generation || keys[slot] == key)
                {
                    return slot;
                }

                slot = (slot + 1) & mask;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Push(long entry)
        {
            long[] stack = _stack;
            if (_top == stack.Length)
            {
                GrowStack();
                stack = _stack;
            }

            stack[_top++] = entry;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowStack()
        {
            int capacity = _stack.Length;
            if (capacity >= Array.MaxLength / 2)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    $"The work stack cannot grow beyond {Array.MaxLength} entries; it currently holds {capacity}.");
            }

            Array.Resize(ref _stack, capacity * 2);
        }

        /// <summary>途中結果表を倍化して全エントリを入れ直す。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Grow()
        {
            long[] oldKeys = _keys;
            int[] oldValues = _values;
            int[] oldGenerations = _generations;
            int generation = _generation;
            int capacity = oldKeys.Length;

            if (capacity >= MaxResultCapacity)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    $"The intermediate result table cannot grow beyond {MaxResultCapacity} entries; " +
                    $"it currently holds {_count} entr(ies).");
            }

            int newCapacity = capacity * 2;
            long[] keys = new long[newCapacity];
            int[] values = new int[newCapacity];

            // 新しい世代の配列はゼロ初期化されている（= どのスロットも現世代ではない）ので、
            // 空きの印を書いて回る必要は無い。移し替えるのは現世代のエントリだけ。
            int[] generations = new int[newCapacity];

            for (int i = 0; i < oldKeys.Length; i++)
            {
                if (oldGenerations[i] != generation)
                {
                    continue;
                }

                long key = oldKeys[i];
                int slot = FindSlot(keys, generations, generation, key);
                generations[slot] = generation;
                keys[slot] = key;
                values[slot] = oldValues[i];
            }

            _keys = keys;
            _values = values;
            _generations = generations;
            _growThreshold = ComputeGrowThreshold(newCapacity);
        }

        /// <summary>
        /// キーが非負であることを表明する。負のキーは「合成」の印と衝突するため、
        /// 取り違えるとスタックの読み解きが静かに壊れる。
        /// </summary>
        [Conditional("DEBUG")]
        private static void AssertKey(long key) =>
            Debug.Assert(key >= 0, $"A workspace key must be non-negative, but was {key}.");
    }
}
