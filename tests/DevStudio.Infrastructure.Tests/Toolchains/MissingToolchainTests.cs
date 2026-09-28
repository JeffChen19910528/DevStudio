using DevStudio.Core.Toolchains;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Toolchains;

/// <summary>
/// SKILL.md §31 "Missing Toolchain Tests": simulate Rust/Go/GCC/Clang/CMake missing via a fake
/// process runner, never by touching the real environment — each detector must report
/// <see cref="ToolchainDetectionState.NotInstalled"/> without throwing, and with no
/// capabilities claimed.
/// </summary>
public class MissingToolchainTests
{
    [Fact]
    public async Task Rust_missing_reports_NotInstalled()
    {
        var result = await new RustToolchainDetector(new FakeProcessRunner()).DetectAsync();
        Assert.Equal(ToolchainDetectionState.NotInstalled, result.State);
        Assert.Empty(result.Capabilities);
    }

    [Fact]
    public async Task Go_missing_reports_NotInstalled()
    {
        var result = await new GoToolchainDetector(new FakeProcessRunner()).DetectAsync();
        Assert.Equal(ToolchainDetectionState.NotInstalled, result.State);
    }

    [Fact]
    public async Task Gcc_missing_reports_NotInstalled()
    {
        var result = await new GccToolchainDetector(new FakeProcessRunner()).DetectAsync();
        Assert.Equal(ToolchainDetectionState.NotInstalled, result.State);
    }

    [Fact]
    public async Task Clang_missing_reports_NotInstalled()
    {
        var result = await new ClangToolchainDetector(new FakeProcessRunner()).DetectAsync();
        Assert.Equal(ToolchainDetectionState.NotInstalled, result.State);
    }

    [Fact]
    public async Task CMake_missing_reports_NotInstalled()
    {
        var result = await new CMakeToolchainDetector(new FakeProcessRunner()).DetectAsync();
        Assert.Equal(ToolchainDetectionState.NotInstalled, result.State);
    }

    [Fact]
    public async Task Rust_with_only_cargo_present_is_PartiallyDetected_not_Detected()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("cargo", 0, "cargo 1.75.0");
        // rustc left unregistered -> NotFound

        var result = await new RustToolchainDetector(runner).DetectAsync();

        Assert.Equal(ToolchainDetectionState.PartiallyDetected, result.State);
    }
}
