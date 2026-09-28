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
/// Unit tests for <see cref="VcpkgPackageAdapter"/>. <c>vcpkg</c> is not installed on the machine
/// these tests were written on, so every command-construction/output-parsing/manifest-editing path
/// is exercised against realistic fixture text through <see cref="FakeProcessRunner"/> and real
/// temporary manifest files — never a real vcpkg invocation (see ADR-015).
/// </summary>
public class VcpkgPackageAdapterTests
{
    private sealed class FixedDetector : IToolchainDetector
    {
        private readonly bool _installed;
        public FixedDetector(bool installed) => _installed = installed;
        public string ToolchainId => WellKnownToolchainIds.Vcpkg;
        public Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_installed
                ? new ToolchainInfo(ToolchainId, "vcpkg", ToolchainDetectionState.Detected, ExecutablePath: "vcpkg")
                : new ToolchainInfo(ToolchainId, "vcpkg", ToolchainDetectionState.NotInstalled));
    }

    private static async Task<ToolchainRegistry> RegistryAsync(bool installed)
    {
        var registry = new ToolchainRegistry();
        registry.Register(new FixedDetector(installed));
        await registry.RefreshAsync();
        return registry;
    }

    private static ProjectInfo MakeProjectInfo(string rootPath) =>
        new("id", "fixture", rootPath, ProjectType.CMake, Path.Combine(rootPath, "CMakeLists.txt"), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());

    private const string MinimalManifest = """
        {
          "name": "myproject",
          "version": "1.0.0",
          "dependencies": [
            "fmt",
            { "name": "boost-algorithm", "version>=": "1.83.0" },
            { "name": "curl", "features": [ "ssl" ] }
          ],
          "builtin-baseline": "abc123"
        }
        """;

    private const string SampleListOutput =
        "boost-algorithm:x64-windows                     1.83.0                     Boost algorithm module\n" +
        "curl:x64-windows                                 8.6.0                      A library for transferring data\n" +
        "fmt:x64-windows                                  10.2.1                     Formatting library\n" +
        "zlib:x64-windows                                 1.3.1                      A compression library\n";

    [Fact]
    public void DetectProject_returns_null_when_project_type_is_not_CMake()
    {
        var adapter = new VcpkgPackageAdapter(new FakeProcessRunner(), new ToolchainRegistry());
        var project = new ProjectInfo("id", "n", "/x", ProjectType.DotNet, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());

        Assert.Null(adapter.DetectProject(project));
    }

    [Fact]
    public void DetectProject_returns_null_when_no_vcpkg_json_exists()
    {
        using var dir = new TempDirectory();
        var adapter = new VcpkgPackageAdapter(new FakeProcessRunner(), new ToolchainRegistry());

        Assert.Null(adapter.DetectProject(MakeProjectInfo(dir.Path)));
    }

    [Fact]
    public async Task DetectProject_reports_unavailable_when_vcpkg_is_not_installed()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("vcpkg.json", MinimalManifest);
        var adapter = new VcpkgPackageAdapter(new FakeProcessRunner(), await RegistryAsync(installed: false));

        var project = adapter.DetectProject(MakeProjectInfo(dir.Path));

        Assert.NotNull(project);
        Assert.Equal(PackageManagerCapabilities.None, project!.Capabilities);
        Assert.NotNull(project.UnavailableReason);
    }

    [Fact]
    public async Task DetectProject_reports_capabilities_without_Update_when_vcpkg_is_installed()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("vcpkg.json", MinimalManifest);
        var adapter = new VcpkgPackageAdapter(new FakeProcessRunner(), await RegistryAsync(installed: true));

        var project = adapter.DetectProject(MakeProjectInfo(dir.Path));

        Assert.NotNull(project);
        Assert.True(project!.Capabilities.ListInstalled);
        Assert.True(project.Capabilities.Add);
        Assert.True(project.Capabilities.Remove);
        Assert.False(project.Capabilities.Update); // See ADR-015: vcpkg's baseline/overrides model has no safe naive per-package version edit.
        Assert.True(project.Capabilities.Restore);
        Assert.True(project.Capabilities.Search);
        Assert.True(project.Capabilities.ManageSources);
        Assert.False(project.Capabilities.ListOutdated);
    }

    [Fact]
    public void ReadDeclaredDependencies_parses_string_and_object_entries()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("vcpkg.json", MinimalManifest);

        var declared = VcpkgManifestReader.ReadDeclaredDependencies(Path.Combine(dir.Path, "vcpkg.json"));

        Assert.Equal(3, declared.Count);
        Assert.Contains(declared, d => d.PortName == "fmt" && d.VersionConstraint is null);
        Assert.Contains(declared, d => d.PortName == "boost-algorithm" && d.VersionConstraint == "1.83.0");
        Assert.Contains(declared, d => d.PortName == "curl" && d.VersionConstraint is null);
    }

    [Fact]
    public async Task ListInstalledAsync_parses_vcpkg_list_and_classifies_direct_vs_transitive()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("vcpkg.json", MinimalManifest);
        var runner = new FakeProcessRunner();
        runner.SetResult("vcpkg", 0, SampleListOutput);
        var adapter = new VcpkgPackageAdapter(runner, await RegistryAsync(installed: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var installed = await adapter.ListInstalledAsync(project);

        Assert.Equal(4, installed.Count);
        Assert.Equal(PackageDependencyKind.Direct, installed.Single(p => p.PackageId == "fmt").Kind);
        Assert.Equal(PackageDependencyKind.Direct, installed.Single(p => p.PackageId == "curl").Kind);
        Assert.Equal(PackageDependencyKind.Transitive, installed.Single(p => p.PackageId == "zlib").Kind);
        Assert.Contains(runner.Requests, r => r.Arguments.Contains("list"));
    }

    [Fact]
    public async Task AddAsync_appends_a_new_dependency_to_the_manifest_without_disturbing_other_fields()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("vcpkg.json", MinimalManifest);
        var adapter = new VcpkgPackageAdapter(new FakeProcessRunner(), await RegistryAsync(installed: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var result = await adapter.AddAsync(project, "zlib", "1.3.1", false, false);

        Assert.True(result.Success);
        var declared = VcpkgManifestReader.ReadDeclaredDependencies(project.ProjectPath);
        Assert.Contains(declared, d => d.PortName == "zlib" && d.VersionConstraint == "1.3.1");
        Assert.Contains("abc123", File.ReadAllText(project.ProjectPath)); // builtin-baseline preserved untouched
    }

    [Fact]
    public async Task RemoveAsync_removes_a_string_entry()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("vcpkg.json", MinimalManifest);
        var adapter = new VcpkgPackageAdapter(new FakeProcessRunner(), await RegistryAsync(installed: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var result = await adapter.RemoveAsync(project, "fmt");

        Assert.True(result.Success);
        var declared = VcpkgManifestReader.ReadDeclaredDependencies(project.ProjectPath);
        Assert.DoesNotContain(declared, d => d.PortName == "fmt");
    }

    [Fact]
    public async Task RemoveAsync_reports_unavailable_when_no_match_found()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("vcpkg.json", MinimalManifest);
        var adapter = new VcpkgPackageAdapter(new FakeProcessRunner(), await RegistryAsync(installed: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var result = await adapter.RemoveAsync(project, "does-not-exist");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task UpdateAsync_is_always_unavailable()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("vcpkg.json", MinimalManifest);
        var adapter = new VcpkgPackageAdapter(new FakeProcessRunner(), await RegistryAsync(installed: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var result = await adapter.UpdateAsync(project, "fmt", "11.0.0");

        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task RestoreAsync_invokes_vcpkg_install()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("vcpkg.json", MinimalManifest);
        var runner = new FakeProcessRunner();
        runner.SetResult("vcpkg", 0, "");
        var adapter = new VcpkgPackageAdapter(runner, await RegistryAsync(installed: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var result = await adapter.RestoreAsync(project);

        Assert.True(result.Success);
        Assert.Equal(new[] { "install" }, runner.Requests.Single(r => r.ExecutablePath == "vcpkg").Arguments);
    }

    [Fact]
    public async Task GetSourcesAsync_reads_registries_from_vcpkg_configuration_json()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("vcpkg.json", MinimalManifest);
        dir.WriteFile("vcpkg-configuration.json", """
            { "registries": [ { "kind": "git", "repository": "https://github.com/example/registry", "baseline": "deadbeef" } ] }
            """);
        var adapter = new VcpkgPackageAdapter(new FakeProcessRunner(), await RegistryAsync(installed: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var sources = await adapter.GetSourcesAsync(project);

        Assert.Single(sources);
        Assert.Equal("https://github.com/example/registry", sources[0].Location);
    }

    [Fact]
    public void ListParser_ignores_malformed_lines()
    {
        var parsed = VcpkgListParser.Parse("not a valid line\n" + SampleListOutput, "/x/vcpkg.json", "vcpkg", new HashSet<string>());
        Assert.Equal(4, parsed.Count);
    }
}
