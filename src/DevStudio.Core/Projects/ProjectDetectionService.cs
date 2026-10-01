using DevStudio.Core.Platform;
using DevStudio.Core.Toolchains;
using DevStudio.Core.Workspace;

namespace DevStudio.Core.Projects;

/// <summary>
/// Walks a workspace with <see cref="IWorkspaceScanner"/> (bounded depth, exclusion-aware, one
/// directory level at a time — SKILL.md §26, §30), runs every registered <see
/// cref="IProjectDetector"/> against each directory, resolves solution references, builds the
/// parent/child project hierarchy by path containment, and falls back to a single "Generic
/// Folder Project" when nothing was detected anywhere (SKILL.md §3, §17). Detection itself never
/// touches disk directly — all filesystem access goes through the injected scanner/detectors,
/// keeping this orchestrator itself free of any I/O dependency.
/// </summary>
public sealed class ProjectDetectionService
{
    private const int MaxDepth = 6;

    private readonly IWorkspaceScanner _scanner;
    private readonly IReadOnlyList<IProjectDetector> _detectors;

    public ProjectDetectionService(IWorkspaceScanner scanner, IReadOnlyList<IProjectDetector> detectors)
    {
        _scanner = scanner;
        _detectors = detectors;
    }

    public async Task<WorkspaceProjectGraph> DetectAsync(
        string rootPath,
        WorkspaceExclusionRules exclusionRules,
        CancellationToken cancellationToken = default)
    {
        var projects = new List<ProjectInfo>();
        var rawSolutions = new List<RawSolutionDetection>();

        await ScanDirectoryAsync(rootPath, depth: 0, exclusionRules, projects, rawSolutions, cancellationToken).ConfigureAwait(false);

        var solutions = ResolveSolutions(rawSolutions, projects);
        ResolveProjectReferences(projects);

        if (projects.Count == 0)
        {
            var generic = new ProjectInfo(
                Id: Guid.NewGuid().ToString("N"),
                Name: GetDisplayName(rootPath),
                RootPath: rootPath,
                ProjectType: ProjectType.Generic,
                ProjectFile: null,
                Languages: Array.Empty<string>(),
                ConfigurationFiles: Array.Empty<string>(),
                Capabilities: Array.Empty<ProjectCapability>());

            return new WorkspaceProjectGraph(new[] { generic }, solutions, new[] { generic });
        }

        var topLevel = BuildHierarchy(projects);
        return new WorkspaceProjectGraph(topLevel, solutions, projects);
    }

