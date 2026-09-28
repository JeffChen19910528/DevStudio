using DevStudio.Core.Packages;
using DevStudio.Core.Projects;
using DevStudio.Infrastructure.Packages;
using DevStudio.Infrastructure.Processes;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Packages;

/// <summary>
/// Real, temporary Python projects driven entirely through <see cref="PackageService"/> → <see
/// cref="PythonPackageAdapter"/> → the real <c>IProcessRunner</c> → real
/// <c>python -m pip install/uninstall/list</c>. Every test creates its own real, throwaway
/// virtual environment (<c>python -m venv</c>) under the temp project root first — this is
/// deliberate: it proves <see cref="PythonPackageAdapter"/>'s real venv-resolution behavior
/// (SKILL.md's "installing into the wrong interpreter is the single most common real pip
/// mistake") and, just as importantly, never mutates this machine's actual global Python
/// environment as a side effect of running the test suite. Requires network access to PyPI.
/// </summary>
public class PythonPackageAdapterIntegrationTests
{
    private static (PackageService Service, ToolchainRegistry Registry) CreateRealServices()
    {
        var processRunner = new ProcessRunner();
        var registry = new ToolchainRegistry();
        registry.Register(new PythonToolchainDetector(processRunner));
        registry.RefreshAsync().GetAwaiter().GetResult();
        var adapter = new PythonPackageAdapter(processRunner, registry);
        return (new PackageService(new PackageManagerRegistry(new[] { adapter })), registry);
    }

    private static async Task CreateRealVenvAsync(TempDirectory temp)
    {
        var processRunner = new ProcessRunner();
        var pythonExecutable = OperatingSystem.IsWindows() ? "python" : "python3";
        var result = await processRunner.RunAsync(new Core.Processes.ProcessStartRequest(pythonExecutable, new[] { "-m", "venv", ".venv" }, temp.Path));
        Assert.Equal(0, result.ExitCode);
    }

    private static ProjectInfo MakeProjectInfo(string rootPath) =>
        new("id", "pkgtestfixture", rootPath, ProjectType.Python, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<Core.Toolchains.ProjectCapability>());

    [Fact]
    public async Task DetectProject_with_only_requirements_txt_reports_full_mutation_capabilities()
    {
        using var temp = new TempDirectory();
        await CreateRealVenvAsync(temp);
        temp.WriteFile("requirements.txt", string.Empty);
        var (service, _) = CreateRealServices();

        var project = Assert.Single(service.DetectApplicableManagers(MakeProjectInfo(temp.Path)));

        Assert.Equal(WellKnownPackageManagerIds.Pip, project.PackageManagerId);
        Assert.True(project.Capabilities.Add);
        Assert.True(project.Capabilities.Restore);
        Assert.False(project.Capabilities.Search); // pip has no real search API this phase — never faked.
        Assert.Null(project.UnavailableReason);
    }

    [Fact]
    public async Task A_project_with_a_poetry_lock_reports_inspection_only_capabilities()
    {
        using var temp = new TempDirectory();
        await CreateRealVenvAsync(temp);
        temp.WriteFile("poetry.lock", "# fake poetry lock for detection purposes\n");
        temp.WriteFile("pyproject.toml", "[tool.poetry]\nname = \"fixture\"\n");
        var (service, _) = CreateRealServices();

        var project = Assert.Single(service.DetectApplicableManagers(MakeProjectInfo(temp.Path)));

        Assert.True(project.Capabilities.ListInstalled);
        Assert.False(project.Capabilities.Add);
        Assert.False(project.Capabilities.Remove);
        Assert.False(project.Capabilities.Restore);
        Assert.NotNull(project.UnavailableReason);
        Assert.Contains("Poetry", project.UnavailableReason);
    }

    [Fact]
    public async Task Add_installs_into_the_projects_own_venv_and_appends_to_requirements_txt()
    {
        using var temp = new TempDirectory();
        await CreateRealVenvAsync(temp);
        var requirementsPath = temp.WriteFile("requirements.txt", string.Empty);
        var (service, _) = CreateRealServices();
        var project = Assert.Single(service.DetectApplicableManagers(MakeProjectInfo(temp.Path)));

        var addResult = await service.AddAsync(project, "six", "1.16.0", prerelease: false, isDevDependency: false);
        Assert.True(addResult.Success, addResult.FailureReason ?? addResult.RawOutput);

        var requirementsContent = await File.ReadAllTextAsync(requirementsPath);
        Assert.Contains("six==1.16.0", requirementsContent, StringComparison.Ordinal);

        var installed = await service.ListInstalledAsync(project);
        Assert.Contains(installed, p => string.Equals(p.PackageId, "six", StringComparison.OrdinalIgnoreCase) && p.ResolvedVersion == "1.16.0");

        // Proves the package landed in the project's OWN venv, not this machine's real global
        // interpreter — the whole point of resolving a project-local venv first.
        var sitePackagesMarker = OperatingSystem.IsWindows()
            ? Directory.EnumerateFiles(Path.Combine(temp.Path, ".venv"), "six.py", SearchOption.AllDirectories)
            : Directory.EnumerateFiles(Path.Combine(temp.Path, ".venv"), "six.py", SearchOption.AllDirectories);
        Assert.NotEmpty(sitePackagesMarker);
    }

    [Fact]
    public async Task Remove_uninstalls_and_removes_the_requirements_txt_line()
    {
        using var temp = new TempDirectory();
        await CreateRealVenvAsync(temp);
        var requirementsPath = temp.WriteFile("requirements.txt", string.Empty);
        var (service, _) = CreateRealServices();
        var project = Assert.Single(service.DetectApplicableManagers(MakeProjectInfo(temp.Path)));

        await service.AddAsync(project, "six", "1.16.0", prerelease: false, isDevDependency: false);
        var removeResult = await service.RemoveAsync(project, "six");

        Assert.True(removeResult.Success, removeResult.FailureReason);
        var requirementsContent = await File.ReadAllTextAsync(requirementsPath);
        Assert.DoesNotContain("six", requirementsContent, StringComparison.OrdinalIgnoreCase);

        var installed = await service.ListInstalledAsync(project);
        Assert.DoesNotContain(installed, p => string.Equals(p.PackageId, "six", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RestoreAsync_installs_everything_declared_in_a_real_requirements_txt()
    {
        using var temp = new TempDirectory();
        await CreateRealVenvAsync(temp);
        temp.WriteFile("requirements.txt", "six==1.16.0\n");
        var (service, _) = CreateRealServices();
        var project = Assert.Single(service.DetectApplicableManagers(MakeProjectInfo(temp.Path)));

        var result = await service.RestoreAsync(project);

        Assert.True(result.Success, result.FailureReason);
        var installed = await service.ListInstalledAsync(project);
        Assert.Contains(installed, p => string.Equals(p.PackageId, "six", StringComparison.OrdinalIgnoreCase));
    }
}
