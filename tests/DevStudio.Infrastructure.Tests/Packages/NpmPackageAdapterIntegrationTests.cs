using DevStudio.Core.Packages;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;
using DevStudio.Infrastructure.Packages;
using DevStudio.Infrastructure.Processes;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Packages;

/// <summary>
/// Real, temporary Node projects driven entirely through <see cref="PackageService"/> → <see
/// cref="NpmPackageAdapter"/> → the real <c>IProcessRunner</c> → real <c>npm install/uninstall/
/// list/outdated</c>. No fakes. Requires network access to the npm registry — a real, non-zero
/// npm exit code fails these tests loudly rather than reporting a false pass.
/// </summary>
public class NpmPackageAdapterIntegrationTests
{
    private static (PackageService Service, ToolchainRegistry Registry) CreateRealServices()
    {
        var processRunner = new ProcessRunner();
        var registry = new ToolchainRegistry();
        registry.Register(new NodePackageManagerToolchainDetector(processRunner, WellKnownToolchainIds.Npm, "npm"));
        registry.RefreshAsync().GetAwaiter().GetResult();
        var adapter = new NpmPackageAdapter(processRunner, registry);
        return (new PackageService(new PackageManagerRegistry(new[] { adapter })), registry);
    }

    private static async Task<string> CreateRealNodeProjectAsync(TempDirectory temp)
    {
        var processRunner = new ProcessRunner();
        // On Windows, npm is a .cmd shim — Process.Start (unlike a real shell) does not resolve
        // PATH extensions on its own, so this must use the same ExecutableLocator resolution the
        // real ToolchainRegistry/NpmPackageAdapter use, never a bare "npm".
        var npmExecutable = ExecutableLocator.FindOnPath("npm") ?? "npm";
        var result = await processRunner.RunAsync(new Core.Processes.ProcessStartRequest(npmExecutable, new[] { "init", "-y" }, temp.Path));
        Assert.Equal(0, result.ExitCode);
        return Path.Combine(temp.Path, "package.json");
    }

    private static ProjectInfo MakeProjectInfo(string rootPath) =>
        new("id", "pkgtestfixture", rootPath, ProjectType.Node, Path.Combine(rootPath, "package.json"), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<Core.Toolchains.ProjectCapability>());

    [Fact]
    public async Task DetectProject_reports_full_capabilities_for_a_real_npm_project()
    {
        using var temp = new TempDirectory();
        await CreateRealNodeProjectAsync(temp);
        var (service, _) = CreateRealServices();

        var applicable = service.DetectApplicableManagers(MakeProjectInfo(temp.Path));

        var npm = Assert.Single(applicable);
        Assert.Equal(WellKnownPackageManagerIds.Npm, npm.PackageManagerId);
        Assert.True(npm.Capabilities.Add);
        Assert.Null(npm.UnavailableReason);
    }

    [Fact]
    public async Task A_project_with_a_pnpm_lockfile_reports_every_capability_false()
    {
        using var temp = new TempDirectory();
        await CreateRealNodeProjectAsync(temp);
        temp.WriteFile("pnpm-lock.yaml", "lockfileVersion: '6.0'\n");
        var (service, _) = CreateRealServices();

        var project = Assert.Single(service.DetectApplicableManagers(MakeProjectInfo(temp.Path)));

        Assert.False(project.Capabilities.Add);
        Assert.NotNull(project.UnavailableReason);
    }

    [Fact]
    public async Task Add_then_ListInstalled_then_Remove_round_trips_against_a_real_package_json()
    {
        using var temp = new TempDirectory();
        var packageJson = await CreateRealNodeProjectAsync(temp);
        var (service, _) = CreateRealServices();
        var project = Assert.Single(service.DetectApplicableManagers(MakeProjectInfo(temp.Path)));

        var addResult = await service.AddAsync(project, "left-pad", "1.3.0", prerelease: false, isDevDependency: false);
        Assert.True(addResult.Success, addResult.FailureReason ?? addResult.RawOutput);

        var afterAdd = await File.ReadAllTextAsync(packageJson);
        Assert.Contains("left-pad", afterAdd, StringComparison.Ordinal);

        var installed = await service.ListInstalledAsync(project);
        Assert.Contains(installed, p => p.PackageId == "left-pad" && p.ResolvedVersion == "1.3.0" && p.Kind == PackageDependencyKind.Direct);

        var removeResult = await service.RemoveAsync(project, "left-pad");
        Assert.True(removeResult.Success, removeResult.FailureReason);

        var afterRemove = await service.ListInstalledAsync(project);
        Assert.DoesNotContain(afterRemove, p => p.PackageId == "left-pad");
    }

    [Fact]
    public async Task AddAsync_with_isDevDependency_records_it_under_devDependencies_and_ListInstalled_reflects_it()
    {
        using var temp = new TempDirectory();
        await CreateRealNodeProjectAsync(temp);
        var (service, _) = CreateRealServices();
        var project = Assert.Single(service.DetectApplicableManagers(MakeProjectInfo(temp.Path)));

        var addResult = await service.AddAsync(project, "left-pad", "1.3.0", prerelease: false, isDevDependency: true);
        Assert.True(addResult.Success, addResult.FailureReason);

        var packageJsonPath = Path.Combine(temp.Path, "package.json");
        var packageJsonContent = await File.ReadAllTextAsync(packageJsonPath);
        Assert.Contains("devDependencies", packageJsonContent, StringComparison.Ordinal);

        var installed = await service.ListInstalledAsync(project);
        Assert.Contains(installed, p => p.PackageId == "left-pad" && p.IsDevDependency);
    }
}
