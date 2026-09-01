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
    }
}
