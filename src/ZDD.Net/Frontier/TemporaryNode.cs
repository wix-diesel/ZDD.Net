using System;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// One node of the temporary node table: where its two branches go. No zero-suppression rule
    /// has been applied yet, so <c>Hi == Bottom</c> nodes are still here; the reduction pass drops them.
    /// </summary>
    internal readonly struct TemporaryNode : IEquatable<TemporaryNode>
    {
        /// <summary>Creates a node with the two given branches.</summary>
        /// <param name="lo">Where the 0-branch (item excluded) goes.</param>
        /// <param name="hi">Where the 1-branch (item included) goes.</param>
        public TemporaryNode(TemporaryNodeId lo, TemporaryNodeId hi)
        {
            Lo = lo;
            Hi = hi;
        }

        /// <summary>Where the 0-branch goes: the item of this level is excluded.</summary>
        public TemporaryNodeId Lo { get; }

        /// <summary>Where the 1-branch goes: the item of this level is included.</summary>
        public TemporaryNodeId Hi { get; }

        /// <summary>Tests whether two nodes have the same two branches.</summary>
        public static bool operator ==(TemporaryNode left, TemporaryNode right) => left.Equals(right);

        /// <summary>Tests whether two nodes differ in a branch.</summary>
        public static bool operator !=(TemporaryNode left, TemporaryNode right) => !left.Equals(right);

        /// <inheritdoc/>
        public bool Equals(TemporaryNode other) => Lo == other.Lo && Hi == other.Hi;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TemporaryNode other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Lo, Hi);

        /// <inheritdoc/>
        public override string ToString() => $"lo: {Lo}, hi: {Hi}";
    }
}
