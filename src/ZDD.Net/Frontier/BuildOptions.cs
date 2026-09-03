using System;
using System.Threading;
using ZDD.Net.Internal;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// The limits and hooks of one frontier build. Nothing is limited by default; set the limits
    /// when a spec may blow up, so the build fails with an exception instead of exhausting memory.
    /// </summary>
    /// <remarks>
    /// The values are read once when a build starts, so one instance can be reused across builds
    /// and a later change does not affect a build already running.
    /// </remarks>
    public sealed class BuildOptions
    {
        /// <summary>The value of a limit that bounds nothing; the default of both limits.</summary>
        public const int Unlimited = int.MaxValue;

        private int _maxNodeCount = Unlimited;
        private int _maxFrontierSize = Unlimited;
        private int _maxDegreeOfParallelism = Environment.ProcessorCount;

        /// <summary>
        /// Upper bound on the temporary nodes one build may create, counting every level together.
        /// Exceeding it throws <see cref="BuildLimitExceededException"/>.
        /// </summary>
        /// <value>Positive. Defaults to <see cref="Unlimited"/>.</value>
        /// <exception cref="ArgumentOutOfRangeException">Value is not positive.</exception>
        public int MaxNodeCount
        {
            get => _maxNodeCount;
            set
            {
                if (value <= 0)
                {
                    ThrowHelper.ThrowArgumentOutOfRangeException(
                        nameof(value),
                        $"'{nameof(MaxNodeCount)}' must be positive, but was {value}.");
                }

                _maxNodeCount = value;
            }
        }

        /// <summary>
        /// Upper bound on the distinct states one level may hold, which is the frontier width and
        /// what actually explodes. Exceeding it throws <see cref="BuildLimitExceededException"/>.
        /// </summary>
        /// <value>Positive. Defaults to <see cref="Unlimited"/>.</value>
        /// <exception cref="ArgumentOutOfRangeException">Value is not positive.</exception>
        public int MaxFrontierSize
        {
            get => _maxFrontierSize;
            set
            {
                if (value <= 0)
                {
                    ThrowHelper.ThrowArgumentOutOfRangeException(
                        nameof(value),
                        $"'{nameof(MaxFrontierSize)}' must be positive, but was {value}.");
                }

                _maxFrontierSize = value;
            }
        }

        /// <summary>
        /// Upper bound on the worker threads a level's expansion may use at once (M4-3, issue #46).
        /// <c>1</c> forces the sequential path unconditionally; above that, a level is still expanded
        /// sequentially when it is too narrow for parallelism to pay for itself (docs/PLAN.md §10-8).
        /// </summary>
        /// <value>Positive. Defaults to <see cref="Environment.ProcessorCount"/>.</value>
        /// <remarks>
        /// Whatever degree is used, the build's node IDs are byte-identical to the sequential ones
        /// (docs/frontier-guide.md §6.3): only <em>how</em> a wide level's states are computed and
        /// merged into the shared per-level table changes, never the order they are registered in.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">Value is not positive.</exception>
        public int MaxDegreeOfParallelism
        {
            get => _maxDegreeOfParallelism;
            set
            {
                ThrowHelper.ThrowIfNegativeOrZero(value, nameof(MaxDegreeOfParallelism));
                _maxDegreeOfParallelism = value;
            }
        }

        /// <summary>Cancels a build in progress; observed between levels and every few hundred states.</summary>
        /// <value>Defaults to <see cref="CancellationToken.None"/>.</value>
        public CancellationToken CancellationToken { get; set; }

        /// <summary>Receives one <see cref="BuildProgress"/> per level, from the root level down to 1.</summary>
        /// <value>Defaults to <see langword="null"/>, which reports nothing.</value>
        /// <remarks>
        /// A level no branch reached is reported as well, with a frontier size of 0, so the reports
        /// count down one level at a time. Called on the building thread: a slow handler slows the build.
        /// </remarks>
        public IProgress<BuildProgress>? Progress { get; set; }

        /// <summary>
        /// Whether to keep a "which spec state does this node correspond to" label for every node,
        /// for <see cref="FrontierBuilder"/>'s state-recording <c>Build</c> overload to hand back as
        /// DOT state labels (M5-4, issue #56). Recording never changes the built <see cref="Core.Zdd"/>
        /// itself — only whether that overload also returns labels for it.
        /// </summary>
        /// <value>Defaults to <see langword="false"/>: no labels are kept, and nothing is spent keeping them.</value>
        public bool RecordStates { get; set; }
    }
}
