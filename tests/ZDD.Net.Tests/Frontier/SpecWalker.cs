using System;
using System.Collections.Generic;
using Xunit;
using ZDD.Net.Frontier;

namespace ZDD.Net.Tests.Frontier
{
    /// <summary>
    /// スペックが受理する集合を、素直な深さ優先探索で列挙するテスト用のドライバ。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 構築器（<c>FrontierBuilder</c>）は M2-4 で入る。それまでスペックの規約を確かめる術がないので、
    /// 「状態を共有せず、枝を全部たどるだけ」の最小のドライバをテスト側に置く。
    /// 状態の重複除去も ZDD 化もしないため、実物の代わりにはならない（変数 20 個程度が限界）。
    /// </para>
    /// <para>
    /// 同時に、規約のうち<b>機械的に確かめられるもの</b>を歩きながら検査する
    /// （子の水準が親より真に小さいこと、範囲、等しい状態のハッシュが一致すること）。
    /// </para>
    /// </remarks>
    internal static class SpecWalker
    {
        /// <summary>スペックが受理する集合を、item の昇順に並べた配列として全部返す。</summary>
        /// <param name="spec">歩くスペック。</param>
        /// <param name="variableCount">アイテムの個数。根の水準はこれ以下でなければならない。</param>
        public static List<int[]> Accepted<TSpec, TState>(TSpec spec, int variableCount)
            where TSpec : struct, IDdSpec<TState>
        {
            List<int[]> accepted = new List<int[]>();
            List<int> chosen = new List<int>();
            Dictionary<int, List<TState>> seenByLevel = new Dictionary<int, List<TState>>();

            TState root = default!;
            int rootLevel = spec.GetRoot(ref root);

            Assert.InRange(rootLevel, DdResult.True, variableCount);

            if (rootLevel == DdResult.True)
            {
                accepted.Add(Array.Empty<int>());
            }
            else if (rootLevel != DdResult.False)
            {
                Walk(spec, variableCount, root, rootLevel, chosen, accepted, seenByLevel);
            }

            return accepted;
        }

        private static void Walk<TSpec, TState>(
            TSpec spec,
            int variableCount,
            TState state,
            int level,
            List<int> chosen,
            List<int[]> accepted,
            Dictionary<int, List<TState>> seenByLevel)
            where TSpec : struct, IDdSpec<TState>
        {
            CheckHashAgreesWithEquality(spec, state, level, seenByLevel);

            for (int value = 0; value <= 1; value++)
            {
                // 構築器は枝ごとにコピーを渡す約束なので、ここでもコピーしてから渡す。
                TState child = state;
                int childLevel = spec.GetChild(ref child, level, value);

                Assert.InRange(childLevel, DdResult.True, level - 1);

                if (childLevel == DdResult.False)
                {
                    continue;
                }

                if (value == 1)
                {
                    chosen.Add(variableCount - level);
                }

                if (childLevel == DdResult.True)
                {
                    // ⊤ に飛んだ時点で、残りのアイテムは「入れない」に確定する（ゼロサプレス）。
                    accepted.Add(chosen.ToArray());
                }
                else
                {
                    Walk(spec, variableCount, child, childLevel, chosen, accepted, seenByLevel);
                }

                if (value == 1)
                {
                    chosen.RemoveAt(chosen.Count - 1);
                }
            }
        }

        /// <summary>同じ水準で等しい状態が同じハッシュを返すことを、出会った状態の総当たりで確かめる。</summary>
        private static void CheckHashAgreesWithEquality<TSpec, TState>(
            TSpec spec,
            TState state,
            int level,
            Dictionary<int, List<TState>> seenByLevel)
            where TSpec : struct, IDdSpec<TState>
        {
            if (!seenByLevel.TryGetValue(level, out List<TState>? seen))
            {
                seen = new List<TState>();
                seenByLevel.Add(level, seen);
            }

            Assert.True(spec.StateEquals(state, state), "StateEquals must be reflexive.");

            foreach (TState other in seen)
            {
                if (spec.StateEquals(state, other))
                {
                    Assert.Equal(spec.StateHashCode(other), spec.StateHashCode(state));
                }
            }

            seen.Add(state);
        }
    }
}
