using System;
using System.Diagnostics;
using System.Linq;
using Xunit;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Stress
{
    /// <summary>
    /// 族を作らない問い合わせ（<see cref="Zdd.IsSubsetOf"/> / <see cref="Zdd.Overlaps"/>）が、
    /// 深い ZDD で<b>変数の個数に対して線形のまま</b>であることの回帰テスト（#90）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>何が起きていたか</b>: <c>QueryOperations</c> は「片方が ⊤ の対」に出会うと、もう片方が
    /// 空集合を持つかを 0-枝の連なりを辿って確かめる。この答をどこにも覚えていなかったため、
    /// <c>{{0}, {1}, …, {n-1}}</c> のように<b>段ごとに 1-枝が ⊤ へ着く</b>形の族では、
    /// 段を 1 つ降りるたびに長さ <c>k</c> の連なりを辿り直すことになり、合計 Σk = O(変数の個数の二乗)
    /// になっていた。この対は演算その場で答が出るので、走査のメモ化からも漏れていた。
    /// </para>
    /// <para>
    /// <b>実測</b>（このテストを書いた機械、Release、変数 1.25 万 → 10 万）:
    /// </para>
    /// <list type="table">
    /// <listheader><term>変数の個数</term><description>直す前 → 直した後</description></listheader>
    /// <item><term>12,500</term><description>393 ms → 1 ms 未満</description></item>
    /// <item><term>25,000</term><description>1,445 ms → 約 20 ms</description></item>
    /// <item><term>50,000</term><description>5,788 ms → 約 44 ms</description></item>
    /// <item><term>100,000</term><description>24,331 ms → 約 82 ms</description></item>
    /// </list>
    /// <para>
    /// <b>なぜ時間で見るか</b>: 二乗か線形かは<b>手間の増え方</b>の話で、答そのものは直す前も後も同じ
    /// （正しさは <c>EnumerationTests</c> の総当たり照合と <c>EnumerationProperties</c> が見ている）。
    /// 内部の走査回数を数える口は公開していないので、ここでは実行時間で見る。
    /// <see cref="Budget"/> は直した後の実測の 20 倍以上、二乗に戻った場合の 10 分の 1 以下にとってあり、
    /// 機械の速さの違いで揺れる幅よりも、二乗と線形の開き（10 万で 300 倍）のほうが桁違いに大きい。
    /// </para>
    /// <para>
    /// <b>お題が 2 つある理由</b>: <see cref="Zdd.IsSubsetOf"/> は「偽が 1 つ出れば偽」、
    /// <see cref="Zdd.Overlaps"/> は「真が 1 つ出れば真」で打ち切る。
    /// <c>Singletons.IsSubsetOf(PowerSet)</c> は最後まで真なので打ち切りが効かず、
    /// そのまま二乗になっていた。一方 <c>Overlaps</c> を同じ入力に掛けると最初の対で真に決着するので、
    /// 二乗にはならない。<see cref="Zdd.Overlaps"/> 側で同じことが起きるのは<b>答が偽で、
    /// なおかつ ⊤ との対が段ごとに現れる</b>ときなので、1 要素集合の族と<b>2 要素集合の族</b>を
    /// 突き合わせる（交わりは空）お題を別に立ててある。
    /// </para>
    /// <para>
    /// <b>作業領域を別に持つ理由</b>: <see cref="DeepZddStressTests"/> の
    /// <see cref="DeepZdd"/> にお題を足すと、あちらの
    /// <c>DotOutputAndStatisticsFinish</c> が見ているノードの総数が動く。
    /// ここは自前の <see cref="ZddManager"/> を持つ。
    /// </para>
    /// </remarks>
    [Trait("Category", "Slow")]
    public class QueryScalingTests : IClassFixture<QueryScalingTests.Families>
    {
        /// <summary>1 回の問い合わせに許す時間。上の remarks の実測を参照。</summary>
        private static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

        private readonly Families _families;

        public QueryScalingTests(Families families)
        {
            _families = families;
        }

        [Fact]
        public void SingletonsBeingBelowThePowerSetStaysLinear()
        {
            Stopwatch watch = Stopwatch.StartNew();
            bool answer = _families.Singletons.IsSubsetOf(_families.PowerSet);
            watch.Stop();

            // どの {i} も U の部分集合なので真。打ち切りは効かず、10 万段すべてを見る。
            Assert.True(answer);
            AssertWithinBudget("Singletons.IsSubsetOf(PowerSet)", watch.Elapsed);
        }

        [Fact]
        public void SingletonsNotMeetingThePairsStaysLinear()
        {
            Stopwatch watch = Stopwatch.StartNew();
            bool answer = _families.Singletons.Overlaps(_families.Pairs);
            watch.Stop();

            // 1 要素集合と 2 要素集合に共通の集合は無いので偽。真での打ち切りは効かない。
            Assert.False(answer);
            AssertWithinBudget("Singletons.Overlaps(Pairs)", watch.Elapsed);

            // Overlaps は可換なので、左右を入れ替えても同じ走査になる。
            watch.Restart();
            bool flipped = _families.Pairs.Overlaps(_families.Singletons);
            watch.Stop();

            Assert.False(flipped);
            AssertWithinBudget("Pairs.Overlaps(Singletons)", watch.Elapsed);
        }

        /// <summary>
        /// <see cref="Families.Pairs"/> の組み立てが本当に「2 要素集合の族」になっていることの確認。
        /// </summary>
        /// <remarks>
        /// 10 万段では答（偽）しか見られないので、同じ組み立てを小さな宇宙で回して形を確かめる。
        /// ここがずれると、上の 2 つは<b>別の形を測っているのに緑のまま</b>になりうる。
        /// </remarks>
        [Fact]
        public void ThePairsFamilyIsEveryTwoElementSubset()
        {
            const int VariableCount = 5;

            using ZddManager manager = new ZddManager(VariableCount);

            Families.Build(manager, VariableCount, out Zdd singletons, out Zdd powerSet, out Zdd pairs);

            int[][] expected = Enumerable
                .Range(0, VariableCount)
                .SelectMany(low => Enumerable.Range(low + 1, VariableCount - low - 1).Select(high => new[] { low, high }))
                .ToArray();

            int[][] actual = pairs.ToArray();
            Array.Sort(actual, (left, right) => left[0] != right[0] ? left[0] - right[0] : left[1] - right[1]);

            Assert.Equal(expected.Length, actual.Length);
            Assert.Equal(expected, actual);

            // 小さな宇宙でも答は同じ。1 要素集合と 2 要素集合は交わらず、どちらも冪集合に収まる。
            Assert.False(singletons.Overlaps(pairs));
            Assert.True(singletons.IsSubsetOf(powerSet));
            Assert.True(pairs.IsSubsetOf(powerSet));
        }

        private static void AssertWithinBudget(string what, TimeSpan elapsed) =>
            Assert.True(
                elapsed < Budget,
                $"{what} on {Families.VariableCount} variables took {elapsed}, which exceeds the {Budget} budget. " +
                "That is the shape of the quadratic re-walk fixed in #90 (QueryOperations.HasEmptySet).");

        /// <summary>
        /// 変数 10 万の族を 1 組だけ組み立てて、このクラスのテストで使い回す。
        /// </summary>
        /// <remarks>
        /// <see cref="DeepZdd"/> と同じく <see cref="ZddManager.CreateNode"/> で下から組む
        /// （公開 API の演算を重ねると、10 万回の演算がそれぞれ 10 万段を降りて二乗になる）。
        /// 測るのは問い合わせだけなので、組み立ての時間は測定に含めない。
        /// </remarks>
        public sealed class Families : IDisposable
        {
            /// <summary>変数の個数。#90 の表のいちばん下の行に合わせてある。</summary>
            public const int VariableCount = 100_000;

            public Families()
            {
                Manager = new ZddManager(VariableCount);

                Build(Manager, VariableCount, out Zdd singletons, out Zdd powerSet, out Zdd pairs);

                Singletons = singletons;
                PowerSet = powerSet;
                Pairs = pairs;

                Warmup();
            }

            public ZddManager Manager { get; }

            /// <summary>{{0}, {1}, …}。1 要素集合をすべて集めた族。</summary>
            public Zdd Singletons { get; }

            /// <summary>2^U。全部分集合の族。</summary>
            public Zdd PowerSet { get; }

            /// <summary>{{i, j} | i &lt; j}。2 要素集合をすべて集めた族。</summary>
            public Zdd Pairs { get; }

            /// <summary>1 要素集合・全部分集合・2 要素集合の族を、下の段から組み上げる。</summary>
            public static void Build(
                ZddManager manager,
                int variableCount,
                out Zdd singletons,
                out Zdd powerSet,
                out Zdd pairs)
            {
                singletons = manager.Empty;
                powerSet = manager.Base;
                pairs = manager.Empty;

                for (int item = variableCount - 1; item >= 0; item--)
                {
                    powerSet = manager.CreateNode(item, lo: powerSet, hi: powerSet);

                    // item を採ったら、その先から 1 つだけ採る ＝ 2 要素集合。
                    // singletons を進める前に読むので、hi は「item より下の 1 要素集合」になる。
                    pairs = manager.CreateNode(item, lo: pairs, hi: singletons);
                    singletons = manager.CreateNode(item, lo: singletons, hi: manager.Base);
                }
            }

            public void Dispose() => Manager.Dispose();

            /// <summary>
            /// 測る前に問い合わせの経路を 1 度通しておく。
            /// </summary>
            /// <remarks>
            /// 10 万段の測定に初回の JIT コンパイルの分が混ざると、直っていても数十 ms 上乗せされる。
            /// 予算には十分な余裕があるが、測っているものを濁らせないために小さな宇宙で暖めておく。
            /// </remarks>
            private void Warmup()
            {
                const int WarmupVariableCount = 64;

                using ZddManager warmup = new ZddManager(WarmupVariableCount);

                Build(warmup, WarmupVariableCount, out Zdd singletons, out Zdd powerSet, out Zdd pairs);

                _ = singletons.IsSubsetOf(powerSet);
                _ = singletons.Overlaps(pairs);
                _ = pairs.Overlaps(singletons);
            }
        }
    }
}
