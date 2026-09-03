using System;
using System.Threading;
using ZDD.Net.Frontier;

namespace ZDD.Net.Tests.Frontier
{
    /// <summary>
    /// 全変数を自由に選べるスペック（べき集合の原型）。以降の判定に効く情報が無いので状態は使わない。
    /// </summary>
    /// <remarks>
    /// 状態が 1 種類しか無いので、正しく重複除去できていればどの水準も幅 1 になる。
    /// 「レベル構成とノード数が期待通りか」をいちばん素直に見られるお題。
    /// </remarks>
    internal readonly struct FreeChoiceSpec : IDdSpec<int>
    {
        private readonly int _itemCount;

        public FreeChoiceSpec(int itemCount)
        {
            _itemCount = itemCount;
        }

        public int GetRoot(ref int state)
        {
            state = 0;
            return _itemCount;
        }

        public int GetChild(ref int state, int level, int value) =>
            level == 1 ? DdResult.True : level - 1;

        public bool StateEquals(in int left, in int right) => true;

        public int StateHashCode(in int state) => 0;
    }

    /// <summary>
    /// ちょうど k 個選ぶスペック（docs/frontier-spec-guide.md §5 の例）。状態は「選んだ個数」だけ。
    /// </summary>
    /// <remarks>
    /// 別の枝が同じ個数に行き着くので、重複除去が効いていれば水準ごとの幅が k+1 で頭打ちになる。
    /// ⊥（超えた・もう届かない）と ⊤（揃った）の両方を返すので、終端への接続も同時に見られる。
    /// </remarks>
    internal readonly struct ExactlyKSpec : IDdSpec<int>
    {
        private readonly int _itemCount;
        private readonly int _k;

        public ExactlyKSpec(int itemCount, int k)
        {
            _itemCount = itemCount;
            _k = k;
        }

        public int GetRoot(ref int taken)
        {
            taken = 0;
            return _itemCount;
        }

        public int GetChild(ref int taken, int level, int value)
        {
            taken += value;

            if (taken > _k)
            {
                return DdResult.False;
            }

            if (taken == _k)
            {
                return DdResult.True;
            }

            int remaining = level - 1;

            return taken + remaining < _k ? DdResult.False : remaining;
        }

        public bool StateEquals(in int left, in int right) => left == right;

        public int StateHashCode(in int state) => state;
    }

    /// <summary>
    /// 1 つおきに水準を飛ばすスペック。飛ばされた水準の item は「入れない」に確定する。
    /// </summary>
    internal readonly struct SkipEveryOtherLevelSpec : IDdSpec<int>
    {
        private readonly int _itemCount;

        public SkipEveryOtherLevelSpec(int itemCount)
        {
            _itemCount = itemCount;
        }

        public int GetRoot(ref int state)
        {
            state = 0;
            return _itemCount;
        }

        public int GetChild(ref int state, int level, int value)
        {
            int next = level - 2;

            return next < 1 ? DdResult.True : next;
        }

        public bool StateEquals(in int left, in int right) => true;

        public int StateHashCode(in int state) => 0;
    }

    /// <summary>
    /// 枝ごとに違う状態を作る（合流しない）スペック。水準を 1 つ下るたびに幅が倍になる。
    /// </summary>
    /// <remarks>
    /// 「これまでの選択の履歴」を状態に入れるのはスペックの書き方としては誤りだが
    /// （docs/frontier-spec-guide.md §4）、上限の超過を確実に起こすお題としてわざとそう書いてある。
    /// </remarks>
    internal readonly struct DistinctStateSpec : IDdSpec<int>
    {
        private readonly int _itemCount;

        public DistinctStateSpec(int itemCount)
        {
            _itemCount = itemCount;
        }

        public int GetRoot(ref int history)
        {
            history = 1;
            return _itemCount;
        }

        public int GetChild(ref int history, int level, int value)
        {
            history = (history * 2) + value;

            return level == 1 ? DdResult.True : level - 1;
        }

        public bool StateEquals(in int left, in int right) => left == right;

        public int StateHashCode(in int state) => state;
    }

