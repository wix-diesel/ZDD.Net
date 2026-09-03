using System;
using System.Collections.Generic;
using ZDD.Net.Internal;

namespace ZDD.Net.Io
{
    /// <summary>
    /// Rendering knobs for <see cref="Core.Zdd.ToDot(DotOptions)"/> / <see cref="Core.Zdd.WriteDot(System.IO.TextWriter, DotOptions)"/>:
    /// spec-state labels, level labels, partial display, and style (M5-4, issue #56).
    /// </summary>
    /// <remarks>
    /// Every property defaults to the plain, unlabeled, untruncated rendering <see cref="Core.Zdd.ToDot(DotOptions)"/>
    /// already produced, so passing a freshly constructed instance changes nothing.
    /// </remarks>
    public sealed class DotOptions
    {
        private int _maxLevels = int.MaxValue;
        private int _maxNodes = int.MaxValue;

        /// <summary>
        /// One label per node, keyed by the internal node id shown in the output as <c>n&lt;id&gt;</c>
        /// (e.g. the <c>stateLabels</c> output of <see cref="Frontier.FrontierBuilder"/>'s
        /// state-recording <c>Build</c> overload). A node with no entry is drawn without a state label.
        /// </summary>
        /// <value>Defaults to <see langword="null"/>, which draws no state labels.</value>
        public IReadOnlyDictionary<int, string>? StateLabels { get; set; }

        /// <summary>
        /// Names an item (0 .. <see cref="Core.ZddManager.VariableCount"/> - 1) for display instead of
        /// the plain <c>x&lt;item&gt;</c> — e.g. <c>GraphSet.Universe.ElementAt</c> for an edge's
        /// <c>(u, v)</c>, or any <see cref="Sets.SetUniverse{T}.ElementAt"/>.
        /// </summary>
        /// <value>Defaults to <see langword="null"/>, which shows <c>x&lt;item&gt;</c>.</value>
        public Func<int, string>? LevelLabel { get; set; }

        /// <summary>
        /// Draws only the top <see cref="MaxLevels"/> levels counted from the displayed root (the
        /// root's own level counts as 1); anything deeper is replaced by a single truncation marker.
        /// </summary>
        /// <value>Positive. Defaults to unlimited.</value>
        /// <exception cref="ArgumentOutOfRangeException">Value is not positive.</exception>
        public int MaxLevels
        {
            get => _maxLevels;
            set
            {
                ThrowHelper.ThrowIfNegativeOrZero(value, nameof(MaxLevels));
                _maxLevels = value;
            }
        }

        /// <summary>
        /// Stops admitting new non-terminal nodes once this many have been drawn; anything beyond the
        /// cutoff is replaced by a single truncation marker instead of being visited.
        /// </summary>
        /// <value>Positive. Defaults to unlimited.</value>
        /// <exception cref="ArgumentOutOfRangeException">Value is not positive.</exception>
        public int MaxNodes
        {
            get => _maxNodes;
            set
            {
                ThrowHelper.ThrowIfNegativeOrZero(value, nameof(MaxNodes));
                _maxNodes = value;
            }
        }

        /// <summary>
        /// Draws only the part reachable from this internal node id instead of from the family's own
        /// root — e.g. a <c>n&lt;id&gt;</c> name read off an earlier, truncated rendering.
        /// </summary>
        /// <value>Defaults to <see langword="null"/>, which starts from the family's own root.</value>
        public int? FocusNodeId { get; set; }

        /// <summary>The <c>shape</c> attribute of non-terminal nodes.</summary>
        /// <value>Defaults to <c>"circle"</c>, <see cref="Core.Zdd.ToDot(DotOptions)"/>'s convention.</value>
        public string NonTerminalShape { get; set; } = "circle";

        /// <summary>The fill color of non-terminal nodes, or <see langword="null"/> for no fill.</summary>
        /// <value>Defaults to <see langword="null"/> (unfilled).</value>
        public string? NonTerminalColor { get; set; }

        /// <summary>The <c>style</c> attribute of a 0-branch (item excluded) edge.</summary>
        /// <value>Defaults to <c>"dashed"</c>.</value>
        public string ZeroEdgeStyle { get; set; } = "dashed";

        /// <summary>The <c>style</c> attribute of a 1-branch (item included) edge.</summary>
        /// <value>Defaults to <c>"solid"</c>.</value>
        public string OneEdgeStyle { get; set; } = "solid";

        /// <summary>
        /// Copies every property into a new instance. Used by <see cref="Graphs.GraphSet"/> /
        /// <see cref="Sets.SetSet{T}"/>'s <c>ToDot</c> convenience overloads to fill in a default
        /// <see cref="LevelLabel"/> without mutating the caller's own instance.
        /// </summary>
        internal DotOptions Clone() => new DotOptions
        {
            StateLabels = StateLabels,
            LevelLabel = LevelLabel,
            MaxLevels = MaxLevels,
            MaxNodes = MaxNodes,
            FocusNodeId = FocusNodeId,
            NonTerminalShape = NonTerminalShape,
            NonTerminalColor = NonTerminalColor,
            ZeroEdgeStyle = ZeroEdgeStyle,
            OneEdgeStyle = OneEdgeStyle,
        };
    }
}
