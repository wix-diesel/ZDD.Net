using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Xunit;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Properties
{
    /// <summary>
    /// 本体 <c>src/ZDD.Net</c> の外部依存がゼロのままであることを機械的に確かめる。
    /// </summary>
    /// <remarks>
    /// このプロジェクトはリポジトリに初めて NuGet パッケージ（CsCheck）を持ち込む。
    /// 「テストにだけ入れる」は約束事なので、約束のほうもテストにしておく
    /// （依存ゼロは docs/OPEN-QUESTIONS.md B1 の決めごと）。
    /// </remarks>
    public class DependencyPolicyTests
    {
        /// <summary>本体プロジェクトのファイル。</summary>
        private static readonly string LibraryProject = Path.Combine("src", "ZDD.Net", "ZDD.Net.csproj");

        /// <summary>
        /// 本体に効いてしまう共通のビルド設定。ここで参照を足されても依存ゼロは崩れる。
        /// </summary>
        /// <remarks>
        /// <c>Directory.Packages.props</c> が並んでいるのは、中央パッケージ管理のファイルも
        /// 全プロジェクトに読み込まれるため。ここに置いてよいのは版を決める
        /// <c>PackageVersion</c> だけで、<c>PackageReference</c> や
        /// <c>GlobalPackageReference</c>（全プロジェクトに参照を配るための項目）を書けば
        /// 本体にもそのまま入ってしまう。
        /// </remarks>
        private static readonly string[] SharedBuildFiles =
        {
            "Directory.Build.props",
            "Directory.Build.targets",
            "Directory.Packages.props",
        };

        [Fact]
        public void TheLibraryProjectHasNoPackageReference()
        {
            string path = Path.Combine(RepositoryRoot(), LibraryProject);
            string[] packages = PackageReferencesIn(path);

            Assert.True(
                packages.Length == 0,
                $"{LibraryProject} must not reference any package, but references {string.Join(", ", packages)}.");
        }

        [Fact]
        public void TheSharedBuildFilesDoNotAddPackagesToTheLibrary()
        {
            string root = RepositoryRoot();

            foreach (string file in SharedBuildFiles)
            {
                string path = Path.Combine(root, file);

                if (!File.Exists(path))
                {
                    continue;
                }

                string[] packages = PackageReferencesIn(path);

                Assert.True(
                    packages.Length == 0,
                    $"{file} applies to every project, so it must not reference " +
                    $"any package, but has {string.Join(", ", packages)}.");
            }
        }

        [Fact]
        public void TheLibraryAssemblyOnlyReferencesTheFramework()
        {
            // csproj だけ見ても、プロジェクト参照や共通設定から入り込む可能性が残る。
            // 出来上がったアセンブリの参照表まで見て初めて依存ゼロが言える。
            AssemblyName[] referenced = typeof(Zdd).Assembly.GetReferencedAssemblies();
            string[] external = referenced
                .Select(name => name.Name!)
                .Where(name => !IsFrameworkAssembly(name))
                .ToArray();

            Assert.True(
                external.Length == 0,
                $"ZDD.Net must not depend on {string.Join(", ", external)}.");
        }

        [Fact]
        public void ThePropertyTestProjectIsWhereCsCheckLives()
        {
            string path = Path.Combine(
                RepositoryRoot(),
                "tests",
                "ZDD.Net.Tests.Properties",
                "ZDD.Net.Tests.Properties.csproj");

            Assert.Contains("CsCheck", PackageReferencesIn(path));
        }

        /// <summary>
        /// ビルド設定ファイルが配っているパッケージ参照。<c>PackageReference</c> と
        /// <c>GlobalPackageReference</c> の両方を拾う（後者は全プロジェクトに参照を配る項目で、
        /// <c>Directory.Packages.props</c> にしか書けない）。版を決めるだけの
        /// <c>PackageVersion</c> は参照ではないので拾わない。
        /// </summary>
        private static string[] PackageReferencesIn(string path)
        {
            XDocument document = XDocument.Load(path);

            return document.Descendants()
                .Where(element =>
                    element.Name.LocalName == "PackageReference"
                    || element.Name.LocalName == "GlobalPackageReference")
                .Select(element =>
                    element.Attribute("Include")?.Value
                    ?? element.Attribute("Update")?.Value
                    ?? "(unnamed)")
                .ToArray();
        }

        /// <summary>
        /// ソリューションのある場所を、テストアセンブリの置き場から上へ辿って探す。
        /// </summary>
        private static string RepositoryRoot()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "ZDD.Net.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException(
                $"Could not find ZDD.Net.sln above '{AppContext.BaseDirectory}'.");
        }

        private static bool IsFrameworkAssembly(string name) =>
            name == "netstandard"
            || name == "mscorlib"
            || name == "System"
            || name.StartsWith("System.", StringComparison.Ordinal);
    }
}
