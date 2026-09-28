namespace DevStudio.Core.Workspace;

/// <summary>
/// Enumerates a workspace directory into a <see cref="FileSystemNode"/> tree. Must be
/// non-blocking on large repositories (SKILL.md §26): implementations enumerate one directory
/// level at a time so the Explorer can expand lazily rather than walking the entire tree
/// up front. Read-only — never creates, deletes, or modifies anything (SKILL.md §9).
/// </summary>
public interface IWorkspaceScanner
{
    /// <summary>Lists the immediate children of <paramref name="directoryPath"/>, applying <paramref name="exclusionRules"/>.</summary>
    Task<IReadOnlyList<FileSystemNode>> GetChildrenAsync(
        string directoryPath,
        WorkspaceExclusionRules exclusionRules,
        CancellationToken cancellationToken = default);
}
