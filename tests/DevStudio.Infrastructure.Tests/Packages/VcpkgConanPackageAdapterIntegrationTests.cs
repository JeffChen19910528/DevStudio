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
/// The one real-environment integration coverage vcpkg/Conan honestly get on this machine
/// (ADR-015): neither <c>vcpkg</c> nor <c>conan</c> is installed here, so this proves end-to-end,
/// against the REAL <see cref="ProcessRunner"/> and REAL <see cref="ToolchainRegistry"/> detection
/// (no <see cref="FakeProcessRunner"/> involved), that a real C/C++ project with a real
/// vcpkg.json/conanfile.txt correctly surfaces as "detected ecosystem, tool unavailable" rather
/// than silently claiming capabilities it cannot deliver.
/// </summary>
public class VcpkgConanPackageAdapterIntegrationTests
{
    private static readonly ProcessRunner RealRunner = new();

    private static async Task<ToolchainRegistry> RealToolchainRegistryAsync()
    {
        var registry = new ToolchainRegistry();
        registry.Register(new VcpkgToolchainDetector(RealRunner));
        registry.Register(new ConanToolchainDetector(RealRunner));
        await registry.RefreshAsync();
        return registry;
    }

    [Fact]
    public async Task Vcpkg_adapter_detects_a_real_vcpkg_json_but_reports_the_real_missing_toolchain()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("vcpkg.json", """{ "name": "x", "version": "1.0.0", "dependencies": [ "fmt" ] }""");
        var adapter = new VcpkgPackageAdapter(RealRunner, await RealToolchainRegistryAsync());
        var project = new ProjectInfo("id", "fixture", dir.Path, ProjectType.CMake, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());

        var packageProject = adapter.DetectProject(project);

        Assert.NotNull(packageProject);
        Assert.Equal(PackageManagerCapabilities.None, packageProject!.Capabilities);
        Assert.NotNull(packageProject.UnavailableReason);
    }

    [Fact]
    public async Task Conan_adapter_detects_a_real_conanfile_but_reports_the_real_missing_toolchain()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("conanfile.txt", "[requires]\nzlib/1.3.1\n");
        var adapter = new ConanPackageAdapter(RealRunner, await RealToolchainRegistryAsync());
        var project = new ProjectInfo("id", "fixture", dir.Path, ProjectType.CMake, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());

        var packageProject = adapter.DetectProject(project);

        Assert.NotNull(packageProject);
        Assert.Equal(PackageManagerCapabilities.None, packageProject!.Capabilities);
        Assert.NotNull(packageProject.UnavailableReason);
    }
}
