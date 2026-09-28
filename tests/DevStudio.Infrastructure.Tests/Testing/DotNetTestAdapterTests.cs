using DevStudio.Core.Build;
using DevStudio.Core.Projects;
using DevStudio.Core.Testing;
using DevStudio.Core.Toolchains;
using DevStudio.Core.Workspace;
using DevStudio.Infrastructure.Testing;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Testing;

/// <summary>Unit tests for <see cref="DotNetTestAdapter"/>'s command construction and its
/// real, fixed-sample-based parsing of <c>--list-tests</c> console output — no process, no
/// real TRX file needed for these (that's <c>DotNetTestIntegrationTests</c>).</summary>
public class DotNetTestAdapterTests
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

    private static ProjectInfo MakeProject(string rootPath) =>
        new("proj-1", "App.Tests", rootPath, ProjectType.DotNet, System.IO.Path.Combine(rootPath, "App.Tests.csproj"), new[] { "C#" }, Array.Empty<string>(), Array.Empty<ProjectCapability>());

    [Fact]
    public async Task DiscoverTestsAsync_passes_the_project_path_and_configuration_as_separate_arguments()
    {
        using var temp = new TempDirectory();
        var runner = new FakeProcessRunner();
        runner.SetResult("dotnet", 0, "");
        var adapter = new DotNetTestAdapter(runner, await RegistryWithDotNetDetectedAsync());

        await adapter.DiscoverTestsAsync(MakeProject(temp.Path), BuildConfiguration.Debug);

        var sent = Assert.Single(runner.Requests);
        Assert.Equal("dotnet", sent.ExecutablePath);
        Assert.Contains("test", sent.Arguments);
        Assert.Contains(System.IO.Path.Combine(temp.Path, "App.Tests.csproj"), sent.Arguments);
        Assert.Contains("--list-tests", sent.Arguments);
        Assert.Contains("Debug", sent.Arguments);
    }

    [Fact]
    public async Task DiscoverTestsAsync_parses_real_VSTest_list_tests_output_by_its_four_space_indentation()
    {
        using var temp = new TempDirectory();
        var runner = new FakeProcessRunner();
        // A faithful sample of real `dotnet test --list-tests` output (captured against a real
        // temporary xUnit project on this machine before writing the parser — see ADR-009):
        // localized header/progress lines are not indented; every real test name is indented
        // with exactly four spaces, including Theory-expanded names carrying parameter values.
        runner.SetResult("dotnet", 0, string.Join("\n", new[]
        {
            "  正在判斷要還原的專案...",
            "  所有專案都在最新狀態，可進行還原。",
            "  Probe -> C:\\temp\\Probe.dll",
            "C:\\temp\\Probe.dll 的測試回合 (.NETCoreApp,Version=v10.0)",
            "可使用下列測試:",
            "    Probe.CalculatorTests.Add_ReturnsExpectedValue",
            "    Probe.CalculatorTests.Add_Theory(a: 1, b: 1, expected: 2)",
            "    Probe.CalculatorTests.Add_Theory(a: 2, b: 2, expected: 4)",
        }));
        var adapter = new DotNetTestAdapter(runner, await RegistryWithDotNetDetectedAsync());

        var tests = await adapter.DiscoverTestsAsync(MakeProject(temp.Path), BuildConfiguration.Debug);

        Assert.Equal(3, tests.Count);
        Assert.Contains(tests, t => t.FullyQualifiedName == "Probe.CalculatorTests.Add_ReturnsExpectedValue");
        Assert.Contains(tests, t => t.FullyQualifiedName == "Probe.CalculatorTests.Add_Theory(a: 1, b: 1, expected: 2)");
        Assert.All(tests, t => Assert.Equal("proj-1", t.ProjectId));
        Assert.All(tests, t => Assert.Null(t.SourceFile)); // real --list-tests never reports a location
    }

    [Fact]
    public async Task DiscoverTestsAsync_reports_zero_tests_when_none_are_discovered()
    {
        using var temp = new TempDirectory();
        var runner = new FakeProcessRunner();
        runner.SetResult("dotnet", 0, "沒有可執行的測試。");
        var adapter = new DotNetTestAdapter(runner, await RegistryWithDotNetDetectedAsync());

        var tests = await adapter.DiscoverTestsAsync(MakeProject(temp.Path), BuildConfiguration.Debug);

        Assert.Empty(tests);
    }

    [Fact]
    public async Task RunTestsAsync_passes_no_build_when_the_caller_already_built()
    {
        using var temp = new TempDirectory();
        var runner = new FakeProcessRunner();
        runner.SetResult("dotnet", 1, ""); // no TRX will be produced — exercised separately; this just checks arguments
        var adapter = new DotNetTestAdapter(runner, await RegistryWithDotNetDetectedAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.RunTestsAsync(MakeProject(temp.Path), BuildConfiguration.Debug, filter: null, skipBuild: true));

        var sent = Assert.Single(runner.Requests);
        Assert.Contains("--no-build", sent.Arguments);
    }

    [Fact]
    public async Task RunTestsAsync_omits_no_build_when_the_caller_did_not_build()
    {
        using var temp = new TempDirectory();
        var runner = new FakeProcessRunner();
        runner.SetResult("dotnet", 1, "");
        var adapter = new DotNetTestAdapter(runner, await RegistryWithDotNetDetectedAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.RunTestsAsync(MakeProject(temp.Path), BuildConfiguration.Debug, filter: null, skipBuild: false));

        var sent = Assert.Single(runner.Requests);
        Assert.DoesNotContain("--no-build", sent.Arguments);
    }

    [Fact]
    public async Task RunTestsAsync_translates_a_structured_filter_into_the_real_VSTest_filter_argument()
    {
        using var temp = new TempDirectory();
        var runner = new FakeProcessRunner();
        runner.SetResult("dotnet", 1, "");
        var adapter = new DotNetTestAdapter(runner, await RegistryWithDotNetDetectedAsync());
        var filter = new TestFilter(FullyQualifiedNames: new[] { "App.Tests.CalculatorTests.Add_ReturnsExpectedValue" });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.RunTestsAsync(MakeProject(temp.Path), BuildConfiguration.Debug, filter, skipBuild: true));

        var sent = Assert.Single(runner.Requests);
        var filterIndex = sent.Arguments.ToList().IndexOf("--filter");
        Assert.True(filterIndex >= 0);
        Assert.Equal("FullyQualifiedName=App.Tests.CalculatorTests.Add_ReturnsExpectedValue", sent.Arguments[filterIndex + 1]);
    }

    [Fact]
    public async Task Reports_a_clear_error_when_dotnet_is_not_detected_and_never_starts_a_process()
    {
        using var temp = new TempDirectory();
        var runner = new FakeProcessRunner();
        var registry = new ToolchainRegistry(); // no detectors registered -> dotnet unknown
        await registry.RefreshAsync();
        var adapter = new DotNetTestAdapter(runner, registry);

        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.DiscoverTestsAsync(MakeProject(temp.Path), BuildConfiguration.Debug));

        Assert.Empty(runner.Requests);
    }

    [Fact]
    public void SupportsProjectType_is_true_only_for_DotNet()
    {
        var adapter = new DotNetTestAdapter(new FakeProcessRunner(), new ToolchainRegistry());

        Assert.True(adapter.SupportsProjectType(ProjectType.DotNet));
        Assert.False(adapter.SupportsProjectType(ProjectType.Rust));
    }
}
