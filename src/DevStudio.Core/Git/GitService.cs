using System.Collections.Concurrent;
using DevStudio.Core.Platform;

namespace DevStudio.Core.Git;

/// <summary>
/// Dispatches to the single real <see cref="IGitAdapter"/> and enforces "one active mutating
/// operation per repository" (SKILL.md §34) — unlike <c>BuildService</c>/<c>TestService</c>'s
/// single global <see cref="System.Threading.CancellationTokenSource"/> (there is only ever one
/// build/test target active at a time), a workspace may contain multiple real Git repositories
/// (SKILL.md §9) that must be free to run operations independently of one another, so
/// concurrency is tracked per repository root rather than globally. Read-only operations
/// (status/log/diff/branch listing/repository discovery) are never restricted — only Stage/
/// Unstage/Discard/Commit/CheckoutBranch/CreateBranch/DeleteBranch go through the per-repository
/// gate, since those are the operations SKILL.md §34 calls out as mutating.
/// </summary>
public sealed class GitService
{
    private readonly IGitAdapter _adapter;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeMutations = new(PathComparer.Comparer);

    public GitService(IGitAdapter adapter) => _adapter = adapter;

    public bool IsMutationRunning(string repositoryRoot) => _activeMutations.ContainsKey(repositoryRoot);

    /// <summary>Cancels whichever mutating operation is currently running for this repository;
    /// a no-op if none is.</summary>
    public void CancelMutation(string repositoryRoot)
    {
        if (_activeMutations.TryGetValue(repositoryRoot, out var cts)) cts.Cancel();
    }

    public Task<string?> FindRepositoryRootAsync(string path, CancellationToken cancellationToken = default) =>
        _adapter.FindRepositoryRootAsync(path, cancellationToken);

    public Task<GitRepositoryStatus> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
        _adapter.GetStatusAsync(repositoryRoot, cancellationToken);

    public Task<IReadOnlyList<GitBranch>> GetBranchesAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
        _adapter.GetBranchesAsync(repositoryRoot, cancellationToken);

    public Task<IReadOnlyList<GitCommit>> GetLogAsync(string repositoryRoot, int maxCount = 100, CancellationToken cancellationToken = default) =>
        _adapter.GetLogAsync(repositoryRoot, maxCount, cancellationToken);

    public Task<GitDiff?> GetDiffAsync(string repositoryRoot, string relativePath, bool staged, CancellationToken cancellationToken = default) =>
        _adapter.GetDiffAsync(repositoryRoot, relativePath, staged, cancellationToken);

    public Task<GitOperationResult> StageAsync(string repositoryRoot, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default) =>
        RunMutationAsync(repositoryRoot, ct => _adapter.StageAsync(repositoryRoot, relativePaths, ct), cancellationToken);

    public Task<GitOperationResult> UnstageAsync(string repositoryRoot, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default) =>
        RunMutationAsync(repositoryRoot, ct => _adapter.UnstageAsync(repositoryRoot, relativePaths, ct), cancellationToken);

    public Task<GitOperationResult> DiscardChangesAsync(string repositoryRoot, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default) =>
        RunMutationAsync(repositoryRoot, ct => _adapter.DiscardChangesAsync(repositoryRoot, relativePaths, ct), cancellationToken);

    public Task<GitOperationResult> CommitAsync(string repositoryRoot, string message, CancellationToken cancellationToken = default) =>
        RunMutationAsync(repositoryRoot, ct => _adapter.CommitAsync(repositoryRoot, message, ct), cancellationToken);

    public Task<GitOperationResult> CheckoutBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default) =>
        RunMutationAsync(repositoryRoot, ct => _adapter.CheckoutBranchAsync(repositoryRoot, branchName, ct), cancellationToken);

    public Task<GitOperationResult> CreateBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default) =>
        RunMutationAsync(repositoryRoot, ct => _adapter.CreateBranchAsync(repositoryRoot, branchName, ct), cancellationToken);

    public Task<GitOperationResult> DeleteBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default) =>
        RunMutationAsync(repositoryRoot, ct => _adapter.DeleteBranchAsync(repositoryRoot, branchName, ct), cancellationToken);

    private async Task<GitOperationResult> RunMutationAsync(
        string repositoryRoot,
        Func<CancellationToken, Task<GitOperationResult>> operation,
        CancellationToken cancellationToken)
    {
        var cts = new CancellationTokenSource();
        if (!_activeMutations.TryAdd(repositoryRoot, cts))
        {
            cts.Dispose();
            throw new InvalidOperationException($"A Git operation is already running for '{repositoryRoot}'. Cancel it or wait for it to finish before starting another.");
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
            return await operation(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return GitOperationResult.Failure(GitErrorKind.OperationCancelled, "The Git operation was cancelled.");
        }
        finally
        {
            _activeMutations.TryRemove(repositoryRoot, out _);
            cts.Dispose();
        }
    }
}
