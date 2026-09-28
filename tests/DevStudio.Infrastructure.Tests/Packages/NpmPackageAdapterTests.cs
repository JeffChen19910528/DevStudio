using DevStudio.Core.Packages;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;
using DevStudio.Infrastructure.Packages;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Packages;

/// <summary>Unit tests for <see cref="NpmPackageAdapter"/>'s command construction and its parsing
/// of real, captured <c>npm list/outdated</c> JSON samples — no real process needed for these
/// (that's <see cref="NpmPackageAdapterIntegrationTests"/>).</summary>
public class NpmPackageAdapterTests
{
    private sealed class FixedNpmDetector : IToolchainDetector
    {
        public string ToolchainId => WellKnownToolchainIds.Npm;
        public Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ToolchainInfo(ToolchainId, "npm", ToolchainDetectionState.Detected, ExecutablePath: "npm"));
    }

    private static async Task<ToolchainRegistry> RegistryWithNpmDetectedAsync()
    {
        var registry = new ToolchainRegistry();
        registry.Register(new FixedNpmDetector());
        await registry.RefreshAsync();
        return registry;
    }

    private static TempDirectory MakeProjectDir(string packageJsonContent)
    {
        var temp = new TempDirectory();
        temp.WriteFile("package.json", packageJsonContent);
        return temp;
    }

    private const string RealListJson = """
        {
          "version": "1.0.0",
          "name": "fixture",
          "dependencies": {
            "left-pad": { "version": "1.3.0", "resolved": "https://registry.npmjs.org/left-pad/-/left-pad-1.3.0.tgz" },
            "mocha": { "version": "12.0.2", "dependencies": { "ms": { "version": "2.1.3" } } }
          }
        }
        """;

    private const string RealOutdatedJson = """
        { "left-pad": { "current": "1.3.0", "wanted": "1.3.0", "latest": "1.3.1" } }
        """;

    [Fact]
    public async Task ListInstalledAsync_parses_direct_and_nested_transitive_packages_from_real_JSON_shape()
    {
        using var dir = MakeProjectDir("""{ "name": "fixture", "dependencies": { "left-pad": "1.3.0" } }""");
        var runner = new FakeProcessRunner();
        runner.SetResult("npm", 0, RealListJson);
        var adapter = new NpmPackageAdapter(runner, await RegistryWithNpmDetectedAsync());
        var project = new PackageProject(Path.Combine(dir.Path, "package.json"), ProjectType.Node, WellKnownPackageManagerIds.Npm, "npm", new PackageManagerCapabilities(ListInstalled: true));

        var result = await adapter.ListInstalledAsync(project);

        Assert.Equal(3, result.Count);
        var leftPad = Assert.Single(result, p => p.PackageId == "left-pad");
        Assert.Equal(PackageDependencyKind.Direct, leftPad.Kind);
        var ms = Assert.Single(result, p => p.PackageId == "ms");
        Assert.Equal(PackageDependencyKind.Transitive, ms.Kind);
    }

    [Fact]
    public async Task ListInstalledAsync_marks_devDependencies_from_the_real_package_json_not_the_npm_list_tree()
    {
        using var dir = MakeProjectDir("""{ "name": "fixture", "devDependencies": { "mocha": "12.0.2" } }""");
        var runner = new FakeProcessRunner();
        runner.SetResult("npm", 0, RealListJson);
        var adapter = new NpmPackageAdapter(runner, await RegistryWithNpmDetectedAsync());
        var project = new PackageProject(Path.Combine(dir.Path, "package.json"), ProjectType.Node, WellKnownPackageManagerIds.Npm, "npm", new PackageManagerCapabilities(ListInstalled: true));

        var result = await adapter.ListInstalledAsync(project);

        var mocha = Assert.Single(result, p => p.PackageId == "mocha");
        Assert.True(mocha.IsDevDependency);
        var leftPad = Assert.Single(result, p => p.PackageId == "left-pad");
        Assert.False(leftPad.IsDevDependency);
    }

    [Fact]
    public async Task ListOutdatedAsync_parses_current_and_latest_from_real_JSON_shape()
    {
        using var dir = MakeProjectDir("""{ "name": "fixture" }""");
        var runner = new FakeProcessRunner();
        runner.SetResult("npm", 1, RealOutdatedJson); // npm exits 1 when packages ARE outdated — a real, documented quirk.
        var adapter = new NpmPackageAdapter(runner, await RegistryWithNpmDetectedAsync());
        var project = new PackageProject(Path.Combine(dir.Path, "package.json"), ProjectType.Node, WellKnownPackageManagerIds.Npm, "npm", new PackageManagerCapabilities(ListOutdated: true));

        var result = await adapter.ListOutdatedAsync(project);

        var entry = Assert.Single(result);
        Assert.Equal("left-pad", entry.PackageId);
        Assert.Equal("1.3.1", entry.LatestVersion);
    }

    [Fact]
    public void DetectProject_with_a_pnpm_lockfile_reports_every_capability_false()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("package.json", """{ "name": "fixture" }""");
        dir.WriteFile("pnpm-lock.yaml", "lockfileVersion: '6.0'\n");
        var runner = new FakeProcessRunner();
        var adapter = new NpmPackageAdapter(runner, new ToolchainRegistry());
        var project = new ProjectInfo("id", "fixture", dir.Path, ProjectType.Node, Path.Combine(dir.Path, "package.json"), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());

        var detected = adapter.DetectProject(project);

        Assert.NotNull(detected);
        Assert.Equal(PackageManagerCapabilities.None, detected!.Capabilities);
        Assert.Contains("pnpm", detected.UnavailableReason);
    }

    [Fact]
    public void DetectProject_returns_null_for_a_non_Node_project()
    {
        var runner = new FakeProcessRunner();
        var adapter = new NpmPackageAdapter(runner, new ToolchainRegistry());
        var project = new ProjectInfo("id", "a", @"C:\proj", ProjectType.DotNet, @"C:\proj\a.csproj", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());

        Assert.Null(adapter.DetectProject(project));
    }
}
