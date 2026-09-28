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
/// Unit tests for <see cref="GoModulePackageAdapter"/>. <c>go</c> is not installed on the machine
/// these tests were written on, so every command-construction/output-parsing path is exercised
/// against realistic fixture text through <see cref="FakeProcessRunner"/> — never a real Go
/// invocation (see ADR-015 for the honest "implemented but not real-environment validated" status
/// this implies).
/// </summary>
public class GoModulePackageAdapterTests
{
    private sealed class FixedDetector : IToolchainDetector
    {
        private readonly bool _installed;
        public FixedDetector(bool installed) => _installed = installed;
        public string ToolchainId => WellKnownToolchainIds.Go;
        public Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_installed
                ? new ToolchainInfo(ToolchainId, "Go", ToolchainDetectionState.Detected, ExecutablePath: "go")
                : new ToolchainInfo(ToolchainId, "Go", ToolchainDetectionState.NotInstalled));
    }

    private static async Task<ToolchainRegistry> RegistryAsync(bool goInstalled)
    {
        var registry = new ToolchainRegistry();
        registry.Register(new FixedDetector(goInstalled));
        await registry.RefreshAsync();
        return registry;
    }

    private static ProjectInfo MakeProjectInfo(string rootPath) =>
        new("id", "fixture", rootPath, ProjectType.Go, Path.Combine(rootPath, "go.mod"), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());

    private const string MinimalGoMod = """
        module example.com/my-module

        go 1.22

        require github.com/direct/pkg v1.2.3

        require (
        	github.com/another/direct v0.9.0
        	github.com/indirect/pkg v2.0.0 // indirect
        )
        """;

    private const string SampleGoListJsonStream = """
        {"Path":"example.com/my-module","Main":true,"Dir":"/x"}
        {"Path":"github.com/direct/pkg","Version":"v1.2.3"}
        {"Path":"github.com/another/direct","Version":"v0.9.0"}
        {"Path":"github.com/indirect/pkg","Version":"v2.0.0","Indirect":true}
        """;

    [Fact]
    public void DetectProject_returns_null_when_project_type_is_not_Go()
    {
        var adapter = new GoModulePackageAdapter(new FakeProcessRunner(), new ToolchainRegistry());
        var project = new ProjectInfo("id", "n", "/x", ProjectType.DotNet, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());

        Assert.Null(adapter.DetectProject(project));
    }

    [Fact]
    public void DetectProject_returns_null_when_no_go_mod_exists()
    {
        using var dir = new TempDirectory();
        var adapter = new GoModulePackageAdapter(new FakeProcessRunner(), new ToolchainRegistry());

        Assert.Null(adapter.DetectProject(MakeProjectInfo(dir.Path)));
    }

    [Fact]
    public async Task DetectProject_reports_unavailable_when_go_is_not_installed()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("go.mod", MinimalGoMod);
        var adapter = new GoModulePackageAdapter(new FakeProcessRunner(), await RegistryAsync(goInstalled: false));

        var project = adapter.DetectProject(MakeProjectInfo(dir.Path));

        Assert.NotNull(project);
        Assert.Equal(PackageManagerCapabilities.None, project!.Capabilities);
        Assert.NotNull(project.UnavailableReason);
    }

    [Fact]
    public async Task DetectProject_reports_capabilities_without_search_when_go_is_installed()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("go.mod", MinimalGoMod);
        var adapter = new GoModulePackageAdapter(new FakeProcessRunner(), await RegistryAsync(goInstalled: true));

        var project = adapter.DetectProject(MakeProjectInfo(dir.Path));

        Assert.NotNull(project);
        Assert.True(project!.Capabilities.ListInstalled);
        Assert.True(project.Capabilities.Add);
        Assert.True(project.Capabilities.Remove);
        Assert.True(project.Capabilities.Update);
        Assert.True(project.Capabilities.Restore);
        Assert.False(project.Capabilities.Search); // No reliable Go module search mechanism — never claimed.
        Assert.False(project.Capabilities.ListOutdated);
    }

    [Fact]
    public void GoModReader_reads_single_line_and_block_requires_distinguishing_indirect()
    {
        using var dir = new TempDirectory();
        var path = dir.WriteFile("go.mod", MinimalGoMod);

        var required = GoModReader.ReadRequiredModules(path);

        Assert.Equal(3, required.Count);
        Assert.Contains(required, m => m.ModulePath == "github.com/direct/pkg" && m.Version == "v1.2.3" && !m.Indirect);
        Assert.Contains(required, m => m.ModulePath == "github.com/another/direct" && !m.Indirect);
        Assert.Contains(required, m => m.ModulePath == "github.com/indirect/pkg" && m.Indirect);
    }

    [Fact]
    public void GoListModuleParser_excludes_the_main_module_and_classifies_indirect_as_transitive()
    {
        var references = GoListModuleParser.Parse(SampleGoListJsonStream, "/x/go.mod", WellKnownPackageManagerIds.GoModules);

        Assert.Equal(3, references.Count);
        Assert.DoesNotContain(references, r => r.PackageId == "example.com/my-module");
        Assert.Equal(PackageDependencyKind.Direct, references.Single(r => r.PackageId == "github.com/direct/pkg").Kind);
        Assert.Equal(PackageDependencyKind.Transitive, references.Single(r => r.PackageId == "github.com/indirect/pkg").Kind);
    }

    [Fact]
    public async Task ListInstalledAsync_runs_go_list_m_json_all_and_parses_the_result()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("go.mod", MinimalGoMod);
        var runner = new FakeProcessRunner();
        runner.SetResult("go", 0, SampleGoListJsonStream);
        var adapter = new GoModulePackageAdapter(runner, await RegistryAsync(goInstalled: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var installed = await adapter.ListInstalledAsync(project);

        Assert.Equal(3, installed.Count);
        Assert.Equal(new[] { "list", "-m", "-json", "all" }, runner.Requests.Single(r => r.ExecutablePath == "go").Arguments);
    }

    [Fact]
    public async Task AddAsync_invokes_go_get_with_module_at_version()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("go.mod", MinimalGoMod);
        var runner = new FakeProcessRunner();
        runner.SetResult("go", 0, "go: added github.com/pkg/errors v0.9.1");
        var adapter = new GoModulePackageAdapter(runner, await RegistryAsync(goInstalled: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var result = await adapter.AddAsync(project, "github.com/pkg/errors", "v0.9.1", false, false);

        Assert.True(result.Success);
        Assert.Equal(new[] { "get", "github.com/pkg/errors@v0.9.1" }, runner.Requests.Single(r => r.ExecutablePath == "go").Arguments);
    }

    [Fact]
    public async Task RemoveAsync_runs_mod_edit_droprequire_then_mod_tidy()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("go.mod", MinimalGoMod);
        var runner = new FakeProcessRunner();
        runner.SetResult("go", 0, "");
        var adapter = new GoModulePackageAdapter(runner, await RegistryAsync(goInstalled: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var result = await adapter.RemoveAsync(project, "github.com/direct/pkg");

        Assert.True(result.Success);
        Assert.Equal(2, runner.Requests.Count(r => r.ExecutablePath == "go"));
        Assert.Equal(new[] { "mod", "edit", "-droprequire", "github.com/direct/pkg" }, runner.Requests[0].Arguments);
        Assert.Equal(new[] { "mod", "tidy" }, runner.Requests[1].Arguments);
    }

    [Fact]
    public async Task RemoveAsync_reports_failure_when_the_edit_step_fails_and_never_runs_tidy()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("go.mod", MinimalGoMod);
        var runner = new FakeProcessRunner();
        runner.SetResult("go", 1, "", "go: no such module");
        var adapter = new GoModulePackageAdapter(runner, await RegistryAsync(goInstalled: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var result = await adapter.RemoveAsync(project, "does.not/exist");

        Assert.False(result.Success);
        Assert.Single(runner.Requests, r => r.ExecutablePath == "go");
    }

    [Fact]
    public async Task RestoreAsync_invokes_go_mod_download()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("go.mod", MinimalGoMod);
        var runner = new FakeProcessRunner();
        runner.SetResult("go", 0, "");
        var adapter = new GoModulePackageAdapter(runner, await RegistryAsync(goInstalled: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var result = await adapter.RestoreAsync(project);

        Assert.True(result.Success);
        Assert.Equal(new[] { "mod", "download" }, runner.Requests.Single(r => r.ExecutablePath == "go").Arguments);
    }
}
