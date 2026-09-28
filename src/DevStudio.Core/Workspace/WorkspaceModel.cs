using DevStudio.Core.Projects;

namespace DevStudio.Core.Workspace;

/// <summary>
/// A DevStudio workspace: a root folder, the projects/solutions detected within it, and the
/// build/run configuration surface for them (SKILL.md §8–§9). Named WorkspaceModel to avoid
/// colliding with UI-framework "Workspace" types used by higher layers. Carries no execution
/// logic of its own — build/run/debug orchestration is a later phase's concern.
/// </summary>
public sealed record WorkspaceModel(
    string Id,
    string RootPath,
    string Name,
    IReadOnlyList<ProjectInfo> Projects,
    IReadOnlyList<SolutionInfo> Solutions,
    IReadOnlyList<BuildConfiguration> BuildConfigurations,
    bool IsTrusted = false,
    string? ActiveProjectId = null);
