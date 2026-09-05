using System;

namespace ZDD.Net.Graphs
{
    /// <summary>A directed edge (arc) from one vertex index to another.</summary>
    /// <remarks>
    /// Unlike <see cref="Edge"/>, equality and hashing are direction-sensitive:
    /// <c>new DirectedEdge(0, 1) != new DirectedEdge(1, 0)</c>. This is the only difference from
    /// <see cref="Edge"/> and the sole reason the two are separate types — a single <c>Edge</c> with an
    /// <c>IsDirected</c> flag would make its equality semantics depend on a flag, which is a bug magnet.
    /// </remarks>
    public readonly struct DirectedEdge : IEquatable<DirectedEdge>
    {
        /// <summary>The tail (source) vertex of the arc.</summary>
        public int From { get; }

        /// <summary>The head (destination) vertex of the arc.</summary>
        public int To { get; }

        /// <summary>Creates an arc from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public DirectedEdge(int from, int to)
        {
            From = from;
            To = to;
        }

        /// <summary>Returns the arc with endpoints swapped (<c>To -&gt; From</c>).</summary>
        public DirectedEdge Reversed() => new DirectedEdge(To, From);

        /// <summary>Returns the undirected edge over the same endpoint pair, discarding direction.</summary>
        public Edge AsUndirected() => new Edge(From, To);

        /// <inheritdoc/>
        public bool Equals(DirectedEdge other) => From == other.From && To == other.To;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is DirectedEdge other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(From, To);

        /// <inheritdoc/>
        public override string ToString() => $"({From} -> {To})";

        /// <summary>Direction-sensitive equality.</summary>
        public static bool operator ==(DirectedEdge left, DirectedEdge right) => left.Equals(right);

        /// <summary>Direction-sensitive inequality.</summary>
        public static bool operator !=(DirectedEdge left, DirectedEdge right) => !left.Equals(right);
    }
}
