using System;
using System.Collections.Generic;

namespace ZDD.Net.Tests.Harness
{
    /// <summary>
    /// 総当たり照合のドライバ。照合するテストが「どの族を回すか」を毎回書かずに済むようにする。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>大きさの目安</b>: 族の総数は 2^(2^n) なので、全族を回せるのは実質 n ≤ 4 まで
    /// （n = 3 で 256 通り、n = 4 で 65536 通り）。それより大きい変数数では、
    /// 全部分集合（2^n 個）を回す <see cref="AllSubsets"/> とランダム族を組み合わせる。
    /// </para>
    /// <para>
    /// <b>CI の実行時間</b>: 既定は <see cref="DefaultVariableCount"/>（= 10）で、
    /// n = 12 まではロードマップの想定どおり総当たりできる。それ以上を試すテストは
    /// <c>[Trait("Category", "Slow")]</c> を付けて、普段の実行から見分けられるようにする。
    /// </para>
    /// </remarks>
    internal static class FamilyCases
    {
        /// <summary>総当たり照合で回してよい変数の個数の上限（docs/ROADMAP.md M1-6）。</summary>
        public const int ExhaustiveVariableLimit = 12;

        /// <summary>特に指定がないときに照合で使う変数の個数。</summary>
        public const int DefaultVariableCount = 10;

        /// <summary>全族を列挙してよい変数の個数の上限（2^(2^n) が int に収まる範囲）。</summary>
        public const int AllFamiliesVariableLimit = 4;

        /// <summary>既定の密度が目安にする、1 つの族に入れたい集合の個数。</summary>
        private const int TargetSetCount = 32;

        /// <summary>
        /// 変数の個数に見合った既定の密度。族の大きさが <see cref="TargetSetCount"/> 個前後に
        /// 落ち着くように選ぶ。ただし変数が少ないうちは冪集合の半分を上限にする。
        /// </summary>
        /// <remarks>
        /// 密度を一定にすると、変数が 1〜2 個のときはほぼ空の族しか出ず、変数が多いときは
        /// 族が指数的に大きくなる。どちらも照合の役に立たない（後者は実行時間だけが伸びる）。
        /// </remarks>
        public static double DefaultDensity(int variableCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(variableCount);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(variableCount, BruteForceFamily.MaxPowerSetVariableCount);

            return Math.Min(0.5, (double)TargetSetCount / (1 << variableCount));
        }

        /// <summary>変数 <paramref name="variableCount"/> 個の部分集合を、ビットマスクで全部返す。</summary>
        public static IEnumerable<int> AllSubsets(int variableCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(variableCount);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(variableCount, ExhaustiveVariableLimit);

            int bound = 1 << variableCount;

            for (int mask = 0; mask < bound; mask++)
            {
                yield return mask;
            }
        }

        /// <summary>変数 <paramref name="variableCount"/> 個で作れる族を、すべて返す（2^(2^n) 通り）。</summary>
        public static IEnumerable<BruteForceFamily> AllFamilies(int variableCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(variableCount);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(variableCount, AllFamiliesVariableLimit);

            int subsetCount = 1 << variableCount;
            long familyCount = 1L << subsetCount;

            for (long family = 0; family < familyCount; family++)
            {
                List<int> masks = new List<int>();

                for (int mask = 0; mask < subsetCount; mask++)
                {
                    if ((family & (1L << mask)) != 0)
                    {
                        masks.Add(mask);
                    }
                }

                yield return BruteForceFamily.FromMasks(variableCount, masks);
            }
        }

        /// <summary>
        /// ランダムな族を <paramref name="count"/> 個返す。<paramref name="seed"/> が同じなら
        /// 何度呼んでも同じ並びになる。密度を省くと <see cref="DefaultDensity"/> を使う。
        /// </summary>
        public static IEnumerable<BruteForceFamily> RandomFamilies(
            int variableCount,
            int count,
            int seed,
            double? density = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);

            double effective = density ?? DefaultDensity(variableCount);
            Random random = new Random(seed);

            for (int i = 0; i < count; i++)
            {
                yield return BruteForceFamily.Random(variableCount, effective, random);
            }
        }

        /// <summary>
        /// ランダムな族を <paramref name="count"/> 個返す。1 つの族が持つ集合の個数を直接指定するので、
        /// 冪集合を走査できない大きさの変数数でも使える。
        /// </summary>
        public static IEnumerable<BruteForceFamily> RandomFamiliesOfSets(
            int variableCount,
            int count,
            int seed,
            int setCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);

            Random random = new Random(seed);

            for (int i = 0; i < count; i++)
            {
                yield return BruteForceFamily.RandomSets(variableCount, setCount, random);
            }
        }
    }
}
