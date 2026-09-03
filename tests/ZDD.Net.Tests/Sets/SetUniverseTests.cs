using System;
using Xunit;
using ZDD.Net.Sets;

namespace ZDD.Net.Tests.Sets
{
    /// <summary>Element &#8596; item-index mapping behavior of <see cref="SetUniverse{T}"/>, independent of <see cref="SetSet{T}"/>.</summary>
    public class SetUniverseTests
    {
        [Fact]
        public void AssignsIndicesInFirstSeenOrderAndDeduplicates()
        {
            var universe = new SetUniverse<string>(new[] { "b", "a", "b", "c" });

            Assert.Equal(3, universe.Count);
            Assert.Equal(new[] { "b", "a", "c" }, universe.Elements);
            Assert.Equal(0, universe.IndexOf("b"));
            Assert.Equal(1, universe.IndexOf("a"));
            Assert.Equal(2, universe.IndexOf("c"));
            Assert.Equal(universe.Count, universe.Manager.VariableCount);
        }

        [Fact]
        public void IndexOfAndElementAtAreInverses()
        {
            var universe = new SetUniverse<string>(new[] { "x", "y", "z" });

            for (int i = 0; i < universe.Count; i++)
            {
                Assert.Equal(i, universe.IndexOf(universe.ElementAt(i)));
            }
        }

        [Fact]
        public void IndexOfThrowsForAnUnknownElement()
        {
            var universe = new SetUniverse<string>(new[] { "x" });

            Assert.Throws<ArgumentException>(() => universe.IndexOf("y"));
            Assert.False(universe.Contains("y"));
            Assert.True(universe.Contains("x"));
        }

        [Fact]
        public void ElementAtThrowsForAnOutOfRangeIndex()
        {
            var universe = new SetUniverse<string>(new[] { "x" });

            Assert.Throws<ArgumentOutOfRangeException>(() => universe.ElementAt(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => universe.ElementAt(1));
        }

        [Fact]
        public void EmptyUniverseIsAllowed()
        {
            var universe = new SetUniverse<string>(Array.Empty<string>());

            Assert.Equal(0, universe.Count);
            Assert.Equal(0, universe.Manager.VariableCount);
        }
    }
}
