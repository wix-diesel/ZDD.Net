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
