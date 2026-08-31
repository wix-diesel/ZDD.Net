using System;
using CsCheck;

namespace ZDD.Net.Tests.Properties.Harness
{
    /// <summary>
    /// ランダムな族の生成器。変数の個数・集合の個数・どの集合かをすべて CsCheck に任せる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>大きさの上限</b>: <see cref="MaxVariableCount"/> は 6。<c>Complement</c> と
    /// <c>HittingSets</c> は冪集合（2^n 個）を返すので、変数を増やすと検算のための走査だけが重くなる。
    /// 変数 ≤ 12 の総当たりは M1-6 の照合テストが受け持っていて、こちらの持ち場は
    /// 「総当たりでは踏まない組合せの形」を掘ることにある。
    /// </para>
    /// <para>
    /// <b>集合の個数</b>: 0〜<see cref="MaxSetCount"/> 個。空の族と <c>{∅}</c> は演算の境界に
    /// なりやすいので、生成器はどちらもふつうに出す（マスク 0 が ∅ に当たる）。
    /// </para>
    /// </remarks>
    internal static class FamilyGen
    {
        /// <summary>生成する族の変数の個数の下限。</summary>
        public const int MinVariableCount = 1;

        /// <summary>生成する族の変数の個数の上限。</summary>
        public const int MaxVariableCount = 6;

        /// <summary>1 つの族に入れる集合の個数の上限。</summary>
        public const int MaxSetCount = 8;

        /// <summary>生成する重みの下限。</summary>
        public const int MinWeight = -9;

        /// <summary>生成する重みの上限。</summary>
        public const int MaxWeight = 9;

        /// <summary>確率を整数で生成するときの分母。</summary>
        public const int PercentScale = 100;

        /// <summary>族を 1 つ生成する。</summary>
        public static Gen<FamilySpec> Family { get; } =
            Gen.Int[MinVariableCount, MaxVariableCount].SelectMany(FamilyOf);

        /// <summary>同じ宇宙に住む族を 2 つ生成する。</summary>
        public static Gen<FamilyPair> Pair { get; } =
            Gen.Int[MinVariableCount, MaxVariableCount]
                .SelectMany(n => Gen.Select(FamilyOf(n), FamilyOf(n)))
                .Select(pair => new FamilyPair(pair.Item1, pair.Item2));

        /// <summary>同じ宇宙に住む族を 3 つ生成する（結合則・分配則に要る）。</summary>
        public static Gen<FamilyTriple> Triple { get; } =
            Gen.Int[MinVariableCount, MaxVariableCount]
                .SelectMany(n => Gen.Select(FamilyOf(n), FamilyOf(n), FamilyOf(n)))
                .Select(triple => new FamilyTriple(triple.Item1, triple.Item2, triple.Item3));

        /// <summary>族と、その宇宙に属する item を 1 つ生成する（単項演算に要る）。</summary>
        public static Gen<FamilyAndItem> FamilyAndItem { get; } =
            Gen.Int[MinVariableCount, MaxVariableCount]
                .SelectMany(n => Gen.Select(FamilyOf(n), Gen.Int[0, n - 1]))
                .Select(pair => new FamilyAndItem(pair.Item1, pair.Item2));

        /// <summary>族と、item ごとの重み・確率を生成する（重み最適化に要る）。</summary>
        /// <remarks>
        /// 重みは負も含む小さな整数（総和が <see cref="int"/> に収まる範囲）。確率は
        /// 0〜100 の整数を 100 で割ったもので、生成も縮小も整数のまま行われるので反例が読める。
        /// </remarks>
        public static Gen<FamilyAndWeights> FamilyAndWeights { get; } =
            Gen.Int[MinVariableCount, MaxVariableCount]
                .SelectMany(n => Gen.Select(
                    FamilyOf(n),
                    Gen.Int[MinWeight, MaxWeight].Array[n],
                    Gen.Int[0, PercentScale].Array[n]))
                .Select(triple => new FamilyAndWeights(triple.Item1, triple.Item2, triple.Item3));

        /// <summary>変数の個数を決め打ちして族を生成する。</summary>
        public static Gen<FamilySpec> FamilyOf(int variableCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(variableCount);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(variableCount, MaxVariableCount);

            int universe = variableCount == 0 ? 0 : (1 << variableCount) - 1;

            return Gen.Int[0, universe].Array[0, MaxSetCount]
                .Select(masks => new FamilySpec(variableCount, masks));
        }
    }

    /// <summary>同じ宇宙に住む族 2 つ。</summary>
    internal sealed class FamilyPair
    {
        public FamilyPair(FamilySpec first, FamilySpec second)
        {
            First = first;
            Second = second;
        }

        public FamilySpec First { get; }

        public FamilySpec Second { get; }

        public override string ToString() =>
            $"n={First.VariableCount} f={FamilySpec.Format(First.Masks)} g={FamilySpec.Format(Second.Masks)}";
    }

    /// <summary>同じ宇宙に住む族 3 つ。</summary>
    internal sealed class FamilyTriple
    {
        public FamilyTriple(FamilySpec first, FamilySpec second, FamilySpec third)
        {
            First = first;
            Second = second;
            Third = third;
        }

        public FamilySpec First { get; }

        public FamilySpec Second { get; }

        public FamilySpec Third { get; }

        public override string ToString() =>
            $"n={First.VariableCount} f={FamilySpec.Format(First.Masks)} " +
            $"g={FamilySpec.Format(Second.Masks)} h={FamilySpec.Format(Third.Masks)}";
    }

    /// <summary>族と、その宇宙の item ごとの重み・確率。</summary>
    internal sealed class FamilyAndWeights
    {
        private readonly int[] _percents;

        public FamilyAndWeights(FamilySpec family, int[] weights, int[] percents)
        {
            Family = family;
            Weights = weights;
            _percents = percents;
        }

        public FamilySpec Family { get; }

        /// <summary>item ごとの重み。長さは <c>Family.VariableCount</c>。</summary>
        public int[] Weights { get; }

        /// <summary>item ごとの確率（0 以上 1 以下）。長さは <c>Family.VariableCount</c>。</summary>
        public double[] Probabilities =>
            Array.ConvertAll(_percents, percent => (double)percent / FamilyGen.PercentScale);

        public override string ToString() =>
            $"n={Family.VariableCount} f={FamilySpec.Format(Family.Masks)} " +
            $"w=[{string.Join(", ", Weights)}] p=[{string.Join(", ", _percents)}]/{FamilyGen.PercentScale}";
    }

    /// <summary>族と、その宇宙に属する item 1 つ。</summary>
    internal sealed class FamilyAndItem
    {
        public FamilyAndItem(FamilySpec family, int item)
        {
            Family = family;
            Item = item;
        }

        public FamilySpec Family { get; }

        public int Item { get; }

        public override string ToString() =>
            $"n={Family.VariableCount} item={Item} f={FamilySpec.Format(Family.Masks)}";
    }
}
