using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ZDD.Net.Core;
using ZDD.Net.Internal;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// Turns a frontier-search specification into a canonical <see cref="Zdd"/>: the top-down
    /// expansion (<see cref="TopDownExpander{TSpec, TState}"/> / <see cref="ArrayTopDownExpander{TSpec}"/>)
    /// followed by the bottom-up reduction into the manager's Core tables (<see cref="BottomUpReducer"/>).
    /// </summary>
    /// <example>
    /// <code>
    /// using ZddManager manager = new ZddManager(variableCount: 5);
    ///
    /// Zdd powerSet = FrontierBuilder.Build&lt;PowerSetSpec, byte&gt;(manager, new PowerSetSpec(itemCount: 5));
    /// Zdd sizeTwoOrThree = FrontierBuilder.Build&lt;CardinalitySpec, int&gt;(
    ///     manager, new CardinalitySpec(itemCount: 5, min: 2, max: 3));
    /// </code>
    /// </example>
    /// <remarks>
    /// This is the library's central entry point: write an <see cref="IDdSpec{TState}"/> (or
    /// <see cref="IArrayDdSpec"/> / <see cref="IHybridDdSpec{TScalar}"/>) and call one of the
    /// <c>Build</c> overloads to get a <see cref="Zdd"/> that every Core operation (<c>Count</c>,
    /// enumeration, <c>Sample</c>, <c>MaxWeight</c>, ...) works on unchanged.
    /// </remarks>
    public static class FrontierBuilder
    {
        /// <summary>Builds a <see cref="Zdd"/> from a fixed-<c>struct</c>-state spec.</summary>
        /// <typeparam name="TSpec">The spec type; a <c>struct</c>, so calls devirtualize and inline.</typeparam>
        /// <typeparam name="TState">The state carried between levels.</typeparam>
        /// <param name="manager">The manager the resulting family, and every node it needs, belongs to.</param>
        /// <param name="spec">The specification to unroll.</param>
        /// <param name="options">Limits, cancellation and progress for the top-down pass; defaults when null.</param>
        /// <returns>The family <paramref name="spec"/> describes, canonical within <paramref name="manager"/>.</returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="manager"/> is null.</exception>
        /// <exception cref="System.InvalidOperationException">
        /// The spec's root level exceeds <paramref name="manager"/>'s <see cref="ZddManager.VariableCount"/>.
        /// </exception>
        /// <exception cref="BuildLimitExceededException">A limit of <paramref name="options"/> was passed.</exception>
        /// <exception cref="System.OperationCanceledException">The options' token was cancelled.</exception>
        /// <exception cref="System.ObjectDisposedException"><paramref name="manager"/> has been disposed.</exception>
        public static Zdd Build<TSpec, TState>(ZddManager manager, TSpec spec, BuildOptions? options = null)
            where TSpec : struct, IDdSpec<TState>
        {
            ThrowHelper.ThrowIfNull(manager, nameof(manager));

            TemporaryNodeTable table = TopDownExpander<TSpec, TState>.Expand(spec, options);
            EnsureFitsManager(manager, table);
            return BottomUpReducer.Reduce(manager, table);
        }

        /// <summary>
        /// Builds a <see cref="Zdd"/> from a fixed-<c>struct</c>-state spec as
        /// <see cref="Build{TSpec, TState}(ZddManager, TSpec, BuildOptions)"/> does, additionally
        /// recording a "which spec state does this node correspond to" label for every node — the
        /// debugging aid <c>docs/frontier-guide.md</c> §7's tutorial and <see cref="Io.DotOptions.StateLabels"/>
        /// are built around (M5-4, issue #56). Ignored (and <paramref name="stateLabels"/> comes back
        /// empty) unless <paramref name="options"/>' <see cref="BuildOptions.RecordStates"/> is set.
        /// </summary>
        /// <typeparam name="TSpec">The spec type; a <c>struct</c>, so calls devirtualize and inline.</typeparam>
        /// <typeparam name="TState">The state carried between levels.</typeparam>
        /// <param name="manager">The manager the resulting family, and every node it needs, belongs to.</param>
        /// <param name="spec">The specification to unroll.</param>
        /// <param name="options">
        /// Limits, cancellation, progress, and whether to record states at all
        /// (<see cref="BuildOptions.RecordStates"/>).
        /// </param>
        /// <param name="stateLabels">
        /// Every recorded node's label, keyed by the node id <see cref="Io.DotWriter"/> shows as
        /// <c>n&lt;id&gt;</c>. Empty when <see cref="BuildOptions.RecordStates"/> is <see langword="false"/>.
        /// </param>
        /// <param name="describeState">
        /// Turns a state into its label; <c>state?.ToString()</c> when <see langword="null"/>, so a spec
        /// whose state type already has a meaningful <c>ToString</c> needs nothing further.
        /// </param>
        /// <returns>The family <paramref name="spec"/> describes, canonical within <paramref name="manager"/>.</returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="manager"/> or <paramref name="options"/> is null.</exception>
        /// <exception cref="System.InvalidOperationException">
        /// The spec's root level exceeds <paramref name="manager"/>'s <see cref="ZddManager.VariableCount"/>.
        /// </exception>
        /// <exception cref="BuildLimitExceededException">A limit of <paramref name="options"/> was passed.</exception>
        /// <exception cref="System.OperationCanceledException">The options' token was cancelled.</exception>
        /// <exception cref="System.ObjectDisposedException"><paramref name="manager"/> has been disposed.</exception>
        public static Zdd Build<TSpec, TState>(
            ZddManager manager,
            TSpec spec,
            BuildOptions options,
            out IReadOnlyDictionary<int, string> stateLabels,
            Func<TState, string>? describeState = null)
            where TSpec : struct, IDdSpec<TState>
        {
            ThrowHelper.ThrowIfNull(manager, nameof(manager));
            ThrowHelper.ThrowIfNull(options, nameof(options));

            if (!options.RecordStates)
            {
                stateLabels = EmptyStateLabels;
                return Build<TSpec, TState>(manager, spec, options);
            }

            Func<TState, string> labelOf = describeState ?? (state => state?.ToString() ?? "null");

            TemporaryNodeTable table =
                TopDownExpander<TSpec, TState>.Expand(spec, options, labelOf, out string?[][] labelsByLevel);
            EnsureFitsManager(manager, table);

            Zdd zdd = BottomUpReducer.Reduce(manager, table, out int[]?[] coreIdsByLevel);
            stateLabels = ToStateLabelMap(coreIdsByLevel, labelsByLevel);
            return zdd;
        }

        /// <summary>
        /// Shared stand-in for "no states were recorded". A genuinely read-only wrapper, not just a
        /// <see cref="Dictionary{TKey, TValue}"/> exposed through the read-only interface — a caller
        /// could otherwise downcast and mutate the one shared instance every non-recording call returns.
        /// </summary>
        private static readonly IReadOnlyDictionary<int, string> EmptyStateLabels =
            new ReadOnlyDictionary<int, string>(new Dictionary<int, string>());

        /// <summary>
        /// Translates recorded (level, index) labels into node-id-keyed ones. A temporary node the
        /// zero-suppression rule elided is simply never a value here — nothing outside this method
        /// would have a use for a label under an id no node was actually created for.
        /// </summary>
        private static IReadOnlyDictionary<int, string> ToStateLabelMap(int[]?[] coreIdsByLevel, string?[][] labelsByLevel)
        {
            Dictionary<int, string> map = new Dictionary<int, string>();
            int levelCount = Math.Min(coreIdsByLevel.Length, labelsByLevel.Length);

            for (int level = 1; level < levelCount; level++)
            {
                string?[] labels = labelsByLevel[level];
                int[]? coreIds = coreIdsByLevel[level];

                if (coreIds is null)
                {
                    continue;
                }

                for (int index = 0; index < labels.Length; index++)
                {
                    if (labels[index] is string label)
                    {
                        map[coreIds[index]] = label;
                    }
                }
            }

            return map;
        }

        /// <summary>Builds a <see cref="Zdd"/> from a variable-length array-state spec.</summary>
        /// <typeparam name="TSpec">The spec type; a <c>struct</c>, so calls devirtualize and inline.</typeparam>
        /// <param name="manager">The manager the resulting family, and every node it needs, belongs to.</param>
        /// <param name="spec">The specification to unroll.</param>
        /// <param name="options">Limits, cancellation and progress for the top-down pass; defaults when null.</param>
        /// <returns>The family <paramref name="spec"/> describes, canonical within <paramref name="manager"/>.</returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="manager"/> is null.</exception>
        /// <exception cref="System.InvalidOperationException">
        /// The spec's <see cref="IArrayDdSpec.ArrayLength"/> is negative, or its root level exceeds
        /// <paramref name="manager"/>'s <see cref="ZddManager.VariableCount"/>.
        /// </exception>
        /// <exception cref="BuildLimitExceededException">A limit of <paramref name="options"/> was passed.</exception>
        /// <exception cref="System.OperationCanceledException">The options' token was cancelled.</exception>
        /// <exception cref="System.ObjectDisposedException"><paramref name="manager"/> has been disposed.</exception>
        public static Zdd Build<TSpec>(ZddManager manager, TSpec spec, BuildOptions? options = null)
            where TSpec : struct, IArrayDdSpec
        {
            ThrowHelper.ThrowIfNull(manager, nameof(manager));

            TemporaryNodeTable table = ArrayTopDownExpander<TSpec>.Expand(spec, options);
            EnsureFitsManager(manager, table);
            return BottomUpReducer.Reduce(manager, table);
        }

        /// <summary>
        /// Builds a <see cref="Zdd"/> from a fixed-<c>struct</c>-state spec exactly as
        /// <see cref="Build{TSpec, TState}(ZddManager, TSpec, BuildOptions)"/> does, except that
        /// passing one of <paramref name="options"/>' limits returns <see langword="false"/>
        /// instead of throwing (issue #138) — for the "pick a limit, and if it doesn't fit try a
        /// different edge order" style of exploration, where an exception would otherwise become
        /// the control flow.
        /// </summary>
        /// <typeparam name="TSpec">The spec type; a <c>struct</c>, so calls devirtualize and inline.</typeparam>
        /// <typeparam name="TState">The state carried between levels.</typeparam>
        /// <param name="manager">The manager the resulting family, and every node it needs, belongs to.</param>
        /// <param name="spec">The specification to unroll.</param>
        /// <param name="options">
        /// Limits, cancellation and progress for the top-down pass. Required — not optional and not
        /// nullable, since a <see cref="TryBuild{TSpec, TState}"/> that sets no limit could never
        /// return <see langword="false"/>, which would make the call pointless.
        /// </param>
        /// <param name="result">
        /// The built family when this returns <see langword="true"/>; <see langword="default"/>
        /// when it returns <see langword="false"/>.
        /// </param>
        /// <returns>
        /// <see langword="false"/> when the build passed <see cref="BuildOptions.MaxNodeCount"/> or
        /// <see cref="BuildOptions.MaxFrontierSize"/>; <see langword="true"/> otherwise. Cancellation
        /// and an exception the spec itself throws are never turned into <see langword="false"/> —
        /// they propagate exactly as <see cref="Build{TSpec, TState}(ZddManager, TSpec, BuildOptions)"/>
        /// would.
        /// </returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="manager"/> or <paramref name="options"/> is null.</exception>
        /// <exception cref="System.InvalidOperationException">
        /// The spec's root level exceeds <paramref name="manager"/>'s <see cref="ZddManager.VariableCount"/>.
        /// </exception>
        /// <exception cref="System.OperationCanceledException">The options' token was cancelled.</exception>
        /// <exception cref="System.ObjectDisposedException"><paramref name="manager"/> has been disposed.</exception>
        /// <remarks>
        /// Limit exceeded is the only failure this converts into <see langword="false"/>; every other
        /// failure the top-down pass (<see cref="TopDownExpander{TSpec, TState}"/>) can raise — an
        /// invalid root level, cancellation, or an exception the spec itself throws — propagates
        /// exactly as it would from <see cref="Build{TSpec, TState}(ZddManager, TSpec, BuildOptions)"/>.
        /// A limit hit is also the only one of those that can happen before the bottom-up reduction
        /// (<see cref="BottomUpReducer"/>) ever writes to <paramref name="manager"/>'s tables — the
        /// top-down pass only ever writes to its own temporary node table. So a caller that gets
        /// <see langword="false"/> back is guaranteed <paramref name="manager"/> is exactly as it was
        /// before the call, its <see cref="ZddManager.NodeCount"/> included: there is nothing to undo.
        /// </remarks>
        public static bool TryBuild<TSpec, TState>(ZddManager manager, TSpec spec, BuildOptions options, out Zdd result)
            where TSpec : struct, IDdSpec<TState>
        {
            ThrowHelper.ThrowIfNull(manager, nameof(manager));
            ThrowHelper.ThrowIfNull(options, nameof(options));

            TemporaryNodeTable table;

            try
            {
                table = TopDownExpander<TSpec, TState>.Expand(spec, options);
            }
            catch (BuildLimitExceededException ex) when (ex.ThrownByExpander)
            {
                result = default;
                return false;
            }

            EnsureFitsManager(manager, table);
            result = BottomUpReducer.Reduce(manager, table);
            return true;
        }

        /// <summary>
        /// Builds a <see cref="Zdd"/> from a variable-length array-state spec exactly as
        /// <see cref="TryBuild{TSpec, TState}"/> does for a fixed-state one — see its remarks for
        /// the semantics <paramref name="options"/>' limits get here.
        /// </summary>
        /// <typeparam name="TSpec">The spec type; a <c>struct</c>, so calls devirtualize and inline.</typeparam>
        /// <param name="manager">The manager the resulting family, and every node it needs, belongs to.</param>
        /// <param name="spec">The specification to unroll.</param>
        /// <param name="options">Limits, cancellation and progress for the top-down pass. Required.</param>
        /// <param name="result">
        /// The built family when this returns <see langword="true"/>; <see langword="default"/>
        /// when it returns <see langword="false"/>.
        /// </param>
        /// <returns>
        /// <see langword="false"/> when the build passed <see cref="BuildOptions.MaxNodeCount"/> or
        /// <see cref="BuildOptions.MaxFrontierSize"/>; <see langword="true"/> otherwise.
        /// </returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="manager"/> or <paramref name="options"/> is null.</exception>
        /// <exception cref="System.InvalidOperationException">
        /// The spec's <see cref="IArrayDdSpec.ArrayLength"/> is negative, or its root level exceeds
        /// <paramref name="manager"/>'s <see cref="ZddManager.VariableCount"/>.
        /// </exception>
        /// <exception cref="System.OperationCanceledException">The options' token was cancelled.</exception>
        /// <exception cref="System.ObjectDisposedException"><paramref name="manager"/> has been disposed.</exception>
        public static bool TryBuild<TSpec>(ZddManager manager, TSpec spec, BuildOptions options, out Zdd result)
            where TSpec : struct, IArrayDdSpec
        {
            ThrowHelper.ThrowIfNull(manager, nameof(manager));
            ThrowHelper.ThrowIfNull(options, nameof(options));

            TemporaryNodeTable table;

            try
            {
                table = ArrayTopDownExpander<TSpec>.Expand(spec, options);
            }
            catch (BuildLimitExceededException ex) when (ex.ThrownByExpander)
            {
                result = default;
                return false;
            }

            EnsureFitsManager(manager, table);
            result = BottomUpReducer.Reduce(manager, table);
            return true;
        }

        /// <summary>
        /// Rejects a table whose root level does not fit <paramref name="manager"/>: a family built
        /// from it would hold nodes at levels <see cref="ZddManager.ItemOf"/> cannot convert back to
        /// an item, which later operations would surface as a confusing failure deep inside Core.
        /// </summary>
        private static void EnsureFitsManager(ZddManager manager, TemporaryNodeTable table)
        {
            if (table.RootLevel > manager.VariableCount)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    $"The spec's root level ({table.RootLevel}) exceeds the manager's VariableCount " +
                    $"({manager.VariableCount}); use a manager with enough variables for the spec, or make " +
                    "the spec return lower levels.");
            }
        }
    }
}
