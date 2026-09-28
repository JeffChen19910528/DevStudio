using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Toolchains;

public class ToolchainProbeTests
{
    [Fact]
    public async Task Success_captures_combined_output_and_exit_code()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("dotnet", 0, "10.0.401");

        var result = await ToolchainProbe.RunAsync(runner, "dotnet", new[] { "--version" });

        Assert.Equal(ToolchainProbeOutcome.Success, result.Outcome);
        Assert.Contains("10.0.401", result.CombinedOutput);
    }

    [Fact]
    public async Task Missing_executable_reports_NotFound_instead_of_throwing()
    {
        var runner = new FakeProcessRunner(); // nothing registered for "rustc"

        var result = await ToolchainProbe.RunAsync(runner, "rustc", new[] { "--version" });

        Assert.Equal(ToolchainProbeOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Timeout_is_reported_distinctly_from_not_found_or_failure()
    {
        var runner = new FakeProcessRunner();
        runner.SetTimeout("cmake");

        var result = await ToolchainProbe.RunAsync(runner, "cmake", new[] { "--version" });

        Assert.Equal(ToolchainProbeOutcome.TimedOut, result.Outcome);
    }

    [Fact]
    public async Task Non_zero_exit_code_is_reported_as_Failed()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("java", 1, "", "some error");

        var result = await ToolchainProbe.RunAsync(runner, "java", new[] { "--version" });

        Assert.Equal(ToolchainProbeOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task Passes_executable_and_arguments_separately_never_as_one_string()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("node", 0, "v22.0.0");

        await ToolchainProbe.RunAsync(runner, "node", new[] { "--version" });

        var request = Assert.Single(runner.Requests);
        Assert.Equal("node", request.ExecutablePath);
        Assert.Equal(new[] { "--version" }, request.Arguments);
    }
}
