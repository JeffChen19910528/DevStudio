namespace DevStudio.Core.Projects;

/// <summary>Full result of detecting a workspace's project structure (SKILL.md §9): top-level
/// projects/solutions (already nested via <see cref="ProjectInfo.ChildProjects"/> where one
/// project's directory contains another's), plus the flat list every project appears in exactly
/// once, for cheap lookup by id or by owning path.</summary>
public sealed record WorkspaceProjectGraph(
    IReadOnlyList<ProjectInfo> TopLevelProjects,
    IReadOnlyList<SolutionInfo> Solutions,
    IReadOnlyList<ProjectInfo> AllProjects);
