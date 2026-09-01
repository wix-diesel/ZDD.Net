namespace ZDD.Net.Frontier
{
    /// <summary>
    /// What a build reports once a level has been expanded. Levels are finished from
    /// <see cref="RootLevel"/> down to 1, so <c>(RootLevel - Level + 1) / RootLevel</c> is the fraction done.
    /// </summary>
    public readonly struct BuildProgress
    {
        /// <summary>Creates a report for a finished level.</summary>
        /// <param name="rootLevel">The level the build started from.</param>
        /// <param name="level">The level just finished.</param>
        /// <param name="frontierSize">The distinct states that level held.</param>
        /// <param name="nodeCount">Temporary nodes created so far, over every level.</param>
        public BuildProgress(int rootLevel, int level, int frontierSize, long nodeCount)
        {
            RootLevel = rootLevel;
            Level = level;
            FrontierSize = frontierSize;
            NodeCount = nodeCount;
        }

        /// <summary>The level the build started from, which is also the number of levels.</summary>
        public int RootLevel { get; }

        /// <summary>The level just finished; counts down to 1.</summary>
        public int Level { get; }

        /// <summary>The distinct states <see cref="Level"/> held: the frontier width there.</summary>
        public int FrontierSize { get; }

        /// <summary>Temporary nodes created so far, counting every level expanded up to now.</summary>
        public long NodeCount { get; }
    }
}
