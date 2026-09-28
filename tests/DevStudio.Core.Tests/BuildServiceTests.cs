using DevStudio.Core.Build;
using DevStudio.Core.Diagnostics;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Workspace;
using Xunit;

namespace DevStudio.Core.Tests;

file sealed class FakeBuildAdapter : IBuildAdapter
{
    private readonly ProjectType _supportedType;
    private readonly Func<BuildRequest, CancellationToken, Task<BuildResult>> _behavior;

    public List<BuildRequest> Requests { get; } = new();

    public FakeBuildAdapter(ProjectType supportedType, Func<BuildRequest, CancellationToken, Task<BuildResult>> behavior)
    {
        _supportedType = supportedType;
        _behavior = behavior;
    }

    public bool SupportsProjectType(ProjectType projectType) => projectType == _supportedType;

    public Task<BuildResult> ExecuteAsync(BuildRequest request, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return _behavior(request, cancellationToken);
    }
}

public class BuildServiceTests
{
    private static BuildTarget MakeTarget(ProjectType type = ProjectType.DotNet) =>
        new(BuildTargetKind.Project, "App", "/repo/App.csproj", "/repo", type);

    private static BuildResult Succeeded(BuildRequest request) => new(
        BuildStatus.Succeeded, 0, TimeSpan.FromSeconds(1), request.Target, request.Operation, Array.Empty<Diagnostic>(), "ok", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static BuildResult Failed(BuildRequest request) => new(
        BuildStatus.Failed, 1, TimeSpan.FromSeconds(1), request.Target, request.Operation,
        new[] { new Diagnostic(DiagnosticSeverity.Error, "CS1002", "; expected", "/repo/Program.cs", 1, 1, DiagnosticSource.Compiler) },
        "fail", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Successful_build_returns_Succeeded_and_updates_LastResult()
    {
        var adapter = new FakeBuildAdapter(ProjectType.DotNet, (r, _) => Task.FromResult(Succeeded(r)));
        var service = new BuildService(new[] { adapter });

        var result = await service.ExecuteAsync(MakeTarget(), BuildConfiguration.Debug, BuildOperation.Build);

        Assert.Equal(BuildStatus.Succeeded, result.Status);
        Assert.Same(result, service.LastResult);
    }

    [Fact]
    public async Task Failed_build_preserves_diagnostics_and_never_reports_Succeeded()
    {
        var adapter = new FakeBuildAdapter(ProjectType.DotNet, (r, _) => Task.FromResult(Failed(r)));
        var service = new BuildService(new[] { adapter });

        var result = await service.ExecuteAsync(MakeTarget(), BuildConfiguration.Debug, BuildOperation.Build);

        Assert.Equal(BuildStatus.Failed, result.Status);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public async Task Cancelled_build_is_reported_as_Cancelled_not_Failed()
    {
        var adapter = new FakeBuildAdapter(ProjectType.DotNet, async (r, ct) =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
                throw new InvalidOperationException("should have been cancelled");
            }
            catch (OperationCanceledException)
            {
                return new BuildResult(BuildStatus.Cancelled, -1, TimeSpan.Zero, r.Target, r.Operation, Array.Empty<Diagnostic>(), "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            }
        });
        var service = new BuildService(new[] { adapter });

        using var cts = new CancellationTokenSource();
        var buildTask = service.ExecuteAsync(MakeTarget(), BuildConfiguration.Debug, BuildOperation.Build, cancellationToken: cts.Token);
        cts.Cancel();
        var result = await buildTask;

        Assert.Equal(BuildStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task No_adapter_for_the_project_type_reports_Unavailable_without_calling_anything()
    {
        var adapter = new FakeBuildAdapter(ProjectType.DotNet, (r, _) => Task.FromResult(Succeeded(r)));
        var service = new BuildService(new[] { adapter });

        var result = await service.ExecuteAsync(MakeTarget(ProjectType.Rust), BuildConfiguration.Debug, BuildOperation.Build);

        Assert.Equal(BuildStatus.Unavailable, result.Status);
        Assert.Empty(adapter.Requests);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task A_second_build_while_one_is_running_throws_instead_of_overlapping()
    {
        var gate = new TaskCompletionSource<bool>();
        var adapter = new FakeBuildAdapter(ProjectType.DotNet, async (r, _) =>
        {
            await gate.Task;
            return Succeeded(r);
        });
        var service = new BuildService(new[] { adapter });

        var firstBuild = service.ExecuteAsync(MakeTarget(), BuildConfiguration.Debug, BuildOperation.Build);
        Assert.True(service.IsRunning);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExecuteAsync(MakeTarget(), BuildConfiguration.Debug, BuildOperation.Build));

        gate.SetResult(true);
        await firstBuild;
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task IsRunning_becomes_false_again_after_a_build_completes()
    {
        var adapter = new FakeBuildAdapter(ProjectType.DotNet, (r, _) => Task.FromResult(Succeeded(r)));
        var service = new BuildService(new[] { adapter });

        await service.ExecuteAsync(MakeTarget(), BuildConfiguration.Debug, BuildOperation.Build);

        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task HasAdapterFor_reflects_registered_adapters()
    {
        var adapter = new FakeBuildAdapter(ProjectType.DotNet, (r, _) => Task.FromResult(Succeeded(r)));
        var service = new BuildService(new[] { adapter });

        Assert.True(service.HasAdapterFor(ProjectType.DotNet));
        Assert.False(service.HasAdapterFor(ProjectType.Rust));
    }
}
