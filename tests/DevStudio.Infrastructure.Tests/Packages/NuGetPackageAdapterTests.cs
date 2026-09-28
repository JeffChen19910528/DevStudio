using DevStudio.Core.Packages;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;
using DevStudio.Infrastructure.Packages;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Packages;

/// <summary>Unit tests for <see cref="NuGetPackageAdapter"/>'s command construction and its
/// parsing of real, captured <c>dotnet list/package search</c> JSON samples (captured from a real
/// SDK invocation before this parser was written) — no real process needed for these (that's
/// <see cref="NuGetPackageAdapterIntegrationTests"/>).</summary>
public class NuGetPackageAdapterTests
{
    private sealed class FixedDotNetDetector : IToolchainDetector
    {
        public string ToolchainId => WellKnownToolchainIds.DotNet;
        public Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ToolchainInfo(ToolchainId, ".NET SDK", ToolchainDetectionState.Detected, ExecutablePath: "dotnet"));
    }

    private static async Task<ToolchainRegistry> RegistryWithDotNetDetectedAsync()
    {
        var registry = new ToolchainRegistry();
        registry.Register(new FixedDotNetDetector());
        await registry.RefreshAsync();
        return registry;
    }

    private static PackageProject MakeProject() =>
        new(@"C:\proj\a.csproj", ProjectType.DotNet, WellKnownPackageManagerIds.NuGet, "NuGet", new PackageManagerCapabilities(ListInstalled: true, ListOutdated: true, Add: true, Remove: true));

    private const string RealListPackageJson = """
        {
          "version": 1,
          "parameters": "--include-transitive",
          "projects": [
            {
              "path": "C:/proj/a.csproj",
              "frameworks": [
                {
                  "framework": "net10.0",
                  "topLevelPackages": [
                    { "id": "Newtonsoft.Json", "requestedVersion": "13.0.3", "resolvedVersion": "13.0.3" }
                  ],
                  "transitivePackages": [
                    { "id": "System.Text.Json", "resolvedVersion": "8.0.0" }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private const string RealOutdatedJson = """
        {
          "version": 1,
          "parameters": "--outdated",
          "projects": [
            {
              "path": "C:/proj/a.csproj",
              "frameworks": [
                {
                  "framework": "net10.0",
                  "topLevelPackages": [
                    { "id": "Newtonsoft.Json", "requestedVersion": "9.0.1", "resolvedVersion": "9.0.1", "latestVersion": "13.0.4" }
                  ]
                }
              ]
            }
          ]
        }
        """;

    [Fact]
    public async Task ListInstalledAsync_parses_direct_and_transitive_packages_from_real_JSON_shape()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("dotnet", 0, RealListPackageJson);
        var adapter = new NuGetPackageAdapter(runner, await RegistryWithDotNetDetectedAsync());

        var result = await adapter.ListInstalledAsync(MakeProject());

        Assert.Equal(2, result.Count);
        var direct = Assert.Single(result, p => p.PackageId == "Newtonsoft.Json");
        Assert.Equal(PackageDependencyKind.Direct, direct.Kind);
        Assert.Equal("13.0.3", direct.ResolvedVersion);
        var transitive = Assert.Single(result, p => p.PackageId == "System.Text.Json");
        Assert.Equal(PackageDependencyKind.Transitive, transitive.Kind);
    }

    [Fact]
    public async Task ListInstalledAsync_sends_include_transitive_and_format_json_arguments()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("dotnet", 0, RealListPackageJson);
        var adapter = new NuGetPackageAdapter(runner, await RegistryWithDotNetDetectedAsync());

        await adapter.ListInstalledAsync(MakeProject());

        var sent = Assert.Single(runner.Requests);
        Assert.Equal(new[] { "list", @"C:\proj\a.csproj", "package", "--include-transitive", "--format", "json" }, sent.Arguments);
    }

    [Fact]
    public async Task ListOutdatedAsync_parses_the_latest_version_from_real_JSON_shape()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("dotnet", 0, RealOutdatedJson);
        var adapter = new NuGetPackageAdapter(runner, await RegistryWithDotNetDetectedAsync());

        var result = await adapter.ListOutdatedAsync(MakeProject());

        var entry = Assert.Single(result);
        Assert.Equal("Newtonsoft.Json", entry.PackageId);
        Assert.Equal("13.0.4", entry.LatestVersion);
        Assert.True(entry.IsOutdated);
    }

    [Fact]
    public async Task AddAsync_includes_version_and_prerelease_flags_when_given()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("dotnet", 0, "");
        var adapter = new NuGetPackageAdapter(runner, await RegistryWithDotNetDetectedAsync());

        await adapter.AddAsync(MakeProject(), "Newtonsoft.Json", "13.0.3", prerelease: true, isDevDependency: false);

        var sent = Assert.Single(runner.Requests);
        Assert.Equal(new[] { "add", @"C:\proj\a.csproj", "package", "Newtonsoft.Json", "--version", "13.0.3", "--prerelease" }, sent.Arguments);
    }

    [Fact]
    public async Task RemoveAsync_a_failing_command_reports_a_structured_failure_not_an_exception()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("dotnet", 1, "", "error NU1101: Unable to find package Nonexistent.Package");
        var adapter = new NuGetPackageAdapter(runner, await RegistryWithDotNetDetectedAsync());

        var result = await adapter.RemoveAsync(MakeProject(), "Nonexistent.Package");

        Assert.False(result.Success);
        Assert.Contains("NU1101", result.FailureReason);
        Assert.Empty(result.ChangedFiles);
    }

    [Fact]
    public async Task DetectProject_reports_Unavailable_when_the_toolchain_registry_has_no_dotnet()
    {
        var runner = new FakeProcessRunner();
        var registry = new ToolchainRegistry(); // no detectors registered — dotnet is "not installed"
        await registry.RefreshAsync();
        var adapter = new NuGetPackageAdapter(runner, registry);
        var project = new ProjectInfo("id", "a", @"C:\proj", ProjectType.DotNet, @"C:\proj\a.csproj", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());

        var detected = adapter.DetectProject(project);

        Assert.NotNull(detected);
        Assert.Equal(PackageManagerCapabilities.None, detected!.Capabilities);
        Assert.NotNull(detected.UnavailableReason);
    }
}
