using ZDD.Net.Frontier;

namespace ZDD.Net.Tests.Frontier
{
    /// <summary>
    /// 状態表のテストで使う状態。<see cref="Live"/> は以降の遷移に効く値、
    /// <see cref="Stale"/> はもう効かない値（スペックによっては等価判定で無視する）。
    /// </summary>
    internal readonly struct PairState
    {
        public PairState(int live, int stale)
        {
            Live = live;
            Stale = stale;
        }

        public int Live { get; }

        public int Stale { get; }
    }

    /// <summary>
    /// <see cref="PairState.Stale"/> を無視するスペック。「スペックが等しいと言う状態は
    /// 1 本にまとまる」ことを確かめるために使う。
    /// </summary>
    /// <remarks>
    /// 状態表のテストは <c>GetRoot</c> / <c>GetChild</c> を呼ばない（遷移は M2-3 の担当）。
    /// ここでは規約を満たす最小の実装を置いてある。
    /// </remarks>
    internal readonly struct LiveOnlySpec : IDdSpec<PairState>
    {
        public int GetRoot(ref PairState state)
        {
            state = new PairState(0, 0);
            return 1;
        }

        public int GetChild(ref PairState state, int level, int value) => DdResult.True;

        public bool StateEquals(in PairState left, in PairState right) => left.Live == right.Live;

        public int StateHashCode(in PairState state) => state.Live;
    }

    /// <summary>
    /// 状態を丸ごと比べるが、ハッシュは<b>常に同じ値</b>を返すスペック。
    /// 表の探索が「ハッシュが一致しても状態が違えば別扱い」を守っているかを見るために使う。
    /// </summary>
    internal readonly struct ConstantHashSpec : IDdSpec<PairState>
    {
        public int GetRoot(ref PairState state)
        {
            state = new PairState(0, 0);
            return 1;
        }

        public int GetChild(ref PairState state, int level, int value) => DdResult.True;

        public bool StateEquals(in PairState left, in PairState right) =>
            left.Live == right.Live && left.Stale == right.Stale;

        public int StateHashCode(in PairState state) => 0;
    }
}
