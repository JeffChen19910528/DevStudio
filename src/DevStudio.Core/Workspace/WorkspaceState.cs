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
    string? ActiveProjectId)
{
    public const int CurrentVersion = 1;
}
