using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Properties.Harness
{
    /// <summary>
    /// 生成された族の「設計図」。ZDD ではなくビットマスクの並びで持つ。
    /// </summary>
    /// <remarks>
    /// <para>
    /// プロパティテストの入力は<b>この型</b>であって <see cref="Zdd"/> ではない。ZDD を直接生成すると
    /// マネージャの寿命が生成器に紛れ込むし、反例を縮めた結果も読めない。マスクの並びなら
    /// シュリンクが「変数を減らす／集合を減らす」という人間に読める形で効く。
    /// </para>
    /// <para>
    /// 同じ族を組み立てる手順を 3 通り持たせてある（<see cref="Build"/> /
    /// <see cref="BuildByChange"/> / <see cref="BuildByFlip"/>）。正準性のプロパティ
    /// 「同じ族はどう組み立ててもノード ID が一致する」を確かめるため。
    /// </para>
    /// </remarks>
    internal sealed class FamilySpec
    {
        private readonly int[] _masks;

        /// <param name="variableCount">この族が住む宇宙の変数の個数。</param>
        /// <param name="masks">
        /// 集合をビットマスクで書いた並び。重複と並び順は正規化されるので、生成器は気にしなくてよい。
        /// </param>
        public FamilySpec(int variableCount, IEnumerable<int> masks)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(variableCount);
            ArgumentNullException.ThrowIfNull(masks);

            int universe = variableCount == 0 ? 0 : (1 << variableCount) - 1;
            int[] normalized = masks.Select(mask => mask & universe).Distinct().Order().ToArray();

            VariableCount = variableCount;
            _masks = normalized;
        }

        /// <summary>この族が住む宇宙の変数の個数。</summary>
        public int VariableCount { get; }

        /// <summary>族に属する集合のビットマスク（昇順・重複なし）。</summary>
        public IReadOnlyList<int> Masks => _masks;

        /// <summary>族に属する集合の個数。</summary>
        public int Count => _masks.Length;

        /// <summary>空の族かどうか。</summary>
        public bool IsEmpty => _masks.Length == 0;

        /// <summary>
        /// 集合ごとに <see cref="ZddManager.Singleton"/> の積を作り、それらの和で族を組み立てる。
        /// </summary>
        public Zdd Build(ZddManager manager)
        {
            ArgumentNullException.ThrowIfNull(manager);

            Zdd result = manager.Empty;

            foreach (int mask in _masks)
            {
                Zdd set = manager.Base;

                foreach (int item in ItemsOf(mask))
                {
                    set *= manager.Singleton(item);
                }

                result |= set;
            }

            return result;
        }

        /// <summary>
        /// 同じ族を <see cref="Zdd.Change"/> で組み立てる。集合は item の降順に反転し、
        /// 和も逆順に取るので、<see cref="Build"/> とは節点を作る順序が違う。
        /// </summary>
        public Zdd BuildByChange(ZddManager manager)
        {
            ArgumentNullException.ThrowIfNull(manager);

            Zdd result = manager.Empty;

            for (int i = _masks.Length - 1; i >= 0; i--)
            {
                Zdd set = manager.Base;

                foreach (int item in ItemsOf(_masks[i]).Reverse())
                {
                    set = set.Change(item);
                }

                result = set | result;
            }

            return result;
        }

        /// <summary>
        /// 同じ族を <see cref="Zdd.Flip"/>（複数 item をまとめて反転）で組み立てる。
        /// </summary>
        public Zdd BuildByFlip(ZddManager manager)
        {
            ArgumentNullException.ThrowIfNull(manager);

            Zdd result = manager.Empty;

            foreach (int mask in _masks)
            {
                result |= manager.Base.Flip(ItemsOf(mask).ToArray());
            }

            return result;
        }

        /// <summary>マスクの並びとして和を取った族。ZDD を使わずに期待値を作るために要る。</summary>
        public FamilySpec UnionOfMasks(FamilySpec other)
        {
            ArgumentNullException.ThrowIfNull(other);
            EnsureSameUniverse(other);

            return new FamilySpec(VariableCount, _masks.Concat(other._masks));
        }

        /// <summary>ビットマスクを item の並びに開く。</summary>
        public static IEnumerable<int> ItemsOf(int mask)
        {
            for (int item = 0; mask != 0; item++, mask >>= 1)
            {
                if ((mask & 1) != 0)
                {
                    yield return item;
                }
            }
        }

        /// <summary>反例として読める形にする（<c>n=3 {{0,1},{2}}</c> のような 1 行）。</summary>
        public override string ToString()
        {
            StringBuilder text = new StringBuilder();
            text.Append("n=").Append(VariableCount).Append(' ');
            text.Append(Format(_masks));
            return text.ToString();
        }

        /// <summary>マスクの並びを <c>{{0,1},{2}}</c> の形に整える。</summary>
        public static string Format(IReadOnlyList<int> masks)
        {
            ArgumentNullException.ThrowIfNull(masks);

            return masks.Count == 0
                ? "{} (empty family)"
                : "{" + string.Join(",", masks.Select(FormatSet)) + "}";
        }

        /// <summary>1 つの集合を <c>{0,1}</c> の形に整える。</summary>
        public static string FormatSet(int mask) => "{" + string.Join(",", ItemsOf(mask)) + "}";

        private void EnsureSameUniverse(FamilySpec other)
        {
            if (other.VariableCount != VariableCount)
            {
                throw new ArgumentException(
                    $"The families live in different universes ({VariableCount} vs {other.VariableCount}).",
                    nameof(other));
            }
        }
    }
}
