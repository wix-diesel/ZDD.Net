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

        private static bool IsFrameworkAssembly(string name) =>
            name == "netstandard"
            || name == "mscorlib"
            || name == "System"
            || name.StartsWith("System.", System.StringComparison.Ordinal);
    }
}
