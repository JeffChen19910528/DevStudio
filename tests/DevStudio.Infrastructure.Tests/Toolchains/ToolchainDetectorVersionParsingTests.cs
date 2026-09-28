using DevStudio.Core.Toolchains;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Toolchains;

public class ToolchainDetectorVersionParsingTests
{
    [Fact]
    public async Task DotNet_parses_the_bare_version_string()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("dotnet", 0, "10.0.401\n");

        var result = await new DotNetToolchainDetector(runner).DetectAsync();

        Assert.Equal(ToolchainDetectionState.Detected, result.State);
        Assert.Equal("10.0.401", result.Version);
        Assert.Contains(ToolchainCapability.Build, result.Capabilities);
        Assert.DoesNotContain(ToolchainCapability.Debug, result.Capabilities);
    }

    [Fact]
    public async Task Node_strips_the_leading_v_from_its_version_string()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("node", 0, "v22.20.0\n");

        var result = await new NodeToolchainDetector(runner).DetectAsync();

        Assert.Equal("22.20.0", result.Version);
    }

    [Fact]
    public async Task Go_parses_the_version_out_of_its_verbose_output()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("go", 0, "go version go1.22.3 windows/amd64\n");

        var result = await new GoToolchainDetector(runner).DetectAsync();

        Assert.Equal(ToolchainDetectionState.Detected, result.State);
        Assert.Equal("1.22.3", result.Version);
    }

    [Fact]
    public async Task CMake_strips_the_version_prefix()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("cmake", 0, "cmake version 3.29.0\n");

        var result = await new CMakeToolchainDetector(runner).DetectAsync();

        Assert.Equal("3.29.0", result.Version);
    }

    [Fact]
    public async Task Python_parses_version_from_either_python_or_python3()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("python3", 0, "Python 3.12.1\n");
        // "python" left unregistered -> NotFound, so the detector must fall through to python3

        var result = await new PythonToolchainDetector(runner).DetectAsync();

        Assert.Equal(ToolchainDetectionState.Detected, result.State);
        Assert.Equal("3.12.1", result.Version);
    }

    [Fact]
    public async Task Java_distinguishes_JDK_from_JRE_by_javac_presence()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("java", 0, "openjdk 25 \"25\" 2026-01-20\n");
        runner.SetResult("javac", 0, "javac 25\n");

        var result = await new JavaToolchainDetector(runner).DetectAsync();

        Assert.Equal("JDK", result.Vendor);
        Assert.Contains(ToolchainCapability.Build, result.Capabilities);
    }

    [Fact]
    public async Task Java_without_javac_is_reported_as_JRE_with_no_build_capability()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("java", 0, "openjdk 25 \"25\" 2026-01-20\n");
        // "javac" left unregistered -> NotFound

        var result = await new JavaToolchainDetector(runner).DetectAsync();

        Assert.Equal("JRE", result.Vendor);
        Assert.DoesNotContain(ToolchainCapability.Build, result.Capabilities);
    }

    [Fact]
    public async Task Git_strips_the_git_version_prefix()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("git", 0, "git version 2.50.1.windows.1\n");

        var result = await new GitToolchainDetector(runner).DetectAsync();

        Assert.Equal("2.50.1.windows.1", result.Version);
    }

    [Fact]
    public async Task Rust_reports_Detected_when_both_rustc_and_cargo_are_present()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("rustc", 0, "rustc 1.75.0\n");
        runner.SetResult("cargo", 0, "cargo 1.75.0\n");

        var result = await new RustToolchainDetector(runner).DetectAsync();

        Assert.Equal(ToolchainDetectionState.Detected, result.State);
        Assert.Contains(ToolchainCapability.Build, result.Capabilities);
    }

    [Fact]
    public async Task NodePackageManager_detects_pnpm_independently_of_npm()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("pnpm", 0, "8.15.0\n");

        var detector = new NodePackageManagerToolchainDetector(runner, WellKnownToolchainIds.Pnpm, "pnpm");
        var result = await detector.DetectAsync();

        Assert.Equal(ToolchainDetectionState.Detected, result.State);
        Assert.Equal(WellKnownToolchainIds.Pnpm, result.Id);
    }
}
