using DevStudio.Core.Packages;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;
using DevStudio.Infrastructure.Packages;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Packages;

/// <summary>
/// Unit tests for <see cref="MavenPackageAdapter"/>. <c>mvn</c> is not installed on the machine
/// these tests were written on, so every command-construction/output-parsing path is exercised
/// against realistic fixture text through <see cref="FakeProcessRunner"/> — never a real Maven
/// invocation (that would be <c>MavenPackageAdapterIntegrationTests</c>, which does not exist this
/// phase because the tool is unavailable; see ADR-015 for the honest "implemented but not
/// real-environment validated" status this implies).
/// </summary>
public class MavenPackageAdapterTests
{
    private sealed class FixedDetector : IToolchainDetector
    {
        private readonly bool _installed;
        public FixedDetector(bool installed) => _installed = installed;
        public string ToolchainId => WellKnownToolchainIds.Maven;
        public Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_installed
                ? new ToolchainInfo(ToolchainId, "Maven", ToolchainDetectionState.Detected, ExecutablePath: "mvn")
                : new ToolchainInfo(ToolchainId, "Maven", ToolchainDetectionState.NotInstalled));
    }

    private static async Task<ToolchainRegistry> RegistryAsync(bool mavenInstalled)
    {
        var registry = new ToolchainRegistry();
        registry.Register(new FixedDetector(mavenInstalled));
        await registry.RefreshAsync();
        return registry;
    }

    private static ProjectInfo MakeProjectInfo(string rootPath) =>
        new("id", "fixture", rootPath, ProjectType.Java, Path.Combine(rootPath, "pom.xml"), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());

    private const string MinimalPom = """
        <project xmlns="http://maven.apache.org/POM/4.0.0">
          <modelVersion>4.0.0</modelVersion>
          <groupId>com.example</groupId>
          <artifactId>my-app</artifactId>
          <version>1.0-SNAPSHOT</version>
          <dependencies>
            <dependency>
              <groupId>junit</groupId>
              <artifactId>junit</artifactId>
              <version>4.13.2</version>
              <scope>test</scope>
            </dependency>
          </dependencies>
        </project>
        """;

    [Fact]
    public void DetectProject_returns_null_when_project_type_is_not_Java()
    {
        var adapter = new MavenPackageAdapter(new FakeProcessRunner(), new ToolchainRegistry());
        var project = new ProjectInfo("id", "n", "/x", ProjectType.DotNet, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());

        Assert.Null(adapter.DetectProject(project));
    }

    [Fact]
    public void DetectProject_returns_null_when_no_pom_xml_exists()
    {
        using var dir = new TempDirectory();
        var adapter = new MavenPackageAdapter(new FakeProcessRunner(), new ToolchainRegistry());

        Assert.Null(adapter.DetectProject(MakeProjectInfo(dir.Path)));
    }

    [Fact]
    public async Task DetectProject_reports_unavailable_when_mvn_is_not_installed()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("pom.xml", MinimalPom);
        var adapter = new MavenPackageAdapter(new FakeProcessRunner(), await RegistryAsync(mavenInstalled: false));

        var result = adapter.DetectProject(MakeProjectInfo(dir.Path));

        Assert.NotNull(result);
        Assert.Equal(PackageManagerCapabilities.None, result!.Capabilities);
        Assert.Contains("not installed", result.UnavailableReason);
    }

    [Fact]
    public async Task DetectProject_reports_full_capabilities_when_mvn_is_installed()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("pom.xml", MinimalPom);
        var adapter = new MavenPackageAdapter(new FakeProcessRunner(), await RegistryAsync(mavenInstalled: true));

        var result = adapter.DetectProject(MakeProjectInfo(dir.Path));

        Assert.NotNull(result);
        Assert.True(result!.Capabilities.Add);
        Assert.True(result.Capabilities.Remove);
        Assert.True(result.Capabilities.TransitiveDependencySupport);
        Assert.False(result.Capabilities.Search); // No safe process-based search this phase.
        Assert.False(result.Capabilities.PrereleaseSupport); // Maven has no such concept.
    }

    [Fact]
    public async Task ListDependenciesAsync_reads_declared_dependencies_from_pom_xml_without_running_a_process()
    {
        using var dir = new TempDirectory();
        var pomPath = dir.WriteFile("pom.xml", MinimalPom);
        var runner = new FakeProcessRunner();
        var project = new PackageProject(pomPath, ProjectType.Java, "maven", "Maven", new PackageManagerCapabilities(ListDependencies: true));
        var adapter = new MavenPackageAdapter(runner, new ToolchainRegistry());

        var dependencies = await adapter.ListDependenciesAsync(project);

        Assert.Empty(runner.Requests); // Purely file-based read, mirrors PythonPackageAdapter's requirements.txt read.
        var dep = Assert.Single(dependencies);
        Assert.Equal("junit:junit", dep.PackageId);
        Assert.Equal("4.13.2", dep.VersionRange);
        Assert.Equal(PackageDependencyKind.Direct, dep.Kind);
    }

    // Realistic fixture text mirroring maven-dependency-plugin's default tree serializer output.
    private const string DependencyTreeOutput = """
        [INFO] --- maven-dependency-plugin:3.6.1:tree (default-cli) @ my-app ---
        [INFO] com.example:my-app:jar:1.0-SNAPSHOT
        [INFO] +- junit:junit:jar:4.13.2:test
        [INFO] |  \- org.hamcrest:hamcrest-core:jar:1.3:test
        [INFO] \- commons-io:commons-io:jar:2.11.0:compile
        [INFO] ------------------------------------------------------------------------
        """;

    [Fact]
    public async Task ListInstalledAsync_parses_direct_and_transitive_dependencies_from_dependency_tree_output()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("mvn", 0, DependencyTreeOutput);
        var project = new PackageProject("/repo/pom.xml", ProjectType.Java, "maven", "Maven", new PackageManagerCapabilities(ListInstalled: true));
        var adapter = new MavenPackageAdapter(runner, await RegistryAsync(mavenInstalled: true));

        var installed = await adapter.ListInstalledAsync(project);

        Assert.Equal(3, installed.Count);
        var junit = Assert.Single(installed, p => p.PackageId == "junit:junit");
        Assert.Equal(PackageDependencyKind.Direct, junit.Kind);
        Assert.Equal("4.13.2", junit.ResolvedVersion);
        Assert.True(junit.IsDevDependency); // scope=test

        var hamcrest = Assert.Single(installed, p => p.PackageId == "org.hamcrest:hamcrest-core");
        Assert.Equal(PackageDependencyKind.Transitive, hamcrest.Kind);

        var commonsIo = Assert.Single(installed, p => p.PackageId == "commons-io:commons-io");
        Assert.Equal(PackageDependencyKind.Direct, commonsIo.Kind);

        var request = Assert.Single(runner.Requests);
        Assert.Equal("dependency:tree", request.Arguments[0]);
    }

    private const string OutdatedOutput = """
        [INFO] The following dependencies in Default-Java-Project have newer versions:
        [INFO]   junit:junit ..................................... 4.12 -> 4.13.2
        """;

    [Fact]
    public async Task ListOutdatedAsync_parses_the_versions_plugin_tabular_diff()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("mvn", 0, OutdatedOutput);
        var project = new PackageProject("/repo/pom.xml", ProjectType.Java, "maven", "Maven", new PackageManagerCapabilities(ListOutdated: true));
        var adapter = new MavenPackageAdapter(runner, await RegistryAsync(mavenInstalled: true));

        var outdated = await adapter.ListOutdatedAsync(project);

        var junit = Assert.Single(outdated);
        Assert.Equal("junit:junit", junit.PackageId);
        Assert.Equal("4.12", junit.ResolvedVersion);
        Assert.Equal("4.13.2", junit.LatestVersion);
        Assert.True(junit.IsOutdated);
    }

    [Fact]
    public async Task AddAsync_adds_a_new_dependency_element_to_pom_xml_and_preserves_existing_ones()
    {
        using var dir = new TempDirectory();
        var pomPath = dir.WriteFile("pom.xml", MinimalPom);
        var project = new PackageProject(pomPath, ProjectType.Java, "maven", "Maven", new PackageManagerCapabilities(Add: true));
        var adapter = new MavenPackageAdapter(new FakeProcessRunner(), await RegistryAsync(mavenInstalled: true));

        var result = await adapter.AddAsync(project, "com.google.guava:guava", "32.1.3-jre", prerelease: false, isDevDependency: false);

        Assert.True(result.Success);
        Assert.Contains(pomPath, result.ChangedFiles);
        var text = File.ReadAllText(pomPath);
        Assert.Contains("guava", text);
        Assert.Contains("junit", text); // existing dependency preserved
    }

    [Fact]
    public async Task RemoveAsync_removes_only_the_matching_dependency_element()
    {
        using var dir = new TempDirectory();
        var pomPath = dir.WriteFile("pom.xml", MinimalPom);
        var project = new PackageProject(pomPath, ProjectType.Java, "maven", "Maven", new PackageManagerCapabilities(Remove: true));
        var adapter = new MavenPackageAdapter(new FakeProcessRunner(), await RegistryAsync(mavenInstalled: true));

        var result = await adapter.RemoveAsync(project, "junit:junit");

        Assert.True(result.Success);
        var text = File.ReadAllText(pomPath);
        Assert.DoesNotContain("junit", text);
    }

    [Fact]
    public async Task RemoveAsync_reports_unavailable_when_no_matching_dependency_exists()
    {
        using var dir = new TempDirectory();
        var pomPath = dir.WriteFile("pom.xml", MinimalPom);
        var project = new PackageProject(pomPath, ProjectType.Java, "maven", "Maven", new PackageManagerCapabilities(Remove: true));
        var adapter = new MavenPackageAdapter(new FakeProcessRunner(), await RegistryAsync(mavenInstalled: true));

        var result = await adapter.RemoveAsync(project, "does.not:exist");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task GetSourcesAsync_always_includes_maven_central_and_reads_declared_repositories()
    {
        const string pomWithRepo = """
            <project xmlns="http://maven.apache.org/POM/4.0.0">
              <modelVersion>4.0.0</modelVersion>
              <groupId>com.example</groupId>
              <artifactId>my-app</artifactId>
              <version>1.0-SNAPSHOT</version>
              <repositories>
                <repository>
                  <id>company-internal</id>
                  <url>https://repo.example.com/maven</url>
                </repository>
              </repositories>
            </project>
            """;
        using var dir = new TempDirectory();
        var pomPath = dir.WriteFile("pom.xml", pomWithRepo);
        var project = new PackageProject(pomPath, ProjectType.Java, "maven", "Maven", new PackageManagerCapabilities(ManageSources: true));
        var adapter = new MavenPackageAdapter(new FakeProcessRunner(), new ToolchainRegistry());

        var sources = await adapter.GetSourcesAsync(project);

        Assert.Contains(sources, s => s.IsDefault && s.Location.Contains("repo.maven.apache.org"));
        Assert.Contains(sources, s => s.Name == "company-internal");
        Assert.DoesNotContain(sources, s => s.Location.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MavenCoordinate_Parse_rejects_a_single_segment_id()
    {
        Assert.Null(MavenCoordinate.Parse("justonesegment", null));
    }

    [Fact]
    public void MavenCoordinate_Parse_extracts_an_embedded_version_when_none_is_supplied_separately()
    {
        var coordinate = MavenCoordinate.Parse("com.example:lib:1.2.3", null);

        Assert.NotNull(coordinate);
        Assert.Equal("com.example", coordinate!.Value.GroupId);
        Assert.Equal("lib", coordinate.Value.ArtifactId);
        Assert.Equal("1.2.3", coordinate.Value.Version);
    }
}
