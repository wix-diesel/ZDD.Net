using System;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Stress
{
    /// <summary>
    /// 変数 10 万の深い ZDD を 1 組だけ組み立てて、<see cref="DeepZddStressTests"/> の
    /// すべてのテストで使い回す。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 使い回すのは、10 万段の族を毎回組み直すのが CI の実行時間として無駄だからである
    /// （組み立て自体はどのテストの検証対象でもない）。<see cref="ZddManager"/> は
    /// スレッドセーフではないが、xUnit の class fixture を共有するテストは同じクラスの中で
    /// 直列に走るので問題にならない。
    /// </para>
    /// <para>
    /// <b>族の組み立ては <see cref="ZddManager.CreateNode"/> で下から行う</b>。
    /// 公開 API の演算を重ねて作ると、10 万回の演算がそれぞれ 10 万段を降りるので
    /// 二乗の時間がかかる。ここで確かめたいのは「深い族に演算をかけても落ちないこと」であって
    /// 「深い族を演算で組めること」ではない。
    /// </para>
    /// </remarks>
    public sealed class DeepZdd : IDisposable
    {
        /// <summary>変数の個数（docs/PLAN.md §4.5・§11-8 の回帰テスト）。</summary>
        public const int VariableCount = 100_000;

        public DeepZdd()
        {
            Manager = new ZddManager(VariableCount);

            // {{0, 1, …, 99999}}: 全部入りの集合 1 つだけ。10 万段の 1 本鎖になる。
            Zdd full = Manager.Base;

            // {{0}, {1}, …, {99999}}: 1 要素集合を全部集めたもの。これも 10 万段。
            Zdd singletons = Manager.Empty;

            // 2^U: どの item も「入れても入れなくてもよい」。集合は 2^100000 個あるがノードは 10 万個。
            Zdd powerSet = Manager.Base;

            for (int item = VariableCount - 1; item >= 0; item--)
            {
                full = Manager.CreateNode(item, lo: Manager.Empty, hi: full);
                singletons = Manager.CreateNode(item, lo: singletons, hi: Manager.Base);
                powerSet = Manager.CreateNode(item, lo: powerSet, hi: powerSet);
            }

            Full = full;
            Singletons = singletons;
            PowerSet = powerSet;
        }

        public ZddManager Manager { get; }

        /// <summary>{{0, 1, …, 99999}}。集合 1 つだけを持つ族。</summary>
        public Zdd Full { get; }

        /// <summary>{{0}, {1}, …, {99999}}。1 要素集合をすべて集めた族。</summary>
        public Zdd Singletons { get; }

        /// <summary>2^U。全部分集合の族。</summary>
        public Zdd PowerSet { get; }

        /// <summary>0, 1, …, 99999 を並べた配列（<see cref="Full"/> の唯一の要素）。</summary>
        public static int[] AllItems()
        {
            int[] items = new int[VariableCount];
            for (int item = 0; item < VariableCount; item++)
            {
                items[item] = item;
            }

            return items;
        }

        public void Dispose() => Manager.Dispose();
    }
}
