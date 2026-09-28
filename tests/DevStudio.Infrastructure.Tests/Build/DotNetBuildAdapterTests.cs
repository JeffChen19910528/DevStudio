using DevStudio.Core.Build;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;
using DevStudio.Core.Workspace;
using DevStudio.Infrastructure.Build;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Build;

public class DotNetBuildAdapterTests
{
    private static ToolchainRegistry RegistryWithDotNetDetected()
    {
        var registry = new ToolchainRegistry();
        registry.Register(new StaticDotNetDetector(ToolchainDetectionState.Detected));
        return registry;
    }

    private sealed class StaticDotNetDetector : IToolchainDetector
    {
        private readonly ToolchainDetectionState _state;
        public StaticDotNetDetector(ToolchainDetectionState state) => _state = state;
        public string ToolchainId => WellKnownToolchainIds.DotNet;
        public Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ToolchainInfo(ToolchainId, "dotnet", _state, ExecutablePath: "dotnet"));
    }

    private static BuildTarget ProjectTarget(string filePath, string workingDirectory) =>
        new(BuildTargetKind.Project, "App", filePath, workingDirectory, ProjectType.DotNet);

    private static BuildTarget SolutionTarget(string filePath, string workingDirectory) =>
        new(BuildTargetKind.Solution, "MySolution", filePath, workingDirectory, ProjectType.DotNet);

    [Fact]
    public async Task Build_passes_the_project_path_and_configuration_as_separate_arguments()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("dotnet", 0, "Build succeeded.");
        var registry = RegistryWithDotNetDetected();
        await registry.RefreshAsync();
        var adapter = new DotNetBuildAdapter(runner, registry);

        var request = new BuildRequest(ProjectTarget(@"C:\repo\App.csproj", @"C:\repo"), BuildConfiguration.Debug, BuildOperation.Build);
        await adapter.ExecuteAsync(request);

        var sent = Assert.Single(runner.Requests);
        Assert.Equal("dotnet", sent.ExecutablePath);
        Assert.Equal(new[] { "build", @"C:\repo\App.csproj", "-c", "Debug" }, sent.Arguments);
        Assert.Equal(@"C:\repo", sent.WorkingDirectory);
    }

    [Fact]
    public async Task Build_uses_the_Release_configuration_when_selected()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("dotnet", 0, "Build succeeded.");
        var registry = RegistryWithDotNetDetected();
        await registry.RefreshAsync();
        var adapter = new DotNetBuildAdapter(runner, registry);

        var request = new BuildRequest(ProjectTarget(@"C:\repo\App.csproj", @"C:\repo"), BuildConfiguration.Release, BuildOperation.Build);
        await adapter.ExecuteAsync(request);

        var sent = Assert.Single(runner.Requests);
        Assert.Contains("Release", sent.Arguments);
        Assert.DoesNotContain("Debug", sent.Arguments);
    }

    [Fact]
    public async Task Restore_omits_the_configuration_flag()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("dotnet", 0, "Restore complete.");
        var registry = RegistryWithDotNetDetected();
        await registry.RefreshAsync();
        var adapter = new DotNetBuildAdapter(runner, registry);

        var request = new BuildRequest(ProjectTarget(@"C:\repo\App.csproj", @"C:\repo"), BuildConfiguration.Debug, BuildOperation.Restore);
        await adapter.ExecuteAsync(request);

        var sent = Assert.Single(runner.Requests);
        Assert.Equal(new[] { "restore", @"C:\repo\App.csproj" }, sent.Arguments);
    }

    [Fact]
    public async Task Clean_includes_the_configuration_flag()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("dotnet", 0, "Clean complete.");
        var registry = RegistryWithDotNetDetected();
        await registry.RefreshAsync();
        var adapter = new DotNetBuildAdapter(runner, registry);

        var request = new BuildRequest(ProjectTarget(@"C:\repo\App.csproj", @"C:\repo"), BuildConfiguration.Debug, BuildOperation.Clean);
        await adapter.ExecuteAsync(request);

        var sent = Assert.Single(runner.Requests);
        Assert.Equal(new[] { "clean", @"C:\repo\App.csproj", "-c", "Debug" }, sent.Arguments);
    }

    [Fact]
    public async Task Rebuild_uses_a_single_no_incremental_build_rather_than_a_two_step_clean_and_build()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("dotnet", 0, "Build succeeded.");
        var registry = RegistryWithDotNetDetected();
        await registry.RefreshAsync();
        var adapter = new DotNetBuildAdapter(runner, registry);

        var request = new BuildRequest(ProjectTarget(@"C:\repo\App.csproj", @"C:\repo"), BuildConfiguration.Debug, BuildOperation.Rebuild);
        await adapter.ExecuteAsync(request);

        Assert.Single(runner.Requests); // exactly one process invocation, not clean-then-build
        var sent = runner.Requests[0];
        Assert.Equal(new[] { "build", @"C:\repo\App.csproj", "-c", "Debug", "--no-incremental" }, sent.Arguments);
    }

    [Fact]
    public async Task Solution_target_passes_the_sln_path_as_the_build_target()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("dotnet", 0, "Build succeeded.");
        var registry = RegistryWithDotNetDetected();
        await registry.RefreshAsync();
        var adapter = new DotNetBuildAdapter(runner, registry);

        var request = new BuildRequest(SolutionTarget(@"C:\repo\MySolution.sln", @"C:\repo"), BuildConfiguration.Debug, BuildOperation.Build);
        await adapter.ExecuteAsync(request);

        var sent = Assert.Single(runner.Requests);
        Assert.Contains(@"C:\repo\MySolution.sln", sent.Arguments);
    }

    [Fact]
    public async Task Working_directory_comes_from_the_target_not_the_current_process()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("dotnet", 0, "Build succeeded.");
        var registry = RegistryWithDotNetDetected();
        await registry.RefreshAsync();
        var adapter = new DotNetBuildAdapter(runner, registry);

        var request = new BuildRequest(ProjectTarget(@"D:\projects\App\App.csproj", @"D:\projects\App"), BuildConfiguration.Debug, BuildOperation.Build);
        await adapter.ExecuteAsync(request);

        Assert.Equal(@"D:\projects\App", runner.Requests[0].WorkingDirectory);
    }

    [Fact]
    public async Task Reports_Unavailable_and_never_starts_a_process_when_dotnet_is_not_detected()
    {
        var runner = new FakeProcessRunner();
        var registry = new ToolchainRegistry(); // no detectors registered -> dotnet unknown
        await registry.RefreshAsync();
        var adapter = new DotNetBuildAdapter(runner, registry);

        var request = new BuildRequest(ProjectTarget(@"C:\repo\App.csproj", @"C:\repo"), BuildConfiguration.Debug, BuildOperation.Build);
        var result = await adapter.ExecuteAsync(request);

        Assert.Equal(BuildStatus.Unavailable, result.Status);
        Assert.Empty(runner.Requests);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task Non_zero_exit_code_is_reported_as_Failed_not_Succeeded()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("dotnet", 1, "", @"C:\repo\Program.cs(1,1): error CS1002: ; expected");
        var registry = RegistryWithDotNetDetected();
        await registry.RefreshAsync();
        var adapter = new DotNetBuildAdapter(runner, registry);

        var request = new BuildRequest(ProjectTarget(@"C:\repo\App.csproj", @"C:\repo"), BuildConfiguration.Debug, BuildOperation.Build);
        var result = await adapter.ExecuteAsync(request);

        Assert.Equal(BuildStatus.Failed, result.Status);
        Assert.Equal(1, result.ExitCode);
        Assert.Single(result.Diagnostics);
    }
}
