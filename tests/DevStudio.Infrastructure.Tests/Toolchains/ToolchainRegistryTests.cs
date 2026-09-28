using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Toolchains;

file sealed class StaticToolchainDetector : IToolchainDetector
{
    private readonly ToolchainInfo _result;
    public string ToolchainId => _result.Id;
    public StaticToolchainDetector(ToolchainInfo result) => _result = result;
    public Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default) => Task.FromResult(_result);
}

file sealed class ThrowingToolchainDetector : IToolchainDetector
{
    public string ToolchainId => "broken";
    public Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("simulated detector crash");
}

public class ToolchainRegistryTests
{
    private static ToolchainInfo Detected(string id, params ToolchainCapability[] capabilities) =>
        new(id, id, ToolchainDetectionState.Detected, Version: "1.0", Capabilities: capabilities);

    [Fact]
    public async Task A_detector_that_throws_does_not_stop_the_others_from_completing()
    {
        var registry = new DevStudio.Infrastructure.Toolchains.ToolchainRegistry(new IToolchainDetector[]
        {
            new ThrowingToolchainDetector(),
            new StaticToolchainDetector(Detected(WellKnownToolchainIds.DotNet)),
        });

        var results = await registry.RefreshAsync();

        Assert.Equal(2, results.Count);
        var broken = results.Single(r => r.Id == "broken");
        Assert.Equal(ToolchainDetectionState.DetectionFailed, broken.State);
        Assert.Equal(ToolchainDetectionState.Detected, results.Single(r => r.Id == WellKnownToolchainIds.DotNet).State);
    }

    [Fact]
    public async Task RefreshAsync_replaces_stale_results_rather_than_accumulating()
    {
        var detector = new StaticToolchainDetector(Detected(WellKnownToolchainIds.DotNet));
        var registry = new DevStudio.Infrastructure.Toolchains.ToolchainRegistry(new[] { detector });

        await registry.RefreshAsync();
        await registry.RefreshAsync();

        Assert.Single(registry.GetAll());
    }

    [Fact]
    public async Task FindByCapability_returns_only_toolchains_that_declare_it()
    {
        var registry = new DevStudio.Infrastructure.Toolchains.ToolchainRegistry(new IToolchainDetector[]
        {
            new StaticToolchainDetector(Detected(WellKnownToolchainIds.DotNet, ToolchainCapability.Build, ToolchainCapability.Test)),
            new StaticToolchainDetector(Detected(WellKnownToolchainIds.Git, ToolchainCapability.Detect)),
        });
        await registry.RefreshAsync();

        var testCapable = registry.FindByCapability(ToolchainCapability.Test);

        Assert.Single(testCapable);
        Assert.Equal(WellKnownToolchainIds.DotNet, testCapable[0].Id);
    }

    [Fact]
    public async Task FindForProjectType_returns_only_toolchains_relevant_to_that_ecosystem()
    {
        var registry = new DevStudio.Infrastructure.Toolchains.ToolchainRegistry(new IToolchainDetector[]
        {
            new StaticToolchainDetector(Detected(WellKnownToolchainIds.DotNet)),
            new StaticToolchainDetector(Detected(WellKnownToolchainIds.Rust)),
        });
        await registry.RefreshAsync();

        var forRust = registry.FindForProjectType(ProjectType.Rust);

        Assert.Single(forRust);
        Assert.Equal(WellKnownToolchainIds.Rust, forRust[0].Id);
    }

    [Fact]
    public async Task Unregister_removes_a_detector_so_it_no_longer_participates_in_refresh()
    {
        var detector = new StaticToolchainDetector(Detected(WellKnownToolchainIds.DotNet));
        var registry = new DevStudio.Infrastructure.Toolchains.ToolchainRegistry();
        registry.Register(detector);
        registry.Unregister(WellKnownToolchainIds.DotNet);

        // No direct way to observe the detector list, so verify indirectly via a refresh.
        var results = await registry.RefreshAsync();
        Assert.Empty(results);
    }
}
