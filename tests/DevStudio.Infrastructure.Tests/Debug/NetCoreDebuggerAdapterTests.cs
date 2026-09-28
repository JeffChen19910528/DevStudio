using DevStudio.Core.Build;
using DevStudio.Core.Debug;
using DevStudio.Core.Projects;
using DevStudio.Core.Run;
using DevStudio.Core.Workspace;
using DevStudio.Infrastructure.Debug;
using DevStudio.Infrastructure.Tests.TestSupport;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Debug;

/// <summary>Unit tests for the parts of <see cref="NetCoreDebuggerAdapter"/> that fail before
/// ever touching a real debugger process — never-built target, missing working directory, and
/// debugger-not-found — mirroring <c>DotNetRunAdapterTests</c>' split between fast unit checks
/// here and the mandatory real-process coverage in <c>NetCoreDebugIntegrationTests</c>.</summary>
public class NetCoreDebuggerAdapterTests
{
    private static DebugConfiguration MakeConfiguration(BuildTarget target, string? workingDirectoryOverride = null) =>
        new(new RunConfiguration("App", target, BuildConfiguration.Debug, WorkingDirectoryOverride: workingDirectoryOverride));

    [Fact]
    public async Task StartAsync_throws_a_clear_not_built_error_when_bin_is_missing()
    {
        using var temp = new TempDirectory();
        var csprojPath = temp.WriteFile("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var target = new BuildTarget(BuildTargetKind.Project, "App", csprojPath, temp.Path, ProjectType.DotNet);

        var adapter = new NetCoreDebuggerAdapter(new FakeProcessRunner(), new NetCoreDebuggerResolver());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.StartAsync(MakeConfiguration(target)));

        Assert.Contains("has not been built", ex.Message);
    }

    [Fact]
    public async Task StartAsync_throws_when_the_working_directory_does_not_exist()
    {
        using var temp = new TempDirectory();
        var csprojPath = temp.WriteFile("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        temp.CreateDirectory("bin/Debug/net10.0");
        temp.WriteFile("bin/Debug/net10.0/App.dll", "fake-binary-placeholder");
        var target = new BuildTarget(BuildTargetKind.Project, "App", csprojPath, temp.Path, ProjectType.DotNet);
        var missingDirectory = System.IO.Path.Combine(temp.Path, "does-not-exist");

        var adapter = new NetCoreDebuggerAdapter(new FakeProcessRunner(), new NetCoreDebuggerResolver());
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.StartAsync(MakeConfiguration(target, missingDirectory)));
    }

    [Fact]
    public async Task StartAsync_reports_ambiguity_instead_of_guessing_when_multiple_built_outputs_match()
    {
        using var temp = new TempDirectory();
        var csprojPath = temp.WriteFile("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        temp.CreateDirectory("bin/Debug/net8.0");
        temp.CreateDirectory("bin/Debug/net10.0");
        temp.WriteFile("bin/Debug/net8.0/App.dll", "fake-binary-placeholder");
        temp.WriteFile("bin/Debug/net10.0/App.dll", "fake-binary-placeholder");
        var target = new BuildTarget(BuildTargetKind.Project, "App", csprojPath, temp.Path, ProjectType.DotNet);

        var adapter = new NetCoreDebuggerAdapter(new FakeProcessRunner(), new NetCoreDebuggerResolver());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.StartAsync(MakeConfiguration(target)));

        Assert.Contains("Multiple built outputs found", ex.Message);
    }

    [Fact]
    public void SupportsProjectType_is_true_only_for_DotNet()
    {
        var adapter = new NetCoreDebuggerAdapter(new FakeProcessRunner(), new NetCoreDebuggerResolver());

        Assert.True(adapter.SupportsProjectType(ProjectType.DotNet));
        Assert.False(adapter.SupportsProjectType(ProjectType.Rust));
    }
}
