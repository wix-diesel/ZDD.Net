using System;
using System.Collections.Generic;
using Xunit;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Core
{
    public class OperationCacheTests
    {
        /// <summary>
        /// 可換な二項演算。順序を入れ替えても同じエントリを共有しなければならない。
        /// <see cref="ZddOperation"/> は internal なので、public な理論データには <c>int</c> で載せる。
        /// </summary>
        public static TheoryData<int> CommutativeOperations => new TheoryData<int>
        {
            (int)ZddOperation.Union,
            (int)ZddOperation.Intersect,
            (int)ZddOperation.SymmetricDifference,
            (int)ZddOperation.Meet,
        };

        /// <summary>
        /// キャッシュサイズの代表値。0 = 無効、1 = 全部が同じスロットに落ちる最悪ケース、
        /// 2 / 8 = 衝突が頻発するサイズ、既定 = ほぼ衝突しないサイズ。
        /// </summary>
        public static TheoryData<int> CacheSizes => new TheoryData<int> { 0, 1, 2, 8, 1024 };

        // ---- 生成とサイズ ----

        [Fact]
        public void CapacitiesAreRoundedToPowersOfTwo()
        {
            OperationCache cache = new OperationCache(initialCapacity: 100, maxCapacity: 1000);

            // 初期サイズは切り上げ、上限は切り下げ（指定した上限を超えないため）。
            Assert.Equal(128, cache.Capacity);
            Assert.Equal(512, cache.MaxCapacity);
            Assert.True(cache.IsEnabled);
        }

        [Fact]
        public void TheDefaultConstructorUsesTheDefaultSizes()
        {
            OperationCache cache = new OperationCache();

            Assert.Equal(OperationCache.DefaultInitialCapacity, cache.Capacity);
            Assert.Equal(OperationCache.DefaultMaxCapacity, cache.MaxCapacity);
        }

        [Fact]
        public void TheInitialCapacityNeverExceedsTheMaximum()
        {
            OperationCache cache = new OperationCache(initialCapacity: 4096, maxCapacity: 16);

            Assert.Equal(16, cache.Capacity);
        }

        [Fact]
        public void AZeroMaximumDisablesTheCacheEntirely()
        {
            OperationCache cache = new OperationCache(initialCapacity: 4096, maxCapacity: 0);

            Assert.Equal(0, cache.Capacity);
            Assert.False(cache.IsEnabled);
        }

        [Theory]
        [InlineData(-1, 16, "initialCapacity")]
        [InlineData(16, -1, "maxCapacity")]
        public void ConstructorRejectsNegativeSizes(int initialCapacity, int maxCapacity, string paramName)
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => new OperationCache(initialCapacity, maxCapacity));

            Assert.Equal(paramName, exception.ParamName);
        }

        [Fact]
        public void ConstructorRejectsAMaximumBeyondTheHardLimit()
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => new OperationCache(0, OperationCache.CapacityLimit + 1));

            Assert.Equal("maxCapacity", exception.ParamName);
        }

        // ---- 引きと書き込み ----

        [Fact]
        public void AStoredBinaryResultIsFoundAgain()
        {
            OperationCache cache = new OperationCache(16, 16);

            cache.PutBinary(ZddOperation.Difference, 7, 9, 42);

            Assert.True(cache.TryGetBinary(ZddOperation.Difference, 7, 9, out int result));
            Assert.Equal(42, result);
        }

        [Fact]
        public void AStoredUnaryResultIsFoundAgain()
        {
            OperationCache cache = new OperationCache(16, 16);

            cache.PutUnary(ZddOperation.Change, 7, item: 3, result: 42);

            Assert.True(cache.TryGetUnary(ZddOperation.Change, 7, 3, out int result));
            Assert.Equal(42, result);
        }

        [Fact]
        public void TheBottomTerminalIsAValidCachedResult()
        {
            OperationCache cache = new OperationCache(16, 16);

            cache.PutBinary(ZddOperation.Intersect, 4, 5, 0);

            // 結果 0（⊥）は「空きエントリ」と区別されなければならない。
            Assert.True(cache.TryGetBinary(ZddOperation.Intersect, 4, 5, out int result));
            Assert.Equal(0, result);
        }

        [Theory]
        [MemberData(nameof(CommutativeOperations))]
        public void CommutativeOperationsShareOneEntryWhateverTheOperandOrder(int operation)
        {
            ZddOperation op = (ZddOperation)operation;

            // サイズ 1 なので、別のエントリに入っていればヒットしようがない。
            OperationCache cache = new OperationCache(1, 1);

            cache.PutBinary(op, 11, 4, 99);

            Assert.True(cache.TryGetBinary(op, 4, 11, out int result));
            Assert.Equal(99, result);
        }

        [Fact]
        public void NonCommutativeOperationsKeepTheOperandOrder()
        {
            OperationCache cache = new OperationCache(64, 64);

            cache.PutBinary(ZddOperation.Difference, 11, 4, 99);

            Assert.False(cache.TryGetBinary(ZddOperation.Difference, 4, 11, out int result));
            Assert.Equal(0, result);
        }

        // ---- 誤ヒットが起きないこと ----

        [Fact]
        public void AnotherOperationNeverHitsAnExistingEntry()
        {
            // サイズ 1 なので全ての引きが同じスロットに落ちる。Op を照合していなければ誤ヒットする。
            OperationCache cache = new OperationCache(1, 1);

            cache.PutBinary(ZddOperation.Union, 5, 6, 123);

            Assert.False(cache.TryGetBinary(ZddOperation.Intersect, 5, 6, out int result));
            Assert.Equal(0, result);
        }

        [Fact]
        public void OtherOperandsNeverHitAnExistingEntry()
        {
            OperationCache cache = new OperationCache(1, 1);

            cache.PutBinary(ZddOperation.Union, 5, 6, 123);

            Assert.False(cache.TryGetBinary(ZddOperation.Union, 5, 7, out _));
            Assert.False(cache.TryGetBinary(ZddOperation.Union, 4, 6, out _));
            Assert.False(cache.TryGetBinary(ZddOperation.Union, int.MaxValue, 6, out _));
        }

        [Fact]
        public void AUnaryEntryIsNeverConfusedWithABinaryOne()
        {
            OperationCache cache = new OperationCache(1, 1);

            // オペランドの組は同じ (5, 6) だが、演算が違えばヒットしてはならない。
            cache.PutUnary(ZddOperation.Change, 5, item: 6, result: 123);

            Assert.False(cache.TryGetBinary(ZddOperation.Union, 5, 6, out _));
            Assert.True(cache.TryGetUnary(ZddOperation.Change, 5, 6, out int result));
            Assert.Equal(123, result);
        }

        [Fact]
        public void NegativeAndLargeOperandsRoundTripThroughTheKey()
        {
            OperationCache cache = new OperationCache(64, 64);

            // ノード ID は常に非負だが、キーの詰め方が符号で壊れないことは確かめておく。
            cache.PutBinary(ZddOperation.Difference, int.MinValue, int.MaxValue, 7);

            Assert.True(cache.TryGetBinary(ZddOperation.Difference, int.MinValue, int.MaxValue, out int result));
            Assert.Equal(7, result);
            Assert.False(cache.TryGetBinary(ZddOperation.Difference, int.MaxValue, int.MinValue, out _));
        }

        // ---- 統計 ----

        [Fact]
        public void StatisticsCountLookupsHitsAndMisses()
        {
            OperationCache cache = new OperationCache(64, 64);

            Assert.Equal(0L, cache.Lookups);
            Assert.Equal(0.0, cache.HitRate);

            cache.PutBinary(ZddOperation.Union, 2, 3, 10);

            Assert.True(cache.TryGetBinary(ZddOperation.Union, 2, 3, out _));
            Assert.True(cache.TryGetBinary(ZddOperation.Union, 3, 2, out _));
            Assert.False(cache.TryGetBinary(ZddOperation.Union, 2, 4, out _));

            Assert.Equal(3L, cache.Lookups);
            Assert.Equal(2L, cache.Hits);
            Assert.Equal(1L, cache.Misses);
            Assert.Equal(2.0 / 3.0, cache.HitRate, 12);

            // 書き込みは参照回数に数えない。
            cache.PutBinary(ZddOperation.Union, 5, 6, 11);
            Assert.Equal(3L, cache.Lookups);
        }

        [Fact]
        public void CollisionsAreCountedOnlyWhenAnotherEntryIsOverwritten()
        {
            OperationCache cache = new OperationCache(1, 1);

            // 空きスロットへの書き込みは衝突ではない。
            cache.PutBinary(ZddOperation.Union, 2, 3, 10);
            Assert.Equal(0L, cache.Collisions);

            // 同じキーの上書きも衝突ではない（結果が変わっても同じ部分問題）。
            cache.PutBinary(ZddOperation.Union, 2, 3, 11);
            Assert.Equal(0L, cache.Collisions);

            // 別の部分問題で追い出したときだけ数える。
            cache.PutBinary(ZddOperation.Union, 4, 5, 12);
            Assert.Equal(1L, cache.Collisions);

            cache.PutBinary(ZddOperation.Intersect, 4, 5, 13);
            Assert.Equal(2L, cache.Collisions);

            // 追い出された側はもう引けない（が、答えが壊れるわけではない）。
            Assert.False(cache.TryGetBinary(ZddOperation.Union, 2, 3, out _));
        }

        [Fact]
        public void ClearDropsEveryEntryButKeepsStatistics()
        {
            OperationCache cache = new OperationCache(64, 64);

            cache.PutBinary(ZddOperation.Union, 2, 3, 10);
            Assert.True(cache.TryGetBinary(ZddOperation.Union, 2, 3, out _));

            cache.Clear();

            Assert.False(cache.TryGetBinary(ZddOperation.Union, 2, 3, out _));
            Assert.Equal(64, cache.Capacity);
            Assert.Equal(2L, cache.Lookups);
            Assert.Equal(1L, cache.Hits);
        }

        [Fact]
        public void ResetStatisticsKeepsTheEntries()
        {
            OperationCache cache = new OperationCache(64, 64);

            cache.PutBinary(ZddOperation.Union, 2, 3, 10);
            Assert.True(cache.TryGetBinary(ZddOperation.Union, 2, 3, out _));

            cache.ResetStatistics();

            Assert.Equal(0L, cache.Lookups);
            Assert.Equal(0L, cache.Hits);
            Assert.Equal(0L, cache.Collisions);
            Assert.True(cache.TryGetBinary(ZddOperation.Union, 2, 3, out _));
        }

        // ---- サイズの自動調整 ----

        [Fact]
        public void TuneGrowsTowardsAQuarterOfTheNodeCount()
        {
            OperationCache cache = new OperationCache(initialCapacity: 4, maxCapacity: 1 << 20);

            // 1000 ノード → 250 エントリ欲しい → 2 の冪に切り上げて 256。
            Assert.True(cache.Tune(1000));
            Assert.Equal(256, cache.Capacity);

            // 既に足りていれば触らない。
            Assert.False(cache.Tune(1000));
            Assert.False(cache.Tune(0));
            Assert.Equal(256, cache.Capacity);
        }

        [Fact]
        public void TuneNeverShrinksAndStopsAtTheMaximum()
        {
            OperationCache cache = new OperationCache(initialCapacity: 4, maxCapacity: 64);

            Assert.True(cache.Tune(1_000_000));
            Assert.Equal(64, cache.Capacity);

            // ノードが減っても縮めない（縮めても得は無く、エントリを捨てるだけ）。
            Assert.False(cache.Tune(8));
            Assert.Equal(64, cache.Capacity);
        }

        [Fact]
        public void TuneCanEnableACacheThatStartedEmpty()
        {
            OperationCache cache = new OperationCache(initialCapacity: 0, maxCapacity: 1024);

            Assert.False(cache.IsEnabled);
            Assert.True(cache.Tune(400));
            Assert.True(cache.IsEnabled);
            Assert.Equal(128, cache.Capacity);
        }

        [Fact]
        public void TuneLeavesADisabledCacheDisabled()
        {
            OperationCache cache = new OperationCache(initialCapacity: 1024, maxCapacity: 0);

            Assert.False(cache.Tune(1_000_000));
            Assert.Equal(0, cache.Capacity);
        }

        [Fact]
        public void TuneMigratesLiveEntriesInsteadOfDroppingThem()
        {
            // bench/ZDD.Net.Benchmarks/CacheTuningReport.cs の union-chain ワークロード（多数の
            // 頂点操作が呼び出しをまたいで部分問題を共有する）で実測: node count が増え続ける
            // インクリメンタルな構築では Tune が呼び出しのほぼ毎回グロースを起こす。グロースの
            // たびに全エントリを捨てていた旧実装ではヒット率がほぼ 0 に落ち込んでいたため、
            // 移行（rehash）に変えた（M4-1, issue #44）。
            OperationCache cache = new OperationCache(initialCapacity: 4, maxCapacity: 1024);

            cache.PutBinary(ZddOperation.Union, 2, 3, 10);
            Assert.True(cache.Tune(4000));

            // Migrate は新しい容量で再ハッシュし正しいスロットへ再配置するので、生存条件は
            // 「同じスロットに落ちること」ではなく「移行先スロットが他エントリに上書きされないこと」。
            // ここは他に書き込みが無いので確実に生き残る。
            Assert.True(cache.TryGetBinary(ZddOperation.Union, 2, 3, out int result));
            Assert.Equal(10, result);
        }

        [Fact]
        public void TuneNeverReturnsAWrongResultEvenWhenMigrationCollides()
        {
            // 移行先でスロットが衝突しても（一部のエントリが上書きされて消えても）、
            // 生き残ったエントリは常に元の値を返す——Op と Key を照合しているため、
            // 別の部分問題を誤って返すことはない。
            OperationCache cache = new OperationCache(initialCapacity: 1, maxCapacity: 256);
            Random random = new Random(20260903);
            Dictionary<(ZddOperation, int, int), int> expected = new Dictionary<(ZddOperation, int, int), int>();

            for (int i = 0; i < 500; i++)
            {
                int a = random.Next(0, 200);
                int b = random.Next(0, 200);
                int result = a * 131 + b;

                cache.PutBinary(ZddOperation.Difference, a, b, result);
                expected[(ZddOperation.Difference, a, b)] = result;

                cache.Tune(cache.Capacity * OperationCache.NodesPerEntry + 1);

                foreach (KeyValuePair<(ZddOperation Op, int A, int B), int> pair in expected)
                {
                    if (cache.TryGetBinary(pair.Key.Op, pair.Key.A, pair.Key.B, out int hit))
                    {
                        Assert.Equal(pair.Value, hit);
                    }
                }
            }
        }

        // ---- 無効なキャッシュ ----

        [Fact]
        public void ADisabledCacheAlwaysMissesAndStoresNothing()
        {
            OperationCache cache = new OperationCache(0, 0);

            cache.PutBinary(ZddOperation.Union, 2, 3, 10);
            cache.PutUnary(ZddOperation.Change, 2, 3, 10);
            cache.Clear();

            Assert.False(cache.TryGetBinary(ZddOperation.Union, 2, 3, out int result));
            Assert.Equal(0, result);
            Assert.False(cache.TryGetUnary(ZddOperation.Change, 2, 3, out _));

            Assert.Equal(2L, cache.Lookups);
            Assert.Equal(0L, cache.Hits);
            Assert.Equal(0L, cache.Collisions);
        }

        // ---- 乱数による照合 ----

        [Theory]
        [MemberData(nameof(CacheSizes))]
        public void RandomTrafficNeverReturnsAResultThatWasNotStored(int capacity)
        {
            OperationCache cache = new OperationCache(capacity, Math.Max(capacity, 1));
            Dictionary<(ZddOperation Op, int A, int B), int> expected =
                new Dictionary<(ZddOperation, int, int), int>();

            ZddOperation[] binary =
            {
                ZddOperation.Union, ZddOperation.Intersect, ZddOperation.Difference,
                ZddOperation.SymmetricDifference, ZddOperation.Meet, ZddOperation.Product,
            };
            ZddOperation[] unary = { ZddOperation.Change, ZddOperation.OnSet, ZddOperation.Maximal };

            Random random = new Random(20260830);

            for (int i = 0; i < 20_000; i++)
            {
                bool isUnary = random.Next(3) == 0;
                int a = random.Next(0, 64);
                int b = random.Next(0, 64);

                if (isUnary)
                {
                    ZddOperation op = unary[random.Next(unary.Length)];
                    if (cache.TryGetUnary(op, a, b, out int hit))
                    {
                        // ヒットしたなら、それは自分が入れた値でなければならない。
                        Assert.Equal(expected[(op, a, b)], hit);
                    }

                    int result = a * 31 + b;
                    cache.PutUnary(op, a, b, result);
                    expected[(op, a, b)] = result;
                }
                else
                {
                    ZddOperation op = binary[random.Next(binary.Length)];
                    (int x, int y) = ZddOperations.IsCommutative(op) && a > b ? (b, a) : (a, b);

                    if (cache.TryGetBinary(op, a, b, out int hit))
                    {
                        Assert.Equal(expected[(op, x, y)], hit);
                    }

                    int result = x * 131 + y;
                    cache.PutBinary(op, a, b, result);
                    expected[(op, x, y)] = result;
                }
            }

            // サイズ 0 は常にミス、既定サイズならほとんど当たる、という当たり前の性質も押さえておく。
            Assert.Equal(20_000L, cache.Lookups);
            if (capacity == 0)
            {
                Assert.Equal(0L, cache.Hits);
            }
            else
            {
                Assert.True(cache.Hits > 0);
            }
        }

        // ---- 実際の演算を通した照合 ----

        [Theory]
        [MemberData(nameof(CacheSizes))]
        public void UnionAgreesWithTheBruteForceFamilyForEveryCacheSize(int capacity)
        {
            const int Levels = 8;
            Random random = new Random(1729);

            for (int trial = 0; trial < 30; trial++)
            {
                UniqueTable table = new UniqueTable(64);
                OperationCache cache = new OperationCache(capacity, Math.Max(capacity, 1));

                HashSet<int> left = RandomFamily(random, Levels);
                HashSet<int> right = RandomFamily(random, Levels);

                int f = Build(table, left, Levels);
                int g = Build(table, right, Levels);

                HashSet<int> union = new HashSet<int>(left);
                union.UnionWith(right);

                int expected = Build(table, union, Levels);

                // 一意化表を通して作った族は正準形なので、ノード ID の一致がそのまま族の一致になる。
                Assert.Equal(expected, Union(table, cache, f, g));

                // 可換性も、キャッシュのサイズに関係なく保たれる。
                Assert.Equal(expected, Union(table, cache, g, f));
            }
        }

        [Fact]
        public void TheCacheActuallySavesWorkOnAUnion()
        {
            const int Levels = 10;
            UniqueTable table = new UniqueTable(256);
            OperationCache cache = new OperationCache(1024, 1024);

            // 「大きさ 2 の部分集合すべて」と「大きさ 3 の部分集合すべて」。どちらも節点を
            // 強く共有する族なので、和の再帰は同じ部分問題に何度も到達する。
            int f = Build(table, SubsetsOfSize(Levels, 2), Levels);
            int g = Build(table, SubsetsOfSize(Levels, 3), Levels);

            int union = Union(table, cache, f, g);

            Assert.Equal(Build(table, UnionOf(SubsetsOfSize(Levels, 2), SubsetsOfSize(Levels, 3)), Levels), union);

            // 部分問題の再訪が実際に潰せていること（潰せていなければヒットは 0 になる）。
            Assert.True(cache.Hits > 0);
            Assert.True(cache.HitRate > 0.0);
        }

        // ---- アロケーション ----

        [Fact]
        public void TheHotPathDoesNotAllocate()
        {
            OperationCache cache = new OperationCache(256, 256);

            // 先に JIT を通しておく。測るのは定常状態のアロケーション。
            Exercise(cache, 200);

            long before = GC.GetAllocatedBytesForCurrentThread();
            Exercise(cache, 20_000);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.Equal(0L, after - before);
        }

        private static void Exercise(OperationCache cache, int iterations)
        {
            for (int i = 0; i < iterations; i++)
            {
                cache.PutBinary(ZddOperation.Union, i, i + 1, i + 2);
                cache.TryGetBinary(ZddOperation.Union, i + 1, i, out _);
                cache.PutUnary(ZddOperation.Change, i, i & 7, i);
                cache.TryGetUnary(ZddOperation.Change, i, i & 7, out _);
            }
        }

        /// <summary>
        /// 変数 <c>1..levels</c> 上のランダムな族を、集合をビットマスクで表した集合として返す。
        /// ビット <c>i - 1</c> が変数レベル <c>i</c> に対応する。
        /// </summary>
        private static HashSet<int> RandomFamily(Random random, int levels)
        {
            HashSet<int> family = new HashSet<int>();
            int size = random.Next(0, 12);

            for (int i = 0; i < size; i++)
            {
                family.Add(random.Next(0, 1 << levels));
            }

            return family;
        }

        /// <summary>変数 <c>1..levels</c> から <paramref name="size"/> 個を選ぶ部分集合すべての族。</summary>
        private static HashSet<int> SubsetsOfSize(int levels, int size)
        {
            HashSet<int> family = new HashSet<int>();

            for (int mask = 0; mask < 1 << levels; mask++)
            {
                if (System.Numerics.BitOperations.PopCount((uint)mask) == size)
                {
                    family.Add(mask);
                }
            }

            return family;
        }

        private static HashSet<int> UnionOf(HashSet<int> left, HashSet<int> right)
        {
            HashSet<int> union = new HashSet<int>(left);
            union.UnionWith(right);
            return union;
        }

        /// <summary>
        /// ビットマスクの集合として与えられた族を ZDD に組み立てる。
        /// </summary>
        /// <remarks>
        /// 再帰しているが、深さは <paramref name="level"/>（テストでは 10 以下）で頭打ちなので、
        /// docs/PLAN.md §4.5 が禁じている「変数数に比例して深くなる再帰」には当たらない。
        /// </remarks>
        private static int Build(UniqueTable table, HashSet<int> family, int level)
        {
            if (level == 0)
            {
                // 残っているのは空集合だけ。族に入っていれば ⊤、いなければ ⊥。
                return family.Contains(0) ? NodeTableTop : NodeTableBottom;
            }

            int bit = 1 << (level - 1);
            HashSet<int> without = new HashSet<int>();
            HashSet<int> with = new HashSet<int>();

            foreach (int mask in family)
            {
                if ((mask & bit) == 0)
                {
                    without.Add(mask);
                }
                else
                {
                    with.Add(mask & ~bit);
                }
            }

            return table.GetNode(level, Build(table, without, level - 1), Build(table, with, level - 1));
        }

        /// <summary>
        /// 和 <c>f ∪ g</c> を、明示スタック（docs/PLAN.md §4.5）と演算キャッシュだけで計算する。
        /// M1-7 の実装はまだ無いので、キャッシュが「本物の演算」の下でも正しく働くことを
        /// 確かめるための最小の利用者としてここに置く。
        /// </summary>
        private static int Union(UniqueTable table, OperationCache cache, int f, int g)
        {
            List<Frame> work = new List<Frame> { new Frame(f, g, resolving: false) };
            List<int> values = new List<int>();

            while (work.Count > 0)
            {
                Frame frame = work[^1];
                work.RemoveAt(work.Count - 1);

                if (frame.Resolving)
                {
                    // 子の結果は「hi が後に積まれる」順で置かれている。
                    int hi = values[^1];
                    int lo = values[^2];
                    values.RemoveRange(values.Count - 2, 2);

                    int node = table.GetNode(frame.Level, lo, hi);
                    cache.PutBinary(ZddOperation.Union, frame.F, frame.G, node);
                    values.Add(node);
                    continue;
                }

                int a = frame.F;
                int b = frame.G;

                // 終端規則: ∅ ∪ x = x、x ∪ x = x。
                if (a == NodeTableBottom || a == b)
                {
                    values.Add(b);
                    continue;
                }

                if (b == NodeTableBottom)
                {
                    values.Add(a);
                    continue;
                }

                if (cache.TryGetBinary(ZddOperation.Union, a, b, out int cached))
                {
                    values.Add(cached);
                    continue;
                }

                // 上位（根側）にある方の変数で分解する。もう一方は 0-枝側にそのまま流す。
                int levelA = table.Nodes[a].Level;
                int levelB = table.Nodes[b].Level;
                int level = Math.Max(levelA, levelB);

                int aLo = levelA == level ? table.Nodes[a].Lo : a;
                int aHi = levelA == level ? table.Nodes[a].Hi : NodeTableBottom;
                int bLo = levelB == level ? table.Nodes[b].Lo : b;
                int bHi = levelB == level ? table.Nodes[b].Hi : NodeTableBottom;

                work.Add(new Frame(a, b, resolving: true) { Level = level });
                work.Add(new Frame(aHi, bHi, resolving: false));
                work.Add(new Frame(aLo, bLo, resolving: false));
            }

            Assert.Single(values);
            return values[0];
        }

        private const int NodeTableBottom = 0;
        private const int NodeTableTop = 1;

        private struct Frame
        {
            public Frame(int f, int g, bool resolving)
            {
                F = f;
                G = g;
                Resolving = resolving;
                Level = 0;
            }

            public int F { get; }

            public int G { get; }

            public bool Resolving { get; }

            public int Level { get; set; }
        }
    }
}
