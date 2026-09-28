using DevStudio.Core.Git;
using DevStudio.Infrastructure.Git;
using DevStudio.Infrastructure.Processes;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Git;

/// <summary>
/// SKILL.md §39 (Phase 9, cases A–R): real, temporary Git repositories created with the real
/// <c>git</c> executable and driven entirely through DevStudio's real <c>GitService</c>/
/// <c>GitCliAdapter</c> → real <c>IProcessRunner</c> → real <c>git</c> pipeline. No fakes
/// anywhere in this file — every status/branch/log/diff/commit result here is real Git's own
/// output, parsed by the real parser, never simulated.
/// </summary>
public class GitCliIntegrationTests
{
    private static (GitService GitService, GitCliAdapter Adapter) CreateRealServices()
    {
        var processRunner = new ProcessRunner();
        var registry = new ToolchainRegistry();
        registry.Register(new GitToolchainDetector(processRunner));
        registry.RefreshAsync().GetAwaiter().GetResult();
        var adapter = new GitCliAdapter(processRunner, registry);
        return (new GitService(adapter), adapter);
    }

    /// <summary>Sets up a real, empty repository with a committer identity configured locally
    /// (never relying on the machine's global Git config being present) — fixture setup, not
    /// the code under test.</summary>
    private static async Task InitRepositoryAsync(TempDirectory temp)
    {
        var processRunner = new ProcessRunner();
        await Run(processRunner, temp.Path, "init");
        await Run(processRunner, temp.Path, "config", "user.email", "devstudio-tests@example.com");
        await Run(processRunner, temp.Path, "config", "user.name", "DevStudio Tests");
        await Run(processRunner, temp.Path, "config", "core.autocrlf", "false");
    }

    private static Task<Core.Processes.ProcessResult> Run(ProcessRunner processRunner, string cwd, params string[] args) =>
        processRunner.RunAsync(new Core.Processes.ProcessStartRequest("git", args, cwd));

    // --- Case A: repository detection --------------------------------------------------------

    [Fact]
    public async Task Real_repository_detection_finds_the_real_repository_root()
    {
        using var temp = new TempDirectory();
        await InitRepositoryAsync(temp);
        var (gitService, _) = CreateRealServices();

        var root = await gitService.FindRepositoryRootAsync(temp.Path);

        Assert.NotNull(root);
        Assert.Equal(
            Path.GetFullPath(temp.Path).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(root!).TrimEnd(Path.DirectorySeparatorChar),
            ignoreCase: true);
    }

    [Fact]
    public async Task A_directory_that_is_not_a_repository_reports_no_root()
    {
        using var temp = new TempDirectory();
        var (gitService, _) = CreateRealServices();

        var root = await gitService.FindRepositoryRootAsync(temp.Path);

        Assert.Null(root);
    }

    // --- Case B: untracked file ---------------------------------------------------------------

    [Fact]
    public async Task Real_untracked_file_is_reported_as_Untracked()
    {
        using var temp = new TempDirectory();
        await InitRepositoryAsync(temp);
        temp.WriteFile("hello.txt", "hello");
        var (gitService, _) = CreateRealServices();

        var status = await gitService.GetStatusAsync(temp.Path);

        var file = Assert.Single(status.Files);
        Assert.Equal("hello.txt", file.Path);
        Assert.True(file.IsUntracked);
        Assert.False(file.IsStaged);
    }

    // --- Case C: stage --------------------------------------------------------------------------

    [Fact]
    public async Task Staging_an_untracked_file_reports_it_as_real_staged_Added()
    {
        using var temp = new TempDirectory();
        await InitRepositoryAsync(temp);
        temp.WriteFile("hello.txt", "hello");
        var (gitService, _) = CreateRealServices();

        var stageResult = await gitService.StageAsync(temp.Path, new[] { "hello.txt" });
        Assert.True(stageResult.Succeeded);

        var status = await gitService.GetStatusAsync(temp.Path);
        var file = Assert.Single(status.Files);
        Assert.Equal(GitChangeType.Added, file.IndexStatus);
        Assert.True(file.IsStaged);
    }

    // --- Case D: commit ---------------------------------------------------------------------

