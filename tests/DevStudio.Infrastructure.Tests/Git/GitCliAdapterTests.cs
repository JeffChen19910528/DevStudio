using DevStudio.Core.Git;
using DevStudio.Core.Toolchains;
using DevStudio.Infrastructure.Git;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Git;

/// <summary>Unit tests for <see cref="GitCliAdapter"/>'s command construction and its parsing of
/// real, captured Git output samples — no real repository or process needed for these (that's
/// <see cref="GitCliIntegrationTests"/>).</summary>
public class GitCliAdapterTests
{
    private static async Task<ToolchainRegistry> RegistryWithGitDetectedAsync()
    {
        var registry = new ToolchainRegistry();
        registry.Register(new FixedGitDetector());
        await registry.RefreshAsync();
        return registry;
    }

    private sealed class FixedGitDetector : IToolchainDetector
    {
        public string ToolchainId => WellKnownToolchainIds.Git;
        public Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ToolchainInfo(ToolchainId, "Git", ToolchainDetectionState.Detected, ExecutablePath: "git"));
    }

    [Fact]
    public async Task FindRepositoryRootAsync_uses_rev_parse_show_toplevel()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("git", 0, "C:/repo\n");
        var adapter = new GitCliAdapter(runner, await RegistryWithGitDetectedAsync());

        var root = await adapter.FindRepositoryRootAsync(@"C:\repo\sub");

        var sent = Assert.Single(runner.Requests);
        Assert.Equal(new[] { "rev-parse", "--show-toplevel" }, sent.Arguments);
        // Real Git always reports the toplevel with forward slashes, even on Windows; the
        // adapter normalizes to *this* OS's own separator (SKILL.md §10 [Phase 11]) — asserting
        // a hard-coded backslash here was itself a real, non-portable test bug found by actually
        // running this suite on Linux, where the adapter correctly produces "C:/repo" instead.
        Assert.Equal("C:/repo".Replace('/', Path.DirectorySeparatorChar), root);
    }

    [Fact]
    public async Task FindRepositoryRootAsync_returns_null_when_git_reports_not_a_repository()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("git", 128, "", "fatal: not a git repository");
        var adapter = new GitCliAdapter(runner, await RegistryWithGitDetectedAsync());

        var root = await adapter.FindRepositoryRootAsync(@"C:\notarepo");

        Assert.Null(root);
    }

    [Fact]
    public async Task GetStatusAsync_parses_a_real_captured_porcelain_v2_sample()
    {
        // A real sample captured from `git status --porcelain=v2 -z --branch` (see ADR-010):
        // one modified-unstaged tracked file, one untracked file, on branch "master" with no
        // upstream configured.
        const string sample =
            "# branch.oid ac20278a5f118eb1e80316ea313c11251684a62a\0" +
            "# branch.head master\0" +
            "1 .M N... 100644 100644 100644 de980441c3ab03a8c07dda1ad27b8a11f39deb1e de980441c3ab03a8c07dda1ad27b8a11f39deb1e tracked.txt\0" +
            "? untracked.txt\0";

        var runner = new FakeProcessRunner();
        runner.SetResult("git", 0, sample);
        var adapter = new GitCliAdapter(runner, await RegistryWithGitDetectedAsync());

        var status = await adapter.GetStatusAsync(@"C:\repo");

        var sent = Assert.Single(runner.Requests);
        Assert.Equal(new[] { "status", "--porcelain=v2", "-z", "--branch" }, sent.Arguments);

        Assert.Equal("master", status.CurrentBranch);
        Assert.Equal(2, status.Files.Count);

        var tracked = status.Files.Single(f => f.Path == "tracked.txt");
        Assert.Equal(GitChangeType.Unmodified, tracked.IndexStatus);
        Assert.Equal(GitChangeType.Modified, tracked.WorktreeStatus);
        Assert.False(tracked.IsStaged);
        Assert.True(tracked.IsWorkingTreeModified);

        var untracked = status.Files.Single(f => f.Path == "untracked.txt");
        Assert.True(untracked.IsUntracked);
    }

    [Fact]
    public async Task GetStatusAsync_parses_a_real_captured_rename_sample_with_upstream_and_ahead_behind()
    {
        const string sample =
            "# branch.oid ac20278\0" +
            "# branch.head master\0" +
            "# branch.upstream origin/master\0" +
            "# branch.ab +2 -1\0" +
            "2 R. N... 100644 100644 100644 de980441c3ab03a8c07dda1ad27b8a11f39deb1e de980441c3ab03a8c07dda1ad27b8a11f39deb1e R100 renamed.txt\0" +
            "orig.txt\0";

        var runner = new FakeProcessRunner();
        runner.SetResult("git", 0, sample);
        var adapter = new GitCliAdapter(runner, await RegistryWithGitDetectedAsync());

        var status = await adapter.GetStatusAsync(@"C:\repo");

        Assert.Equal("origin/master", status.UpstreamBranch);
        Assert.Equal(2, status.AheadBy);
        Assert.Equal(1, status.BehindBy);

        var file = Assert.Single(status.Files);
        Assert.Equal("renamed.txt", file.Path);
        Assert.Equal("orig.txt", file.OriginalPath);
        Assert.Equal(GitChangeType.Renamed, file.IndexStatus);
        Assert.True(file.IsStaged);
    }

    [Fact]
    public async Task GetLogAsync_parses_a_real_captured_field_and_record_separated_sample()
    {
        // \x1f/\x1e are hex escapes that greedily consume up to 4 following hex digits (e.g.
        // "\x1fac20278" would parse as one U+1FAC character, not 0x1F followed by literal
        // text) — \u001f/\u001e are always exactly 4 hex digits, so they don't have this trap.
        const string sample = "ac20278a5f118eb1e80316ea313c11251684a62a\u001fac20278\u001fa\u001fa@a.com\u001f2026-09-28T14:30:35+08:00\u001finit\u001e\n";

        var runner = new FakeProcessRunner();
        runner.SetResult("git", 0, sample);
        var adapter = new GitCliAdapter(runner, await RegistryWithGitDetectedAsync());

        var log = await adapter.GetLogAsync(@"C:\repo");

        var commit = Assert.Single(log);
        Assert.Equal("ac20278a5f118eb1e80316ea313c11251684a62a", commit.Sha);
        Assert.Equal("ac20278", commit.ShortSha);
        Assert.Equal("a", commit.AuthorName);
        Assert.Equal("a@a.com", commit.AuthorEmail);
        Assert.Equal("init", commit.Subject);
    }

    [Fact]
    public async Task GetLogAsync_returns_an_empty_list_for_a_repository_with_no_commits_yet_rather_than_throwing()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("git", 128, "", "fatal: your current branch 'master' does not have any commits yet");
        var adapter = new GitCliAdapter(runner, await RegistryWithGitDetectedAsync());

        var log = await adapter.GetLogAsync(@"C:\repo");

        Assert.Empty(log);
    }

    [Fact]
    public async Task GetDiffAsync_parses_a_real_captured_unified_diff_sample()
    {
        const string sample =
            "diff --git a/tracked.txt b/tracked.txt\n" +
            "index de98044..7be73ce 100644\n" +
            "--- a/tracked.txt\n" +
            "+++ b/tracked.txt\n" +
            "@@ -1,3 +1,3 @@\n" +
            " a\n" +
            "-b\n" +
            "+B\n" +
            " c\n";

        var runner = new FakeProcessRunner();
        runner.SetResult("git", 0, sample);
        var adapter = new GitCliAdapter(runner, await RegistryWithGitDetectedAsync());

        var diff = await adapter.GetDiffAsync(@"C:\repo", "tracked.txt", staged: false);

        Assert.NotNull(diff);
        Assert.False(diff!.IsBinary);
        var hunk = Assert.Single(diff.Hunks);
        Assert.Equal(1, hunk.OldStart);
        Assert.Equal(3, hunk.OldLineCount);
        Assert.Collection(hunk.Lines,
            l => Assert.Equal((GitDiffLineKind.Context, "a"), (l.Kind, l.Content)),
            l => Assert.Equal((GitDiffLineKind.Removed, "b"), (l.Kind, l.Content)),
            l => Assert.Equal((GitDiffLineKind.Added, "B"), (l.Kind, l.Content)),
            l => Assert.Equal((GitDiffLineKind.Context, "c"), (l.Kind, l.Content)));
    }

    [Fact]
    public async Task GetDiffAsync_marks_a_real_binary_file_as_binary_never_as_garbled_text()
    {
        const string sample = "diff --git a/image.png b/image.png\nBinary files a/image.png and b/image.png differ\n";

        var runner = new FakeProcessRunner();
        runner.SetResult("git", 0, sample);
        var adapter = new GitCliAdapter(runner, await RegistryWithGitDetectedAsync());

        var diff = await adapter.GetDiffAsync(@"C:\repo", "image.png", staged: false);

        Assert.NotNull(diff);
        Assert.True(diff!.IsBinary);
        Assert.Empty(diff.Hunks);
    }

    [Fact]
    public async Task GetDiffAsync_returns_null_when_git_reports_no_difference()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("git", 0, "");
        var adapter = new GitCliAdapter(runner, await RegistryWithGitDetectedAsync());

        var diff = await adapter.GetDiffAsync(@"C:\repo", "unchanged.txt", staged: false);

        Assert.Null(diff);
    }

    [Fact]
    public async Task StageAsync_passes_paths_as_separate_structured_arguments_after_a_double_dash()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("git", 0, "");
        var adapter = new GitCliAdapter(runner, await RegistryWithGitDetectedAsync());

        await adapter.StageAsync(@"C:\repo", new[] { "a file.txt", "b.txt" });

        var sent = Assert.Single(runner.Requests);
        Assert.Equal(new[] { "add", "--", "a file.txt", "b.txt" }, sent.Arguments);
    }

    [Fact]
    public async Task CommitAsync_passes_the_whole_message_as_one_argument_never_shell_concatenated()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("git", 0, "");
        var adapter = new GitCliAdapter(runner, await RegistryWithGitDetectedAsync());

        const string message = "Fix \"the\" bug & don't break `it`";
        await adapter.CommitAsync(@"C:\repo", message);

        var sent = Assert.Single(runner.Requests);
        Assert.Equal(new[] { "commit", "-m", message }, sent.Arguments);
    }

    [Fact]
    public async Task CommitAsync_maps_a_real_nothing_to_commit_failure_to_NothingToCommit()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("git", 1, "On branch master\nnothing to commit, working tree clean\n");
        var adapter = new GitCliAdapter(runner, await RegistryWithGitDetectedAsync());

        var result = await adapter.CommitAsync(@"C:\repo", "message");

        Assert.False(result.Succeeded);
        Assert.Equal(GitErrorKind.NothingToCommit, result.ErrorKind);
    }

    [Fact]
    public async Task CheckoutBranchAsync_maps_a_real_conflicting_changes_failure_to_CheckoutBlocked()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("git", 1, "", "error: Your local changes to the following files would be overwritten by checkout:\n\ttracked.txt");
        var adapter = new GitCliAdapter(runner, await RegistryWithGitDetectedAsync());

        var result = await adapter.CheckoutBranchAsync(@"C:\repo", "other");

        Assert.False(result.Succeeded);
        Assert.Equal(GitErrorKind.CheckoutBlocked, result.ErrorKind);
    }

    [Fact]
    public async Task DeleteBranchAsync_never_passes_the_force_flag()
    {
        var runner = new FakeProcessRunner();
        runner.SetResult("git", 0, "");
        var adapter = new GitCliAdapter(runner, await RegistryWithGitDetectedAsync());

        await adapter.DeleteBranchAsync(@"C:\repo", "feature");

        var sent = Assert.Single(runner.Requests);
        Assert.Equal(new[] { "branch", "-d", "feature" }, sent.Arguments);
        Assert.DoesNotContain("-D", sent.Arguments);
    }

    [Fact]
    public async Task Operations_fail_fast_with_GitNotInstalled_when_git_was_not_detected()
    {
        var runner = new FakeProcessRunner();
        var registry = new ToolchainRegistry();
        await registry.RefreshAsync();
        var adapter = new GitCliAdapter(runner, registry);

        var result = await adapter.StageAsync(@"C:\repo", new[] { "a.txt" });

        Assert.False(result.Succeeded);
        Assert.Equal(GitErrorKind.GitNotInstalled, result.ErrorKind);
        Assert.Empty(runner.Requests);
    }
}
