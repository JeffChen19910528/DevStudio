using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DevStudio.Core.Git;
using DevStudio.Core.Processes;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Git;

/// <summary>
/// Drives the real <c>git</c> executable (SKILL.md §5–§6, §10, §16, §21, §36 [Phase 9]).
/// Resolves the executable from the live <see cref="IToolchainRegistry"/> result, exactly like
/// <see cref="Build.DotNetBuildAdapter"/>/<see cref="Testing.DotNetTestAdapter"/> resolve
/// <c>dotnet</c> — never a hard-coded path, never assumed present merely because Phase 3
/// detected it once at startup. Every status/branch/log query uses a real, stable
/// machine-readable Git output format (NUL-separated porcelain v2 for status, a unit/record
/// -separator-framed custom format for log, tab-separated <c>--format</c> fields for branches)
/// rather than parsing Git's localized human-oriented text (SKILL.md §36).
/// </summary>
public sealed class GitCliAdapter : IGitAdapter
{
    // Real `git log --format` field/record separators: 0x1f (unit separator) between fields
    // within one commit, 0x1e (record separator) after each commit — verified against a real
    // repository before writing this parser (see ADR-010). Neither control character can appear
    // in a commit's own subject line under normal use, unlike '\t'/'\n'/'|', so there is no
    // realistic field-collision risk.
    private const char LogFieldSeparator = '\x1f';
    private const char LogRecordSeparator = '\x1e';
    private const string LogFormat = "%H\x1f%h\x1f%an\x1f%ae\x1f%aI\x1f%s\x1e";

    private static readonly Regex HunkHeaderRegex = new(
        @"^@@ -(?<oldStart>\d+)(,(?<oldCount>\d+))? \+(?<newStart>\d+)(,(?<newCount>\d+))? @@",
        RegexOptions.Compiled);

    private readonly IProcessRunner _processRunner;
    private readonly IToolchainRegistry _toolchainRegistry;

    public GitCliAdapter(IProcessRunner processRunner, IToolchainRegistry toolchainRegistry)
    {
        _processRunner = processRunner;
        _toolchainRegistry = toolchainRegistry;
    }

    public async Task<string?> FindRepositoryRootAsync(string path, CancellationToken cancellationToken = default)
    {
        var git = ResolveGit();
        var result = await RunAsync(git, new[] { "rev-parse", "--show-toplevel" }, path, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) return null;

        // Git always reports the toplevel with forward slashes, even on Windows; normalize to
        // this OS's native separators so callers can compare it against other OS paths directly.
        var raw = result.StandardOutput.Trim();
        return raw.Length == 0 ? null : raw.Replace('/', Path.DirectorySeparatorChar);
    }