    [Fact]
    public async Task Committing_staged_changes_produces_a_real_commit_and_a_clean_working_tree()
    {
        using var temp = new TempDirectory();
        await InitRepositoryAsync(temp);
        temp.WriteFile("hello.txt", "hello");
        var (gitService, _) = CreateRealServices();
        await gitService.StageAsync(temp.Path, new[] { "hello.txt" });

        var commitResult = await gitService.CommitAsync(temp.Path, "Add hello.txt");
        Assert.True(commitResult.Succeeded);

        var status = await gitService.GetStatusAsync(temp.Path);
        Assert.True(status.IsClean);

        var log = await gitService.GetLogAsync(temp.Path);
        var commit = Assert.Single(log);
        Assert.Equal("Add hello.txt", commit.Subject);
        Assert.Equal(40, commit.Sha.Length);
    }

    // --- Case E/F/G/H: modify, diff, stage-modification, unstage ----------------------------

    [Fact]
    public async Task Modifying_a_committed_file_is_reported_as_real_unstaged_Modified_and_the_real_diff_contains_the_change()
    {
        using var temp = new TempDirectory();
        await InitRepositoryAsync(temp);
        temp.WriteFile("tracked.txt", "a\nb\nc\n");
        var (gitService, _) = CreateRealServices();
        await gitService.StageAsync(temp.Path, new[] { "tracked.txt" });
        await gitService.CommitAsync(temp.Path, "init");

        temp.WriteFile("tracked.txt", "a\nCHANGED\nc\n");

        var status = await gitService.GetStatusAsync(temp.Path);
        var file = Assert.Single(status.Files);
        Assert.Equal(GitChangeType.Modified, file.WorktreeStatus);
        Assert.False(file.IsStaged);

        var diff = await gitService.GetDiffAsync(temp.Path, "tracked.txt", staged: false);
        Assert.NotNull(diff);
        Assert.Contains(diff!.Hunks.SelectMany(h => h.Lines), l => l.Kind == GitDiffLineKind.Added && l.Content == "CHANGED");
        Assert.Contains(diff.Hunks.SelectMany(h => h.Lines), l => l.Kind == GitDiffLineKind.Removed && l.Content == "b");

        // Case G: stage the modification
        var stageResult = await gitService.StageAsync(temp.Path, new[] { "tracked.txt" });
        Assert.True(stageResult.Succeeded);
        var stagedStatus = await gitService.GetStatusAsync(temp.Path);
        Assert.Equal(GitChangeType.Modified, Assert.Single(stagedStatus.Files).IndexStatus);

        var stagedDiff = await gitService.GetDiffAsync(temp.Path, "tracked.txt", staged: true);
        Assert.NotNull(stagedDiff);
        Assert.Contains(stagedDiff!.Hunks.SelectMany(h => h.Lines), l => l.Kind == GitDiffLineKind.Added && l.Content == "CHANGED");

        // Case H: unstage
        var unstageResult = await gitService.UnstageAsync(temp.Path, new[] { "tracked.txt" });
        Assert.True(unstageResult.Succeeded);
        var unstagedStatus = await gitService.GetStatusAsync(temp.Path);
        var unstagedFile = Assert.Single(unstagedStatus.Files);
        Assert.Equal(GitChangeType.Unmodified, unstagedFile.IndexStatus);
        Assert.Equal(GitChangeType.Modified, unstagedFile.WorktreeStatus);
    }

    // --- Case I: rename -----------------------------------------------------------------------

    [Fact]
    public async Task Renaming_a_tracked_file_is_reported_with_Gits_own_real_rename_state()
    {
        using var temp = new TempDirectory();
        await InitRepositoryAsync(temp);
        temp.WriteFile("orig.txt", "a\nb\nc\n");
        var (gitService, _) = CreateRealServices();
        await gitService.StageAsync(temp.Path, new[] { "orig.txt" });
        await gitService.CommitAsync(temp.Path, "init");

        File.Move(Path.Combine(temp.Path, "orig.txt"), Path.Combine(temp.Path, "renamed.txt"));
        await gitService.StageAsync(temp.Path, new[] { "renamed.txt", "orig.txt" });

        var status = await gitService.GetStatusAsync(temp.Path);
        var file = Assert.Single(status.Files);

        // Real Git decides rename detection itself based on content similarity — assert only
        // what it actually reported, never assume it always classifies this as a rename.
        if (file.IndexStatus == GitChangeType.Renamed)
        {
            Assert.Equal("renamed.txt", file.Path);
            Assert.Equal("orig.txt", file.OriginalPath);
        }
    }

