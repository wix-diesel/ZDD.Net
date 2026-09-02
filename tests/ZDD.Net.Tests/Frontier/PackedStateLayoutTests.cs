using System;
using Xunit;
using ZDD.Net.Frontier;

namespace ZDD.Net.Tests.Frontier
{
    /// <summary>
    /// 状態の詰め方（<see cref="PackedStateLayout"/>）。
    /// </summary>
    /// <remarks>
    /// スロット 1 個は「フロンティア内のスロット番号か小さな番兵」なので普通は 1 バイトで足りる。
    /// 足りなくなったときだけ 2 バイト・4 バイトへ広げる、という切り替えが要点なので、
    /// その境界（値域の幅 255/256、65535/65536）と、負値・バイアス付きの往復を見る。
    /// 窓は広がる一方（既に他の表が詰めた状態を入れ続けなければならない）なので、
    /// 一度広げたら元の窓を含む、という点も併せて確かめる。
    /// </remarks>
    public class PackedStateLayoutTests
    {
        /// <summary>初期の窓は、負の番兵のぶんだけ 0 より下から始まる。</summary>
        [Fact]
        public void ANewLayoutIsOneBytePerSlotWithRoomForSmallSentinels()
        {
            PackedStateLayout layout = new PackedStateLayout();

            Assert.Equal(1, layout.BytesPerSlot);
            Assert.Equal(-8, layout.Bias);
            Assert.Equal(2, layout.StrideFor(arrayLength: 2));
            AssertRoundTrips(layout, new[] { -8, 0, 247 });
        }

        /// <summary>
        /// スロット幅の切り替え境界。窓（初期は <c>-8..247</c>）と新しい値を合わせた値域の幅が
        /// 255 を超えたら 2 バイト、65535 を超えたら 4 バイトになる。
        /// </summary>
        [Theory]
        [InlineData(-8, 247, 1)]
        [InlineData(0, 247, 1)]
        [InlineData(0, 248, 2)]
        [InlineData(-9, 247, 2)]
        [InlineData(-8, 65527, 2)]
        [InlineData(-8, 65528, 4)]
        [InlineData(-9, 65526, 2)]
        [InlineData(-9, 65527, 4)]
        public void TheWidthFollowsTheSpanOfTheValuesSeen(int min, int max, int expectedBytesPerSlot)
        {
            PackedStateLayout layout = new PackedStateLayout();

            Extend(layout, min, max);

            Assert.Equal(expectedBytesPerSlot, layout.BytesPerSlot);
            AssertRoundTrips(layout, new[] { min, max, min, max });
        }

        /// <summary>境界のすぐ内側は詰められ、すぐ外側は拒まれる（呼び手が広げる合図）。</summary>
        [Theory]
        [InlineData(-8, true)]
        [InlineData(-9, false)]
        [InlineData(247, true)]
        [InlineData(248, false)]
        public void PackingRejectsExactlyTheValuesOutsideTheWindow(int value, bool expected)
        {
            PackedStateLayout layout = new PackedStateLayout();
            byte[] destination = new byte[1];

            Assert.Equal(expected, layout.TryPack(new[] { value }, destination));
        }

        /// <summary>mate 配列そのままの状態（小さな負の番兵つき）は、広げずに 1 バイトで収まる。</summary>
        [Fact]
        public void AMateArrayStateNeedsNoWidening()
        {
            PackedStateLayout layout = new PackedStateLayout();
            int[] state = { 0, -1, -2, 200 };

            Assert.True(layout.TryPack(state, new byte[state.Length]));
            Assert.Equal(1, layout.BytesPerSlot);
            AssertRoundTrips(layout, state);
        }

