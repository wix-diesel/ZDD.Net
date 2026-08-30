using System;
using System.Collections.Generic;
using System.Linq;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Harness
{
    /// <summary>
    /// 素朴な族（<see cref="BruteForceFamily"/>）と ZDD（<see cref="Zdd"/>）の間を行き来する。
    /// </summary>
    /// <remarks>
    /// 照合は「素朴側で作った族を ZDD に組み立てる」→「ZDD 側で演算する」→
    /// 「結果を族に戻して素朴側の答えと比べる」という往復で行う。その両端がここにある。
    /// どちらの向きも <b>ZDD の演算 API を使わない</b>（<see cref="ZddManager.CreateNode"/> と
    /// ノード表の読み出しだけで済ませる）。検証したい当のものを検証に使わないため。
    /// </remarks>
    internal static class ZddFamilies
    {
        /// <summary>
        /// 素朴な族から ZDD を組み立てる。
        /// </summary>
        /// <remarks>
        /// 段ごとに族を「その item を含まない側 / 含む側」へ割り、下の段から
        /// <see cref="ZddManager.CreateNode"/> で積み上げる。<b>再帰しない</b>ので、
        /// 変数が多くてもスタックを消費しない。
        /// 同じ内容の部分族は同じキーになるので、共有されるノードは 1 度しか作られない。
        /// </remarks>
        public static Zdd Build(ZddManager manager, BruteForceFamily family)
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(family);

            if (manager.VariableCount != family.VariableCount)
            {
                throw new ArgumentException(
                    $"The manager has {manager.VariableCount} variable(s) but the family has {family.VariableCount}.",
                    nameof(family));
            }

            int variableCount = manager.VariableCount;

            List<Dictionary<string, Group>> levels = new List<Dictionary<string, Group>>(variableCount + 1);
            for (int item = 0; item <= variableCount; item++)
            {
                levels.Add(new Dictionary<string, Group>(StringComparer.Ordinal));
            }

            string rootKey = Register(levels[0], family.Masks);

            for (int item = 0; item < variableCount; item++)
            {
                int bit = 1 << item;

                // 書き込む先は次の段 levels[item + 1] なので、列挙中の辞書は変わらない。
                foreach (Group group in levels[item].Values)
                {
                    group.LoKey = Register(levels[item + 1], group.Masks.Where(mask => (mask & bit) == 0));
                    group.HiKey = Register(
                        levels[item + 1],
                        group.Masks.Where(mask => (mask & bit) != 0).Select(mask => mask & ~bit));
                }
            }

            // 族はマスクの集合だけで決まるので、段をまたいで同じキーが現れても同じノードでよい。
            Dictionary<string, Zdd> built = new Dictionary<string, Zdd>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, Group> entry in levels[variableCount])
            {
                // 全 item を割り振り終えた段に残るのは空集合だけ。
                built[entry.Key] = entry.Value.Masks.Count == 0 ? manager.Empty : manager.Base;
            }

            for (int item = variableCount - 1; item >= 0; item--)
            {
                foreach (KeyValuePair<string, Group> entry in levels[item])
                {
                    Group group = entry.Value;
                    built[entry.Key] = manager.CreateNode(item, built[group.LoKey!], built[group.HiKey!]);
                }
            }

            return built[rootKey];
        }

        /// <summary>item の並びで書いた集合たちから、直接 ZDD を組み立てる（短く書きたいとき用）。</summary>
        public static Zdd Build(ZddManager manager, params int[][] sets)
        {
            ArgumentNullException.ThrowIfNull(manager);
            return Build(manager, BruteForceFamily.FromSets(manager.VariableCount, sets));
        }

        /// <summary>
        /// ZDD が表す族を素朴な族に落とす。根から終端 ⊤ までのパスを明示スタックで全部辿る。
        /// </summary>
        /// <remarks>
        /// パスの本数は族の要素数に等しいので、大きな族には使えない（照合は小さい変数数で行う前提）。
        /// </remarks>
        public static BruteForceFamily ToBruteForce(in Zdd zdd)
        {
            ZddManager manager = zdd.Manager;
            int variableCount = manager.VariableCount;

            if (variableCount > BruteForceFamily.MaxVariableCount)
            {
                throw new ArgumentException(
                    $"A family of {variableCount} variable(s) does not fit in a bit mask " +
                    $"(the limit is {BruteForceFamily.MaxVariableCount}).",
                    nameof(zdd));
            }

            NodeTable nodes = manager.Table.Nodes;
            List<int> masks = new List<int>();

            Stack<(int Id, int Mask)> stack = new Stack<(int, int)>();
            stack.Push((zdd.Id, 0));

            while (stack.Count > 0)
            {
                (int id, int mask) = stack.Pop();

                if (id == NodeTable.Bottom)
                {
                    continue;
                }

                if (id == NodeTable.Top)
                {
                    masks.Add(mask);
                    continue;
                }

                ZddNode node = nodes[id];
                int item = manager.ItemOf(node.Level);

                stack.Push((node.Lo, mask));
                stack.Push((node.Hi, mask | (1 << item)));
            }

            return BruteForceFamily.FromMasks(variableCount, masks);
        }

        /// <summary>族を登録して、その内容を表すキーを返す。同じ内容なら同じキーになる。</summary>
        private static string Register(Dictionary<string, Group> level, IEnumerable<int> masks)
        {
            SortedSet<int> sorted = new SortedSet<int>(masks);
            string key = string.Join(",", sorted);

            if (!level.ContainsKey(key))
            {
                level.Add(key, new Group(sorted));
            }

            return key;
        }

        /// <summary>組み立ての途中で現れる「ある段より下だけを見た族」。</summary>
        private sealed class Group
        {
            public Group(SortedSet<int> masks) => Masks = masks;

            public SortedSet<int> Masks { get; }

            public string? LoKey { get; set; }

            public string? HiKey { get; set; }
        }
    }
}
