namespace DevStudio.Core.Git;

/// <summary>
/// Normalized change classification, mapped from real <c>git status --porcelain=v2</c> letter
/// codes (SKILL.md §11 [Phase 9]: never collapsed into one generic "Modified"). <c>Unmerged</c>
/// covers every real conflict combination porcelain v2's "u" line type reports — the specific
/// combination (both added, both modified, etc.) is not distinguished further this phase.
/// </summary>
public enum GitChangeType
{
    Unmodified,
    Modified,
    Added,
    Deleted,
    Renamed,
    Copied,
    TypeChanged,
    Unmerged,
    Untracked,
    Ignored,
}

/// <summary>
/// One file's real, normalized status (SKILL.md §7, §11). <see cref="IndexStatus"/>/<see
/// cref="WorktreeStatus"/> preserve porcelain v2's real distinction between the index (staged)
/// and working tree columns — a file can genuinely be staged-Modified and worktree-Modified at
/// once (staged one change, then made another). <see cref="OriginalPath"/> is populated only for
/// a real rename/copy porcelain v2 itself reported; it is never guessed by DevStudio.
/// </summary>
public sealed record GitFileStatus(
    string Path,
    string? OriginalPath,
    GitChangeType IndexStatus,
    GitChangeType WorktreeStatus,
    bool IsStaged,
    bool IsWorkingTreeModified,
    bool IsUntracked,
    bool IsIgnored,
    bool IsConflicted);

/// <summary>
/// A repository's real, current status (SKILL.md §10, §37) — always freshly re-queried from
/// <c>git status</c>, never inferred from a previous run's cached result plus a guess about what
/// changed.
/// </summary>
public sealed record GitRepositoryStatus(
    string RepositoryRoot,
    string? CurrentBranch,
    string? UpstreamBranch,
    int AheadBy,
    int BehindBy,
    IReadOnlyList<GitFileStatus> Files)
{
    public bool IsClean => Files.Count == 0;
}

public sealed record GitBranch(
    string Name,
    bool IsCurrent,
    bool IsRemote,
    string? UpstreamName,
    string CommitSha);

public sealed record GitCommit(
    string Sha,
    string ShortSha,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthorDate,
    string Subject);

public enum GitDiffLineKind
{
    Context,
    Added,
    Removed,
}

public sealed record GitDiffLine(GitDiffLineKind Kind, string Content);

public sealed record GitDiffHunk(
    string Header,
    int OldStart,
    int OldLineCount,
    int NewStart,
    int NewLineCount,
    IReadOnlyList<GitDiffLine> Lines);

/// <summary>
/// A real, parsed unified diff for one file (SKILL.md §15–§16). <see cref="IsBinary"/> is set
/// when Git itself reports "Binary files ... differ" — never guessed from a file extension —
/// and <see cref="Hunks"/> is then always empty rather than containing fabricated/garbled text.
/// </summary>
public sealed record GitDiff(string Path, string? OriginalPath, bool IsBinary, IReadOnlyList<GitDiffHunk> Hunks);

/// <summary>Normalized Git failure classification (SKILL.md §35). Only <see
/// cref="GitCommandFailed"/> is derived purely from a nonzero exit code (always reliable);
/// every other value is a best-effort match against Git's own (English-locale) stderr text and
/// may fall back to <see cref="GitCommandFailed"/> if the real message doesn't match a known
/// pattern — the real, raw message is always preserved in <see cref="GitOperationResult.Message"/>
/// regardless of which kind was inferred.</summary>
public enum GitErrorKind
{
    None,
    NotRepository,
    GitNotInstalled,
    NothingToCommit,
    CheckoutBlocked,
    InvalidBranchName,
    BranchDeletionBlocked,
    OperationCancelled,
    GitCommandFailed,
}

public sealed record GitOperationResult(bool Succeeded, GitErrorKind ErrorKind, string? Message, string? RawOutput = null)
{
    public static GitOperationResult Success(string? rawOutput = null) => new(true, GitErrorKind.None, null, rawOutput);
    public static GitOperationResult Failure(GitErrorKind kind, string message, string? rawOutput = null) => new(false, kind, message, rawOutput);
}