    /// <summary>指定した水準に来たら、自分が持つトークンをキャンセルするスペック。</summary>
    /// <remarks>展開の途中でキャンセルが観測されることを見るために使う。</remarks>
    internal readonly struct CancellingSpec : IDdSpec<int>
    {
        private readonly CancellationTokenSource _source;
        private readonly int _itemCount;
        private readonly int _cancelAtLevel;

        public CancellingSpec(CancellationTokenSource source, int itemCount, int cancelAtLevel)
        {
            _source = source;
            _itemCount = itemCount;
            _cancelAtLevel = cancelAtLevel;
        }

        public int GetRoot(ref int state)
        {
            state = 0;
            return _itemCount;
        }

        public int GetChild(ref int state, int level, int value)
        {
            if (level == _cancelAtLevel)
            {
                _source.Cancel();
            }

            return level == 1 ? DdResult.True : level - 1;
        }

        public bool StateEquals(in int left, in int right) => true;

        public int StateHashCode(in int state) => 0;
    }

    /// <summary>
    /// 水準を下るごとに幅が倍になり、<c>width</c> に達したところで頭打ちになるスペック（M4-3、issue #46）。
    /// </summary>
    /// <remarks>
    /// <c>state = (state * 2 + value) % width</c> という単純な式だけで「最初は倍々に増え、
    /// 表現できる値の種類数（<c>width</c>）に届いたら頭打ちになる」という形が作れる。並列パスは
    /// 一定以上の幅の水準でしか働かないので（<c>TopDownExpander.MinPartitionWidth</c>）、
    /// パーティション分割・結合が実際に動くだけの幅を、状態を無限に作らずに用意するのに使う。
    /// </remarks>
    internal readonly struct WideFrontierSpec : IDdSpec<int>
    {
        private readonly int _itemCount;
        private readonly int _width;

        public WideFrontierSpec(int itemCount, int width)
        {
            _itemCount = itemCount;
            _width = width;
        }

        public int GetRoot(ref int state)
        {
            state = 0;
            return _itemCount;
        }

        public int GetChild(ref int state, int level, int value)
        {
            state = (state * 2 + value) % _width;

            return level == 1 ? DdResult.True : level - 1;
        }

        public bool StateEquals(in int left, in int right) => left == right;

        public int StateHashCode(in int state) => state;
    }

    /// <summary><see cref="WideFrontierSpec"/> の配列状態版（<see cref="IArrayDdSpec"/>）。状態は 1 スロットだけ使う。</summary>
    internal readonly struct WideFrontierArraySpec : IArrayDdSpec
    {
        private readonly int _itemCount;
        private readonly int _width;

        public WideFrontierArraySpec(int itemCount, int width)
        {
            _itemCount = itemCount;
            _width = width;
        }

        public int ArrayLength => 1;

        public int GetRoot(Span<int> state)
        {
            state[0] = 0;
            return _itemCount;
        }

        public int GetChild(Span<int> state, int level, int value)
        {
            state[0] = (state[0] * 2 + value) % _width;

            return level == 1 ? DdResult.True : level - 1;
        }
    }

