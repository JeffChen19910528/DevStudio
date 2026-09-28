namespace DevStudio.Core.Workspace;

/// <summary>One entry in the workspace file tree (SKILL.md §7–§8). Directory children are loaded
/// lazily by <see cref="IWorkspaceScanner"/> — this record itself carries no I/O behavior.</summary>
public sealed record FileSystemNode(
    string Name,
    string FullPath,
    bool IsDirectory,
    IReadOnlyList<FileSystemNode>? Children = null);
