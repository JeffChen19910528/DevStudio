using System.Collections.Concurrent;
using DevStudio.Core.Platform;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;

namespace DevStudio.Core.Packages;

/// <summary>
/// The single entry point the UI talks to (SKILL.md §31's mandatory
/// "Project Detection → Package Manager Detection → Registry → Capability-based Adapter →
/// IProcessRunner → real tool" pipeline). Deliberately thin — it dispatches to whichever
/// <see cref="IPackageManagerAdapter"/> (and capability interface) applies and, like <see
/// cref="Git.GitService"/>, enforces "one active mutating operation per project" rather than a
/// single global lock, since a workspace can have several independent projects/package managers.
/// <see cref="PackageService"/> does not itself check Workspace Trust — exactly like
/// <c>GitService</c>/<c>TestService</c>, that gate is owned by the UI layer, applied before a
/// mutating call reaches here (SKILL.md §11, §17).
/// </summary>
public sealed class PackageService
{
    private readonly PackageManagerRegistry _registry;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeMutations = new(PathComparer.Comparer);

    public PackageService(PackageManagerRegistry registry) => _registry = registry;

    public bool IsMutationRunning(string projectPath) => _activeMutations.ContainsKey(projectPath);

    public IReadOnlyList<PackageProject> DetectApplicableManagers(ProjectInfo project) => _registry.DetectApplicableManagers(project);

    public async Task<IReadOnlyList<PackageReference>> ListInstalledAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        if (!project.Capabilities.ListInstalled || _registry.GetAdapter(project.PackageManagerId) is not IPackageInspector inspector)
        {
            return Array.Empty<PackageReference>();
        }
        return await inspector.ListInstalledAsync(project, outputSink, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PackageDependency>> ListDependenciesAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        if (!project.Capabilities.ListDependencies || _registry.GetAdapter(project.PackageManagerId) is not IPackageInspector inspector)
        {
            return Array.Empty<PackageDependency>();
        }
        return await inspector.ListDependenciesAsync(project, outputSink, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PackageReference>> ListOutdatedAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        if (!project.Capabilities.ListOutdated || _registry.GetAdapter(project.PackageManagerId) is not IPackageInspector inspector)
        {
            return Array.Empty<PackageReference>();
        }
        return await inspector.ListOutdatedAsync(project, outputSink, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PackageSearchResult>> SearchAsync(PackageProject project, string query, bool includePrerelease, CancellationToken cancellationToken = default)
    {
        if (!project.Capabilities.Search || _registry.GetAdapter(project.PackageManagerId) is not IPackageSearcher searcher)
        {
            return Array.Empty<PackageSearchResult>();
        }
        return await searcher.SearchAsync(project, query, includePrerelease, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PackageSource>> GetSourcesAsync(PackageProject project, CancellationToken cancellationToken = default)
    {
        if (!project.Capabilities.ManageSources || _registry.GetAdapter(project.PackageManagerId) is not IPackageSourceManager sourceManager)
        {
            return Array.Empty<PackageSource>();
        }
        return await sourceManager.GetSourcesAsync(project, cancellationToken).ConfigureAwait(false);
    }

    public Task<PackageOperationResult> AddAsync(PackageProject project, string packageId, string? version, bool prerelease, bool isDevDependency, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default) =>
        RunMutationAsync(project, PackageOperation.Add, packageId, ct =>
            _registry.GetAdapter(project.PackageManagerId) is IPackageInstaller installer && project.Capabilities.Add
                ? installer.AddAsync(project, packageId, version, prerelease, isDevDependency, outputSink, ct)
                : Task.FromResult(PackageOperationResult.Unavailable(PackageOperation.Add, project.ProjectPath, "This package manager does not support adding a package for this project.", packageId)),
            cancellationToken);

    public Task<PackageOperationResult> RemoveAsync(PackageProject project, string packageId, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default) =>
        RunMutationAsync(project, PackageOperation.Remove, packageId, ct =>
            _registry.GetAdapter(project.PackageManagerId) is IPackageRemover remover && project.Capabilities.Remove
                ? remover.RemoveAsync(project, packageId, outputSink, ct)
                : Task.FromResult(PackageOperationResult.Unavailable(PackageOperation.Remove, project.ProjectPath, "This package manager does not support removing a package for this project.", packageId)),
            cancellationToken);

    public Task<PackageOperationResult> UpdateAsync(PackageProject project, string packageId, string? targetVersion, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default) =>
        RunMutationAsync(project, PackageOperation.Update, packageId, ct =>
            _registry.GetAdapter(project.PackageManagerId) is IPackageUpdater updater && project.Capabilities.Update
                ? updater.UpdateAsync(project, packageId, targetVersion, outputSink, ct)
                : Task.FromResult(PackageOperationResult.Unavailable(PackageOperation.Update, project.ProjectPath, "This package manager does not support updating a package for this project.", packageId)),
            cancellationToken);

    public Task<PackageOperationResult> RestoreAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default) =>
        RunMutationAsync(project, PackageOperation.Restore, null, ct =>
            _registry.GetAdapter(project.PackageManagerId) is IPackageUpdater updater && project.Capabilities.Restore
                ? updater.RestoreAsync(project, outputSink, ct)
                : Task.FromResult(PackageOperationResult.Unavailable(PackageOperation.Restore, project.ProjectPath, "This package manager does not support restoring dependencies for this project.")),
            cancellationToken);

    private async Task<PackageOperationResult> RunMutationAsync(
        PackageProject project,
        PackageOperation operation,
        string? packageId,
        Func<CancellationToken, Task<PackageOperationResult>> action,
        CancellationToken cancellationToken)
    {
        var cts = new CancellationTokenSource();
        if (!_activeMutations.TryAdd(project.ProjectPath, cts))
        {
            cts.Dispose();
            throw new InvalidOperationException($"A package operation is already running for '{project.ProjectPath}'. Wait for it to finish before starting another.");
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
            return await action(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return PackageOperationResult.Cancelled(operation, project.ProjectPath, packageId);
        }
        finally
        {
            _activeMutations.TryRemove(project.ProjectPath, out _);
            cts.Dispose();
        }
    }
}
