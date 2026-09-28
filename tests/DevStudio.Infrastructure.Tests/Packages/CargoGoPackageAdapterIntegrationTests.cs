using DevStudio.Core.Packages;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;
using DevStudio.Infrastructure.Packages;
using DevStudio.Infrastructure.Processes;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Packages;

/// <summary>
/// The one real-environment integration coverage Cargo/Go Modules honestly get on this machine
/// (SKILL.md-style honesty rule, ADR-015): neither <c>cargo</c> nor <c>go</c> is installed here,
/// so this proves end-to-end, against the REAL <see cref="ProcessRunner"/> and REAL <see
/// cref="ToolchainRegistry"/> detection (no <see cref="FakeProcessRunner"/> involved), that a real
/// Rust/Go project with a real Cargo.toml/go.mod correctly surfaces as "detected ecosystem, tool
/// unavailable" rather than silently claiming capabilities it cannot deliver. A real `cargo add` /
/// `go get` invocation is NOT exercised anywhere in this test suite, because no such binary exists
/// on this machine — see PHASE14_PROGRESS_NOTES.md / ADR-015 for the honest real-environment
/// matrix.
/// </summary>
public class CargoGoPackageAdapterIntegrationTests
{
    private static readonly ProcessRunner RealRunner = new();

    private static async Task<ToolchainRegistry> RealToolchainRegistryAsync()
    {
        var registry = new ToolchainRegistry();
        registry.Register(new RustToolchainDetector(RealRunner));
        registry.Register(new GoToolchainDetector(RealRunner));
        await registry.RefreshAsync();
        return registry;
    }

    [Fact]
    public async Task Cargo_adapter_detects_a_real_Cargo_toml_but_reports_the_real_missing_toolchain()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("Cargo.toml", "[package]\nname = \"x\"\nversion = \"0.1.0\"\n");
        var adapter = new CargoPackageAdapter(RealRunner, await RealToolchainRegistryAsync());
        var project = new ProjectInfo("id", "fixture", dir.Path, ProjectType.Rust, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());

        var packageProject = adapter.DetectProject(project);

        Assert.NotNull(packageProject);
        Assert.Equal(PackageManagerCapabilities.None, packageProject!.Capabilities);
        Assert.NotNull(packageProject.UnavailableReason);
    }

    [Fact]
    public async Task Go_adapter_detects_a_real_go_mod_but_reports_the_real_missing_toolchain()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("go.mod", "module example.com/x\n\ngo 1.22\n");
        var adapter = new GoModulePackageAdapter(RealRunner, await RealToolchainRegistryAsync());
        var project = new ProjectInfo("id", "fixture", dir.Path, ProjectType.Go, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());

        var packageProject = adapter.DetectProject(project);

        Assert.NotNull(packageProject);
        Assert.Equal(PackageManagerCapabilities.None, packageProject!.Capabilities);
        Assert.NotNull(packageProject.UnavailableReason);
    }
}
