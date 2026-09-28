using DevStudio.Core.Toolchains;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Toolchains;

public class VisualStudioDetectorTests
{
    private const string FakeVswherePath = "fake-vswhere.exe";

    [Fact]
    public async Task Parses_a_single_instance_and_derives_edition_from_the_product_id()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult(FakeVswherePath, 0, """
            [
              {
                "instanceId": "abc123",
                "installationPath": "C:\\VS\\Enterprise",
                "installationVersion": "18.10.12217.157",
                "displayName": "Visual Studio Enterprise 2026",
                "productId": "Microsoft.VisualStudio.Product.Enterprise",
                "isComplete": true,
                "isLaunchable": true
              }
            ]
            """);

        var detector = new VisualStudioDetector(runner, FakeVswherePath);
        var instances = await detector.DetectAllAsync();

        var instance = Assert.Single(instances);
        Assert.Equal("Enterprise", instance.Edition);
        Assert.Equal(ToolchainDetectionState.Detected, instance.State);
        Assert.Equal("18.10.12217.157", instance.Version);
    }

    [Fact]
    public async Task Represents_multiple_instances_independently_without_picking_one()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult(FakeVswherePath, 0, """
            [
              { "instanceId": "a", "installationPath": "C:\\VS\\Enterprise", "installationVersion": "18.10.0", "displayName": "VS 2026 Enterprise", "productId": "Microsoft.VisualStudio.Product.Enterprise", "isComplete": true, "isLaunchable": true },
              { "instanceId": "b", "installationPath": "C:\\VS\\BuildTools", "installationVersion": "17.9.0", "displayName": "VS 2022 Build Tools", "productId": "Microsoft.VisualStudio.Product.BuildTools", "isComplete": true, "isLaunchable": true }
            ]
            """);

        var detector = new VisualStudioDetector(runner, FakeVswherePath);
        var instances = await detector.DetectAllAsync();

        Assert.Equal(2, instances.Count);
        Assert.Contains(instances, i => i.Edition == "Enterprise");
        Assert.Contains(instances, i => i.Edition == "BuildTools");
    }

    [Fact]
    public async Task An_incomplete_installation_is_PartiallyDetected_with_a_warning()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult(FakeVswherePath, 0, """
            [
              { "instanceId": "a", "installationPath": "C:\\VS\\Broken", "installationVersion": "18.0.0", "displayName": "VS Broken", "productId": "Microsoft.VisualStudio.Product.Community", "isComplete": false, "isLaunchable": true }
            ]
            """);

        var instances = await new VisualStudioDetector(runner, FakeVswherePath).DetectAllAsync();

        var instance = Assert.Single(instances);
        Assert.Equal(ToolchainDetectionState.PartiallyDetected, instance.State);
        Assert.NotNull(instance.DetectionWarning);
    }

    [Fact]
    public async Task Malformed_vswhere_output_yields_an_empty_list_instead_of_throwing()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult(FakeVswherePath, 0, "{ this is not valid JSON at all [[[");

        var instances = await new VisualStudioDetector(runner, FakeVswherePath).DetectAllAsync();

        Assert.Empty(instances);
    }

    [Fact]
    public async Task Missing_vswhere_yields_an_empty_list_instead_of_throwing()
    {
        var runner = new FakeProcessRunner(); // nothing registered for the fake vswhere path

        var instances = await new VisualStudioDetector(runner, FakeVswherePath).DetectAllAsync();

        Assert.Empty(instances);
    }
}
