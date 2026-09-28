namespace DevStudio.Core.Git;

/// <summary>
/// Drives the real <c>git</c> executable via <c>GitService → IGitAdapter → GitCliAdapter →
/// IProcessRunner → real git</c> (SKILL.md §2, §6 [Phase 9]). Every method operates against an
/// explicit repository root rather than an implicit "current" one, so a workspace containing
/// multiple repositories never has one repository's operation accidentally applied to another's
/// working tree (SKILL.md §9).
/// </summary>
public interface IGitAdapter
{
    /// <summary>Resolves the real repository root containing <paramref name="path"/> via Git
    /// itself (<c>git rev-parse --show-toplevel</c>) — never by walking directories looking only
    /// for a <c>.git</c> folder (SKILL.md §8). Returns <c>null</c> when <paramref name="path"/>
    /// is not inside a Git repository at all.</summary>
    Task<string?> FindRepositoryRootAsync(string path, CancellationToken cancellationToken = default);

    Task<GitRepositoryStatus> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitBranch>> GetBranchesAsync(string repositoryRoot, CancellationToken cancellationToken = default);

    /// <summary>Returns the most recent <paramref name="maxCount"/> commits reachable from
    /// HEAD, oldest-to-newest reversed (newest first) — an empty list for a repository with no
    /// commits yet, never an exception (a real, unborn-branch repository legitimately has none).</summary>
    Task<IReadOnlyList<GitCommit>> GetLogAsync(string repositoryRoot, int maxCount = 100, CancellationToken cancellationToken = default);

    /// <summary>Returns the real diff for one file, or <c>null</c> when Git reports no
    /// difference for it (including an untracked file not yet staged, which <c>git diff</c>
    /// never covers — SKILL.md §15's "do not invent diff results").</summary>
    Task<GitDiff?> GetDiffAsync(string repositoryRoot, string relativePath, bool staged, CancellationToken cancellationToken = default);

    Task<GitOperationResult> StageAsync(string repositoryRoot, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default);

    Task<GitOperationResult> UnstageAsync(string repositoryRoot, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default);

    /// <summary>Discards working-tree changes for tracked files only (<c>git restore</c>) —
    /// never deletes an untracked file; see SKILL.md §18's explicit "Unsupported / Deferred"
    /// permission for untracked-file deletion.</summary>
    Task<GitOperationResult> DiscardChangesAsync(string repositoryRoot, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default);

    Task<GitOperationResult> CommitAsync(string repositoryRoot, string message, CancellationToken cancellationToken = default);

    /// <summary>Never forces a checkout — a real Git refusal (uncommitted changes that would be
    /// overwritten) is surfaced as <see cref="GitErrorKind.CheckoutBlocked"/> with the actual
    /// Git message, never silently discarded/stashed/reset around (SKILL.md §24).</summary>
    Task<GitOperationResult> CheckoutBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default);

    Task<GitOperationResult> CreateBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default);

    /// <summary>Always a safe (non-force) delete — never <c>git branch -D</c> (SKILL.md §26).</summary>
    Task<GitOperationResult> DeleteBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default);
}
