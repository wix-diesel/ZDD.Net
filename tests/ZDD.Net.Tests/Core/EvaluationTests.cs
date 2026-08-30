using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Core
{
    /// <summary>
    /// ボトムアップ評価基盤（<see cref="IDdEval{TValue}"/> /
    /// <see cref="ZddEvaluation.Evaluate{TEval, TValue}"/>）と、その上に乗る
    /// <see cref="Zdd.Count"/> / <see cref="Zdd.CountApprox"/> / <see cref="Zdd.CountBySize"/> の検証。
    /// </summary>
    /// <remarks>
    /// 濃度の照合相手は <see cref="BruteForceFamily"/>（族の集合を実体として持つ素朴実装）で、
    /// 「ZDD が数えた個数」と「素朴に列挙した個数」が一致することを確かめる。
    /// 冪集合や二項係数のように答が閉じた式で書ける族は、素朴実装も介さずに直接比べる。
    /// </remarks>
    public class EvaluationTests
    {
        /// <summary>濃度の照合に使う変数の個数の上限（docs/ROADMAP.md M1-12）。</summary>
        private const int MaxCountingVariableCount = BruteForceFamily.MaxPowerSetVariableCount;

        // ---- 既知の族 ----

        [Fact]
        public void TerminalFamiliesHaveTheirDefinedCardinalities()
        {
            using ZddManager manager = new ZddManager(4);

            // ∅ は集合を 1 つも持たず、{∅} は空集合を 1 つだけ持つ。
            Assert.Equal(BigInteger.Zero, manager.Empty.Count);
            Assert.Equal(BigInteger.One, manager.Base.Count);

            Assert.Equal(0.0, manager.Empty.CountApprox);
            Assert.Equal(1.0, manager.Base.CountApprox);

            // 分布は「要素数 k の集合の個数」。∅ には集合が無いので空、{∅} は要素数 0 が 1 つ。
            Assert.Empty(manager.Empty.CountBySize());
            Assert.Equal(new[] { BigInteger.One }, manager.Base.CountBySize());

            // 1 要素集合 1 つだけの族。
            Assert.Equal(BigInteger.One, manager.Singleton(2).Count);
            Assert.Equal(new[] { BigInteger.Zero, BigInteger.One }, manager.Singleton(2).CountBySize());
        }

        [Fact]
        public void ThePowerSetHasTwoToTheVariableCountElements()
        {
            for (int variableCount = 0; variableCount <= MaxCountingVariableCount; variableCount++)
            {
                using ZddManager manager = new ZddManager(variableCount);
                Zdd powerSet = PowerSetOf(manager);

                // 2^n。ノードは n 個しか無いのに、数える対象は 2^n 個ある。
                Assert.Equal(BigInteger.Pow(2, variableCount), powerSet.Count);
                Assert.Equal(Math.Pow(2, variableCount), powerSet.CountApprox);
                Assert.Equal((long)variableCount, powerSet.NodeCount);
            }
        }

        [Fact]
        public void TheSizeDistributionOfThePowerSetIsTheBinomialCoefficients()
        {
            for (int variableCount = 0; variableCount <= MaxCountingVariableCount; variableCount++)
            {
                using ZddManager manager = new ZddManager(variableCount);

                BigInteger[] bySize = PowerSetOf(manager).CountBySize();

                // 要素数 k の部分集合はちょうど C(n, k) 個ある。
                Assert.Equal(variableCount + 1, bySize.Length);
                for (int size = 0; size <= variableCount; size++)
                {
                    Assert.Equal(Binomial(variableCount, size), bySize[size]);
                }

                Assert.Equal(BigInteger.Pow(2, variableCount), Total(bySize));
            }
        }

        // ---- 素朴実装との照合 ----

        [Fact]
        public void EveryFamilyOfThreeVariablesCountsLikeTheNaiveImplementation()
        {
            const int VariableCount = 3;

            using ZddManager manager = new ZddManager(VariableCount);

            // 3 変数の族は 2^8 = 256 通り。すべて試せる。
            foreach (BruteForceFamily family in FamilyCases.AllFamilies(VariableCount))
            {
                AssertCountsMatchNaive(manager, family);
            }
        }

        [Fact]
        [Trait("Category", "Slow")]
        public void EveryFamilyOfFourVariablesCountsLikeTheNaiveImplementation()
        {
            const int VariableCount = FamilyCases.AllFamiliesVariableLimit;

            using ZddManager manager = new ZddManager(VariableCount);

            foreach (BruteForceFamily family in FamilyCases.AllFamilies(VariableCount))
            {
                AssertCountsMatchNaive(manager, family);
            }
        }

        [Fact]
        public void CountsMatchTheNaiveEnumerationUpToSixteenVariables()
        {
            for (int variableCount = 0; variableCount <= MaxCountingVariableCount; variableCount++)
            {
                using ZddManager manager = new ZddManager(variableCount);

                // 境界（∅ / {∅} / 冪集合）と、ランダムな族を混ぜて回す。
                AssertCountsMatchNaive(manager, BruteForceFamily.Empty(variableCount));
                AssertCountsMatchNaive(manager, BruteForceFamily.Base(variableCount));
                AssertCountsMatchNaive(manager, BruteForceFamily.PowerSet(variableCount));

                foreach (BruteForceFamily family in FamilyCases.RandomFamilies(variableCount, 8, seed: 1200 + variableCount))
                {
                    AssertCountsMatchNaive(manager, family);
                }
            }
        }

        // ---- double 近似 ----

        [Fact]
        public void TheApproximateCountIsExactWhileTheCardinalityFitsInADouble()
        {
            // 2^53 までは double でも整数が 1 つ残らず表せるので、近似ではなく厳密に一致する。
            for (int variableCount = 0; variableCount <= 53; variableCount++)
            {
                using ZddManager manager = new ZddManager(variableCount);
                Zdd powerSet = PowerSetOf(manager);

                Assert.Equal((double)powerSet.Count, powerSet.CountApprox);
            }
        }

        [Fact]
        public void TheApproximateCountSaturatesToInfinityInsteadOfThrowing()
        {
            // 2^2000 は double.MaxValue（およそ 1.8 × 10^308 ≒ 2^1024）を超える。
            const int VariableCount = 2000;

            using ZddManager manager = new ZddManager(VariableCount);
            Zdd powerSet = PowerSetOf(manager);

            Assert.Equal(BigInteger.Pow(2, VariableCount), powerSet.Count);
            Assert.Equal(double.PositiveInfinity, powerSet.CountApprox);

            // 溢れるのは double 側だけで、厳密な濃度は最後まで正しい。
            // 2^1023 はまだ double に収まる（溢れるのは 2^1024 から）。
            using ZddManager small = new ZddManager(1023);
            Assert.True(double.IsFinite(PowerSetOf(small).CountApprox));
        }

        // ---- 要素数別の分布 ----

        [Fact]
        public void TheSizeDistributionEndsAtTheLargestSet()
        {
            using ZddManager manager = new ZddManager(6);

            // 最大の集合が {1, 4}（要素数 2）なので、分布の長さは 3 で止まる。
            Zdd family = ZddFamilies.Build(manager, new[] { 0 }, new[] { 1, 4 }, new[] { 3 });
            Assert.Equal(new[] { BigInteger.Zero, new BigInteger(2), BigInteger.One }, family.CountBySize());

            // 空集合を持つ族では添字 0 が 1 になる。
            Zdd withEmptySet = family | manager.Base;
            Assert.Equal(new[] { BigInteger.One, new BigInteger(2), BigInteger.One }, withEmptySet.CountBySize());

            // 返る配列は呼び出しごとに新しく、書き換えても族は変わらない。
            BigInteger[] bySize = family.CountBySize();
            bySize[0] = 99;
            Assert.Equal(BigInteger.Zero, family.CountBySize()[0]);
        }

        // ---- 利用者が書く評価器 ----

        [Fact]
        public void AUserDefinedEvaluatorCanSumTheSizesOfEverySet()
        {
            const int VariableCount = 8;

            using ZddManager manager = new ZddManager(VariableCount);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 16, seed: 1301))
            {
                Zdd zdd = ZddFamilies.Build(manager, family);

                // 素朴側: 集合ごとに要素数を数えて足す。
                BigInteger expected = family.Masks.Aggregate(
                    BigInteger.Zero,
                    (sum, mask) => sum + BitOperations.PopCount((uint)mask));

                (BigInteger count, BigInteger sizeSum) = zdd.Evaluate<SizeSumEval, (BigInteger Count, BigInteger SizeSum)>(default);

                Assert.Equal(new BigInteger(family.Count), count);
                Assert.Equal(expected, sizeSum);

                // 同じ値は分布からも出せる（Σ k · (要素数 k の個数)）。
                BigInteger[] bySize = zdd.CountBySize();
                BigInteger fromDistribution = BigInteger.Zero;
                for (int size = 0; size < bySize.Length; size++)
                {
                    fromDistribution += size * bySize[size];
                }

                Assert.Equal(expected, fromDistribution);
            }
        }

        [Fact]
        public void AnEvaluatorSeesEachNodeOnceAndEachTerminalOnce()
        {
            const int VariableCount = 10;

            using ZddManager manager = new ZddManager(VariableCount);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 8, seed: 1302))
            {
                Zdd zdd = ZddFamilies.Build(manager, family);

                int[] counters = new int[3];
                zdd.Evaluate<VisitCountingEval, int>(new VisitCountingEval(counters));

                // 共有されたノードを 2 度評価していないこと（メモ化の要）。
                Assert.Equal(zdd.NodeCount, counters[0]);

                // 終端は族の形に依らず 1 度ずつ。
                Assert.Equal(1, counters[1]);
                Assert.Equal(1, counters[2]);
            }
        }

        [Fact]
        public void AnEvaluatorSeesTheItemIndexOfEveryNode()
        {
            const int VariableCount = 12;

            using ZddManager manager = new ZddManager(VariableCount);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 8, seed: 1303))
            {
                Zdd zdd = ZddFamilies.Build(manager, family);

                bool[] seen = new bool[VariableCount];
                zdd.Evaluate<ItemMarkingEval, int>(new ItemMarkingEval(seen));

                // 評価器が受け取る item は、族が実際に使っている変数そのもの
                // （内部のレベルではなく 0 始まりの item index。docs/OPEN-QUESTIONS.md B5）。
                int[] items = Enumerable.Range(0, VariableCount).Where(item => seen[item]).ToArray();
                Assert.Equal(zdd.Support(), items);
            }
        }

        [Fact]
        public void EvaluatorsCanBeNestedInsideOneAnother()
        {
            const int VariableCount = 6;

            using ZddManager manager = new ZddManager(VariableCount);

            Zdd outer = ZddFamilies.Build(manager, new[] { 0, 1 }, new[] { 2 }, new[] { 3, 4, 5 });
            Zdd inner = PowerSetOf(manager);

            // 評価器の中からさらに評価を回しても、作業領域は入れ子で借りられる。
            BigInteger nodes = outer.Evaluate<NestedCountEval, BigInteger>(new NestedCountEval(inner));

            Assert.Equal(inner.Count * outer.NodeCount, nodes);

            // 入れ子から戻ったあとも、外側の評価はふつうに続けられる。
            Assert.Equal(new BigInteger(3), outer.Count);
        }

        // ---- 境界と後始末 ----

        [Fact]
        public void EvaluatingADefaultHandleThrows()
        {
            Zdd invalid = default;

            Assert.Throws<InvalidOperationException>(() => invalid.Count);
            Assert.Throws<InvalidOperationException>(() => invalid.CountApprox);
            Assert.Throws<InvalidOperationException>(() => invalid.CountBySize());
            Assert.Throws<InvalidOperationException>(() => invalid.Evaluate<CardinalityEval, BigInteger>(default));
        }

        [Fact]
        public void EvaluatingAFamilyOfADisposedManagerThrows()
        {
            ZddManager manager = new ZddManager(4);
            Zdd family = manager.Singleton(1) | manager.Base;
            manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => family.Count);
            Assert.Throws<ObjectDisposedException>(() => family.CountApprox);
            Assert.Throws<ObjectDisposedException>(() => family.CountBySize());
        }

        [Fact]
        public void AnEvaluatorThatThrowsDoesNotStrandTheWorkspace()
        {
            const int VariableCount = 6;

            using ZddManager manager = new ZddManager(VariableCount);
            Zdd family = ZddFamilies.Build(manager, new[] { 0, 1 }, new[] { 2, 3 }, new[] { 4, 5 });

            Assert.Throws<InvalidTimeZoneException>(
                () => family.Evaluate<ThrowingEval, int>(default));

            // 借りた作業領域は finally で返るので、次の評価も演算もそのまま通る。
            Assert.Equal(new BigInteger(3), family.Count);
            Assert.Equal(new BigInteger(4), (family | manager.Base).Count);
        }

        [Fact]
        public void RepeatedEvaluationsReturnTheSameValue()
        {
            const int VariableCount = 8;

            using ZddManager manager = new ZddManager(VariableCount);
            Zdd family = ZddFamilies.Build(manager, BruteForceFamily.Random(VariableCount, 0.25, seed: 1304));

            BigInteger first = family.Count;

            // 途中結果表は評価のたびに世代を進めるだけなので、前回の残りが混ざらないこと。
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(first, family.Count);
                Assert.Equal((double)first, family.CountApprox);
            }
        }

        // ---- 深い ZDD（スタックオーバーフロー回帰テスト） ----

        [Fact]
        public void DeepDiagramsDoNotOverflowTheStack()
        {
            // 変数 10 万。素直な再帰実装ならここで StackOverflowException になり、
            // .NET では catch できずプロセスごと落ちる（docs/PLAN.md §4.5）。
            const int VariableCount = 100_000;

            using ZddManager manager = new ZddManager(VariableCount);

            // 集合を 1 つだけ持つ族 {{0, 1, …, 99999}}。10 万段の 1 本鎖になる。
            Zdd chain = manager.Base;
            for (int item = VariableCount - 1; item >= 0; item--)
            {
                chain = manager.CreateNode(item, lo: manager.Empty, hi: chain);
            }

            Assert.Equal((long)VariableCount, chain.NodeCount);
            Assert.Equal(BigInteger.One, chain.Count);
            Assert.Equal(1.0, chain.CountApprox);

            // 10 万段すべてで枝が分かれる族。集合は 2^100000 個あるが、ノードは 10 万個しかない。
            Zdd powerSet = PowerSetOf(manager);

            Assert.Equal(double.PositiveInfinity, powerSet.CountApprox);

            // 深いまま厳密にも数える。上位 60 段だけが分岐し、その下は 1 本鎖なので 2^60 個。
            // 2^100000 を BigInteger で組み上げるのは、深さの回帰テストとしては高くつくだけで
            // 何も足さない（鎖の Count == 1 と合わせて経路は同じ）。
            const int FreeItems = 60;

            // item 60 以降は 1 本鎖（chain の下半分と同じノードが共有される）。
            Zdd tail = manager.Base;
            for (int item = VariableCount - 1; item >= FreeItems; item--)
            {
                tail = manager.CreateNode(item, lo: manager.Empty, hi: tail);
            }

            // その上の item 0〜59 は「入れても入れなくてもよい」。
            Zdd mixed = tail;
            for (int item = FreeItems - 1; item >= 0; item--)
            {
                mixed = manager.CreateNode(item, mixed, mixed);
            }

            Assert.Equal(BigInteger.Pow(2, FreeItems), mixed.Count);
            Assert.Equal(Math.Pow(2, FreeItems), mixed.CountApprox);

            // 利用者の評価器も同じ走査に乗るので、同じく深さで落ちない。
            int[] counters = new int[3];
            powerSet.Evaluate<VisitCountingEval, int>(new VisitCountingEval(counters));
            Assert.Equal(VariableCount, counters[0]);
        }

        // ---- 補助 ----

        /// <summary>
        /// 素朴実装と、<see cref="Zdd.Count"/> / <see cref="Zdd.CountApprox"/> /
        /// <see cref="Zdd.CountBySize"/> の 3 つを突き合わせる。
        /// </summary>
        private static void AssertCountsMatchNaive(ZddManager manager, BruteForceFamily family)
        {
            Zdd zdd = ZddFamilies.Build(manager, family);

            Assert.Equal(new BigInteger(family.Count), zdd.Count);

            // 変数 16 個までなら濃度は 2^65536 …ではなく高々 2^16 個なので、double でも厳密。
            Assert.Equal((double)family.Count, zdd.CountApprox);

            BigInteger[] bySize = zdd.CountBySize();

            // 分布の総和は濃度に一致する。
            Assert.Equal(new BigInteger(family.Count), Total(bySize));

            // 要素数ごとの内訳も素朴に数えたものと一致する。
            int[] expected = new int[manager.VariableCount + 1];
            foreach (int mask in family.Masks)
            {
                expected[BitOperations.PopCount((uint)mask)]++;
            }

            int largest = Array.FindLastIndex(expected, count => count > 0);
            Assert.Equal(largest + 1, bySize.Length);

            for (int size = 0; size < bySize.Length; size++)
            {
                Assert.Equal(new BigInteger(expected[size]), bySize[size]);
            }
        }

        /// <summary>全体集合の冪集合 2^U。どの item も「入れても入れなくてもよい」ノードを積む。</summary>
        private static Zdd PowerSetOf(ZddManager manager)
        {
            Zdd result = manager.Base;

            for (int item = manager.VariableCount - 1; item >= 0; item--)
            {
                result = manager.CreateNode(item, result, result);
            }

            return result;
        }

        private static BigInteger Total(BigInteger[] bySize)
        {
            BigInteger sum = BigInteger.Zero;
            foreach (BigInteger count in bySize)
            {
                sum += count;
            }

            return sum;
        }

        /// <summary>二項係数 C(n, k)。分布の照合相手。</summary>
        private static BigInteger Binomial(int n, int k)
        {
            BigInteger result = BigInteger.One;

            for (int i = 0; i < k; i++)
            {
                result = result * (n - i) / (i + 1);
            }

            return result;
        }

        /// <summary>族の濃度と「集合の要素数の総和」を同時に数える、利用者が書く評価器の例。</summary>
        private readonly struct SizeSumEval : IDdEval<(BigInteger Count, BigInteger SizeSum)>
        {
            public (BigInteger Count, BigInteger SizeSum) EvalTerminal(bool isTrue) =>
                isTrue ? (BigInteger.One, BigInteger.Zero) : (BigInteger.Zero, BigInteger.Zero);

            public (BigInteger Count, BigInteger SizeSum) EvalNode(
                int item,
                (BigInteger Count, BigInteger SizeSum) lo,
                (BigInteger Count, BigInteger SizeSum) hi) =>
                // 1-枝の先の集合は、それぞれ item のぶんだけ要素数が 1 つ多い。
                (lo.Count + hi.Count, lo.SizeSum + hi.SizeSum + hi.Count);
        }

        /// <summary>呼ばれた回数を数えるだけの評価器（メモ化の確認用）。</summary>
        private readonly struct VisitCountingEval : IDdEval<int>
        {
            private readonly int[] _counters;

            public VisitCountingEval(int[] counters) => _counters = counters;

            public int EvalTerminal(bool isTrue)
            {
                _counters[isTrue ? 2 : 1]++;
                return 0;
            }

            public int EvalNode(int item, int lo, int hi)
            {
                _counters[0]++;
                return 0;
            }
        }

        /// <summary>受け取った item に印を付けるだけの評価器。</summary>
        private readonly struct ItemMarkingEval : IDdEval<int>
        {
            private readonly bool[] _seen;

            public ItemMarkingEval(bool[] seen) => _seen = seen;

            public int EvalTerminal(bool isTrue) => 0;

            public int EvalNode(int item, int lo, int hi)
            {
                _seen[item] = true;
                return 0;
            }
        }

        /// <summary>ノードごとに別の族の濃度を数える評価器（評価の入れ子）。</summary>
        private readonly struct NestedCountEval : IDdEval<BigInteger>
        {
            private readonly Zdd _other;

            public NestedCountEval(Zdd other) => _other = other;

            public BigInteger EvalTerminal(bool isTrue) => BigInteger.Zero;

            public BigInteger EvalNode(int item, BigInteger lo, BigInteger hi) => lo + hi + _other.Count;
        }

        /// <summary>必ず例外を投げる評価器（後始末の確認用）。</summary>
        private readonly struct ThrowingEval : IDdEval<int>
        {
            public int EvalTerminal(bool isTrue) => 0;

            public int EvalNode(int item, int lo, int hi) => throw new InvalidTimeZoneException("boom");
        }
    }
}
