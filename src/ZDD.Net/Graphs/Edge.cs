using System;

namespace ZDD.Net.Graphs
{
    /// <summary>An undirected edge between two vertex indices.</summary>
    /// <remarks>
    /// Equality and hashing are order-independent (<c>(u, v)</c> and <c>(v, u)</c> are the same edge),
    /// matching the undirected semantics of <see cref="Graph"/>. <see cref="U"/> and <see cref="V"/>
    /// themselves keep whatever order the caller supplied.
    /// </remarks>
    public readonly struct Edge : IEquatable<Edge>
    {
        /// <summary>One endpoint of the edge.</summary>
        public int U { get; }

        /// <summary>The other endpoint of the edge.</summary>
        public int V { get; }

        /// <summary>Creates an edge between <paramref name="u"/> and <paramref name="v"/>.</summary>
        public Edge(int u, int v)
        {
            U = u;
            V = v;
        }

        /// <summary>Returns the endpoint that is not <paramref name="vertex"/>.</summary>
        /// <exception cref="ArgumentException"><paramref name="vertex"/> is neither endpoint.</exception>
        public int Other(int vertex)
        {
            if (vertex == U)
            {
                return V;
            }

            if (vertex == V)
            {
                return U;
            }

            throw new ArgumentException($"Vertex {vertex} is not an endpoint of this edge.", nameof(vertex));
        }

        /// <inheritdoc/>
        public bool Equals(Edge other) =>
            (U == other.U && V == other.V) || (U == other.V && V == other.U);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Edge other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Math.Min(U, V), Math.Max(U, V));

        /// <inheritdoc/>
        public override string ToString() => $"({U}, {V})";

        /// <summary>Order-independent equality.</summary>
        public static bool operator ==(Edge left, Edge right) => left.Equals(right);

        /// <summary>Order-independent inequality.</summary>
        public static bool operator !=(Edge left, Edge right) => !left.Equals(right);
    }
}
