using DevStudio.Core.Git;
using Xunit;

namespace DevStudio.Core.Tests;

file sealed class FakeGitAdapter : IGitAdapter
{
    public List<string> StageCalls { get; } = new();
    public List<string> UnstageCalls { get; } = new();
    public List<string> CommitCalls { get; } = new();
    public bool ThrowOnStage { get; set; }
    public TaskCompletionSource? HangUntilCancelled { get; set; }
    public GitOperationResult ResultToReturn { get; set; } = GitOperationResult.Success();

    public Task<string?> FindRepositoryRootAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(path);

    public Task<GitRepositoryStatus> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GitRepositoryStatus(repositoryRoot, "main", null, 0, 0, Array.Empty<GitFileStatus>()));

    public Task<IReadOnlyList<GitBranch>> GetBranchesAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GitBranch>>(Array.Empty<GitBranch>());

    public Task<IReadOnlyList<GitCommit>> GetLogAsync(string repositoryRoot, int maxCount = 100, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GitCommit>>(Array.Empty<GitCommit>());

    public Task<GitDiff?> GetDiffAsync(string repositoryRoot, string relativePath, bool staged, CancellationToken cancellationToken = default) =>
        Task.FromResult<GitDiff?>(null);

    public async Task<GitOperationResult> StageAsync(string repositoryRoot, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
    {
        StageCalls.Add(repositoryRoot);
        if (ThrowOnStage) throw new InvalidOperationException("simulated adapter failure");
        if (HangUntilCancelled is not null)
        {
            using var registration = cancellationToken.Register(() => HangUntilCancelled.TrySetResult());
            await HangUntilCancelled.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        return ResultToReturn;
    }

    public Task<GitOperationResult> UnstageAsync(string repositoryRoot, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
    {
        UnstageCalls.Add(repositoryRoot);
        return Task.FromResult(ResultToReturn);
    }

    public Task<GitOperationResult> DiscardChangesAsync(string repositoryRoot, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default) =>
        Task.FromResult(ResultToReturn);

    public Task<GitOperationResult> CommitAsync(string repositoryRoot, string message, CancellationToken cancellationToken = default)
    {
        CommitCalls.Add(message);
        return Task.FromResult(ResultToReturn);
    }

    public Task<GitOperationResult> CheckoutBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default) =>
        Task.FromResult(ResultToReturn);

    public Task<GitOperationResult> CreateBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default) =>
        Task.FromResult(ResultToReturn);

    public Task<GitOperationResult> DeleteBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default) =>
        Task.FromResult(ResultToReturn);
}

public class GitServiceTests
{
    [Fact]
    public async Task StageAsync_dispatches_to_the_adapter_and_returns_its_result()
    {
        var adapter = new FakeGitAdapter();
        var service = new GitService(adapter);

        var result = await service.StageAsync("/repo", new[] { "a.txt" });

        Assert.True(result.Succeeded);
        Assert.Equal("/repo", Assert.Single(adapter.StageCalls));
    }

    [Fact]
    public async Task A_second_mutating_operation_against_the_same_repository_is_rejected_while_one_is_running()
    {
        var adapter = new FakeGitAdapter { HangUntilCancelled = new TaskCompletionSource() };
        var service = new GitService(adapter);

        var first = service.StageAsync("/repo", new[] { "a.txt" });
        Assert.True(service.IsMutationRunning("/repo"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UnstageAsync("/repo", new[] { "a.txt" }));

        adapter.HangUntilCancelled!.TrySetResult();
        await first;
    }

    [Fact]
    public async Task Mutations_against_different_repositories_do_not_block_each_other()
    {
        var adapter = new FakeGitAdapter { HangUntilCancelled = new TaskCompletionSource() };
        var service = new GitService(adapter);

        var first = service.StageAsync("/repo-a", new[] { "a.txt" });
        Assert.True(service.IsMutationRunning("/repo-a"));

        var second = await service.CommitAsync("/repo-b", "message");
        Assert.True(second.Succeeded);

        adapter.HangUntilCancelled!.TrySetResult();
        await first;
    }

    [Fact]
    public async Task CancelMutation_cancels_the_in_flight_operation_and_reports_OperationCancelled()
    {
        var adapter = new FakeGitAdapter { HangUntilCancelled = new TaskCompletionSource() };
        var service = new GitService(adapter);

        var pending = service.StageAsync("/repo", new[] { "a.txt" });
        service.CancelMutation("/repo");

        var result = await pending;

        Assert.False(result.Succeeded);
        Assert.Equal(GitErrorKind.OperationCancelled, result.ErrorKind);
        Assert.False(service.IsMutationRunning("/repo"));
    }

    [Fact]
    public async Task An_adapter_exception_during_a_mutation_propagates_rather_than_being_silently_swallowed()
    {
        var adapter = new FakeGitAdapter { ThrowOnStage = true };
        var service = new GitService(adapter);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StageAsync("/repo", new[] { "a.txt" }));
        Assert.False(service.IsMutationRunning("/repo"));
    }

    [Fact]
    public async Task Read_only_operations_are_never_gated_by_the_mutation_lock()
    {
        var adapter = new FakeGitAdapter { HangUntilCancelled = new TaskCompletionSource() };
        var service = new GitService(adapter);

        var pending = service.StageAsync("/repo", new[] { "a.txt" });

        var status = await service.GetStatusAsync("/repo");
        var branches = await service.GetBranchesAsync("/repo");
        var log = await service.GetLogAsync("/repo");

        Assert.NotNull(status);
        Assert.Empty(branches);
        Assert.Empty(log);

        adapter.HangUntilCancelled!.TrySetResult();
        await pending;
    }
}