    public async Task<GitRepositoryStatus> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        var git = ResolveGit();
        var result = await RunAsync(git, new[] { "status", "--porcelain=v2", "-z", "--branch" }, repositoryRoot, cancellationToken).ConfigureAwait(false);
        return ParseStatus(repositoryRoot, result.StandardOutput);
    }

    public async Task<IReadOnlyList<GitBranch>> GetBranchesAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        var git = ResolveGit();
        const string format = "%(refname:short)\t%(objectname)\t%(upstream:short)\t%(HEAD)";

        var local = await RunAsync(git, new[] { "branch", "--format=" + format }, repositoryRoot, cancellationToken).ConfigureAwait(false);
        var remote = await RunAsync(git, new[] { "branch", "-r", "--format=" + format }, repositoryRoot, cancellationToken).ConfigureAwait(false);

        var branches = new List<GitBranch>();
        branches.AddRange(ParseBranches(local.StandardOutput, isRemote: false));
        branches.AddRange(ParseBranches(remote.StandardOutput, isRemote: true));
        return branches;
    }

    public async Task<IReadOnlyList<GitCommit>> GetLogAsync(string repositoryRoot, int maxCount = 100, CancellationToken cancellationToken = default)
    {
        var git = ResolveGit();
        var result = await RunAsync(git, new[] { "log", "--format=" + LogFormat, "-n", maxCount.ToString(CultureInfo.InvariantCulture) }, repositoryRoot, cancellationToken).ConfigureAwait(false);

        // A repository with no commits yet ("unborn HEAD") makes `git log` fail with a real,
        // expected non-zero exit code and a fatal message — an empty history, not an error to
        // surface to the user (SKILL.md §7's "an empty list, never an exception" contract).
        if (result.ExitCode != 0) return Array.Empty<GitCommit>();

        return ParseLog(result.StandardOutput);
    }

    public async Task<GitDiff?> GetDiffAsync(string repositoryRoot, string relativePath, bool staged, CancellationToken cancellationToken = default)
    {
        var git = ResolveGit();
        var arguments = new List<string> { "diff" };
        if (staged) arguments.Add("--staged");
        arguments.Add("--");
        arguments.Add(relativePath);

        var result = await RunAsync(git, arguments, repositoryRoot, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(result.StandardOutput)) return null;

        return ParseDiff(result.StandardOutput);
    }

    public async Task<GitOperationResult> StageAsync(string repositoryRoot, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "add", "--" };
        arguments.AddRange(relativePaths);
        return await RunOperationAsync(arguments, repositoryRoot, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GitOperationResult> UnstageAsync(string repositoryRoot, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "restore", "--staged", "--" };
        arguments.AddRange(relativePaths);
        return await RunOperationAsync(arguments, repositoryRoot, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GitOperationResult> DiscardChangesAsync(string repositoryRoot, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
    {
        // `git restore` only ever touches tracked paths — it never deletes an untracked file,
        // matching SKILL.md §18's requirement that untracked-file deletion stay unimplemented
        // rather than reached accidentally through this method.
        var arguments = new List<string> { "restore", "--" };
        arguments.AddRange(relativePaths);
        return await RunOperationAsync(arguments, repositoryRoot, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GitOperationResult> CommitAsync(string repositoryRoot, string message, CancellationToken cancellationToken = default)
    {
        var result = await RunOperationAsync(new[] { "commit", "-m", message }, repositoryRoot, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded) return result;

        // Real Git's own authoritative check for "nothing staged" (rather than a redundant
        // pre-flight status call from DevStudio) — matched against the actual English-locale
        // message this machine's Git produces; the raw message is preserved either way.
        if (result.RawOutput?.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase) == true)
        {
            return GitOperationResult.Failure(GitErrorKind.NothingToCommit, "There are no staged changes to commit.", result.RawOutput);
        }

        return result;
    }

    public async Task<GitOperationResult> CheckoutBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default)
    {
        var result = await RunOperationAsync(new[] { "checkout", branchName }, repositoryRoot, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded) return result;

        if (result.RawOutput?.Contains("Your local changes", StringComparison.OrdinalIgnoreCase) == true ||
            result.RawOutput?.Contains("overwritten by checkout", StringComparison.OrdinalIgnoreCase) == true)
        {
            return GitOperationResult.Failure(GitErrorKind.CheckoutBlocked, "Checkout was blocked because it would overwrite real uncommitted changes.", result.RawOutput);
        }

        return result;
    }

    public async Task<GitOperationResult> CreateBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default)
    {
        var result = await RunOperationAsync(new[] { "branch", branchName }, repositoryRoot, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded) return result;

        if (result.RawOutput?.Contains("not a valid branch name", StringComparison.OrdinalIgnoreCase) == true)
        {
            return GitOperationResult.Failure(GitErrorKind.InvalidBranchName, $"'{branchName}' is not a valid branch name.", result.RawOutput);
        }

        return result;
    }

    public async Task<GitOperationResult> DeleteBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default)
    {
        // Never `-D` (force) — a real, non-force `-d` delete, so Git itself refuses when the
        // branch has unmerged commits (SKILL.md §26) rather than DevStudio deciding to force it.
        var result = await RunOperationAsync(new[] { "branch", "-d", branchName }, repositoryRoot, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded) return result;

        if (result.RawOutput?.Contains("not fully merged", StringComparison.OrdinalIgnoreCase) == true ||
            result.RawOutput?.Contains("checked out at", StringComparison.OrdinalIgnoreCase) == true)
        {
            return GitOperationResult.Failure(GitErrorKind.BranchDeletionBlocked, "Git refused to delete this branch.", result.RawOutput);
        }

        return result;
    }

    private async Task<GitOperationResult> RunOperationAsync(IReadOnlyList<string> arguments, string repositoryRoot, CancellationToken cancellationToken)
    {
        var git = ResolveGit();
        if (git is null)
        {
            return GitOperationResult.Failure(GitErrorKind.GitNotInstalled, "Git is not installed or has not been detected. Run Tools → Refresh Toolchains and try again.");
        }

        var result = await RunAsync(git, arguments, repositoryRoot, cancellationToken).ConfigureAwait(false);
        if (result.WasCancelled)
        {
            return GitOperationResult.Failure(GitErrorKind.OperationCancelled, "The Git operation was cancelled.");
        }

        var rawOutput = string.IsNullOrEmpty(result.StandardError) ? result.StandardOutput : result.StandardError;
        return result.ExitCode == 0
            ? GitOperationResult.Success(result.StandardOutput)
            : GitOperationResult.Failure(GitErrorKind.GitCommandFailed, rawOutput.Trim(), rawOutput);
    }

    private Task<ProcessResult> RunAsync(string? git, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        if (git is null)
        {
            return Task.FromResult(new ProcessResult(-1, string.Empty, "Git is not installed or has not been detected.", TimeSpan.Zero, WasCancelled: false, WasTimedOut: false));
        }

        var request = new ProcessStartRequest(git, arguments, workingDirectory, OutputEncoding: Encoding.UTF8);
        return _processRunner.RunAsync(request, cancellationToken: cancellationToken);
    }

    private string? ResolveGit()
    {
        var git = _toolchainRegistry.Get(WellKnownToolchainIds.Git);
        if (git is null || !git.IsUsable) return null;
        return git.ExecutablePath ?? "git";
    }

    // ------------------------------------------------------------------------------------------
    // Parsing — every format below was verified against a real Git repository before this
    // parser was written (see ADR-010's "Status parsing strategy"/"Log parsing strategy"
    // sections for the exact captured samples).
    // ------------------------------------------------------------------------------------------

    private static GitRepositoryStatus ParseStatus(string repositoryRoot, string output)
    {
        string? currentBranch = null;
        string? upstreamBranch = null;
        var aheadBy = 0;
        var behindBy = 0;
        var files = new List<GitFileStatus>();

        var records = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < records.Length; i++)
        {
            var record = records[i];

            if (record.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                var head = record["# branch.head ".Length..];
                currentBranch = head == "(detached)" ? null : head;
            }
            else if (record.StartsWith("# branch.upstream ", StringComparison.Ordinal))
            {
                upstreamBranch = record["# branch.upstream ".Length..];
            }
            else if (record.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                var parts = record["# branch.ab ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    if (part.StartsWith('+') && int.TryParse(part[1..], out var ahead)) aheadBy = ahead;
                    else if (part.StartsWith('-') && int.TryParse(part[1..], out var behind)) behindBy = behind;
                }
            }
            else if (record.StartsWith("# ", StringComparison.Ordinal))
            {
                // Another real "# branch.*" header this parser doesn't need (e.g. branch.oid).
            }
            else if (record.StartsWith("? ", StringComparison.Ordinal))
            {
                files.Add(new GitFileStatus(record[2..], null, GitChangeType.Unmodified, GitChangeType.Untracked, false, true, true, false, false));
            }
            else if (record.StartsWith("! ", StringComparison.Ordinal))
            {
                files.Add(new GitFileStatus(record[2..], null, GitChangeType.Unmodified, GitChangeType.Ignored, false, false, false, true, false));
            }
            else if (record.StartsWith("1 ", StringComparison.Ordinal))
            {
                files.Add(ParseOrdinaryEntry(record));
            }
            else if (record.StartsWith("2 ", StringComparison.Ordinal))
            {
                // The original path is real Git's own NEXT NUL-delimited record, not embedded
                // in this one — verified against a real `git mv` before writing this parser
                // (see ADR-010).
                var originalPath = i + 1 < records.Length ? records[++i] : null;
                files.Add(ParseRenameOrCopyEntry(record, originalPath));
            }
            else if (record.StartsWith("u ", StringComparison.Ordinal))
            {
                files.Add(ParseUnmergedEntry(record));
            }
        }

        return new GitRepositoryStatus(repositoryRoot, currentBranch, upstreamBranch, aheadBy, behindBy, files);
    }

    // "1 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <path>"
    private static GitFileStatus ParseOrdinaryEntry(string record)
    {
        var fields = record.Split(' ', 9);
        var xy = fields[1];
        var path = fields[8];
        var index = MapStatusLetter(xy[0]);
        var worktree = MapStatusLetter(xy[1]);
        return new GitFileStatus(path, null, index, worktree, index != GitChangeType.Unmodified, worktree != GitChangeType.Unmodified, false, false, false);
    }

    // "2 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <X><score> <path>" — <origPath> is the caller's
    // next NUL-delimited record, per real porcelain v2 output (verified via a real `git mv`
    // before writing this parser — see ADR-010).
    private static GitFileStatus ParseRenameOrCopyEntry(string record, string? originalPath)
    {
        // One extra field versus an ordinary "1" entry: <X><score> (e.g. "R100") sits between
        // <hI> and <path> — verified against a real `git mv` (see ADR-010); a `Split(' ', 9)`
        // here would leave "R100 renamed.txt" un-split as the final element.
        var fields = record.Split(' ', 10);
        var xy = fields[1];
        var path = fields[9];
        var index = MapStatusLetter(xy[0]);
        var worktree = MapStatusLetter(xy[1]);
        return new GitFileStatus(path, originalPath, index, worktree, true, worktree != GitChangeType.Unmodified, false, false, false);
    }

    // "u <XY> <sub> <m1> <m2> <m3> <mW> <h1> <h2> <h3> <path>"
    private static GitFileStatus ParseUnmergedEntry(string record)
    {
        var fields = record.Split(' ', 11);
        var path = fields[10];
        return new GitFileStatus(path, null, GitChangeType.Unmerged, GitChangeType.Unmerged, false, true, false, false, true);
    }

    private static GitChangeType MapStatusLetter(char letter) => letter switch
    {
        '.' => GitChangeType.Unmodified,
        'M' => GitChangeType.Modified,
        'A' => GitChangeType.Added,
        'D' => GitChangeType.Deleted,
        'R' => GitChangeType.Renamed,
        'C' => GitChangeType.Copied,
        'T' => GitChangeType.TypeChanged,
        'U' => GitChangeType.Unmerged,
        _ => GitChangeType.Unmodified,
    };

    private static IReadOnlyList<GitBranch> ParseBranches(string output, bool isRemote)
    {
        var branches = new List<GitBranch>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.TrimEnd('\r').Split('\t');
            if (fields.Length < 4) continue;

            var name = fields[0];
            var sha = fields[1];
            var upstream = fields[2].Length == 0 ? null : fields[2];
            var isCurrent = fields[3] == "*";
            branches.Add(new GitBranch(name, isCurrent, isRemote, upstream, sha));
        }
        return branches;
    }

    private static IReadOnlyList<GitCommit> ParseLog(string output)
    {
        var commits = new List<GitCommit>();
        foreach (var record in output.Split(LogRecordSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = record.TrimStart('\n', '\r');
            var fields = trimmed.Split(LogFieldSeparator);
            if (fields.Length < 6) continue;

            var authorDate = DateTimeOffset.TryParse(fields[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : DateTimeOffset.MinValue;

            commits.Add(new GitCommit(fields[0], fields[1], fields[2], fields[3], authorDate, fields[5]));
        }
        return commits;
    }

    private static GitDiff ParseDiff(string output)
    {
        var lines = output.Split('\n');
        string? path = null;
        string? originalPath = null;
        var isBinary = false;
        var hunks = new List<GitDiffHunk>();

        string? currentHeader = null;
        int oldStart = 0, oldCount = 0, newStart = 0, newCount = 0;
        var currentLines = new List<GitDiffLine>();

        void FlushHunk()
        {
            if (currentHeader is null) return;
            hunks.Add(new GitDiffHunk(currentHeader, oldStart, oldCount, newStart, newCount, currentLines));
            currentLines = new List<GitDiffLine>();
            currentHeader = null;
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                FlushHunk();
                var match = Regex.Match(line, @"^diff --git a/(?<a>.+) b/(?<b>.+)$");
                if (match.Success)
                {
                    originalPath = match.Groups["a"].Value;
                    path = match.Groups["b"].Value;
                }
                continue;
            }

            if (line.StartsWith("Binary files ", StringComparison.Ordinal) && line.EndsWith(" differ", StringComparison.Ordinal))
            {
                isBinary = true;
                continue;
            }

            if (line.StartsWith("---", StringComparison.Ordinal) || line.StartsWith("+++", StringComparison.Ordinal) ||
                line.StartsWith("index ", StringComparison.Ordinal) || line.StartsWith("new file mode", StringComparison.Ordinal) ||
                line.StartsWith("deleted file mode", StringComparison.Ordinal) || line.StartsWith("old mode", StringComparison.Ordinal) ||
                line.StartsWith("new mode", StringComparison.Ordinal) || line.StartsWith("similarity index", StringComparison.Ordinal) ||
                line.StartsWith("rename from", StringComparison.Ordinal) || line.StartsWith("rename to", StringComparison.Ordinal))
            {
                continue;
            }

            var hunkMatch = HunkHeaderRegex.Match(line);
            if (hunkMatch.Success)
            {
                FlushHunk();
                currentHeader = line;
                oldStart = int.Parse(hunkMatch.Groups["oldStart"].Value, CultureInfo.InvariantCulture);
                oldCount = hunkMatch.Groups["oldCount"].Success ? int.Parse(hunkMatch.Groups["oldCount"].Value, CultureInfo.InvariantCulture) : 1;
                newStart = int.Parse(hunkMatch.Groups["newStart"].Value, CultureInfo.InvariantCulture);
                newCount = hunkMatch.Groups["newCount"].Success ? int.Parse(hunkMatch.Groups["newCount"].Value, CultureInfo.InvariantCulture) : 1;
                continue;
            }

            if (currentHeader is null) continue;

            if (line.StartsWith("+", StringComparison.Ordinal)) currentLines.Add(new GitDiffLine(GitDiffLineKind.Added, line[1..]));
            else if (line.StartsWith("-", StringComparison.Ordinal)) currentLines.Add(new GitDiffLine(GitDiffLineKind.Removed, line[1..]));
            else if (line.StartsWith(" ", StringComparison.Ordinal)) currentLines.Add(new GitDiffLine(GitDiffLineKind.Context, line[1..]));
            // A line reading exactly "\ No newline at end of file" is a real Git diff marker,
            // not file content — deliberately not added as a content line.
        }
        FlushHunk();

        return new GitDiff(path ?? string.Empty, originalPath, isBinary, hunks);
    }
}
