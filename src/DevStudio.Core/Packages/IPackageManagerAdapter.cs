using DevStudio.Core.Processes;
using DevStudio.Core.Projects;

namespace DevStudio.Core.Packages;

/// <summary>
/// Identity and applicability only (SKILL.md §4's capability-oriented design) — deliberately not
/// a mega-interface with dozens of mandatory methods. An adapter that cannot, say, search a
/// remote registry simply does not implement <see cref="IPackageSearcher"/>; it is never forced
/// to provide a throwing/no-op implementation of a capability it lacks. <see
/// cref="PackageManagerRegistry"/> is the only thing that enumerates these.
/// </summary>
public interface IPackageManagerAdapter
{
    /// <summary>Stable id — one of <see cref="WellKnownPackageManagerIds"/>.</summary>
    string Id { get; }

    string DisplayName { get; }

    /// <summary>Read-only applicability check against a project's own files (SKILL.md §6's
    /// project-detection precedent: filenames/content only, never a process execution). Returns
    /// null when this manager's ecosystem is not present in the project at all.</summary>
    PackageProject? DetectProject(ProjectInfo project);
}

/// <summary>Read-only inspection: installed/declared dependencies and outdated-package detection.
/// Never requires Workspace Trust (SKILL.md §12: read-only package inspection may be allowed
/// without trust where safe) — these only ever read state the tool/project already has.</summary>
public interface IPackageInspector
{
    Task<IReadOnlyList<PackageReference>> ListInstalledAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PackageDependency>> ListDependenciesAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PackageReference>> ListOutdatedAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default);
}

/// <summary>Remote package search — potentially network-dependent (SKILL.md §15): callers must
/// pass a real <paramref name="cancellationToken"/> and treat a network failure as a structured,
/// reportable outcome, never a silent empty list.</summary>
public interface IPackageSearcher
{
    Task<IReadOnlyList<PackageSearchResult>> SearchAsync(PackageProject project, string query, bool includePrerelease, CancellationToken cancellationToken = default);
}

/// <summary>Adds a dependency. This — like <see cref="IPackageRemover"/>/<see
/// cref="IPackageUpdater"/> — is a mutation and MUST be gated by Workspace Trust by the caller
/// (<see cref="PackageService"/> does not itself check trust; SKILL.md's Workspace Trust gate is
/// owned by the UI layer exactly like Build/Run/Debug/Test/Git already do it).</summary>
public interface IPackageInstaller
{
    Task<PackageOperationResult> AddAsync(PackageProject project, string packageId, string? version, bool prerelease, bool isDevDependency, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default);
}

public interface IPackageRemover
{
    Task<PackageOperationResult> RemoveAsync(PackageProject project, string packageId, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default);
}

public interface IPackageUpdater
{
    Task<PackageOperationResult> UpdateAsync(PackageProject project, string packageId, string? targetVersion, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default);

    /// <summary>Re-resolves/installs a project's already-declared dependencies (e.g. <c>dotnet
    /// restore</c>/<c>npm install</c>/<c>pip install -r requirements.txt</c>) — a mutation
    /// because it can execute third-party install/build scripts (SKILL.md §17), so it is gated
    /// exactly like Add/Remove/Update, never run automatically on workspace open.</summary>
    Task<PackageOperationResult> RestoreAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default);
}

public interface IPackageSourceManager
{
    Task<IReadOnlyList<PackageSource>> GetSourcesAsync(PackageProject project, CancellationToken cancellationToken = default);
}
