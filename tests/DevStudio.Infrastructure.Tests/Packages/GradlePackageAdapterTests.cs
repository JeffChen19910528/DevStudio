using DevStudio.Core.Packages;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;
using DevStudio.Infrastructure.Packages;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Packages;

/// <summary>
/// Unit tests for <see cref="GradlePackageAdapter"/>. Like Maven, <c>gradle</c> is not installed on
/// the machine these tests were written on — parsing is exercised against realistic fixture text
/// only. Always "implemented but not real-environment validated," never "real-tested" (ADR-015).
/// </summary>
public class GradlePackageAdapterTests
{
    private sealed class FixedDetector : IToolchainDetector
    {
        private readonly bool _installed;
        public FixedDetector(bool installed) => _installed = installed;
        public string ToolchainId => WellKnownToolchainIds.Gradle;
        public Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_installed
                ? new ToolchainInfo(ToolchainId, "Gradle", ToolchainDetectionState.Detected, ExecutablePath: "gradle")
                : new ToolchainInfo(ToolchainId, "Gradle", ToolchainDetectionState.NotInstalled));
    }

    private static async Task<ToolchainRegistry> RegistryAsync(bool installed)
    {
        var registry = new ToolchainRegistry();
        registry.Register(new FixedDetector(installed));
        await registry.RefreshAsync();
        return registry;
    }

    private static ProjectInfo MakeProjectInfo(string rootPath, string? projectFile = null) =>
        new("id", "fixture", rootPath, ProjectType.Java, projectFile, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());

    [Fact]
    public void DetectProject_returns_null_when_no_gradle_build_file_exists()
    {
        using var dir = new TempDirectory();
        var adapter = new GradlePackageAdapter(new FakeProcessRunner(), new ToolchainRegistry());

        Assert.Null(adapter.DetectProject(MakeProjectInfo(dir.Path)));
    }

    [Fact]
    public async Task DetectProject_never_claims_Add_Remove_or_Update_capability()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("build.gradle", "dependencies { implementation 'junit:junit:4.13.2' }");
        var adapter = new GradlePackageAdapter(new FakeProcessRunner(), await RegistryAsync(installed: true));

        var project = adapter.DetectProject(MakeProjectInfo(dir.Path));

        Assert.NotNull(project);
        Assert.False(project!.Capabilities.Add);
        Assert.False(project.Capabilities.Remove);
        Assert.False(project.Capabilities.Update);
        Assert.False(project.Capabilities.Restore);
        Assert.True(project.Capabilities.ListInstalled);
        Assert.True(project.Capabilities.TransitiveDependencySupport);
    }

    [Fact]
    public async Task DetectProject_prefers_build_gradle_kts_when_both_exist()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("build.gradle", "");
        dir.WriteFile("build.gradle.kts", "");
        var adapter = new GradlePackageAdapter(new FakeProcessRunner(), await RegistryAsync(installed: true));

        var project = adapter.DetectProject(MakeProjectInfo(dir.Path));

        Assert.NotNull(project);
        Assert.EndsWith("build.gradle.kts", project!.ProjectPath);
    }

    [Fact]
    public async Task DetectProject_reports_unavailable_when_gradle_is_not_installed()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("build.gradle", "");
        var adapter = new GradlePackageAdapter(new FakeProcessRunner(), await RegistryAsync(installed: false));

        var project = adapter.DetectProject(MakeProjectInfo(dir.Path));

        Assert.NotNull(project);
        Assert.Equal(PackageManagerCapabilities.None, project!.Capabilities);
    }

    // Realistic fixture text mirroring Gradle's documented `dependencies` ASCII-tree renderer.
    private const string DependenciesOutput = """
        > Task :dependencies

        ------------------------------------------------------------
        Root project 'fixture'
        ------------------------------------------------------------

        compileClasspath - Compile classpath for source set 'main'.
        +--- org.springframework:spring-core:5.3.10
        |    \--- org.springframework:spring-jcl:5.3.10
        \--- com.google.guava:guava:31.0-jre
        """;

    [Fact]
    public async Task ListInstalledAsync_parses_direct_and_transitive_dependencies()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("gradle", 0, DependenciesOutput);
        var project = new PackageProject("/repo/build.gradle", ProjectType.Java, "gradle", "Gradle", new PackageManagerCapabilities(ListInstalled: true));
        var adapter = new GradlePackageAdapter(runner, await RegistryAsync(installed: true));

        var installed = await adapter.ListInstalledAsync(project);

        Assert.Equal(3, installed.Count);
        var springCore = Assert.Single(installed, p => p.PackageId == "org.springframework:spring-core");
        Assert.Equal(PackageDependencyKind.Direct, springCore.Kind);

        var springJcl = Assert.Single(installed, p => p.PackageId == "org.springframework:spring-jcl");
        Assert.Equal(PackageDependencyKind.Transitive, springJcl.Kind);

        var guava = Assert.Single(installed, p => p.PackageId == "com.google.guava:guava");
        Assert.Equal(PackageDependencyKind.Direct, guava.Kind);
        Assert.Equal("31.0-jre", guava.ResolvedVersion);
    }

    [Fact]
    public async Task ListDependenciesAsync_parses_string_notation_declarations_without_running_a_process()
    {
        using var dir = new TempDirectory();
        var buildFile = dir.WriteFile("build.gradle", "dependencies {\n    implementation 'org.apache.commons:commons-lang3:3.14.0'\n    testImplementation \"junit:junit:4.13.2\"\n}");
        var runner = new FakeProcessRunner();
        var project = new PackageProject(buildFile, ProjectType.Java, "gradle", "Gradle", new PackageManagerCapabilities(ListDependencies: true));
        var adapter = new GradlePackageAdapter(runner, new ToolchainRegistry());

        var dependencies = await adapter.ListDependenciesAsync(project);

        Assert.Empty(runner.Requests);
        Assert.Equal(2, dependencies.Count);
        Assert.Contains(dependencies, d => d.PackageId == "org.apache.commons:commons-lang3" && d.VersionRange == "3.14.0");
        Assert.Contains(dependencies, d => d.PackageId == "junit:junit" && d.VersionRange == "4.13.2");
    }

    [Fact]
    public void GradleBuildScriptParser_does_not_match_map_notation_declarations()
    {
        // Documents the real, deliberate limitation from the type-level remarks: map notation is
        // not parsed, so it must not be silently misreported as a string-notation match.
        const string script = "dependencies { implementation group: 'junit', name: 'junit', version: '4.13.2' }";

        var results = GradleBuildScriptParser.ParseStringNotationDependencies(script);

        Assert.Empty(results);
    }
}
