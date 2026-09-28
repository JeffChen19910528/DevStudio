namespace DevStudio.Core.Workspace;

/// <summary>Result of attempting to load a workspace's persisted state (SKILL.md §13): a
/// corrupted or unreadable file is reported via <see cref="WasCorrupted"/>, not an exception,
/// and the caller decides whether to proceed with a fresh workspace.</summary>
public sealed record WorkspaceStateLoadResult(WorkspaceState? State, bool WasCorrupted);

/// <summary>Persists one workspace's <see cref="WorkspaceState"/> (SKILL.md §11, §13). Never
/// stores source code or secrets — metadata only.</summary>
public interface IWorkspaceStateStore
{
    Task<WorkspaceStateLoadResult> LoadAsync(string workspaceRootPath, CancellationToken cancellationToken = default);

    Task SaveAsync(string workspaceRootPath, WorkspaceState state, CancellationToken cancellationToken = default);
}
