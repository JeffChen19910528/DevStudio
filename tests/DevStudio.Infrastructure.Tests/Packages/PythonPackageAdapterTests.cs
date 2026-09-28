using DevStudio.Core.Packages;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;
using DevStudio.Infrastructure.Packages;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Packages;

/// <summary>Unit tests for <see cref="PythonPackageAdapter"/>'s dependency-style detection and its
/// parsing of real, captured <c>pip list</c> JSON samples — no real process needed for these
/// (that's <see cref="PythonPackageAdapterIntegrationTests"/>).</summary>
public class PythonPackageAdapterTests
{
    private sealed class FixedPythonDetector : IToolchainDetector
    {
        public string ToolchainId => WellKnownToolchainIds.Python;
        public Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ToolchainInfo(ToolchainId, "Python", ToolchainDetectionState.Detected, ExecutablePath: "python"));
    }

    private static async Task<ToolchainRegistry> RegistryWithPythonDetectedAsync()
    {
        var registry = new ToolchainRegistry();
        registry.Register(new FixedPythonDetector());
        await registry.RefreshAsync();
        return registry;
    }

    private static ProjectInfo MakeProjectInfo(string rootPath) =>
        new("id", "fixture", rootPath, ProjectType.Python, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());

    [Theory]
    [InlineData("poetry.lock", "", PythonDependencyStyle.Poetry)]
    [InlineData("uv.lock", "", PythonDependencyStyle.Uv)]
    [InlineData("Pipfile", "", PythonDependencyStyle.Pipenv)]
    [InlineData("requirements.txt", "", PythonDependencyStyle.RequirementsTxt)]
    public void DetectStyle_recognizes_each_real_manifest_file(string fileName, string content, PythonDependencyStyle expected)
    {
        using var dir = new TempDirectory();
        dir.WriteFile(fileName, content);

        Assert.Equal(expected, PythonPackageAdapter.DetectStyle(dir.Path));
    }

    [Fact]
    public void DetectStyle_recognizes_a_pyproject_toml_tool_poetry_section_without_a_lockfile()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("pyproject.toml", "[tool.poetry]\nname = \"fixture\"\n");

        Assert.Equal(PythonDependencyStyle.Poetry, PythonPackageAdapter.DetectStyle(dir.Path));
    }

    [Fact]
    public void DetectStyle_with_no_recognized_manifest_at_all_is_PlainPip()
    {
        using var dir = new TempDirectory();

        Assert.Equal(PythonDependencyStyle.PlainPip, PythonPackageAdapter.DetectStyle(dir.Path));
    }

    [Fact]
    public async Task DetectProject_for_a_PlainPip_project_grants_full_mutation_capabilities_except_Restore()
    {
        using var dir = new TempDirectory();
        var adapter = new PythonPackageAdapter(new FakeProcessRunner(), await RegistryWithPythonDetectedAsync());

        var detected = adapter.DetectProject(MakeProjectInfo(dir.Path));

        Assert.NotNull(detected);
        Assert.True(detected!.Capabilities.Add);
        Assert.False(detected.Capabilities.Restore); // nothing declared to restore from without requirements.txt
        Assert.Null(detected.UnavailableReason);
    }

    [Fact]
    public async Task DetectProject_for_a_Poetry_project_grants_inspection_but_not_mutation()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("poetry.lock", "");
        var adapter = new PythonPackageAdapter(new FakeProcessRunner(), await RegistryWithPythonDetectedAsync());

        var detected = adapter.DetectProject(MakeProjectInfo(dir.Path));

        Assert.NotNull(detected);
        Assert.True(detected!.Capabilities.ListInstalled);
        Assert.False(detected.Capabilities.Add);
        Assert.False(detected.Capabilities.Remove);
        Assert.False(detected.Capabilities.Update);
        Assert.NotNull(detected.UnavailableReason);
    }

    private const string RealPipListJson = """
        [{"name": "requests", "version": "2.31.0"}, {"name": "six", "version": "1.16.0"}]
        """;

    [Fact]
    public async Task ListInstalledAsync_parses_real_pip_list_json_and_marks_declared_packages_Direct()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("requirements.txt", "requests==2.31.0\n");
        var runner = new FakeProcessRunner();
        runner.SetResult("python", 0, RealPipListJson);
        var adapter = new PythonPackageAdapter(runner, await RegistryWithPythonDetectedAsync());
        var project = new PackageProject(Path.Combine(dir.Path, "requirements.txt"), ProjectType.Python, WellKnownPackageManagerIds.Pip, "pip", new PackageManagerCapabilities(ListInstalled: true));

        var result = await adapter.ListInstalledAsync(project);

        var requests = Assert.Single(result, p => p.PackageId == "requests");
        Assert.Equal(PackageDependencyKind.Direct, requests.Kind);
        var six = Assert.Single(result, p => p.PackageId == "six");
        Assert.Equal(PackageDependencyKind.Transitive, six.Kind); // present in the environment but not declared in requirements.txt
    }

    [Fact]
    public async Task ListDependenciesAsync_parses_a_real_requirements_txt_without_running_any_process()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("requirements.txt", "# a comment\nrequests>=2.0,<3.0\nsix==1.16.0\n\n-e ./local-package\n");
        var runner = new FakeProcessRunner(); // deliberately given no canned result — must not be invoked
        var adapter = new PythonPackageAdapter(runner, await RegistryWithPythonDetectedAsync());
        var project = new PackageProject(Path.Combine(dir.Path, "requirements.txt"), ProjectType.Python, WellKnownPackageManagerIds.Pip, "pip", new PackageManagerCapabilities(ListDependencies: true));

        var result = await adapter.ListDependenciesAsync(project);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, d => d.PackageId == "requests" && d.VersionRange == ">=2.0,<3.0");
        Assert.Contains(result, d => d.PackageId == "six" && d.VersionRange == "==1.16.0");
        Assert.Empty(runner.Requests);
    }
}
