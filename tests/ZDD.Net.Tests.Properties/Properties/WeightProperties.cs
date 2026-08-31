using System;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using ZDD.Net.Core;
using ZDD.Net.Tests.Properties.Harness;

namespace ZDD.Net.Tests.Properties.Properties
{
    /// <summary>
    /// 重み最適化（<c>MaxWeight</c> / <c>MinWeight</c> / <c>TopK</c>）と、確率・期待値・頻度
    /// （<c>Probability</c> / <c>ExpectedValue</c> / <c>ItemFrequency</c>）が満たすべき性質。
    /// </summary>
    /// <remarks>
    /// どれも「全解を並べずに求めた答」なので、性質はすべて<b>列挙して求めた答との一致</b>に帰着する。
    /// 生成される宇宙は小さい（<see cref="FamilyGen.MaxVariableCount"/>）ので、
    /// 照合側は素直に <c>Sets()</c> を舐めて書ける。
    /// </remarks>
    public class WeightProperties
    {
        /// <summary>浮動小数の照合に許す誤差。</summary>
        private const double Tolerance = 1e-9;

        private readonly ITestOutputHelper _output;

        public WeightProperties(ITestOutputHelper output) => _output = output;

        [Fact]
        public void TheOptimumIsTheBestSetInTheEnumeration() =>
            PropertyCheck.Sample(
                FamilyGen.FamilyAndWeights,
                input =>
                {
                    using ZddManager manager = new ZddManager(input.Family.VariableCount);
                    Zdd family = input.Family.Build(manager);
                    int[] weights = input.Weights;

                    if (family.IsEmpty)
                    {
                        // 集合が 1 つも無ければ最適解も無い。
                        Assert.Throws<InvalidOperationException>(() => family.MaxWeight(weights));
                        Assert.Throws<InvalidOperationException>(() => family.MinWeight(weights));
                        return;
                    }

                    int[] enumerated = family.Sets().Select(set => WeightOf(set, weights)).ToArray();

                    WeightedSet<int> best = family.MaxWeight(weights);
                    WeightedSet<int> worst = family.MinWeight(weights);

                    Assert.Equal(enumerated.Max(), best.Weight);
                    Assert.Equal(enumerated.Min(), worst.Weight);

                    // 返る集合は族に属し、報告された重みをちょうど持つ。
                    foreach (WeightedSet<int> found in new[] { best, worst })
                    {
                        Assert.True(family.Contains(found.Items));
                        Assert.Equal(found.Weight, WeightOf(found.Items, weights));
                    }
                },
                _output);

        [Fact]
        public void MinimizingIsMaximizingTheNegatedWeights() =>
            PropertyCheck.Sample(
                FamilyGen.FamilyAndWeights,
                input =>
                {
                    using ZddManager manager = new ZddManager(input.Family.VariableCount);
                    Zdd family = input.Family.Build(manager);

                    if (family.IsEmpty)
                    {
                        return;
                    }

                    int[] negated = Array.ConvertAll(input.Weights, weight => -weight);

                    Assert.Equal(family.MinWeight(input.Weights).Weight, -family.MaxWeight(negated).Weight);
                },
                _output);

        [Fact]
        public void TopKIsTheEnumerationSortedByWeight() =>
            PropertyCheck.Sample(
                FamilyGen.FamilyAndWeights,
                input =>
                {
                    using ZddManager manager = new ZddManager(input.Family.VariableCount);
                    Zdd family = input.Family.Build(manager);
                    int[] weights = input.Weights;

                    int[] sorted = family.Sets()
                        .Select(set => WeightOf(set, weights))
                        .OrderByDescending(weight => weight)
                        .ToArray();

                    for (int k = 0; k <= sorted.Length + 2; k++)
                    {
                        WeightedSet<int>[] top = family.TopK(weights, k);

                        Assert.Equal(Math.Min(k, sorted.Length), top.Length);

                        // 重みの並びは全列挙を降順に並べた先頭 k 個と一致する（同値も含めて）。
                        Assert.Equal(sorted.Take(top.Length), top.Select(entry => entry.Weight));

                        // 集合そのものは族に属し、互いに異なる。
                        foreach (WeightedSet<int> entry in top)
                        {
                            Assert.True(family.Contains(entry.Items));
                            Assert.Equal(entry.Weight, WeightOf(entry.Items, weights));
                        }

                        Assert.Equal(
                            top.Length,
                            top.Select(entry => string.Join(',', entry.Items)).Distinct().Count());
                    }
                },
                _output);

        [Fact]
        public void ProbabilityIsTheSumOverTheEnumeration() =>
            PropertyCheck.Sample(
                FamilyGen.FamilyAndWeights,
                input =>
                {
                    using ZddManager manager = new ZddManager(input.Family.VariableCount);
                    Zdd family = input.Family.Build(manager);
                    double[] probabilities = input.Probabilities;

                    double expected = family.Sets()
                        .Sum(set => ProbabilityOf(set, probabilities));

                    Assert.Equal(expected, family.Probability(probabilities), Tolerance);

                    // 族と補は宇宙を二分するので、確率を足すと 1 になる。
                    Assert.Equal(
                        1.0,
                        family.Probability(probabilities) + family.Complement().Probability(probabilities),
                        Tolerance);
                },
                _output);

        [Fact]
        public void ItemFrequencyAndExpectedValueAreTheAveragesOverTheEnumeration() =>
            PropertyCheck.Sample(
                FamilyGen.FamilyAndWeights,
                input =>
                {
                    using ZddManager manager = new ZddManager(input.Family.VariableCount);
                    Zdd family = input.Family.Build(manager);

                    if (family.IsEmpty)
                    {
                        // 一様分布そのものが定義できない。
                        Assert.Throws<InvalidOperationException>(() => family.ItemFrequency());
                        return;
                    }

                    int[][] sets = family.Sets().ToArray();
                    double[] frequency = family.ItemFrequency();

                    Assert.Equal(input.Family.VariableCount, frequency.Length);

                    for (int item = 0; item < frequency.Length; item++)
                    {
                        int here = item;

                        Assert.Equal(
                            (double)sets.Count(set => set.Contains(here)) / sets.Length,
                            frequency[item],
                            Tolerance);
                    }

                    double[] weights = Array.ConvertAll(input.Weights, weight => (double)weight);
                    double average = sets.Sum(set => set.Sum(item => weights[item])) / sets.Length;

                    Assert.Equal(average, family.ExpectedValue(weights), Tolerance);
                },
                _output);

        private static int WeightOf(int[] set, int[] weights)
        {
            int weight = 0;

            foreach (int item in set)
            {
                weight += weights[item];
            }

            return weight;
        }

        private static double ProbabilityOf(int[] set, double[] probabilities)
        {
            double probability = 1.0;

            for (int item = 0; item < probabilities.Length; item++)
            {
                probability *= set.Contains(item) ? probabilities[item] : 1.0 - probabilities[item];
            }

            return probability;
        }
    }
}
