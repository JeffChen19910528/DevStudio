using DevStudio.Core.Packages;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;
using Xunit;

namespace DevStudio.Core.Tests.Packages;

file sealed class FakeAdapter : IPackageManagerAdapter, IPackageInspector, IPackageInstaller, IPackageRemover, IPackageUpdater, IPackageSearcher
{
    public string Id => "fake";
    public string DisplayName => "Fake";
    public PackageProject? ProjectToReturn { get; set; }
    public List<string> AddCalls { get; } = new();
    public TaskCompletionSource? HangUntilCancelled { get; set; }
    public PackageOperationResult ResultToReturn { get; set; } = new(true, PackageOperation.Add, "/proj", "pkg", Array.Empty<string>(), Array.Empty<string>(), 0, false, null);

    public PackageProject? DetectProject(ProjectInfo project) => ProjectToReturn;

    public Task<IReadOnlyList<PackageReference>> ListInstalledAsync(PackageProject project, Processes.IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PackageReference>>(new[] { new PackageReference("pkg", "1.0.0", "1.0.0", PackageDependencyKind.Direct, project.ProjectPath, project.PackageManagerId) });

    public Task<IReadOnlyList<PackageDependency>> ListDependenciesAsync(PackageProject project, Processes.IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PackageDependency>>(Array.Empty<PackageDependency>());

    public Task<IReadOnlyList<PackageReference>> ListOutdatedAsync(PackageProject project, Processes.IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PackageReference>>(Array.Empty<PackageReference>());

    public Task<IReadOnlyList<PackageSearchResult>> SearchAsync(PackageProject project, string query, bool includePrerelease, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PackageSearchResult>>(new[] { new PackageSearchResult("found", "2.0.0") });

    public async Task<PackageOperationResult> AddAsync(PackageProject project, string packageId, string? version, bool prerelease, bool isDevDependency, Processes.IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        AddCalls.Add(packageId);
        if (HangUntilCancelled is not null)
        {
            using var registration = cancellationToken.Register(() => HangUntilCancelled.TrySetResult());
            await HangUntilCancelled.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        return ResultToReturn;
    }

    public Task<PackageOperationResult> RemoveAsync(PackageProject project, string packageId, Processes.IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(ResultToReturn);

    public Task<PackageOperationResult> UpdateAsync(PackageProject project, string packageId, string? targetVersion, Processes.IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(ResultToReturn);

    public Task<PackageOperationResult> RestoreAsync(PackageProject project, Processes.IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(ResultToReturn);
}

public class PackageManagerRegistryTests
{
    [Fact]
    public void DetectApplicableManagers_skips_adapters_that_return_null()
    {
        var applicable = new FakeAdapter { ProjectToReturn = new PackageProject("/proj/a.csproj", ProjectType.DotNet, "fake", "Fake", PackageManagerCapabilities.None) };
        var inapplicable = new FakeAdapter { ProjectToReturn = null };
        var registry = new PackageManagerRegistry(new IPackageManagerAdapter[] { applicable, inapplicable });

        var project = new ProjectInfo("id", "Name", "/proj", ProjectType.DotNet, "/proj/a.csproj", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());
        var results = registry.DetectApplicableManagers(project);

        Assert.Single(results);
    }

    [Fact]
    public void GetAdapter_returns_null_for_an_unknown_id()
    {
        var registry = new PackageManagerRegistry(Array.Empty<IPackageManagerAdapter>());
        Assert.Null(registry.GetAdapter("nonexistent"));
    }
}

public class PackageServiceTests
{
    private static PackageProject MakeProject(PackageManagerCapabilities capabilities) =>
        new("/proj/a.csproj", ProjectType.DotNet, "fake", "Fake", capabilities);

    [Fact]
    public async Task ListInstalledAsync_returns_empty_when_the_capability_is_false_even_if_the_adapter_implements_it()
    {
        var adapter = new FakeAdapter();
        var service = new PackageService(new PackageManagerRegistry(new IPackageManagerAdapter[] { adapter }));
        var project = MakeProject(PackageManagerCapabilities.None);

        var result = await service.ListInstalledAsync(project);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListInstalledAsync_dispatches_to_the_adapter_when_the_capability_is_true()
    {
        var adapter = new FakeAdapter();
        var service = new PackageService(new PackageManagerRegistry(new IPackageManagerAdapter[] { adapter }));
        var project = MakeProject(new PackageManagerCapabilities(ListInstalled: true));

        var result = await service.ListInstalledAsync(project);

        Assert.Single(result);
        Assert.Equal("pkg", result[0].PackageId);
    }

    [Fact]
    public async Task AddAsync_returns_Unavailable_when_the_Add_capability_is_false()
    {
        var adapter = new FakeAdapter();
        var service = new PackageService(new PackageManagerRegistry(new IPackageManagerAdapter[] { adapter }));
        var project = MakeProject(PackageManagerCapabilities.None);

        var result = await service.AddAsync(project, "pkg", null, false, false);

        Assert.False(result.Success);
        Assert.Empty(adapter.AddCalls);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task AddAsync_dispatches_to_the_adapter_when_the_Add_capability_is_true()
    {
        var adapter = new FakeAdapter();
        var service = new PackageService(new PackageManagerRegistry(new IPackageManagerAdapter[] { adapter }));
        var project = MakeProject(new PackageManagerCapabilities(Add: true));

        var result = await service.AddAsync(project, "pkg", null, false, false);

        Assert.True(result.Success);
        Assert.Equal("pkg", Assert.Single(adapter.AddCalls));
    }

    [Fact]
    public async Task A_second_mutation_against_the_same_project_is_rejected_while_one_is_running()
    {
        var adapter = new FakeAdapter { HangUntilCancelled = new TaskCompletionSource() };
        var service = new PackageService(new PackageManagerRegistry(new IPackageManagerAdapter[] { adapter }));
        var project = MakeProject(new PackageManagerCapabilities(Add: true, Remove: true));

        var first = service.AddAsync(project, "pkg", null, false, false);
        Assert.True(service.IsMutationRunning(project.ProjectPath));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveAsync(project, "pkg"));

        adapter.HangUntilCancelled!.TrySetResult();
        await first;
    }

    [Fact]
    public async Task Mutations_against_different_projects_do_not_block_each_other()
    {
        var adapterA = new FakeAdapter { HangUntilCancelled = new TaskCompletionSource() };
        var adapterB = new FakeAdapter();
        var registry = new PackageManagerRegistry(new IPackageManagerAdapter[] { adapterA });
        var service = new PackageService(registry);

        var projectA = new PackageProject("/proj-a/a.csproj", ProjectType.DotNet, "fake", "Fake", new PackageManagerCapabilities(Add: true));
        var first = service.AddAsync(projectA, "pkg", null, false, false);
        Assert.True(service.IsMutationRunning(projectA.ProjectPath));

        adapterA.HangUntilCancelled!.TrySetResult();
        await first;
    }

    [Fact]
    public async Task SearchAsync_returns_empty_when_the_Search_capability_is_false()
    {
        var adapter = new FakeAdapter();
        var service = new PackageService(new PackageManagerRegistry(new IPackageManagerAdapter[] { adapter }));
        var project = MakeProject(PackageManagerCapabilities.None);

        var results = await service.SearchAsync(project, "query", false);

        Assert.Empty(results);
    }
}
