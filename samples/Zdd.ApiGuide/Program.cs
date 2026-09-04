using System;
using System.Numerics;
using ZDD.Net.Core;

namespace ZDD.Net.Samples.ApiGuide
{
    /// <summary>
    /// docs/api-guide.md に載せているコード片をそのまま集めたサンプル。
    /// ここに書いたコードは <c>dotnet run --project samples/Zdd.ApiGuide</c> で実際に動く
    /// （CI もこれを実行して確かめる。.github/workflows/ci.yml を参照）。
    /// ガイドの本文を直すときは、対応するメソッドも一緒に直すこと。
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            BasicFamilyAlgebra();
            SetOperators();
            EnumerationAndCounting();
            RankingAndSampling();
            WeightOptimization();
            CustomEvaluator();

            Console.Out.WriteLine("all api-guide samples passed");
            return 0;
        }

        /// <summary>
        /// 「ZDD とは何か」節: 3 要素 {0, 1, 2} の冪集合から、和 {0} を含む集合だけを取り出す。
        /// </summary>
        private static void BasicFamilyAlgebra()
        {
            using ZddManager manager = new ZddManager(variableCount: 3);

            // 2^{0,1,2} = {∅, {0}, {1}, {2}, {0,1}, {0,2}, {1,2}, {0,1,2}}
            Zdd powerSet = manager.Empty.Complement();
            Assert(powerSet.Count == 8, "powerset of 3 items has 8 subsets");

            // item 0 を含む集合だけを残す。
            Zdd containingItem0 = powerSet.OnSet(0);
            Assert(containingItem0.Count == 4, "half of the powerset contains item 0");

            foreach (int[] set in containingItem0.Sets())
            {
                Assert(Array.IndexOf(set, 0) < 0, "OnSet already removed item 0 from the sets");
            }
        }

        /// <summary>
        /// 家族代数の演算子（<c>|</c> <c>&amp;</c> <c>-</c> <c>^</c> <c>*</c> <c>/</c> <c>%</c> <c>~</c>）を使う例。
        /// </summary>
        private static void SetOperators()
        {
            using ZddManager manager = new ZddManager(variableCount: 4);

            Zdd a = manager.Singleton(0) | manager.Singleton(1); // {{0}, {1}}
            Zdd b = manager.Singleton(1) | manager.Singleton(2); // {{1}, {2}}

            Zdd union = a | b; // {{0}, {1}, {2}}
            Assert(union.Count == 3, "union has 3 sets");

            Zdd intersect = a & b; // {{1}}
            Assert(intersect.Count == 1 && intersect.Contains(1), "intersection keeps only {1}");

            Zdd difference = a - b; // {{0}}
            Assert(difference.Count == 1 && difference.Contains(0), "difference keeps only {0}");

            Zdd symmetricDifference = a ^ b; // {{0}, {2}}
            Assert(symmetricDifference.Count == 2, "symmetric difference has 2 sets");

            // 積: それぞれから 1 つずつ選んだ和の集まり。
            Zdd product = a * b; // {{0,1}, {0,2}, {1}, {1,2}}
            Assert(product.Count == 4, "product joins every pair");

            // 商・剰余: F == F / G * G + F % G が常に成り立つ。
            Zdd quotient = product / b;
            Zdd remainder = product % b;
            Zdd reconstructed = quotient * b | remainder;
            Assert(reconstructed == product, "F == F / G * G + F % G");

            Zdd complement = manager.Empty.Complement(); // 2^U
            Assert((~complement).IsEmpty, "complement of the full powerset is empty");
        }

        /// <summary>
        /// <see cref="Zdd.Count"/> / <see cref="Zdd.CountApprox"/> / <see cref="Zdd.Sets"/> /
        /// <see cref="Zdd.EnumerateInto"/> の実例。
        /// </summary>
        private static void EnumerationAndCounting()
        {
            using ZddManager manager = new ZddManager(variableCount: 20);

            // 20 要素の冪集合。厳密な濃度は 2^20 = 1,048,576。
            Zdd powerSet = manager.Empty.Complement();

            BigInteger exact = powerSet.Count;
            Assert(exact == BigInteger.Pow(2, 20), "Count is exact");

            // CountApprox は double 版。濃度が 2^53 以下なら Count と厳密に一致する。
            double approx = powerSet.CountApprox;
            Assert(approx == (double)exact, "CountApprox matches Count for small families");

            // Sets() は遅延列挙。族が大きくても LINQ の Take で先頭だけ舐められる。
            int firstFive = 0;
            foreach (int[] set in powerSet.Sets())
            {
                firstFive++;
                if (firstFive == 5)
                {
                    break;
                }
            }

            Assert(firstFive == 5, "Sets() can be short-circuited without walking the whole family");

            // EnumerateInto: Sets() と違い、集合 1 つごとに new int[] しない。
            // MaxSetSize がバッファに必要な長さ（この族に含まれる集合の最大要素数）。
            int[] buffer = new int[powerSet.MaxSetSize];
            int firstFiveViaSpan = 0;
            SetSpanEnumerator enumerator = powerSet.EnumerateInto(buffer);
            while (enumerator.MoveNext())
            {
                // enumerator.Current はバッファそのものへのビュー。次の MoveNext で上書きされる。
                firstFiveViaSpan++;
                if (firstFiveViaSpan == 5)
                {
                    break;
                }
            }

            Assert(firstFiveViaSpan == 5, "EnumerateInto can also be short-circuited without walking the whole family");
        }

        /// <summary>
        /// <see cref="Zdd.ElementAt"/> と <see cref="Zdd.Sample(Random)"/> の実例。
        /// 「10^24 個の解から一様に 1 つ」を全列挙せずに行う、ZDD の目玉機能。
        /// </summary>
        private static void RankingAndSampling()
        {
            using ZddManager manager = new ZddManager(variableCount: 40);

            // 40 要素の冪集合。濃度は 2^40 ≈ 10^12 で、列挙して数えるのは非現実的。
            Zdd powerSet = manager.Empty.Complement();

            // unranking: 濃度なみに大きい族からでも、根から 1 本の経路を降りるだけで k 番目が引ける。
            int[] first = powerSet.ElementAt(BigInteger.Zero);
            Assert(first.Length == 0, "the 0th set in Default order is the empty set");

            BigInteger last = powerSet.Count - BigInteger.One;
            int[] fullSet = powerSet.ElementAt(last);
            Assert(fullSet.Length == 40, "the last set in Default order is the full set");

            // ranking は unranking の逆。
            BigInteger rank = powerSet.IndexOf(fullSet);
            Assert(rank == last, "IndexOf inverts ElementAt");

            // 一様ランダムサンプリング。種を固定すれば再現できる。
            Random random = new Random(Seed: 42);
            int[] sample = powerSet.Sample(random);
            Assert(powerSet.Contains(sample), "a sampled set always belongs to the family");
        }

        /// <summary>
        /// <see cref="Zdd.MaxWeight(ReadOnlySpan{int})"/> の実例。全解を並べず、
        /// ノード数に比例する 1 回のボトムアップ DP で最大重みの集合を求める。
        /// </summary>
        private static void WeightOptimization()
        {
            using ZddManager manager = new ZddManager(variableCount: 4);

            // {0,1,2,3} の冪集合から、要素数がちょうど 2 の集合だけを残す
            // （「重さの合計」を分かりやすくするための下ごしらえ）。
            Zdd powerSet = manager.Empty.Complement();
            Zdd pairs = powerSet.Intersect(BuildExactlyTwo(manager));

            int[] weights = { 3, 1, 4, 1 }; // item 0..3 の重み
            WeightedSet<int> best = pairs.MaxWeight(weights);

            // {0, 2} の重みが 3 + 4 = 7 で最大。
            Assert(best.Weight == 7, "max-weight pair is {0, 2} with weight 7");
            Assert(best.Items.Length == 2 && best.Items[0] == 0 && best.Items[1] == 2, "MaxWeight returns the set itself");
        }

        /// <summary>要素数がちょうど 2 の族を、CountBySize と同じ形の DP で組み立てる。</summary>
        private static Zdd BuildExactlyTwo(ZddManager manager)
        {
            Zdd result = manager.Empty;
            int n = manager.VariableCount;

            for (int first = 0; first < n; first++)
            {
                for (int second = first + 1; second < n; second++)
                {
                    result |= manager.Singleton(first).Product(manager.Singleton(second));
                }
            }

            return result;
        }

        /// <summary>
        /// <see cref="IDdEval{TValue}"/> を自作する例。族に属する集合の個数を数える
        /// <see cref="CardinalityEval"/> 相当を、利用者コードとして再実装している。
        /// </summary>
        private static void CustomEvaluator()
        {
            using ZddManager manager = new ZddManager(variableCount: 5);

            Zdd powerSet = manager.Empty.Complement();
            BigInteger count = powerSet.Evaluate<CountingEval, BigInteger>(default);

            Assert(count == powerSet.Count, "a hand-written IDdEval matches the built-in Count");
        }

        /// <summary>
        /// <see cref="IDdEval{TValue}"/> の実装は必ず <see langword="struct"/> にする
        /// （interface 型で受け取ると仮想呼び出しになり、ノードごとに何度も走る分だけ遅くなる）。
        /// </summary>
        private readonly struct CountingEval : IDdEval<BigInteger>
        {
            public BigInteger EvalTerminal(bool isTrue) => isTrue ? BigInteger.One : BigInteger.Zero;

            public BigInteger EvalNode(int item, BigInteger lo, BigInteger hi) => lo + hi;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"api-guide sample failed: {message}");
            }
        }
    }
}
