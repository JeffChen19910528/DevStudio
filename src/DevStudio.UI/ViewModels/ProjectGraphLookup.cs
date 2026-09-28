using DevStudio.Core.Platform;
using DevStudio.Core.Projects;

namespace DevStudio.UI.ViewModels;

/// <summary>Path-keyed view of a <see cref="WorkspaceProjectGraph"/> so the Explorer can annotate
/// tree nodes with the project/solution that owns their directory (SKILL.md §20) without walking
/// the graph on every node construction. Keyed/compared via <see cref="PathComparer"/> (SKILL.md
/// §10 [Phase 11]) rather than a hard-coded case-insensitive comparer.</summary>
public sealed class ProjectGraphLookup
{
    public static ProjectGraphLookup Empty { get; } = new(
        new Dictionary<string, ProjectInfo>(PathComparer.Comparer),
        new Dictionary<string, SolutionInfo>(PathComparer.Comparer));

    public IReadOnlyDictionary<string, ProjectInfo> ProjectsByRootPath { get; }
    public IReadOnlyDictionary<string, SolutionInfo> SolutionsByDirectory { get; }

    public ProjectGraphLookup(
        IReadOnlyDictionary<string, ProjectInfo> projectsByRootPath,
        IReadOnlyDictionary<string, SolutionInfo> solutionsByDirectory)
    {
        ProjectsByRootPath = projectsByRootPath;
        SolutionsByDirectory = solutionsByDirectory;
    }

    public static ProjectGraphLookup FromGraph(WorkspaceProjectGraph graph)
    {
        var projects = new Dictionary<string, ProjectInfo>(PathComparer.Comparer);
        foreach (var project in graph.AllProjects)
        {
            projects[Path.GetFullPath(project.RootPath)] = project;
        }

        var solutions = new Dictionary<string, SolutionInfo>(PathComparer.Comparer);
        foreach (var solution in graph.Solutions)
        {
            var directory = Path.GetDirectoryName(solution.SolutionFilePath);
            if (directory is not null)
            {
                solutions[Path.GetFullPath(directory)] = solution;
            }
        }

        return new ProjectGraphLookup(projects, solutions);
    }

    public ProjectInfo? FindProject(string directoryPath) =>
        ProjectsByRootPath.TryGetValue(Path.GetFullPath(directoryPath), out var project) ? project : null;

    public SolutionInfo? FindSolution(string directoryPath) =>
        SolutionsByDirectory.TryGetValue(Path.GetFullPath(directoryPath), out var solution) ? solution : null;

    /// <summary>The most specific (deepest) project whose root contains <paramref
    /// name="filePath"/>, or null if no detected project owns it (SKILL.md §23) — an
    /// intentionally conservative "Unknown" rather than guessing.</summary>
    public ProjectInfo? FindOwningProject(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        ProjectInfo? best = null;

        foreach (var project in ProjectsByRootPath.Values)
        {
            var root = Path.GetFullPath(project.RootPath).TrimEnd(Path.DirectorySeparatorChar);
            var isUnderOrAt = fullPath.StartsWith(root + Path.DirectorySeparatorChar, PathComparer.Comparison)
                || string.Equals(Path.GetDirectoryName(fullPath), root, PathComparer.Comparison);

            if (!isUnderOrAt) continue;
            if (best is null || root.Length > Path.GetFullPath(best.RootPath).Length)
            {
                best = project;
            }
        }

        return best;
    }
}
