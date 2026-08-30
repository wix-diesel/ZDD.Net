namespace ZDD.Net.Internal
{
    /// <summary>
    /// Marker type used by the test projects to assert that the library assembly is
    /// referenced and that <c>InternalsVisibleTo</c> is wired up correctly.
    /// </summary>
    internal static class AssemblyMarker
    {
        /// <summary>
        /// Identifies which target framework asset of the library was loaded, so that the
        /// netstandard2.0 and net10.0 builds can be told apart at run time. The two builds
        /// must behave identically; only <c>#if NET</c> fast paths may differ.
        /// </summary>
        internal const string TargetFrameworkMoniker =
#if NET
            "net10.0";
#else
            "netstandard2.0";
#endif
    }
}