        /// <summary>広げた窓は、必ず前の窓を丸ごと含む（既に詰めた状態が入らなくなると壊れる）。</summary>
        [Fact]
        public void AWiderWindowStillHoldsEverythingTheOldOneDid()
        {
            PackedStateLayout layout = new PackedStateLayout();

            layout.Extend(new[] { 1000 });

            Assert.Equal(2, layout.BytesPerSlot);
            AssertRoundTrips(layout, new[] { -8, 0, 247, 1000 });

            layout.Extend(new[] { 1_000_000 });

            Assert.Equal(4, layout.BytesPerSlot);
            AssertRoundTrips(layout, new[] { -8, 0, 247, 1000, 1_000_000 });
        }

        [Fact]
        public void TheWidestLayoutHoldsEveryIntWithoutABias()
        {
            PackedStateLayout layout = new PackedStateLayout();

            layout.Extend(new[] { int.MinValue, int.MaxValue });

            Assert.Equal(4, layout.BytesPerSlot);
            Assert.Equal(0, layout.Bias);
            AssertRoundTrips(layout, new[] { int.MinValue, -1, 0, 1, int.MaxValue });
        }

        /// <summary>広げるたびに版が上がる（表はこれを見て既存の状態を詰め直す）。</summary>
        [Fact]
        public void EveryExtensionBumpsTheVersion()
        {
            PackedStateLayout layout = new PackedStateLayout();
            int version = layout.Version;

            layout.Extend(new[] { 300 });
            Assert.NotEqual(version, layout.Version);

            version = layout.Version;
            layout.Extend(new[] { 100000 });
            Assert.NotEqual(version, layout.Version);
        }

        /// <summary>
        /// 値域をじりじり広げ続けるスペックでも、詰め直しは高々 2 回で打ち止めになる。
        /// </summary>
        /// <remarks>
        /// 広げた窓は前の窓を含むので、次に外れる値が来た時点で値域は必ず今の幅を超える——
        /// つまり幅は 1 → 2 → 4 と上がるだけで、同じ幅のまま詰め直すことがない。
        /// </remarks>
        [Fact]
        public void WideningNeverReencodesMoreThanTwice()
        {
            PackedStateLayout layout = new PackedStateLayout();
            int widenings = 0;

            for (int i = 1; i <= 20; i++)
            {
                int[] state = { -8 - i };

                if (!layout.TryPack(state, new byte[state.Length * layout.BytesPerSlot]))
                {
                    layout.Extend(state);
                    widenings++;
                }
            }

            Assert.Equal(2, widenings);
            Assert.Equal(4, layout.BytesPerSlot);
            Assert.Equal(2, layout.Version);
            AssertRoundTrips(layout, new[] { -28, -8, 0, 247 });
        }

        /// <summary>スロット数が大きくても、必要なバイト数の計算が壊れない。</summary>
        [Fact]
        public void AStrideThatCannotBeAllocatedIsRejected()
        {
            PackedStateLayout layout = new PackedStateLayout();
            layout.Extend(new[] { int.MaxValue });

            Assert.Equal(4, layout.BytesPerSlot);
            Assert.Equal(4 * 1000, layout.StrideFor(1000));
            Assert.Throws<InvalidOperationException>(() => layout.StrideFor(Array.MaxLength / 2));
        }

        /// <summary>状態を <paramref name="layout"/> で詰めて戻すと、元の値がそのまま返る。</summary>
        private static void AssertRoundTrips(PackedStateLayout layout, int[] state)
        {
            byte[] packed = new byte[state.Length * layout.BytesPerSlot];
            Assert.True(layout.TryPack(state, packed), "The layout must hold every value it was extended for.");

            int[] unpacked = new int[state.Length];
            PackedStateLayout.Unpack(packed, unpacked, layout.Bias, layout.BytesPerSlot);

            Assert.Equal(state, unpacked);
        }

        /// <summary><paramref name="min"/> と <paramref name="max"/> が入るまで広げる。</summary>
        private static void Extend(PackedStateLayout layout, int min, int max)
        {
            int[] state = { min, max };

            if (!layout.TryPack(state, new byte[state.Length * layout.BytesPerSlot]))
            {
                layout.Extend(state);
            }
        }
    }
}
