using DevStudio.Core.Projects;

namespace DevStudio.Core.Packages;

/// <summary>
/// Resolves which package manager(s) apply to a project and what each can do (SKILL.md §5's
/// registry precedent — mirrors <c>ToolchainRegistry</c>/<c>BuildService</c>/<c>TestService</c>'s
/// "inspect the project, ask every registered adapter, use whichever applies" shape). The UI never
/// branches on project/language here or anywhere downstream — it only ever sees the generic
/// <see cref="PackageProject"/>/<see cref="PackageManagerCapabilities"/> contracts this produces.
/// </summary>
public sealed class PackageManagerRegistry
{
    private readonly IReadOnlyList<IPackageManagerAdapter> _adapters;

    public PackageManagerRegistry(IEnumerable<IPackageManagerAdapter> adapters) => _adapters = adapters.ToList();

    /// <summary>Every package manager applicable to this project, one entry per ecosystem
    /// actually detected in it (a project can have more than one, e.g. a Node project with both
    /// npm and — once implemented — a native sub-build). Read-only: only ever inspects files
    /// already loaded onto <paramref name="project"/>/its own directory, never runs a process.</summary>
    public IReadOnlyList<PackageProject> DetectApplicableManagers(ProjectInfo project) =>
        _adapters
            .Select(adapter => adapter.DetectProject(project))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

    public IPackageManagerAdapter? GetAdapter(string packageManagerId) =>
        _adapters.FirstOrDefault(a => string.Equals(a.Id, packageManagerId, StringComparison.Ordinal));
}
