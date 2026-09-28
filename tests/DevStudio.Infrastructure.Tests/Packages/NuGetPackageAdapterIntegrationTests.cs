using DevStudio.Core.Packages;
using DevStudio.Core.Projects;
using DevStudio.Infrastructure.Packages;
using DevStudio.Infrastructure.Processes;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Packages;

/// <summary>
/// Real, temporary .NET projects created with the real <c>dotnet</c> CLI and driven entirely
/// through <see cref="PackageService"/> → <see cref="NuGetPackageAdapter"/> → the real
/// <c>IProcessRunner</c> → real <c>dotnet add/remove/list/restore package</c>. No fakes — every
/// installed/outdated package result here is the SDK's own real JSON output, parsed by the real
/// parser (verified against this exact SDK's shape before the parser was written). Requires
/// network access to nuget.org; if that is genuinely unavailable in a given environment, the
/// underlying <c>dotnet</c> invocation itself fails with a real, non-zero exit code and these
/// tests fail loudly rather than silently reporting a false pass.
/// </summary>
public class NuGetPackageAdapterIntegrationTests
{
    private static (PackageService Service, ToolchainRegistry Registry) CreateRealServices()
    {
        var processRunner = new ProcessRunner();
        var registry = new ToolchainRegistry();
        registry.Register(new DotNetToolchainDetector(processRunner));
        registry.RefreshAsync().GetAwaiter().GetResult();
        var adapter = new NuGetPackageAdapter(processRunner, registry);
        return (new PackageService(new PackageManagerRegistry(new[] { adapter })), registry);
    }

    private static async Task<string> CreateRealClassLibraryAsync(TempDirectory temp)
    {
        var processRunner = new ProcessRunner();
        var result = await processRunner.RunAsync(new Core.Processes.ProcessStartRequest(
            "dotnet", new[] { "new", "classlib", "-o", temp.Path, "-n", "PkgTestFixture", "--force" }, temp.Path));
        Assert.Equal(0, result.ExitCode);
        return Path.Combine(temp.Path, "PkgTestFixture.csproj");
    }

    private static ProjectInfo MakeProjectInfo(string csprojPath, string rootPath) =>
        new("id", "PkgTestFixture", rootPath, ProjectType.DotNet, csprojPath, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<Core.Toolchains.ProjectCapability>());

    [Fact]
    public async Task DetectProject_reports_full_capabilities_for_a_real_dotnet_project()
    {
        using var temp = new TempDirectory();
        var csproj = await CreateRealClassLibraryAsync(temp);
        var (service, _) = CreateRealServices();

        var applicable = service.DetectApplicableManagers(MakeProjectInfo(csproj, temp.Path));

        var nuget = Assert.Single(applicable);
        Assert.Equal(WellKnownPackageManagerIds.NuGet, nuget.PackageManagerId);
        Assert.True(nuget.Capabilities.Add);
        Assert.True(nuget.Capabilities.ListInstalled);
        Assert.Null(nuget.UnavailableReason);
    }

    [Fact]
    public async Task Add_then_ListInstalled_then_Remove_round_trips_against_a_real_project_file()
    {
        using var temp = new TempDirectory();
        var csproj = await CreateRealClassLibraryAsync(temp);
        var originalContent = await File.ReadAllTextAsync(csproj);
        var (service, _) = CreateRealServices();
        var project = Assert.Single(service.DetectApplicableManagers(MakeProjectInfo(csproj, temp.Path)));

        var addResult = await service.AddAsync(project, "Newtonsoft.Json", "13.0.3", prerelease: false, isDevDependency: false);
        Assert.True(addResult.Success, addResult.FailureReason ?? addResult.RawOutput);
        Assert.Contains(csproj, addResult.ChangedFiles);

        var afterAdd = await File.ReadAllTextAsync(csproj);
        Assert.Contains("Newtonsoft.Json", afterAdd, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(originalContent, afterAdd);

        var installed = await service.ListInstalledAsync(project);
        Assert.Contains(installed, p => string.Equals(p.PackageId, "Newtonsoft.Json", StringComparison.OrdinalIgnoreCase) && p.ResolvedVersion == "13.0.3");

        var removeResult = await service.RemoveAsync(project, "Newtonsoft.Json");
        Assert.True(removeResult.Success, removeResult.FailureReason);

        var afterRemove = await File.ReadAllTextAsync(csproj);
        Assert.DoesNotContain("Newtonsoft.Json", afterRemove, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestoreAsync_succeeds_against_a_real_project_with_no_extra_packages()
    {
        using var temp = new TempDirectory();
        var csproj = await CreateRealClassLibraryAsync(temp);
        var (service, _) = CreateRealServices();
        var project = Assert.Single(service.DetectApplicableManagers(MakeProjectInfo(csproj, temp.Path)));

        var result = await service.RestoreAsync(project);

        Assert.True(result.Success, result.FailureReason);
    }

    [Fact]
    public async Task SearchAsync_returns_real_results_from_nuget_org()
    {
        using var temp = new TempDirectory();
        var csproj = await CreateRealClassLibraryAsync(temp);
        var (service, _) = CreateRealServices();
        var project = Assert.Single(service.DetectApplicableManagers(MakeProjectInfo(csproj, temp.Path)));

        var results = await service.SearchAsync(project, "Newtonsoft.Json", includePrerelease: false);

        Assert.Contains(results, r => string.Equals(r.PackageId, "Newtonsoft.Json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListOutdatedAsync_reports_a_real_outdated_package()
    {
        using var temp = new TempDirectory();
        var csproj = await CreateRealClassLibraryAsync(temp);
        var (service, _) = CreateRealServices();
        var project = Assert.Single(service.DetectApplicableManagers(MakeProjectInfo(csproj, temp.Path)));

        // A deliberately old, real, stable version so a newer one genuinely exists on nuget.org.
        var addResult = await service.AddAsync(project, "Newtonsoft.Json", "9.0.1", prerelease: false, isDevDependency: false);
        Assert.True(addResult.Success, addResult.FailureReason);

        var outdated = await service.ListOutdatedAsync(project);

        Assert.Contains(outdated, p => string.Equals(p.PackageId, "Newtonsoft.Json", StringComparison.OrdinalIgnoreCase) && p.IsOutdated);
    }
}