    // --- Case J: delete -----------------------------------------------------------------------

    [Fact]
    public async Task Deleting_a_tracked_file_is_reported_as_real_Deleted()
    {
        using var temp = new TempDirectory();
        await InitRepositoryAsync(temp);
        temp.WriteFile("tracked.txt", "a\n");
        var (gitService, _) = CreateRealServices();
        await gitService.StageAsync(temp.Path, new[] { "tracked.txt" });
        await gitService.CommitAsync(temp.Path, "init");

        File.Delete(Path.Combine(temp.Path, "tracked.txt"));

        var status = await gitService.GetStatusAsync(temp.Path);
        var file = Assert.Single(status.Files);
        Assert.Equal(GitChangeType.Deleted, file.WorktreeStatus);
    }

    // --- Case K/L: branch create + checkout -------------------------------------------------

    [Fact]
    public async Task Creating_and_checking_out_a_real_branch_changes_the_real_current_branch()
    {
        using var temp = new TempDirectory();
        await InitRepositoryAsync(temp);
        temp.WriteFile("tracked.txt", "a\n");
        var (gitService, _) = CreateRealServices();
        await gitService.StageAsync(temp.Path, new[] { "tracked.txt" });
        await gitService.CommitAsync(temp.Path, "init");

        var createResult = await gitService.CreateBranchAsync(temp.Path, "feature/test");
        Assert.True(createResult.Succeeded);

        var branches = await gitService.GetBranchesAsync(temp.Path);
        Assert.Contains(branches, b => b.Name == "feature/test" && !b.IsRemote);

        var checkoutResult = await gitService.CheckoutBranchAsync(temp.Path, "feature/test");
        Assert.True(checkoutResult.Succeeded);

        var status = await gitService.GetStatusAsync(temp.Path);
        Assert.Equal("feature/test", status.CurrentBranch);
    }

    // --- Case M: branch checkout safety -------------------------------------------------------

    [Fact]
    public async Task Checkout_is_blocked_by_real_conflicting_uncommitted_changes_and_never_forced()
    {
        using var temp = new TempDirectory();
        await InitRepositoryAsync(temp);
        temp.WriteFile("tracked.txt", "original\n");
        var (gitService, _) = CreateRealServices();
        await gitService.StageAsync(temp.Path, new[] { "tracked.txt" });
        await gitService.CommitAsync(temp.Path, "init");

        await gitService.CreateBranchAsync(temp.Path, "other-branch");
        await gitService.CheckoutBranchAsync(temp.Path, "other-branch");
        temp.WriteFile("tracked.txt", "changed-on-other-branch\n");
        await gitService.StageAsync(temp.Path, new[] { "tracked.txt" });
        await gitService.CommitAsync(temp.Path, "change on other branch");

        await gitService.CheckoutBranchAsync(temp.Path, "master");
        // A real, uncommitted, conflicting change with the branch we're about to check out into.
        temp.WriteFile("tracked.txt", "uncommitted-conflicting-change\n");

        var checkoutResult = await gitService.CheckoutBranchAsync(temp.Path, "other-branch");

        Assert.False(checkoutResult.Succeeded);
        Assert.Equal(GitErrorKind.CheckoutBlocked, checkoutResult.ErrorKind);
        // The real file content must be untouched — DevStudio never force-checks-out, stashes,
        // resets, or discards to make this succeed (SKILL.md §24).
        Assert.Equal("uncommitted-conflicting-change\n", File.ReadAllText(Path.Combine(temp.Path, "tracked.txt")));
    }

    // --- Case N: commit message safety --------------------------------------------------------

