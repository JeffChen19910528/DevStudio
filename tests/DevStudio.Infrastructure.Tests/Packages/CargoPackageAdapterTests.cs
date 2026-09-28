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
/// Unit tests for <see cref="CargoPackageAdapter"/>. Neither <c>cargo</c> nor <c>rustc</c> is
/// installed on the machine these tests were written on, so every command-construction/
/// output-parsing path is exercised against realistic fixture text through
/// <see cref="FakeProcessRunner"/> — never a real Cargo invocation (see ADR-015 for the honest
/// "implemented but not real-environment validated" status this implies).
/// </summary>
public class CargoPackageAdapterTests
{
    private sealed class FixedDetector : IToolchainDetector
    {
        private readonly bool _installed;
        public FixedDetector(bool installed) => _installed = installed;
        public string ToolchainId => WellKnownToolchainIds.Rust;
        public Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_installed
                ? new ToolchainInfo(ToolchainId, "Rust", ToolchainDetectionState.Detected, ExecutablePath: "cargo")
                : new ToolchainInfo(ToolchainId, "Rust", ToolchainDetectionState.NotInstalled));
    }

    private static async Task<ToolchainRegistry> RegistryAsync(bool cargoInstalled)
    {
        var registry = new ToolchainRegistry();
        registry.Register(new FixedDetector(cargoInstalled));
        await registry.RefreshAsync();
        return registry;
    }

    private static ProjectInfo MakeProjectInfo(string rootPath) =>
        new("id", "fixture", rootPath, ProjectType.Rust, Path.Combine(rootPath, "Cargo.toml"), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());

    private const string MinimalCargoToml = """
        [package]
        name = "my-crate"
        version = "0.1.0"

        [dependencies]
        serde = "1.0"
        rand = { version = "0.8", features = ["small_rng"] }

        [dev-dependencies]
        criterion = "0.5"
        """;

    private const string SampleMetadataJson = """
        {
          "packages": [
            { "id": "my-crate 0.1.0 (path+file:///x)", "name": "my-crate", "version": "0.1.0",
              "dependencies": [ { "name": "serde" }, { "name": "rand" } ] },
            { "id": "serde 1.0.210", "name": "serde", "version": "1.0.210", "dependencies": [] },
            { "id": "rand 0.8.5", "name": "rand", "version": "0.8.5", "dependencies": [ { "name": "rand_core" } ] },
            { "id": "rand_core 0.6.4", "name": "rand_core", "version": "0.6.4", "dependencies": [] }
          ],
          "workspace_members": [ "my-crate 0.1.0 (path+file:///x)" ]
        }
        """;

    private const string SampleSearchOutput = "serde = \"1.0.210\"    # A generic serialization/deserialization framework\nserde_json = \"1.0.128\"    # A JSON serialization file format\n... and 397 crates more (use --limit N to see more)\n";

    [Fact]
    public void DetectProject_returns_null_when_project_type_is_not_Rust()
    {
        var adapter = new CargoPackageAdapter(new FakeProcessRunner(), new ToolchainRegistry());
        var project = new ProjectInfo("id", "n", "/x", ProjectType.DotNet, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());

        Assert.Null(adapter.DetectProject(project));
    }

    [Fact]
    public void DetectProject_returns_null_when_no_Cargo_toml_exists()
    {
        using var dir = new TempDirectory();
        var adapter = new CargoPackageAdapter(new FakeProcessRunner(), new ToolchainRegistry());

        Assert.Null(adapter.DetectProject(MakeProjectInfo(dir.Path)));
    }

    [Fact]
    public async Task DetectProject_reports_unavailable_when_cargo_is_not_installed()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("Cargo.toml", MinimalCargoToml);
        var adapter = new CargoPackageAdapter(new FakeProcessRunner(), await RegistryAsync(cargoInstalled: false));

        var project = adapter.DetectProject(MakeProjectInfo(dir.Path));

        Assert.NotNull(project);
        Assert.Equal(PackageManagerCapabilities.None, project!.Capabilities);
        Assert.NotNull(project.UnavailableReason);
    }

    [Fact]
    public async Task DetectProject_reports_full_capabilities_when_cargo_is_installed()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("Cargo.toml", MinimalCargoToml);
        var adapter = new CargoPackageAdapter(new FakeProcessRunner(), await RegistryAsync(cargoInstalled: true));

        var project = adapter.DetectProject(MakeProjectInfo(dir.Path));

        Assert.NotNull(project);
        Assert.True(project!.Capabilities.ListInstalled);
        Assert.True(project.Capabilities.Add);
        Assert.True(project.Capabilities.Remove);
        Assert.True(project.Capabilities.Update);
        Assert.True(project.Capabilities.Restore);
        Assert.True(project.Capabilities.Search);
        Assert.False(project.Capabilities.ListOutdated);
        Assert.False(project.Capabilities.ManageSources);
    }

    [Fact]
    public void CargoTomlReader_reads_direct_dependencies_across_all_three_sections()
    {
        using var dir = new TempDirectory();
        var path = dir.WriteFile("Cargo.toml", MinimalCargoToml);

        var declared = CargoTomlReader.ReadDeclaredDependencies(path);

        Assert.Contains(declared, d => d.PackageId == "serde" && d.VersionRange == "1.0");
        Assert.Contains(declared, d => d.PackageId == "rand" && d.VersionRange == "0.8");
        Assert.Contains(declared, d => d.PackageId == "criterion" && d.VersionRange == "0.5");
    }

    [Fact]
    public void CargoMetadataParser_classifies_direct_versus_transitive_from_root_dependency_edges()
    {
        var references = CargoMetadataParser.ParseInstalled(SampleMetadataJson, "/x/Cargo.toml", WellKnownPackageManagerIds.Cargo);

        Assert.Equal(3, references.Count);
        Assert.Equal(PackageDependencyKind.Direct, references.Single(r => r.PackageId == "serde").Kind);
        Assert.Equal(PackageDependencyKind.Direct, references.Single(r => r.PackageId == "rand").Kind);
        Assert.Equal(PackageDependencyKind.Transitive, references.Single(r => r.PackageId == "rand_core").Kind);
    }

    [Fact]
    public void CargoSearchParser_parses_name_version_and_description_and_skips_the_footer_line()
    {
        var results = CargoSearchParser.Parse(SampleSearchOutput);

        Assert.Equal(2, results.Count);
        Assert.Equal("serde", results[0].PackageId);
        Assert.Equal("1.0.210", results[0].Version);
        Assert.Equal("A generic serialization/deserialization framework", results[0].Description);
    }

    [Fact]
    public async Task ListInstalledAsync_runs_cargo_metadata_and_parses_the_result()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("Cargo.toml", MinimalCargoToml);
        var runner = new FakeProcessRunner();
        runner.SetResult("cargo", 0, SampleMetadataJson);
        var adapter = new CargoPackageAdapter(runner, await RegistryAsync(cargoInstalled: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var installed = await adapter.ListInstalledAsync(project);

        Assert.Equal(3, installed.Count);
        Assert.Contains(runner.Requests, r => r.Arguments.Contains("metadata"));
    }

    [Fact]
    public async Task AddAsync_invokes_cargo_add_with_package_and_dev_flag()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("Cargo.toml", MinimalCargoToml);
        var runner = new FakeProcessRunner();
        runner.SetResult("cargo", 0, "Adding anyhow v1.0.86 to dependencies");
        var adapter = new CargoPackageAdapter(runner, await RegistryAsync(cargoInstalled: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var result = await adapter.AddAsync(project, "anyhow", "1.0.86", prerelease: false, isDevDependency: true);

        Assert.True(result.Success);
        var request = runner.Requests.Single(r => r.ExecutablePath == "cargo");
        Assert.Equal(new[] { "add", "anyhow@1.0.86", "--dev" }, request.Arguments);
    }

    [Fact]
    public async Task RemoveAsync_invokes_cargo_rm()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("Cargo.toml", MinimalCargoToml);
        var runner = new FakeProcessRunner();
        runner.SetResult("cargo", 0, "Removing serde from dependencies");
        var adapter = new CargoPackageAdapter(runner, await RegistryAsync(cargoInstalled: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var result = await adapter.RemoveAsync(project, "serde");

        Assert.True(result.Success);
        var request = runner.Requests.Single(r => r.ExecutablePath == "cargo");
        Assert.Equal(new[] { "rm", "serde" }, request.Arguments);
    }

    [Fact]
    public async Task RestoreAsync_invokes_cargo_fetch()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("Cargo.toml", MinimalCargoToml);
        var runner = new FakeProcessRunner();
        runner.SetResult("cargo", 0, "");
        var adapter = new CargoPackageAdapter(runner, await RegistryAsync(cargoInstalled: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var result = await adapter.RestoreAsync(project);

        Assert.True(result.Success);
        Assert.Equal(new[] { "fetch" }, runner.Requests.Single(r => r.ExecutablePath == "cargo").Arguments);
    }

    [Fact]
    public async Task AddAsync_reports_failure_reason_when_cargo_exits_non_zero()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("Cargo.toml", MinimalCargoToml);
        var runner = new FakeProcessRunner();
        runner.SetResult("cargo", 1, "", "error: no matching package named `does-not-exist` found");
        var adapter = new CargoPackageAdapter(runner, await RegistryAsync(cargoInstalled: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var result = await adapter.AddAsync(project, "does-not-exist", null, false, false);

        Assert.False(result.Success);
        Assert.Contains("no matching package", result.FailureReason);
    }
}
