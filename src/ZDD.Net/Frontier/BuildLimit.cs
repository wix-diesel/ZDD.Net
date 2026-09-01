namespace ZDD.Net.Frontier
{
    /// <summary>The limit of <see cref="BuildOptions"/> a build ran into.</summary>
    public enum BuildLimit
    {
        /// <summary><see cref="BuildOptions.MaxNodeCount"/>: the temporary nodes of every level together.</summary>
        NodeCount,

        /// <summary><see cref="BuildOptions.MaxFrontierSize"/>: the distinct states of a single level.</summary>
        FrontierSize,
    }
}