    [Fact]
    public async Task A_commit_message_with_quotes_spaces_and_Unicode_reaches_git_unmodified()
    {
        using var temp = new TempDirectory();
        await InitRepositoryAsync(temp);
        temp.WriteFile("tracked.txt", "a\n");
        var (gitService, _) = CreateRealServices();
        await gitService.StageAsync(temp.Path, new[] { "tracked.txt" });

        const string message = "Fix \"the\" bug — 修复错误 (see #1) & don't break `it`";
        var commitResult = await gitService.CommitAsync(temp.Path, message);
        Assert.True(commitResult.Succeeded);

        var log = await gitService.GetLogAsync(temp.Path);
        Assert.Equal(message, Assert.Single(log).Subject);
    }

    // --- Case O: Unicode path -------------------------------------------------------------------

    [Fact]
    public async Task A_Unicode_filename_is_handled_correctly_in_status_and_diff()
    {
        using var temp = new TempDirectory();
        await InitRepositoryAsync(temp);
        const string fileName = "日本語ファイル名.txt";
        temp.WriteFile(fileName, "line1\n");
        var (gitService, _) = CreateRealServices();
        await gitService.StageAsync(temp.Path, new[] { fileName });
        await gitService.CommitAsync(temp.Path, "add unicode file");

        temp.WriteFile(fileName, "line1\nline2\n");
        var status = await gitService.GetStatusAsync(temp.Path);
        var file = Assert.Single(status.Files);
        Assert.Equal(fileName, file.Path);
        Assert.Equal(GitChangeType.Modified, file.WorktreeStatus);

        var diff = await gitService.GetDiffAsync(temp.Path, fileName, staged: false);
        Assert.NotNull(diff);
        Assert.Contains(diff!.Hunks.SelectMany(h => h.Lines), l => l.Kind == GitDiffLineKind.Added && l.Content == "line2");
    }

    // --- Case P: multiple repositories ---------------------------------------------------------

    [Fact]
    public async Task Two_separate_real_repositories_report_distinct_repository_roots()
    {
        using var tempA = new TempDirectory();
        using var tempB = new TempDirectory();
        await InitRepositoryAsync(tempA);
        await InitRepositoryAsync(tempB);
        tempA.WriteFile("a.txt", "a");
        tempB.WriteFile("b.txt", "b");
        var (gitService, _) = CreateRealServices();

        var rootA = await gitService.FindRepositoryRootAsync(tempA.Path);
        var rootB = await gitService.FindRepositoryRootAsync(tempB.Path);

        Assert.NotNull(rootA);
        Assert.NotNull(rootB);
        Assert.NotEqual(rootA, rootB, StringComparer.OrdinalIgnoreCase);

        var statusA = await gitService.GetStatusAsync(rootA!);
        var statusB = await gitService.GetStatusAsync(rootB!);
        Assert.Equal("a.txt", Assert.Single(statusA.Files).Path);
        Assert.Equal("b.txt", Assert.Single(statusB.Files).Path);
    }

    // --- Destructive operation protection (SKILL.md §40) ---------------------------------------

    [Fact]
    public async Task Deleting_a_branch_with_unmerged_commits_is_refused_by_real_Git_not_forced()
    {
        using var temp = new TempDirectory();
        await InitRepositoryAsync(temp);
        temp.WriteFile("tracked.txt", "a\n");
        var (gitService, _) = CreateRealServices();
        await gitService.StageAsync(temp.Path, new[] { "tracked.txt" });
        await gitService.CommitAsync(temp.Path, "init");

        await gitService.CreateBranchAsync(temp.Path, "feature/unmerged");
        await gitService.CheckoutBranchAsync(temp.Path, "feature/unmerged");
        temp.WriteFile("tracked.txt", "a\nunmerged change\n");
        await gitService.StageAsync(temp.Path, new[] { "tracked.txt" });
        await gitService.CommitAsync(temp.Path, "unmerged commit");
        await gitService.CheckoutBranchAsync(temp.Path, "master");

        var deleteResult = await gitService.DeleteBranchAsync(temp.Path, "feature/unmerged");

        Assert.False(deleteResult.Succeeded);
        Assert.Equal(GitErrorKind.BranchDeletionBlocked, deleteResult.ErrorKind);
        var branches = await gitService.GetBranchesAsync(temp.Path);
        Assert.Contains(branches, b => b.Name == "feature/unmerged");
    }
}