    /// <summary>
    /// <see cref="WideFrontierSpec"/> と同じ形だが、<c>level</c> が <c>poisonLevel</c> になったら
    /// 全パーティションが必ず例外を投げる（M4-3、issue #46: 並列展開中の例外伝播を試すためのスペック）。
    /// </summary>
    /// <remarks>
    /// どの順で完了するかに依存させないため、投げる直前に <paramref name="rendezvous"/>
    /// （パーティション数ぶんの参加者を持つ <see cref="Barrier"/>）で全パーティションを足並みそろえてから
    /// 一斉に投げる——<c>poisonLevel</c> はすでに幅が頭打ちに達している水準を選ぶので、そのラウンドの
    /// 全パーティションがちょうど 1 回ずつ <see cref="Barrier.SignalAndWait(TimeSpan)"/> を呼ぶ。
    /// 複数の例外が同時に起きる（<c>AggregateException</c> のまま伝播するはず）状況を確実に作れる。
    /// </remarks>
    internal readonly struct AlwaysThrowingWideSpec : IDdSpec<int>
    {
        private readonly int _itemCount;
        private readonly int _width;
        private readonly int _poisonLevel;
        private readonly Barrier _rendezvous;

        public AlwaysThrowingWideSpec(int itemCount, int width, int poisonLevel, Barrier rendezvous)
        {
            _itemCount = itemCount;
            _width = width;
            _poisonLevel = poisonLevel;
            _rendezvous = rendezvous;
        }

        public int GetRoot(ref int state)
        {
            state = 0;
            return _itemCount;
        }

        public int GetChild(ref int state, int level, int value)
        {
            if (level == _poisonLevel)
            {
                // 全パーティションがここへ揃うまで待つ: 揃わなければテストのバグ（想定した
                // パーティション数とテストが渡した Barrier の参加者数がずれている等）なので、
                // 待ちっぱなしにせず、タイムアウトなら意味の分かる例外で止める
                // （揃った場合と同じ FrontierPoisonException にしてしまうと、テスト失敗時に
                // 「タイムアウトだった」のか「本来の合図で投げた」のか区別が付かなくなる）。
                if (!_rendezvous.SignalAndWait(TimeSpan.FromSeconds(30)))
                {
                    throw new TimeoutException(
                        $"Rendezvous at level {level} timed out: fewer than {_rendezvous.ParticipantCount} " +
                        "partitions reached the poison level within 30s. Check that the partition count the " +
                        "test computed still matches the Barrier's participant count.");
                }

                throw new FrontierPoisonException();
            }

            state = (state * 2 + value) % _width;

            return level == 1 ? DdResult.True : level - 1;
        }

        public bool StateEquals(in int left, in int right) => left == right;

        public int StateHashCode(in int state) => state;
    }

    /// <summary>
    /// <see cref="AlwaysThrowingWideSpec"/> の「1 回だけ」版: <paramref name="counter"/> を全パーティションで
    /// 共有し、<c>poisonLevel</c> 以下で最初に呼ばれた 1 回だけ例外を投げる（それ以降は普通に進む）。
    /// </summary>
    /// <remarks>
    /// スレッド間で可変フィールドを共有しており、それ自体は <c>IDdSpec</c> の契約
    /// （docs/frontier-spec-guide.md §4: スペックはスレッド間で状態を共有しないこと）に反する——
    /// ここでは「並列展開中にちょうど 1 回だけ例外が起きる」という状況を意図的に作るためだけに使う、
    /// テスト専用の反例である。
    /// </remarks>
    internal sealed class SingleThrowCounter
    {
        private int _count;

        /// <summary>最初に呼んだ 1 回だけ <see langword="true"/>。</summary>
        public bool ShouldThrowOnce() => Interlocked.Increment(ref _count) == 1;
    }

    /// <summary>例外の型自体に意味を持たせないための、このテストだけの例外型。</summary>
    internal sealed class FrontierPoisonException : Exception
    {
    }

    internal readonly struct SingleThrowWideSpec : IDdSpec<int>
    {
        private readonly int _itemCount;
        private readonly int _width;
        private readonly int _poisonLevel;
        private readonly SingleThrowCounter _counter;

        public SingleThrowWideSpec(int itemCount, int width, int poisonLevel, SingleThrowCounter counter)
        {
            _itemCount = itemCount;
            _width = width;
            _poisonLevel = poisonLevel;
            _counter = counter;
        }

        public int GetRoot(ref int state)
        {
            state = 0;
            return _itemCount;
        }

        public int GetChild(ref int state, int level, int value)
        {
            if (level <= _poisonLevel && _counter.ShouldThrowOnce())
            {
                throw new FrontierPoisonException();
            }

            state = (state * 2 + value) % _width;

            return level == 1 ? DdResult.True : level - 1;
        }

        public bool StateEquals(in int left, in int right) => left == right;

        public int StateHashCode(in int state) => state;
    }

    /// <summary>根で決まった値を返すだけのスペック。終端の根と、規約外の戻り値を試すために使う。</summary>
    internal readonly struct FixedRootSpec : IDdSpec<int>
    {
        private readonly int _rootResult;

        public FixedRootSpec(int rootResult)
        {
            _rootResult = rootResult;
        }

        public int GetRoot(ref int state)
        {
            state = 0;
            return _rootResult;
        }

        public int GetChild(ref int state, int level, int value) => DdResult.True;

        public bool StateEquals(in int left, in int right) => true;

        public int StateHashCode(in int state) => 0;
    }
}
