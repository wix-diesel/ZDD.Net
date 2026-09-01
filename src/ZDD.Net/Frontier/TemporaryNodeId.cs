using System;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// A branch destination in the temporary node table: either a terminal, or the node a level
    /// holds at an index. Level 0 is reserved for the terminals, so <c>default</c> is &#8869;.
    /// </summary>
    /// <remarks>
    /// A node's index within its level is the index its state got from that level's state table,
    /// which is what makes two branches reaching the same state name the same node.
    /// </remarks>
    internal readonly struct TemporaryNodeId : IEquatable<TemporaryNodeId>
    {
        /// <summary>The level the two terminals live at; no expanded node is ever there.</summary>
        private const int TerminalLevel = 0;

        private const int BottomIndex = 0;
        private const int TopIndex = 1;

        private readonly int _level;
        private readonly int _index;

        /// <summary>Names the node at <paramref name="index"/> of <paramref name="level"/>.</summary>
        /// <param name="level">The node's level, 1 or above.</param>
        /// <param name="index">The node's index within that level.</param>
        public TemporaryNodeId(int level, int index)
        {
            _level = level;
            _index = index;
        }

        /// <summary>The &#8869; terminal (&#8709;): the branch leads to no set at all.</summary>
        public static TemporaryNodeId Bottom => new TemporaryNodeId(TerminalLevel, BottomIndex);

        /// <summary>The &#8868; terminal (<c>{&#8709;}</c>): the choices made along the branch form a set.</summary>
        public static TemporaryNodeId Top => new TemporaryNodeId(TerminalLevel, TopIndex);

        /// <summary>The level this names, or 0 for a terminal.</summary>
        public int Level => _level;

        /// <summary>The index within <see cref="Level"/>, or which terminal this is.</summary>
        public int Index => _index;

        /// <summary>Whether this names a terminal rather than an expanded node.</summary>
        public bool IsTerminal => _level == TerminalLevel;

        /// <summary>Whether this is the &#8869; terminal.</summary>
        public bool IsBottom => _level == TerminalLevel && _index == BottomIndex;

        /// <summary>Whether this is the &#8868; terminal.</summary>
        public bool IsTop => _level == TerminalLevel && _index == TopIndex;

        /// <summary>Tests whether two ids name the same node or the same terminal.</summary>
        public static bool operator ==(TemporaryNodeId left, TemporaryNodeId right) => left.Equals(right);

        /// <summary>Tests whether two ids name different nodes.</summary>
        public static bool operator !=(TemporaryNodeId left, TemporaryNodeId right) => !left.Equals(right);

        /// <inheritdoc/>
        public bool Equals(TemporaryNodeId other) => _level == other._level && _index == other._index;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TemporaryNodeId other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(_level, _index);

        /// <inheritdoc/>
        public override string ToString()
        {
            if (IsTerminal)
            {
                return IsTop ? "Top" : "Bottom";
            }

            return $"({_level}, {_index})";
        }
    }
}
