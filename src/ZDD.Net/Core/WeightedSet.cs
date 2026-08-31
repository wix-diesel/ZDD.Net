using System;
using System.Text;

namespace ZDD.Net.Core
{
    /// <summary>
    /// 重み最適化が返す「集合 1 つと、その重み」の組。
    /// </summary>
    /// <typeparam name="TWeight">重みの型。</typeparam>
    /// <remarks>
    /// <para>
    /// <b>最適値と最適集合を一緒に返す</b>のは、片方だけでは使い道が限られるためである。
    /// 「最短路の長さ」だけ分かっても経路が要るのが普通で、経路の復元は最適値の DP 表さえあれば
    /// 根から 1 本降りるだけ（O(変数の個数)）で済む。別々の API にすると DP をもう一度回すことになる。
    /// </para>
    /// <para>
    /// <b><see cref="Items"/> は毎回新しい配列</b>で、昇順・重複なし。呼び出し側が書き換えても
    /// 族には影響しない（列挙・<see cref="Zdd.ElementAt(System.Numerics.BigInteger, ZddEnumerationOrder)"/>
    /// と同じ約束）。
    /// </para>
    /// </remarks>
    public readonly struct WeightedSet<TWeight>
    {
        private readonly int[]? _items;

        /// <summary>集合と重みを組にする。</summary>
        /// <param name="weight">集合の重み。</param>
        /// <param name="items">集合に属する item index。昇順・重複なし。</param>
        internal WeightedSet(TWeight weight, int[] items)
        {
            Weight = weight;
            _items = items;
        }

        /// <summary>この集合の重み（属する item の重みの総和）。</summary>
        public TWeight Weight { get; }

        /// <summary>この集合に属する item index。昇順・重複なし。空集合なら長さ 0。</summary>
        public int[] Items => _items ?? Array.Empty<int>();

        /// <summary>集合の要素数。</summary>
        public int Size => _items?.Length ?? 0;

        /// <summary><c>{0, 2} (weight 7)</c> の形で表す。</summary>
        public override string ToString()
        {
            StringBuilder text = new StringBuilder();
            int[] items = Items;

            if (items.Length == 0)
            {
                text.Append('∅');
            }
            else
            {
                text.Append('{');

                for (int i = 0; i < items.Length; i++)
                {
                    if (i > 0)
                    {
                        text.Append(", ");
                    }

                    text.Append(items[i]);
                }

                text.Append('}');
            }

            return text.Append(" (weight ").Append(Weight).Append(')').ToString();
        }
    }
}
