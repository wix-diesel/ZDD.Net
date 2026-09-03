using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Io;
using ZDD.Net.Sets;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Sets
{
    /// <summary>
    /// M3-8 completion criteria for <see cref="SetSet{T}"/>: a string-element family working end to
    /// end, non-<c>int</c> element types, the <c>Count</c> property not colliding with LINQ's
    /// <c>Count()</c>, cross-universe operations throwing, deterministic enumeration order, and each
    /// wrapper operation matching its underlying <see cref="Zdd"/> operation.
    /// </summary>
    public class SetSetTests
    {
        private enum Color
        {
            Red,
            Green,
            Blue,
        }

        private sealed record Point(int X, int Y);

        [Fact]
        public void FromSetsBuildsTheGivenFamilyOverAStringUniverse()
        {
            SetSet<string> family = SetSet<string>.FromSets(new[]
            {
                new[] { "a" },
                new[] { "a", "b" },
                new[] { "b", "c" },
            });

            Assert.Equal(new[] { "a", "b", "c" }, family.Universe.Elements);
            Assert.Equal(3, family.Count);
            Assert.True(family.Contains(new[] { "a" }));
            Assert.True(family.Contains(new[] { "a", "b" }));
            Assert.True(family.Contains(new[] { "b", "c" }));
            Assert.False(family.Contains(new[] { "c" }));
            Assert.False(family.Contains(Array.Empty<string>()));
        }

        [Fact]
        public void SetOperationsMatchOrdinarySetAlgebra()
        {
            var universe = new SetUniverse<string>(new[] { "a", "b", "c" });

            SetSet<string> f = SetSet<string>.FromSets(universe, new[] { new[] { "a" }, new[] { "a", "b" } });
            SetSet<string> g = SetSet<string>.FromSets(universe, new[] { new[] { "a" }, new[] { "b", "c" } });

            AssertSameFamily(f | g, new[] { "a" }, new[] { "a", "b" }, new[] { "b", "c" });
            AssertSameFamily(f & g, new[] { "a" });
            AssertSameFamily(f - g, new[] { "a", "b" });
            AssertSameFamily(f ^ g, new[] { "a", "b" }, new[] { "b", "c" });

            Assert.Equal(f.Union(g), f | g);
            Assert.Equal(f.Intersect(g), f & g);
            Assert.Equal(f.Difference(g), f - g);
            Assert.Equal(f.SymmetricDifference(g), f ^ g);
        }

        [Fact]
        public void EnumerationOrderIsTheDeterministicDefaultOrder()
        {
            SetSet<string> family = SetSet<string>.FromSets(new[]
            {
                new[] { "a" },
                new[] { "a", "b" },
                new[] { "b", "c" },
            });

            // Universe order is first-seen: a=0, b=1, c=2. Default order is depth-first, 0-branch
            // (item excluded) first, so sets without "a" come before sets with "a".
            var expected = new[]
            {
                new HashSet<string> { "b", "c" },
                new HashSet<string> { "a" },
                new HashSet<string> { "a", "b" },
            };

            List<IReadOnlySet<string>> actual = family.ToList();

            Assert.Equal(expected.Length, actual.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.True(expected[i].SetEquals(actual[i]), $"Set at position {i} did not match.");
            }
        }

        [Fact]
        public void CountPropertyAndLinqCountExtensionBothWork()
        {
            SetSet<string> family = SetSet<string>.PowerSet(new[] { "a", "b", "c" });

            BigInteger exact = family.Count;

            // Both resolve without a cast or ambiguity error: the property (no parens) and the
            // LINQ extension method (parens, dot-call syntax) have different call shapes.
            int viaDotCall = family.Count();
            int viaLinqExtension = Enumerable.Count(family);

            Assert.Equal(new BigInteger(8), exact);
            Assert.Equal(8, viaDotCall);
            Assert.Equal(8, viaLinqExtension);
            Assert.Equal(8L, family.LongCount());
            Assert.Equal(8.0, family.CountApprox);
        }

        [Fact]
        public void PowerSetHasTwoToTheNMembers()
        {
            SetSet<string> powerSet = SetSet<string>.PowerSet(new[] { "a", "b", "c", "d" });

            Assert.Equal(BigInteger.Pow(2, 4), powerSet.Count);
            Assert.True(powerSet.Contains(Array.Empty<string>()));
            Assert.True(powerSet.Contains(new[] { "a", "b", "c", "d" }));
        }

        [Fact]
        public void OperationsBetweenDifferentUniversesThrow()
        {
            var universe1 = new SetUniverse<string>(new[] { "a", "b" });
            var universe2 = new SetUniverse<string>(new[] { "a", "b" });

            SetSet<string> f = SetSet<string>.FromSets(universe1, new[] { new[] { "a" } });
            SetSet<string> g = SetSet<string>.FromSets(universe2, new[] { new[] { "a" } });

            var ex = Assert.Throws<ArgumentException>(() => f.Union(g));
            Assert.Equal("other", ex.ParamName);
            Assert.Throws<ArgumentException>(() => f.Product(g));
            Assert.Throws<ArgumentException>(() => f.SupersetsOf(g));
        }

        [Fact]
        public void ContainsThrowsForAnElementOutsideTheUniverse()
        {
            SetSet<string> family = SetSet<string>.PowerSet(new[] { "a", "b" });

            Assert.Throws<ArgumentException>(() => family.Contains(new[] { "z" }));
        }

        [Fact]
        public void ElementOrderWithinAMemberSetIsDeterministicAscendingByUniverseIndex()
        {
            // Index order is first-seen: z=0, m=1, a=2 — deliberately not insertion order into the
            // member set below, and not alphabetical, so a HashSet's unspecified bucket order would
            // very likely disagree with it.
            var universe = new SetUniverse<string>(new[] { "z", "m", "a" });
            SetSet<string> family = SetSet<string>.FromSets(universe, new[] { new[] { "a", "z", "m" } });

            IReadOnlySet<string> only = Assert.Single(family);
            Assert.Equal(new[] { "z", "m", "a" }, only);
        }

        [Fact]
        public void ElementAtAndIndexOfRoundTrip()
        {
            SetSet<string> family = SetSet<string>.PowerSet(new[] { "a", "b", "c" });

            for (BigInteger rank = 0; rank < family.Count; rank++)
            {
                IReadOnlySet<string> set = family.ElementAt(rank);
                Assert.Equal(rank, family.IndexOf(set));
            }
        }

        [Fact]
        public void SampleReturnsAMemberOfTheFamily()
        {
            SetSet<string> family = SetSet<string>.PowerSet(new[] { "a", "b", "c" });
            var random = new Random(42);

            IReadOnlySet<string> single = family.Sample(random);
            Assert.True(family.Contains(single));

            IReadOnlySet<string>[] many = family.Sample(10, random);
            Assert.Equal(10, many.Length);
            Assert.All(many, set => Assert.True(family.Contains(set)));
        }

        [Fact]
        public void MaxWeightMinWeightAndTopKPickTheExpectedSets()
        {
            SetSet<string> family = SetSet<string>.PowerSet(new[] { "a", "b", "c" });
            var weights = new Dictionary<string, int> { ["a"] = 1, ["b"] = 5, ["c"] = 3 };

            (IReadOnlySet<string> Set, int Weight) max = family.MaxWeight(weights);
            Assert.Equal(9, max.Weight);
            Assert.True(max.Set.SetEquals(new[] { "a", "b", "c" }));

            (IReadOnlySet<string> Set, int Weight) min = family.MinWeight(weights);
            Assert.Equal(0, min.Weight);
            Assert.Empty(min.Set);

            (IReadOnlySet<string> Set, int Weight)[] top2 = family.TopK(weights, 2);
            Assert.Equal(2, top2.Length);
            Assert.Equal(9, top2[0].Weight);
            Assert.Equal(8, top2[1].Weight);
            Assert.True(top2[1].Set.SetEquals(new[] { "b", "c" }));
        }

        [Fact]
        public void ProbabilityMatchesIndependentInclusionModel()
        {
            SetSet<string> family = SetSet<string>.FromSets(new[] { new[] { "a" } });
            var probabilities = new Dictionary<string, double> { ["a"] = 0.5 };

            Assert.Equal(0.5, family.Probability(probabilities), precision: 10);
        }

        [Fact]
        public void WorksWithEnumElements()
        {
            SetSet<Color> family = SetSet<Color>.FromSets(new[]
            {
                new[] { Color.Red },
                new[] { Color.Red, Color.Blue },
            });

            Assert.Equal(2, family.Count);
            Assert.True(family.Contains(new[] { Color.Red, Color.Blue }));
            Assert.False(family.Contains(new[] { Color.Blue }));
            Assert.Throws<ArgumentException>(() => family.Contains(new[] { Color.Green }));
        }

        [Fact]
        public void WorksWithRecordElementsUsingStructuralEquality()
        {
            var p1 = new Point(0, 0);
            var p2 = new Point(1, 1);

            SetSet<Point> family = SetSet<Point>.FromSets(new[] { new[] { p1, p2 } });

            // A distinct instance with the same field values is the same record element.
            Assert.True(family.Contains(new[] { new Point(0, 0), new Point(1, 1) }));
        }

        [Fact]
        public void CustomComparerDeduplicatesUniverseElements()
        {
            var universe = new SetUniverse<string>(new[] { "A", "a", "b" }, StringComparer.OrdinalIgnoreCase);

            Assert.Equal(2, universe.Count);
            Assert.Equal(new[] { "A", "b" }, universe.Elements);

            SetSet<string> family = SetSet<string>.FromSets(universe, new[] { new[] { "A" } });
            Assert.True(family.Contains(new[] { "a" }));
        }

        [Fact]
        public void OperationsMatchTheUnderlyingZddOperations()
        {
            var universe = new SetUniverse<string>(new[] { "a", "b", "c" });
            ZddManager manager = universe.Manager;

            Zdd RawSet(params string[] items)
            {
                Zdd set = manager.Base;
                foreach (string item in items)
                {
                    set *= manager.Singleton(universe.IndexOf(item));
                }

                return set;
            }

            SetSet<string> f = SetSet<string>.FromSets(universe, new[] { new[] { "a" }, new[] { "a", "b" } });
            SetSet<string> g = SetSet<string>.FromSets(universe, new[] { new[] { "a" }, new[] { "b", "c" } });

            Zdd rawF = RawSet("a") | RawSet("a", "b");
            Zdd rawG = RawSet("a") | RawSet("b", "c");

            Assert.Equal(rawF, f.Zdd);
            Assert.Equal(rawG, g.Zdd);

            Assert.Equal(rawF.Union(rawG), f.Union(g).Zdd);
            Assert.Equal(rawF.Intersect(rawG), f.Intersect(g).Zdd);
            Assert.Equal(rawF.Difference(rawG), f.Difference(g).Zdd);
            Assert.Equal(rawF.SymmetricDifference(rawG), f.SymmetricDifference(g).Zdd);
            Assert.Equal(rawF.Product(rawG), f.Product(g).Zdd);
            Assert.Equal(rawF.Quotient(rawG), f.Quotient(g).Zdd);
            Assert.Equal(rawF.Meet(rawG), f.Meet(g).Zdd);
            Assert.Equal(rawF.SupersetsOf(rawG), f.SupersetsOf(g).Zdd);
            Assert.Equal(rawF.SubsetsOf(rawG), f.SubsetsOf(g).Zdd);
            Assert.Equal(rawF.Maximal(), f.Maximal().Zdd);
            Assert.Equal(rawF.Minimal(), f.Minimal().Zdd);
        }

        [Fact]
        public void EmptyAndPowerSetFactoriesMatchTheirZddCounterparts()
        {
            var universe = new SetUniverse<string>(new[] { "a", "b" });

            SetSet<string> empty = SetSet<string>.Empty(universe);
            Assert.True(empty.IsEmpty);
            Assert.Equal(universe.Manager.Empty, empty.Zdd);

            SetSet<string> powerSet = SetSet<string>.PowerSet(universe);
            Assert.Equal(universe.Manager.Empty.Complement(), powerSet.Zdd);
        }

        // ---- ToDot（M5-4、issue #56）----

        [Fact]
        public void ToDotLabelsEachLevelByItsElementByDefault()
        {
            var universe = new SetUniverse<string>(new[] { "a", "b", "c" });
            SetSet<string> powerSet = SetSet<string>.PowerSet(universe);

            string dot = powerSet.ToDot();

            Assert.DoesNotContain("label=\"x", dot, StringComparison.Ordinal);
            Assert.Contains("label=\"a\"", dot, StringComparison.Ordinal);
            Assert.Contains("label=\"b\"", dot, StringComparison.Ordinal);
            Assert.Contains("label=\"c\"", dot, StringComparison.Ordinal);

            DotSyntax.Validate(dot);
        }

        [Fact]
        public void SetSetWriteDotProducesTheSameTextAsToDot()
        {
            var universe = new SetUniverse<string>(new[] { "a", "b" });
            SetSet<string> powerSet = SetSet<string>.PowerSet(universe);

            using StringWriter writer = new StringWriter();
            powerSet.WriteDot(writer);

            Assert.Equal(powerSet.ToDot(), writer.ToString());
        }

        private static void AssertSameFamily(SetSet<string> family, params string[][] expectedSets)
        {
            List<string> expected = expectedSets.Select(Canon).OrderBy(s => s, StringComparer.Ordinal).ToList();
            List<string> actual = family.Select(set => Canon(set.ToArray())).OrderBy(s => s, StringComparer.Ordinal).ToList();

            Assert.Equal(expected, actual);
        }

        private static string Canon(IEnumerable<string> set) =>
            string.Join(",", set.OrderBy(s => s, StringComparer.Ordinal));
    }
}
