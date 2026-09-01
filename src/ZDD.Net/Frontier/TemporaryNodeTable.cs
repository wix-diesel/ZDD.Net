using System;
using System.Diagnostics;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// What the top-down pass produces: the levels of a diagram, each an array of
    /// <see cref="TemporaryNode"/>, still unreduced. The bottom-up pass turns it into a ZDD (M2-4).
    /// </summary>
    /// <remarks>
    /// Duplicate states are already merged, so a node here is one distinct state; what is missing is
    /// the zero-suppression rule and the sharing of identical nodes across levels, which need the
    /// levels in the opposite order.
    /// </remarks>
    internal sealed class TemporaryNodeTable
    {
        private static readonly TemporaryNode[] NoNodes = Array.Empty<TemporaryNode>();

        /// <summary>Nodes by level; entry 0 is the terminals' level and always empty.</summary>
        private readonly TemporaryNode[][] _levels;

        /// <summary>Takes ownership of the per-level node arrays.</summary>
        /// <param name="rootLevel">The level the build started from; <c>levels</c> must have that many entries plus one.</param>
        /// <param name="levels">Nodes by level, indexed <c>0 .. rootLevel</c>; entry 0 must be empty.</param>
        public TemporaryNodeTable(int rootLevel, TemporaryNode[][] levels)
        {
            Debug.Assert(rootLevel >= 1, "A table with levels must have a positive root level.");
            Debug.Assert(levels.Length == rootLevel + 1, "The level array must be indexable by level.");
            Debug.Assert(levels[0].Length == 0, "Level 0 belongs to the terminals and holds no node.");
            Debug.Assert(levels[rootLevel].Length == 1, "The root level holds exactly the root state.");

            _levels = levels;
            RootLevel = rootLevel;
            Root = new TemporaryNodeId(rootLevel, 0);

            long nodeCount = 0;
            for (int level = 1; level <= rootLevel; level++)
            {
                nodeCount += levels[level].Length;
            }

            NodeCount = nodeCount;
        }

        private TemporaryNodeTable(bool isTrue)
        {
            _levels = new TemporaryNode[][] { NoNodes };
            RootLevel = 0;
            Root = isTrue ? TemporaryNodeId.Top : TemporaryNodeId.Bottom;
            NodeCount = 0;
        }

        /// <summary>The level the build started from, or 0 when the root is a terminal.</summary>
        public int RootLevel { get; }

        /// <summary>Where the build starts: node 0 of <see cref="RootLevel"/>, or a terminal.</summary>
        public TemporaryNodeId Root { get; }

        /// <summary>Nodes over every level; the reduction cannot produce more than this.</summary>
        public long NodeCount { get; }

        /// <summary>The nodes of one level, in state-table index order.</summary>
        /// <param name="level">A level in <c>0 .. RootLevel</c>; levels no branch reached are empty.</param>
        public ReadOnlySpan<TemporaryNode> this[int level] => _levels[level];

        /// <summary>A table whose root is a terminal, so nothing was expanded at all.</summary>
        /// <param name="isTrue"><see langword="true"/> for &#8868; (<c>{&#8709;}</c>), <see langword="false"/> for &#8869; (&#8709;).</param>
        public static TemporaryNodeTable Terminal(bool isTrue) => new TemporaryNodeTable(isTrue);

        /// <summary>The number of nodes at one level: the frontier width there.</summary>
        /// <param name="level">A level in <c>0 .. RootLevel</c>.</param>
        public int Width(int level) => _levels[level].Length;
    }
}
