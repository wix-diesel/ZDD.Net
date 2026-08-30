using System.Reflection;
using Xunit;
using ZDD.Net.Internal;

namespace ZDD.Net.Tests
{
    public class SolutionSmokeTests
    {
        [Fact]
        public void LibraryAssemblyIsReferencedAndInternalsAreVisible()
        {
            Assembly assembly = typeof(AssemblyMarker).Assembly;

            Assert.Equal("ZDD.Net", assembly.GetName().Name);
        }

        [Fact]
        public void LibraryHasNoExternalDependencies()
        {
            // Zero external dependencies is a project invariant (docs/OPEN-QUESTIONS.md, B1).
            // Anything outside the framework itself must be rejected here.
            AssemblyName[] referenced = typeof(AssemblyMarker).Assembly.GetReferencedAssemblies();

            foreach (AssemblyName name in referenced)
            {
                Assert.True(
                    IsFrameworkAssembly(name.Name!),
                    $"ZDD.Net must not depend on '{name.Name}'.");
            }
        }

        [Fact]
        public void TestProjectResolvesTheExpectedLibraryAsset()
        {
            // ZDD.Net.Tests runs against the net10.0 build; ZDD.Net.Tests.NetStandard
            // compiles these same sources against the netstandard2.0 build. If this ever
            // fails, the multi-targeting is silently collapsing to a single asset and the
            // #if NET branches are no longer being covered.
#if ZDD_TESTS_NETSTANDARD_ASSET
            Assert.Equal("netstandard2.0", AssemblyMarker.TargetFrameworkMoniker);
#else
            Assert.Equal("net10.0", AssemblyMarker.TargetFrameworkMoniker);
#endif
        }

        private static bool IsFrameworkAssembly(string name) =>
            name == "netstandard"
            || name == "mscorlib"
            || name == "System"
            || name.StartsWith("System.", System.StringComparison.Ordinal);
    }
}
