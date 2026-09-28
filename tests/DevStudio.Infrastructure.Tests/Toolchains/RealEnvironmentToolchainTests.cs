using DevStudio.Core.Toolchains;
using DevStudio.Infrastructure.Processes;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Toolchains;

/// <summary>
/// SKILL.md §32 "Real Environment Validation": actually run every detector against this
/// machine rather than assuming the Phase 0 snapshot still holds. Expected available: .NET,
/// Python, Node, Java, Git, Docker, Visual Studio/MSVC. Expected unavailable: CMake, Rust, Go,
/// standalone GCC/Clang. If the environment has changed since Phase 0, these tests fail loudly
/// instead of silently assuming stale facts.
/// </summary>
public class RealEnvironmentToolchainTests
{
    private static readonly ProcessRunner Runner = new();

    [Fact]
    public async Task DotNet_is_available_on_this_machine()
    {
        var result = await new DotNetToolchainDetector(Runner).DetectAsync();
        Assert.Equal(ToolchainDetectionState.Detected, result.State);
        Assert.NotNull(result.Version);
    }

    [Fact]
    public async Task Python_is_available_on_this_machine()
    {
        var result = await new PythonToolchainDetector(Runner).DetectAsync();
        Assert.Equal(ToolchainDetectionState.Detected, result.State);
    }

    [Fact]
    public async Task NodeJs_is_available_on_this_machine()
    {
        var result = await new NodeToolchainDetector(Runner).DetectAsync();
        Assert.Equal(ToolchainDetectionState.Detected, result.State);
    }

    [Fact]
    public async Task Java_is_available_on_this_machine()
    {
        var result = await new JavaToolchainDetector(Runner).DetectAsync();
        Assert.Equal(ToolchainDetectionState.Detected, result.State);
    }

    [Fact]
    public async Task Git_is_available_on_this_machine()
    {
        var result = await new GitToolchainDetector(Runner).DetectAsync();
        Assert.Equal(ToolchainDetectionState.Detected, result.State);
    }

    [Fact]
    public async Task Docker_is_available_on_this_machine()
    {
        var result = await new DockerToolchainDetector(Runner).DetectAsync();
        Assert.Equal(ToolchainDetectionState.Detected, result.State);
    }

    [Fact]
    public async Task VisualStudio_2026_is_discoverable_via_vswhere_on_this_machine()
    {
        var instances = await new VisualStudioDetector(Runner).DetectAllAsync();
        Assert.NotEmpty(instances);
        var enterprise = Assert.Single(instances, i => i.Edition == "Enterprise");
        Assert.NotNull(enterprise.MsBuildPath);
        // No C++ workload installed on this machine (verified via real filesystem inspection) —
        // MSVC toolset path is correctly absent rather than guessed at.
        Assert.Null(enterprise.MsvcToolsetPath);
    }

    [Fact]
    public async Task Msvc_is_not_installed_on_this_machine_despite_Visual_Studio_being_present()
    {
        // Corrects a Phase 0 assumption: Visual Studio is installed and MSBuild is present
        // (verified above), but neither detected VS instance has a VC\Tools\MSVC directory —
        // the C++ workload was never installed. Detecting this distinction, instead of assuming
        // "Visual Studio present" implies "MSVC present," is exactly what Phase 3 exists to do.
        var visualStudioDetector = new VisualStudioDetector(Runner);
        var result = await new MsvcToolchainDetector(visualStudioDetector).DetectAsync();
        Assert.Equal(ToolchainDetectionState.NotInstalled, result.State);
    }

    [Fact]
    public async Task CMake_is_not_installed_on_this_machine()
    {
        var result = await new CMakeToolchainDetector(Runner).DetectAsync();
        Assert.Equal(ToolchainDetectionState.NotInstalled, result.State);
    }

    [Fact]
    public async Task Rust_is_not_installed_on_this_machine()
    {
        var result = await new RustToolchainDetector(Runner).DetectAsync();
        Assert.Equal(ToolchainDetectionState.NotInstalled, result.State);
    }

    [Fact]
    public async Task Go_is_not_installed_on_this_machine()
    {
        var result = await new GoToolchainDetector(Runner).DetectAsync();
        Assert.Equal(ToolchainDetectionState.NotInstalled, result.State);
    }

    [Fact]
    public async Task Standalone_Gcc_is_not_installed_on_this_machine()
    {
        var result = await new GccToolchainDetector(Runner).DetectAsync();
        Assert.Equal(ToolchainDetectionState.NotInstalled, result.State);
    }

    [Fact]
    public async Task Standalone_Clang_is_not_installed_on_this_machine()
    {
        var result = await new ClangToolchainDetector(Runner).DetectAsync();
        Assert.Equal(ToolchainDetectionState.NotInstalled, result.State);
    }

    // Phase 14 P1-A: real, honest facts on this specific machine (a bare JDK only, no build
    // tool). This is the one thing about Maven/Gradle that IS real-environment validatable here
    // — their real absence — which is exactly why MavenPackageAdapter/GradlePackageAdapter must
    // never be described as "real-tested" for anything beyond this NotInstalled path (ADR-015).
    [Fact]
    public async Task Maven_is_not_installed_on_this_machine()
    {
        var result = await new MavenToolchainDetector(Runner).DetectAsync();
        Assert.Equal(ToolchainDetectionState.NotInstalled, result.State);
    }

    [Fact]
    public async Task Gradle_is_not_installed_on_this_machine()
    {
        var result = await new GradleToolchainDetector(Runner).DetectAsync();
        Assert.Equal(ToolchainDetectionState.NotInstalled, result.State);
    }

    // Phase 14 P1-C: real, honest facts on this specific machine — neither vcpkg nor conan is
    // installed. This is the one thing about VcpkgPackageAdapter/ConanPackageAdapter that IS
    // real-environment validatable here (ADR-015).
    [Fact]
    public async Task Vcpkg_is_not_installed_on_this_machine()
    {
        var result = await new VcpkgToolchainDetector(Runner).DetectAsync();
        Assert.Equal(ToolchainDetectionState.NotInstalled, result.State);
    }

    [Fact]
    public async Task Conan_is_not_installed_on_this_machine()
    {
        var result = await new ConanToolchainDetector(Runner).DetectAsync();
        Assert.Equal(ToolchainDetectionState.NotInstalled, result.State);
    }
}