    private async Task ScanDirectoryAsync(
        string directoryPath,
        int depth,
        WorkspaceExclusionRules exclusionRules,
        List<ProjectInfo> projects,
        List<RawSolutionDetection> rawSolutions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var children = await _scanner.GetChildrenAsync(directoryPath, exclusionRules, cancellationToken).ConfigureAwait(false);
        var fileNames = children.Where(c => !c.IsDirectory).Select(c => c.Name).ToList();

        foreach (var detector in _detectors)
        {
            var result = await detector.DetectAsync(directoryPath, fileNames, cancellationToken).ConfigureAwait(false);
            if (result is null) continue;

            if (result.Project is not null) projects.Add(result.Project);
            if (result.Solution is not null) rawSolutions.Add(result.Solution);
        }

        if (depth >= MaxDepth) return;

        foreach (var subdirectory in children.Where(c => c.IsDirectory))
        {
            await ScanDirectoryAsync(subdirectory.FullPath, depth + 1, exclusionRules, projects, rawSolutions, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Links each project's raw <see cref="ProjectInfo.ProjectReferenceFilePaths"/>
    /// to the <see cref="ProjectInfo.Id"/> of matching projects already in the scanned graph.
    /// References to projects outside the scanned depth or excluded directories are silently
    /// skipped — the project still builds, MSBuild resolves those dependencies itself.</summary>
    private static void ResolveProjectReferences(List<ProjectInfo> projects)
    {
        var byFilePath = new Dictionary<string, int>(PathComparer.Comparer);
        for (var i = 0; i < projects.Count; i++)
        {
            if (projects[i].ProjectFile is { } file)
                byFilePath[Path.GetFullPath(file)] = i;
        }

        for (var i = 0; i < projects.Count; i++)
        {
            var project = projects[i];
            if (project.ProjectReferenceFilePaths.Count == 0) continue;

            var resolvedIds = new List<string>();
            foreach (var refPath in project.ProjectReferenceFilePaths)
            {
                if (byFilePath.TryGetValue(refPath, out var depIndex))
                    resolvedIds.Add(projects[depIndex].Id);
            }

            if (resolvedIds.Count > 0)
                projects[i] = project with { DependencyProjectIds = resolvedIds };
        }
    }

    private static List<SolutionInfo> ResolveSolutions(List<RawSolutionDetection> rawSolutions, List<ProjectInfo> projects)
    {
        var resolved = new List<SolutionInfo>();

        foreach (var raw in rawSolutions)
        {
            var projectIds = new List<string>();

            foreach (var referencedPath in raw.ReferencedProjectFilePaths)
            {
                var match = projects.FirstOrDefault(p =>
                    p.ProjectFile is not null && PathsEqual(p.ProjectFile, referencedPath));

                if (match is not null)
                {
                    projectIds.Add(match.Id);
                    continue;
                }

                // Referenced by the solution but not found during detection (moved, deleted, or
                // outside the scanned depth/exclusions). Represent it rather than drop it silently.
                var stub = new ProjectInfo(
                    Id: Guid.NewGuid().ToString("N"),
                    Name: Path.GetFileNameWithoutExtension(referencedPath),
                    RootPath: Path.GetDirectoryName(referencedPath) ?? referencedPath,
                    ProjectType: GuessProjectTypeFromExtension(referencedPath),
                    ProjectFile: referencedPath,
                    Languages: Array.Empty<string>(),
                    ConfigurationFiles: Array.Empty<string>(),
                    Capabilities: Array.Empty<ProjectCapability>(),
                    DetectionConfidence: DetectionConfidence.Partial,
                    DetectionWarning: $"Referenced by solution '{raw.Name}' but not found on disk during detection.");

                projects.Add(stub);
                projectIds.Add(stub.Id);
            }

            resolved.Add(new SolutionInfo(raw.Id, raw.Name, raw.SolutionFilePath, projectIds, raw.DetectionConfidence, raw.DetectionWarning));
        }

        return resolved;
    }

    private static ProjectType GuessProjectTypeFromExtension(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".csproj" or ".fsproj" or ".vbproj" => ProjectType.DotNet,
        _ => ProjectType.Unknown,
    };

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), PathComparer.Comparison);

    /// <summary>Nests each project under the closest ancestor project by directory containment,
    /// so e.g. a solution's projects under src/API, src/Core become that solution's <see
    /// cref="ProjectInfo.ChildProjects"/> (SKILL.md §8), and returns only the ones with no
    /// parent (the top level the Explorer should show first).</summary>
    private static List<ProjectInfo> BuildHierarchy(List<ProjectInfo> projects)
    {
        var byId = projects.ToDictionary(p => p.Id);
        var childIds = new Dictionary<string, List<string>>();

        foreach (var project in projects)
        {
            var parent = FindClosestAncestor(project, projects);
            if (parent is null) continue;

            if (!childIds.TryGetValue(parent.Id, out var list))
            {
                childIds[parent.Id] = list = new List<string>();
            }
            list.Add(project.Id);
        }

        ProjectInfo Materialize(ProjectInfo project)
        {
            if (!childIds.TryGetValue(project.Id, out var ids))
            {
                return project;
            }

            var children = ids.Select(id => Materialize(byId[id])).ToList();
            return project with { ChildProjects = children };
        }

        var parentedIds = childIds.Values.SelectMany(v => v).ToHashSet();
        return projects.Where(p => !parentedIds.Contains(p.Id)).Select(Materialize).ToList();
    }

    private static ProjectInfo? FindClosestAncestor(ProjectInfo project, List<ProjectInfo> allProjects)
    {
        ProjectInfo? best = null;

        foreach (var candidate in allProjects)
        {
            if (candidate.Id == project.Id) continue;
            if (!IsUnder(project.RootPath, candidate.RootPath)) continue;

            if (best is null || candidate.RootPath.Length > best.RootPath.Length)
            {
                best = candidate;
            }
        }

        return best;
    }

    private static bool IsUnder(string childPath, string potentialAncestorPath)
    {
        var child = Path.GetFullPath(childPath).TrimEnd(Path.DirectorySeparatorChar);
        var ancestor = Path.GetFullPath(potentialAncestorPath).TrimEnd(Path.DirectorySeparatorChar);

        if (string.Equals(child, ancestor, PathComparer.Comparison)) return false;
        return child.StartsWith(ancestor + Path.DirectorySeparatorChar, PathComparer.Comparison);
    }

    private static string GetDisplayName(string path) =>
        Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : path;
}
