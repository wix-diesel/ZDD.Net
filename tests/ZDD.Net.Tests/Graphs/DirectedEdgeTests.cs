using System;
using Xunit;
using ZDD.Net.Graphs;

namespace ZDD.Net.Tests.Graphs
{
    /// <summary>
    /// M7-1 completion criteria for <see cref="DirectedEdge"/>: direction-sensitive equality/hashing,
    /// and the <see cref="DirectedEdge.Reversed"/> / <see cref="DirectedEdge.AsUndirected"/> conversions.
    /// </summary>
    public class DirectedEdgeTests
    {
        [Fact]
        public void OppositeDirectionArcsAreNotEqual()
        {
            var forward = new DirectedEdge(0, 1);
            var backward = new DirectedEdge(1, 0);

            Assert.NotEqual(forward, backward);
            Assert.True(forward != backward);
            Assert.False(forward == backward);
        }

        [Fact]
        public void HashCodeMatchesTheDocumentedOrderedFormula()
        {
            // GetHashCode only promises equal objects hash equally, not that unequal objects hash
            // differently — so this checks the documented Combine(From, To) formula directly rather
            // than asserting an inequality that isn't part of the contract.
            var edge = new DirectedEdge(3, 9);

            Assert.Equal(HashCode.Combine(3, 9), edge.GetHashCode());
        }

        [Fact]
        public void SameDirectionArcsAreEqual()
        {
            var a = new DirectedEdge(2, 5);
            var b = new DirectedEdge(2, 5);

            Assert.Equal(a, b);
            Assert.True(a == b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void ReversedSwapsEndpoints()
        {
            var edge = new DirectedEdge(3, 7);

            DirectedEdge reversed = edge.Reversed();

            Assert.Equal(7, reversed.From);
            Assert.Equal(3, reversed.To);
        }

        [Fact]
        public void AsUndirectedKeepsTheEndpointPair()
        {
            var edge = new DirectedEdge(4, 9);

            Edge undirected = edge.AsUndirected();

            Assert.Equal(new Edge(4, 9), undirected);
        }
    }
}
