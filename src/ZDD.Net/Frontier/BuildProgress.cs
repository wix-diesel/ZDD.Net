namespace ZDD.Net.Frontier
{
    /// <summary>
    /// What a build reports as it leaves a level behind, from <see cref="RootLevel"/> down to 1, so
    /// <c>(RootLevel - Level + 1) / RootLevel</c> is the fraction done.
    /// </summary>
    /// <remarks>
    /// A level no branch reached is reported too, with a <see cref="FrontierSize"/> of 0: the reports
    /// of one build are then the width of every level, which is what diagnosing a blown-up build needs.
    /// </remarks>
    public readonly struct BuildProgress
    {
        /// <summary>Creates a report for a level the build has left behind.</summary>
        /// <param name="rootLevel">The level the build started from.</param>
        /// <param name="level">The level just left behind.</param>
        /// <param name="frontierSize">The distinct states that level held; 0 if no branch reached it.</param>
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

        /// <summary>The level just left behind; counts down to 1.</summary>
        public int Level { get; }

        /// <summary>
        /// The distinct states <see cref="Level"/> held: the frontier width there.
        /// 0 when no branch reached that level, which is what a spec that skips levels leaves behind.
        /// </summary>
        public int FrontierSize { get; }

        /// <summary>Temporary nodes created so far, counting every level expanded up to now.</summary>
        public long NodeCount { get; }
    }
}
