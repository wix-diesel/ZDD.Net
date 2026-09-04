using System;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// Thrown when a build passes one of the limits of <see cref="BuildOptions"/>. The build stops
    /// there and frees what it held, which is the point: an unbounded frontier ends in an OOM instead.
    /// </summary>
    /// <remarks>
    /// Raising the limit only helps when the width is nearly enough already; a spec that keeps
    /// useless distinctions, or a bad item order, is what usually has to change.
    /// </remarks>
    public sealed class BuildLimitExceededException : InvalidOperationException
    {
        /// <summary>Creates an exception naming the limit that was passed and where.</summary>
        /// <param name="limit">Which limit was passed.</param>
        /// <param name="limitValue">The value that limit was set to.</param>
        /// <param name="level">The level being filled when the limit was passed.</param>
        /// <param name="message">A message explaining what grew past what.</param>
        public BuildLimitExceededException(BuildLimit limit, int limitValue, int level, string message)
            : this(limit, limitValue, level, message, thrownByExpander: false)
        {
        }

        /// <summary>
        /// The overload <see cref="TopDownExpander{TSpec, TState}"/> / <see cref="ArrayTopDownExpander{TSpec}"/>
        /// use for their own limit checks, marking <see cref="ThrownByExpander"/> so
        /// <see cref="FrontierBuilder.TryBuild{TSpec, TState}"/> can tell an actual limit hit apart
        /// from a spec that happens to throw this same public exception type itself (issue #138):
        /// only an instance built through this constructor may become <see langword="false"/>
        /// there — one built through the public constructor above always propagates, exactly like
        /// any other exception a spec throws.
        /// </summary>
        internal BuildLimitExceededException(BuildLimit limit, int limitValue, int level, string message, bool thrownByExpander)
            : base(message)
        {
            Limit = limit;
            LimitValue = limitValue;
            Level = level;
            ThrownByExpander = thrownByExpander;
        }

        /// <summary>Which limit of <see cref="BuildOptions"/> was passed.</summary>
        public BuildLimit Limit { get; }

        /// <summary>The value <see cref="Limit"/> was set to.</summary>
        public int LimitValue { get; }

        /// <summary>The level whose states or nodes the build was adding when it stopped.</summary>
        public int Level { get; }

        /// <summary>
        /// <see langword="true"/> only for an instance the top-down pass itself threw for an actual
        /// limit hit; <see langword="false"/> (the default) for one built through the public
        /// constructor, including one a spec throws. See <see cref="FrontierBuilder.TryBuild{TSpec, TState}"/>.
        /// </summary>
        internal bool ThrownByExpander { get; }
    }
}
