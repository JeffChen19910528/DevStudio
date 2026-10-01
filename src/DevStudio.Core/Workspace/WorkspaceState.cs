namespace DevStudio.Core.Workspace;

/// <summary>
/// Everything about a workspace that should survive closing and reopening it (SKILL.md §10):
/// which documents were open, which one was active, and which project was active. Explicitly
/// versioned (SKILL.md §12) so a future format change has something to branch on; today there
/// is exactly one version and no migration path yet.
/// </summary>
public sealed record WorkspaceState(
    int Version,
    string RootPath,
    IReadOnlyList<string> OpenDocumentPaths,
    string? ActiveDocumentPath,
    string? ActiveProjectId,
    /// <summary>Last-used build configuration for the main project (e.g. "Debug", "Release").
    /// Null means "use the application default" so old saved states stay valid after upgrading.</summary>
    string? BuildConfigurationName = null,
    /// <summary>Last-used build configuration for dependency projects. Null means "use the
    /// application default". Stored independently so each workspace can mix configurations
    /// (e.g. main=Debug, deps=Release) without affecting other workspaces.</summary>
    string? DependencyBuildConfigurationName = null)
{
    public const int CurrentVersion = 1;
}
