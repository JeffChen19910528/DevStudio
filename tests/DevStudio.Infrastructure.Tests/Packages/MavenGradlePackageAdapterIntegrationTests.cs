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
/// The one real-environment integration coverage Maven/Gradle honestly get on this machine
/// (SKILL.md-style honesty rule, ADR-015): both tools are genuinely absent here, so this proves
/// end-to-end, against the REAL <see cref="ProcessRunner"/> and REAL <see
/// cref="ToolchainRegistry"/> detection (no <see cref="FakeProcessRunner"/> involved), that a real
/// Java project with a real pom.xml/build.gradle correctly surfaces as "detected ecosystem, tool
/// unavailable" rather than silently claiming capabilities it cannot deliver. A real `mvn add` /
/// `gradle dependencies` invocation is NOT exercised anywhere in this test suite, because no such
/// binary exists on this machine — see PHASE14_PROGRESS_NOTES.md / ADR-015 for the honest
/// real-environment matrix.
/// </summary>
public class MavenGradlePackageAdapterIntegrationTests
{
    private static readonly ProcessRunner RealRunner = new();

    private static async Task<ToolchainRegistry> RealToolchainRegistryAsync()
    {
        var registry = new ToolchainRegistry();
        registry.Register(new MavenToolchainDetector(RealRunner));
        registry.Register(new GradleToolchainDetector(RealRunner));
        await registry.RefreshAsync();
        return registry;
    }

    private static ProjectInfo MakeProjectInfo(string rootPath) =>
        new("id", "fixture", rootPath, ProjectType.Java, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());

    [Fact]
    public async Task Maven_adapter_detects_a_real_pom_xml_but_reports_the_real_missing_toolchain()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("pom.xml", "<project xmlns=\"http://maven.apache.org/POM/4.0.0\"><modelVersion>4.0.0</modelVersion><groupId>g</groupId><artifactId>a</artifactId><version>1.0</version></project>");
        var adapter = new MavenPackageAdapter(RealRunner, await RealToolchainRegistryAsync());

        var project = adapter.DetectProject(MakeProjectInfo(dir.Path));

        Assert.NotNull(project);
        Assert.Equal(PackageManagerCapabilities.None, project!.Capabilities);
        Assert.NotNull(project.UnavailableReason);
    }

    [Fact]
    public async Task Gradle_adapter_detects_a_real_build_gradle_but_reports_the_real_missing_toolchain()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("build.gradle", "dependencies { implementation 'junit:junit:4.13.2' }");
        var adapter = new GradlePackageAdapter(RealRunner, await RealToolchainRegistryAsync());

        var project = adapter.DetectProject(MakeProjectInfo(dir.Path));

        Assert.NotNull(project);
        Assert.Equal(PackageManagerCapabilities.None, project!.Capabilities);
        Assert.NotNull(project.UnavailableReason);
    }
}
