using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Graphs;
using ZDD.Net.Sets;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Sets
{
    /// <summary>
    /// M8-3 completion criteria for <see cref="SetSet{T}"/>'s size filters (<c>Larger</c> /
    /// <c>Smaller</c> / <c>LenEquals</c>), lazy enumeration (<c>MinIter</c> / <c>MaxIter</c> /
    /// <c>RandIter</c>) and <c>Complement</c>: matched against brute force and against
    /// <see cref="GraphSet"/>'s same-named methods on the very same edge sets, with
    /// <c>Complement</c> pinned to the universe rather than to the manager's variable count.
    /// </summary>
    public class SetSetFiltersTests
    {
        private static readonly string[] ThreeElements = ["a", "b", "c"];

        // ==================== Size filters ====================

        [Fact]
        public void SizeFiltersMatchBruteForceForEveryFamilyOfAThreeElementUniverse()
        {
            // 2^(2^3) = 256 families, and every threshold from 0 (the boundary cases the criterion
            // calls out: Larger(0) / Smaller(0) / LenEquals(0)) up past the universe's size.
            foreach (BruteForceFamily expected in FamilyCases.AllFamilies(ThreeElements.Length))
            {
                var universe = new SetUniverse<string>(ThreeElements);
                SetSet<string> family = FamilyOf(universe, expected);

                for (int n = 0; n <= ThreeElements.Length + 1; n++)
                {
                    Assert.Equal(Filtered(expected, mask => PopCount(mask) > n), Masks(universe, family.Larger(n)));
                    Assert.Equal(Filtered(expected, mask => PopCount(mask) < n), Masks(universe, family.Smaller(n)));
                    Assert.Equal(Filtered(expected, mask => PopCount(mask) == n), Masks(universe, family.LenEquals(n)));
                }
            }
        }

        [Fact]
        public void SizeFiltersAtZeroBehaveAsTheOpenBoundsSay()
        {
            var universe = new SetUniverse<string>(ThreeElements);
            SetSet<string> powerSet = SetSet<string>.PowerSet(universe);

            // Larger/Smaller are open bounds (Graphillion's larger_than / smaller_than), LenEquals is closed.
            Assert.Equal(7, powerSet.Larger(0).Count);          // every subset but the empty one
            Assert.True(powerSet.Smaller(0).IsEmpty);           // no set has fewer than zero elements
            Assert.Equal(1, powerSet.LenEquals(0).Count);       // exactly the empty set
            Assert.Equal(Array.Empty<string>(), Assert.Single(powerSet.LenEquals(0)));

            // The result stays on the same universe, so it composes with the family algebra as usual.
            Assert.Same(universe, powerSet.Larger(0).Universe);
            Assert.Equal(powerSet, powerSet.Larger(0) | powerSet.LenEquals(0));
        }

        [Fact]
        public void SizeFiltersBeyondTheUniverseYieldTheEmptyFamily()
        {
            var universe = new SetUniverse<string>(ThreeElements);
            SetSet<string> powerSet = SetSet<string>.PowerSet(universe);

            Assert.True(powerSet.Larger(3).IsEmpty);
            Assert.True(powerSet.Larger(99).IsEmpty);
            Assert.True(powerSet.LenEquals(4).IsEmpty);
            Assert.Equal(powerSet, powerSet.Smaller(4));
        }

        [Fact]
        public void SizeFiltersRejectANegativeThreshold()
        {
            SetSet<string> family = SetSet<string>.FromSets(new[] { new[] { "a" } });

            Assert.Equal("n", Assert.Throws<ArgumentOutOfRangeException>(() => family.Larger(-1)).ParamName);
            Assert.Equal("n", Assert.Throws<ArgumentOutOfRangeException>(() => family.Smaller(-1)).ParamName);
            Assert.Equal("n", Assert.Throws<ArgumentOutOfRangeException>(() => family.LenEquals(-1)).ParamName);
        }

        // ==================== Complement ====================

        [Fact]
        public void ComplementMatchesBruteForceForEveryFamilyOfAThreeElementUniverse()
        {
            foreach (BruteForceFamily expected in FamilyCases.AllFamilies(ThreeElements.Length))
            {
                var universe = new SetUniverse<string>(ThreeElements);
                SetSet<string> family = FamilyOf(universe, expected);

                Assert.Equal(expected.Complement().Masks, Masks(universe, family.Complement()));
                Assert.Equal(family, family.Complement().Complement());
            }
        }

        [Fact]
        public void ComplementOfEmptyIsThePowerSetAndViceVersa()
        {
            var universe = new SetUniverse<string>(ThreeElements);

            Assert.Equal(SetSet<string>.PowerSet(universe), SetSet<string>.Empty(universe).Complement());
            Assert.True(SetSet<string>.PowerSet(universe).Complement().IsEmpty);
        }

        [Fact]
        public void ComplementStaysWithinTheUniverseWhenTheManagerHasMoreVariables()
        {
            // The design decision this pins (issue #189): Complement is 2^Universe \ F, not
            // Zdd.Complement()'s 2^(manager variables) \ F. SetUniverse<T> sizes its own manager to its
            // element count, so the two coincide for every family built through the public API — the
            // mismatch is built here directly, through the internal constructor, so that a later change
            // to either side (a wider manager, or Complement() swapped in) fails this test.
            var universe = new SetUniverse<string>(ThreeElements);          // 3 elements: a, b, c
            using var wider = new ZddManager(5);                            // 5 variables: a, b, c + two nameless

            Zdd a = wider.Singleton(0);
            Zdd ab = wider.Singleton(0) * wider.Singleton(1);
            var family = new SetSet<string>(universe, a | ab);              // { {a}, {a, b} }

            SetSet<string> complement = family.Complement();

            // 2^{a,b,c} has 8 subsets, two of them are members: 6 left.
            Assert.Equal(6, complement.Count);
            Assert.All(complement, set => Assert.Subset(new HashSet<string>(ThreeElements), new HashSet<string>(set)));
            Assert.Equal(family.Zdd.ComplementWithin(0, 1, 2), complement.Zdd);

            // Zdd.Complement() would have brought in the two variables that name no element: 2^5 - 2 = 30.
            Assert.Equal(30, family.Zdd.Complement().Count);
            Assert.NotEqual(family.Zdd.Complement(), complement.Zdd);
        }

        [Fact]
        public void ComplementCoincidesWithZddComplementForAFamilyBuiltThroughThePublicApi()
        {
            // The other half of the criterion above: no gratuitous difference for ordinary families,
            // since SetUniverse<T>'s manager has exactly one variable per element.
            var universe = new SetUniverse<string>(ThreeElements);
            SetSet<string> family = SetSet<string>.FromSets(universe, new[] { new[] { "a" }, new[] { "b", "c" } });

            Assert.Equal(universe.Count, universe.Manager.VariableCount);
            Assert.Equal(family.Zdd.Complement(), family.Complement().Zdd);
        }

        // ==================== MinIter / MaxIter ====================

        [Fact]
        public void MinIterAndMaxIterEnumerateEveryMemberSetInWeightOrder()
        {
            var universe = new SetUniverse<string>(ThreeElements);
            var weights = new Dictionary<string, int> { ["a"] = 5, ["b"] = -2, ["c"] = 3 };

            foreach (BruteForceFamily expected in FamilyCases.AllFamilies(ThreeElements.Length))
            {
                SetSet<string> family = FamilyOf(universe, expected);

                List<int> ascending = family.MinIter(weights).Select(set => Weight(set, weights)).ToList();
                List<int> descending = family.MaxIter(weights).Select(set => Weight(set, weights)).ToList();

                Assert.Equal(expected.Count, ascending.Count);   // every member set, none repeated or lost
                Assert.Equal(expected.Count, descending.Count);
                Assert.Equal(ascending.OrderBy(w => w), ascending);
                Assert.Equal(ascending.OrderByDescending(w => w), descending);

                Assert.Equal(expected.Masks, Masks(universe, family.MinIter(weights)));
                Assert.Equal(expected.Masks, Masks(universe, family.MaxIter(weights)));
            }
        }

        [Fact]
        public void MaxIterHeadMatchesTopKAndMinIterHeadMatchesMinWeight()
        {
            var universe = new SetUniverse<string>(["a", "b", "c", "d", "e"]);
            SetSet<string> family = SetSet<string>.PowerSet(universe).Larger(1).Smaller(5);

            // Superincreasing magnitudes (one of them negative), so no two member sets share a weight
            // and "the same order" is a meaningful thing to assert at all — ties are free to come out in
            // either order, and the previous test already covers a weight function that produces them.
            var weights = new Dictionary<string, int> { ["a"] = -1, ["b"] = 2, ["c"] = -4, ["d"] = 8, ["e"] = 16 };

            (IReadOnlySet<string> Set, int Weight)[] top = family.TopK(weights, 6);
            List<IReadOnlySet<string>> head = family.MaxIter(weights).Take(6).ToList();

            Assert.Equal(6, head.Count);
            Assert.Equal(top.Select(t => t.Weight), head.Select(set => Weight(set, weights)));
            Assert.Equal(top.Select(t => Canon(t.Set)), head.Select(Canon));

            (IReadOnlySet<string> Set, int Weight) lightest = family.MinWeight(weights);
            IReadOnlySet<string> first = family.MinIter(weights).First();

            Assert.Equal(lightest.Weight, Weight(first, weights));
            Assert.Equal(Canon(lightest.Set), Canon(first));

            (IReadOnlySet<string> Set, int Weight) heaviest = family.MaxWeight(weights);
            Assert.Equal(heaviest.Weight, Weight(head[0], weights));
            Assert.Equal(Canon(heaviest.Set), Canon(head[0]));
        }

        [Fact]
        public void MinIterAndMaxIterAcceptLongAndDoubleWeights()
        {
            var universe = new SetUniverse<string>(ThreeElements);
            SetSet<string> family = SetSet<string>.PowerSet(universe);

            var ints = new Dictionary<string, int> { ["a"] = 3, ["b"] = 1, ["c"] = 2 };
            var longs = ints.ToDictionary(pair => pair.Key, pair => (long)pair.Value);
            var doubles = ints.ToDictionary(pair => pair.Key, pair => (double)pair.Value);

            Assert.Equal(
                family.MinIter(ints).Select(Canon),
                family.MinIter(longs).Select(Canon));

            Assert.Equal(
                family.MaxIter(ints).Select(Canon),
                family.MaxIter(doubles).Select(Canon));

            Assert.Equal(
                family.MinIter(doubles).Select(set => (double)Weight(set, ints)),
                family.MinIter(doubles).Select(set => set.Sum(element => doubles[element])));
        }

        [Fact]
        public void MinIterIsLazyOverAFamilyFarTooLargeToEnumerate()
        {
            // 2^40 member sets: taking the first few must not depend on the family's size.
            var universe = new SetUniverse<int>(Enumerable.Range(0, 40));
            SetSet<int> powerSet = SetSet<int>.PowerSet(universe);
            Dictionary<int, int> weights = Enumerable.Range(0, 40).ToDictionary(i => i, i => i + 1);

            Assert.Equal(BigInteger.Pow(2, 40), powerSet.Count);

            var stopwatch = Stopwatch.StartNew();
            List<IReadOnlySet<int>> lightest = powerSet.MinIter(weights).Take(3).ToList();
            stopwatch.Stop();

            // Ascending weight over positive weights: {}, {0} (weight 1), {1} (weight 2).
            Assert.Equal(new[] { "", "0", "1" }, lightest.Select(set => string.Join(",", set.Select(i => i.ToString()))));
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"MinIter took {stopwatch.Elapsed}, so it is not lazy.");
        }

        [Fact]
        public void MinIterAndMaxIterOfAnEmptyFamilyAreEmptyAndOfTheEmptySetAreASingleEmptySet()
        {
            var universe = new SetUniverse<string>(ThreeElements);
            var weights = new Dictionary<string, int> { ["a"] = 1, ["b"] = 1, ["c"] = 1 };

            Assert.Empty(SetSet<string>.Empty(universe).MinIter(weights));
            Assert.Empty(SetSet<string>.Empty(universe).MaxIter(weights));

            SetSet<string> justTheEmptySet = SetSet<string>.FromSets(universe, new[] { Array.Empty<string>() });

            Assert.Empty(Assert.Single(justTheEmptySet.MinIter(weights)));
            Assert.Empty(Assert.Single(justTheEmptySet.MaxIter(weights)));
        }

        [Fact]
        public void MinIterAndMaxIterRejectAWeightDictionaryMissingAUniverseElement()
        {
            var universe = new SetUniverse<string>(ThreeElements);

            // "c" is a universe element even though this family never uses it, so it needs a weight too;
            // the check runs eagerly, when the enumerable is created rather than on the first MoveNext.
            SetSet<string> family = SetSet<string>.FromSets(universe, new[] { new[] { "a" }, new[] { "a", "b" } });
            var missingC = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };

            ArgumentException ex = Assert.Throws<ArgumentException>(() => family.MinIter(missingC));
            Assert.Equal("weights", ex.ParamName);
            Assert.Contains("c", ex.Message, StringComparison.Ordinal);

            Assert.Throws<ArgumentException>(() => family.MaxIter(missingC));
            Assert.Throws<ArgumentException>(() => family.MinIter(missingC.ToDictionary(p => p.Key, p => (long)p.Value)));
            Assert.Throws<ArgumentException>(() => family.MaxIter(missingC.ToDictionary(p => p.Key, p => (double)p.Value)));

            // The same rule the existing weight APIs already follow, which is why the dictionary shape
            // is kept here instead of GraphSet's Func<Edge, TWeight>.
            Assert.Throws<ArgumentException>(() => family.MinWeight(missingC));

            // Extra entries for elements outside the universe are simply ignored.
            var extra = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 3, ["z"] = 4 };
            Assert.Equal(2, family.MinIter(extra).Count());
        }

        [Fact]
        public void MinIterAndMaxIterRejectANullWeightDictionary()
        {
            SetSet<string> family = SetSet<string>.FromSets(new[] { new[] { "a" } });

            Assert.Throws<ArgumentNullException>(() => family.MinIter((IReadOnlyDictionary<string, int>)null!));
            Assert.Throws<ArgumentNullException>(() => family.MaxIter((IReadOnlyDictionary<string, long>)null!));
            Assert.Throws<ArgumentNullException>(() => family.MinIter((IReadOnlyDictionary<string, double>)null!));
        }

        // ==================== RandIter ====================

        [Fact]
        public void RandIterKeepsProducingMemberSetsAndIsBoundedOnlyByTheCaller()
        {
            var universe = new SetUniverse<string>(ThreeElements);
            SetSet<string> family = SetSet<string>.FromSets(universe, new[] { new[] { "a" }, new[] { "a", "b" }, new[] { "c" } });

            // Far more draws than the family has members: RandIter never completes on its own.
            List<IReadOnlySet<string>> drawn = family.RandIter(new Random(1)).Take(500).ToList();

            Assert.Equal(500, drawn.Count);
            Assert.All(drawn, set => Assert.True(family.Contains(set)));
            Assert.Equal(3, drawn.Select(Canon).Distinct().Count());   // with replacement, all three show up

            // Lazy: creating the enumerable draws nothing, and a break stops the draws.
            IEnumerable<IReadOnlySet<string>> endless = family.RandIter(new Random(1));
            Assert.Equal(drawn.Take(10).Select(Canon), endless.Take(10).Select(Canon));
        }

        [Fact]
        public void RandIterIsDeterministicForAGivenSeedAndAgreesWithSample()
        {
            var universe = new SetUniverse<string>(ThreeElements);
            SetSet<string> family = SetSet<string>.PowerSet(universe);

            Assert.Equal(
                family.RandIter(new Random(42)).Take(20).Select(Canon),
                family.RandIter(new Random(42)).Take(20).Select(Canon));

            // Same draws as calling Sample in a loop with the same source — RandIter is just that, lazily.
            var random = new Random(42);
            List<string> byHand = Enumerable.Range(0, 20).Select(_ => Canon(family.Sample(random))).ToList();

            Assert.Equal(byHand, family.RandIter(new Random(42)).Take(20).Select(Canon));
        }

        [Fact]
        public void RandIterRejectsANullRandomEagerlyAndAnEmptyFamilyOnTheFirstDraw()
        {
            SetSet<string> family = SetSet<string>.FromSets(new[] { new[] { "a" } });

            Assert.Throws<ArgumentNullException>(() => family.RandIter(null!));

            SetSet<string> empty = SetSet<string>.Empty(family.Universe);
            IEnumerable<IReadOnlySet<string>> draws = empty.RandIter(new Random(1));   // fine on its own

            Assert.Throws<InvalidOperationException>(() => draws.First());
        }

        // ==================== Agreement with GraphSet's same-named methods ====================

        [Fact]
        public void SizeFiltersAgreeWithGraphSetsOnTheVerySameEdgeSets()
        {
            Graph graph = Graph.Grid(3, 3);
            List<Edge[]> edgeSets = EdgeSetsOf(GraphSet.Paths(graph, from: 0, to: 8));

            GraphSet asGraphSet = GraphSet.FromSets(graph, edgeSets);
            SetSet<Edge> asSetSet = SetSet<Edge>.FromSets(new SetUniverse<Edge>(graph.Edges), edgeSets);

            Assert.Equal(asGraphSet.Count, asSetSet.Count);

            for (int n = 0; n <= graph.EdgeCount + 1; n++)
            {
                Assert.Equal(Masks(graph, asGraphSet.Larger(n)), Masks(graph, asSetSet.Larger(n)));
                Assert.Equal(Masks(graph, asGraphSet.Smaller(n)), Masks(graph, asSetSet.Smaller(n)));
                Assert.Equal(Masks(graph, asGraphSet.LenEquals(n)), Masks(graph, asSetSet.LenEquals(n)));
            }
        }

        [Fact]
        public void MinIterAndMaxIterAgreeWithGraphSetsOnTheVerySameEdgeSets()
        {
            Graph graph = Graph.Grid(3, 3);
            List<Edge[]> edgeSets = EdgeSetsOf(GraphSet.Paths(graph, from: 0, to: 8));

            GraphSet asGraphSet = GraphSet.FromSets(graph, edgeSets);
            SetSet<Edge> asSetSet = SetSet<Edge>.FromSets(new SetUniverse<Edge>(graph.Edges), edgeSets);

            // The deliberate difference the docs call out: a Func<Edge, int> on the graph layer, a
            // dictionary keyed by element on the set layer. Same weights either way.
            Func<Edge, int> weightOf = edge => (edge.U * 3) + edge.V;
            Dictionary<Edge, int> weights = graph.Edges.ToDictionary(edge => edge, weightOf);

            Assert.Equal(
                asGraphSet.MinIter(weightOf).Select(set => Canon(graph, set)),
                asSetSet.MinIter(weights).Select(set => Canon(graph, set)));

            Assert.Equal(
                asGraphSet.MaxIter(weightOf).Select(set => Canon(graph, set)),
                asSetSet.MaxIter(weights).Select(set => Canon(graph, set)));

            Assert.Equal(
                asGraphSet.RandIter(new Random(11)).Take(25).Select(set => Canon(graph, set)),
                asSetSet.RandIter(new Random(11)).Take(25).Select(set => Canon(graph, set)));
        }

        // ==================== Helpers ====================

        private static SetSet<string> FamilyOf(SetUniverse<string> universe, BruteForceFamily family) =>
            SetSet<string>.FromSets(universe, family.Masks.Select(SetOf));

        private static string[] SetOf(int mask)
        {
            var set = new List<string>();

            for (int i = 0; i < ThreeElements.Length; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    set.Add(ThreeElements[i]);
                }
            }

            return set.ToArray();
        }

        private static List<Edge[]> EdgeSetsOf(GraphSet family) =>
            family.Select(set => set.ToArray()).ToList();

        private static int PopCount(int mask) => BitOperations.PopCount((uint)mask);

        private static SortedSet<int> Filtered(BruteForceFamily family, Func<int, bool> keeps) =>
            new SortedSet<int>(family.Masks.Where(keeps));

        private static int Weight(IReadOnlySet<string> set, IReadOnlyDictionary<string, int> weights) =>
            set.Sum(element => weights[element]);

        private static string Canon(IEnumerable<string> set) =>
            string.Join(",", set.OrderBy(s => s, StringComparer.Ordinal));

        private static string Canon(Graph graph, IEnumerable<Edge> set) =>
            string.Join(",", set.Select(edge => IndexOf(graph, edge)).OrderBy(i => i));

        private static SortedSet<int> Masks(SetUniverse<string> universe, IEnumerable<IReadOnlySet<string>> family)
        {
            var masks = new SortedSet<int>();

            foreach (IReadOnlySet<string> set in family)
            {
                int mask = 0;

                foreach (string element in set)
                {
                    mask |= 1 << universe.IndexOf(element);
                }

                Assert.True(masks.Add(mask), "A family enumerated the same set twice.");
            }

            return masks;
        }

        private static SortedSet<int> Masks(Graph graph, IEnumerable<IReadOnlySet<Edge>> family)
        {
            var masks = new SortedSet<int>();

            foreach (IReadOnlySet<Edge> set in family)
            {
                int mask = 0;

                foreach (Edge edge in set)
                {
                    mask |= 1 << IndexOf(graph, edge);
                }

                Assert.True(masks.Add(mask), "A family enumerated the same set twice.");
            }

            return masks;
        }

        private static int IndexOf(Graph graph, Edge edge)
        {
            for (int i = 0; i < graph.EdgeCount; i++)
            {
                if (graph.GetEdge(i) == edge)
                {
                    return i;
                }
            }

            throw new ArgumentException($"Edge {edge} is not part of the graph.", nameof(edge));
        }
    }
}
