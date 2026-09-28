using DevStudio.Core.Build;
using DevStudio.Core.Projects;
using DevStudio.Core.Run;
using DevStudio.Core.Toolchains;
using DevStudio.Core.Workspace;
using DevStudio.Infrastructure.Run;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Run;

public class DotNetRunAdapterTests
{
    private sealed class StaticDotNetDetector : IToolchainDetector
    {
        private readonly ToolchainDetectionState _state;
        public StaticDotNetDetector(ToolchainDetectionState state) => _state = state;
        public string ToolchainId => WellKnownToolchainIds.DotNet;
        public Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ToolchainInfo(ToolchainId, "dotnet", _state, ExecutablePath: "dotnet"));
    }

    private static async Task<ToolchainRegistry> RegistryWithDotNetDetectedAsync()
    {
        var registry = new ToolchainRegistry();
        registry.Register(new StaticDotNetDetector(ToolchainDetectionState.Detected));
        await registry.RefreshAsync();
        return registry;
    }

    private static ToolchainRegistry RegistryWithoutDotNet() => new();

    private static (TempDirectory Directory, BuildTarget Target) MakeBuiltProject()
    {
        var directory = new TempDirectory();
        var projectFile = directory.WriteFile("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>");
        directory.CreateDirectory("bin/Debug/net10.0");
        directory.WriteFile("bin/Debug/net10.0/App.dll", "fake-binary-placeholder");
        var target = new BuildTarget(BuildTargetKind.Project, "App", projectFile, directory.Path, ProjectType.DotNet);
        return (directory, target);
    }

    private static RunConfiguration MakeConfiguration(BuildTarget target, IReadOnlyList<string>? arguments = null, IReadOnlyDictionary<string, string>? environment = null, string? workingDirectoryOverride = null) =>
        new("App", target, BuildConfiguration.Debug, Arguments: arguments ?? Array.Empty<string>(), EnvironmentVariables: environment, WorkingDirectoryOverride: workingDirectoryOverride);

    [Fact]
    public async Task StartAsync_launches_dotnet_run_with_project_configuration_and_no_build()
    {
        var built = MakeBuiltProject();
        using var builtDirectory = built.Directory;
        var runner = new FakeProcessRunner();
        runner.SetStartBehavior("dotnet", _ => new FakeRunningProcess());
        var adapter = new DotNetRunAdapter(runner, await RegistryWithDotNetDetectedAsync());

        await adapter.StartAsync(MakeConfiguration(built.Target));

        var sent = Assert.Single(runner.StartRequests);
        Assert.Equal("dotnet", sent.ExecutablePath);
        Assert.Equal(new[] { "run", "--project", built.Target.FilePath, "-c", "Debug", "--no-build" }, sent.Arguments);
        Assert.Equal(built.Directory.Path, sent.WorkingDirectory);
    }

    [Fact]
    public async Task StartAsync_appends_arguments_after_a_double_dash_separator_preserving_multi_word_arguments()
    {
        var built = MakeBuiltProject();
        using var builtDirectory = built.Directory;
        var runner = new FakeProcessRunner();
        runner.SetStartBehavior("dotnet", _ => new FakeRunningProcess());
        var adapter = new DotNetRunAdapter(runner, await RegistryWithDotNetDetectedAsync());

        await adapter.StartAsync(MakeConfiguration(built.Target, arguments: new[] { "--name", "hello world", "--count", "3" }));

        var sent = Assert.Single(runner.StartRequests);
        Assert.Equal(
            new[] { "run", "--project", built.Target.FilePath, "-c", "Debug", "--no-build", "--", "--name", "hello world", "--count", "3" },
            sent.Arguments);
    }

    [Fact]
    public async Task StartAsync_passes_environment_variables_through_to_the_process_request()
    {
        var built = MakeBuiltProject();
        using var builtDirectory = built.Directory;
        var runner = new FakeProcessRunner();
        runner.SetStartBehavior("dotnet", _ => new FakeRunningProcess());
        var adapter = new DotNetRunAdapter(runner, await RegistryWithDotNetDetectedAsync());
        var env = new Dictionary<string, string> { ["GREETING"] = "hi" };

        await adapter.StartAsync(MakeConfiguration(built.Target, environment: env));

        var sent = Assert.Single(runner.StartRequests);
        Assert.Equal("hi", sent.Environment?["GREETING"]);
    }

    [Fact]
    public async Task StartAsync_uses_the_working_directory_override_when_provided()
    {
        var built = MakeBuiltProject();
        using var builtDirectory = built.Directory;
        using var overrideDirectory = new TempDirectory();
        var runner = new FakeProcessRunner();
        runner.SetStartBehavior("dotnet", _ => new FakeRunningProcess());
        var adapter = new DotNetRunAdapter(runner, await RegistryWithDotNetDetectedAsync());

        await adapter.StartAsync(MakeConfiguration(built.Target, workingDirectoryOverride: overrideDirectory.Path));

        var sent = Assert.Single(runner.StartRequests);
        Assert.Equal(overrideDirectory.Path, sent.WorkingDirectory);
    }

    [Fact]
    public async Task StartAsync_throws_when_the_dotnet_SDK_is_not_detected_and_never_starts_a_process()
    {
        var built = MakeBuiltProject();
        using var builtDirectory = built.Directory;
        var runner = new FakeProcessRunner();
        var adapter = new DotNetRunAdapter(runner, RegistryWithoutDotNet());

        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.StartAsync(MakeConfiguration(built.Target)));

        Assert.Empty(runner.StartRequests);
    }

    [Fact]
    public async Task StartAsync_throws_a_clear_not_built_error_instead_of_silently_building_when_bin_is_missing()
    {
        using var directory = new TempDirectory();
        var projectFile = directory.WriteFile("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>");
        var target = new BuildTarget(BuildTargetKind.Project, "App", projectFile, directory.Path, ProjectType.DotNet);
        var runner = new FakeProcessRunner();
        var adapter = new DotNetRunAdapter(runner, await RegistryWithDotNetDetectedAsync());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.StartAsync(MakeConfiguration(target)));

        Assert.Contains("has not been built", ex.Message);
        Assert.Empty(runner.StartRequests);
    }

    [Fact]
    public async Task StartAsync_throws_when_the_working_directory_does_not_exist()
    {
        var built = MakeBuiltProject();
        using var builtDirectory = built.Directory;
        var runner = new FakeProcessRunner();
        var adapter = new DotNetRunAdapter(runner, await RegistryWithDotNetDetectedAsync());
        var missingDirectory = System.IO.Path.Combine(built.Directory.Path, "does-not-exist");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.StartAsync(MakeConfiguration(built.Target, workingDirectoryOverride: missingDirectory)));

        Assert.Empty(runner.StartRequests);
    }
}
