using DevStudio.Core.Projects;

namespace DevStudio.Core.Packages;

/// <summary>
/// Stable package-manager identifiers (SKILL.md §6's "ProjectType = DotNet, never
/// = dotnet.exe" precedent applied to package managers) — never an executable name. Only the
/// Phase 13 P0 ids are real; the remainder are documented here as the known future extension
/// points so a later phase's adapters slot into the same registry without renaming anything.
/// </summary>
public static class WellKnownPackageManagerIds
{
    public const string NuGet = "nuget";
    public const string Pip = "pip";
    public const string Npm = "npm";

    // Deferred to a later phase (SKILL.md phase discipline) — intentionally not backed by any
    // adapter yet. Listed so PackageManagerRegistry / detection code can name them consistently
    // once they are implemented, instead of each future adapter inventing its own id string.
    public const string Pnpm = "pnpm";
    public const string Yarn = "yarn";
    public const string Poetry = "poetry";
    public const string Uv = "uv";
    public const string Maven = "maven";
    public const string Gradle = "gradle";
    public const string Cargo = "cargo";
    public const string GoModules = "gomodules";
    public const string Vcpkg = "vcpkg";
    public const string Conan = "conan";
}

/// <summary>How a dependency edge relates a package to its parent (SKILL.md's direct-vs-
/// transitive distinction, §3).</summary>
public enum PackageDependencyKind
{
    Direct,
    Transitive,
}

/// <summary>One real package reference resolved against a project (never invented/estimated —
/// an adapter only ever reports what its underlying tool actually returned).</summary>
public sealed record PackageReference(
    string PackageId,
    string? RequestedVersion,
    string? ResolvedVersion,
    PackageDependencyKind Kind,
    string ProjectPath,
    string PackageManagerId,
    string? TargetFramework = null,
    bool IsDevDependency = false,
    bool IsOutdated = false,
    string? LatestVersion = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>One edge in a dependency graph: <paramref name="PackageId"/> is required by
/// <paramref name="ParentPackageId"/> (null when the parent is the project itself).</summary>
public sealed record PackageDependency(
    string PackageId,
    string? VersionRange,
    PackageDependencyKind Kind,
    string? ParentPackageId);

/// <summary>A single version a package registry reports for a package (SKILL.md §15's "do not
/// assume all ecosystems behave identically" — prerelease/stable semantics are per-ecosystem but
/// this shape is common enough to share).</summary>
public sealed record PackageVersion(
    string Version,
    bool IsPrerelease,
    bool IsLatest = false,
    bool IsLatestStable = false)
{
    public bool IsStable => !IsPrerelease;
}

/// <summary>A remote or local feed a package manager resolves packages from. Never carries a
/// credential/token (SKILL.md §13's "never expose package-manager credentials or tokens") — only
/// the name/location a user would already see in their own manager's own configuration UI.</summary>
public sealed record PackageSource(
    string Name,
    string Location,
    bool Enabled,
    bool IsDefault,
    PackageSourceType SourceType);

public enum PackageSourceType
{
    Registry,
    LocalFolder,
    Other,
}

/// <summary>A remote search result. Ecosystems disagree on what metadata they expose (SKILL.md
/// §15) — everything below Version is genuinely nullable, never backfilled with a guess.</summary>
public sealed record PackageSearchResult(
    string PackageId,
    string Version,
    string? Description = null,
    string? LatestVersion = null,
    long? Downloads = null,
    string? Source = null,
    string? License = null,
    string? ProjectUrl = null);

/// <summary>Describes what capabilities apply for a specific project + package manager pairing
/// (SKILL.md §3's <c>PackageManagerCapabilities</c>) — capabilities are a fact about *this*
/// project/tool combination, not an assumption that every package manager behaves identically.
/// A Poetry-managed Python project, for example, reports <see cref="Add"/>/<see cref="Remove"/>/
/// <see cref="Update"/> as false from <see cref="PythonPackageAdapter"/> even though pip itself
/// is installed, because running pip against a Poetry-managed project would silently diverge
/// from its own declared dependency file.</summary>
public sealed record PackageManagerCapabilities(
    bool ListInstalled = false,
    bool ListDependencies = false,
    bool Search = false,
    bool Add = false,
    bool Remove = false,
    bool Update = false,
    bool Restore = false,
    bool ListOutdated = false,
    bool ManageSources = false,
    bool LockfileSupport = false,
    bool TransitiveDependencySupport = false,
    bool PrereleaseSupport = false)
{
    public static readonly PackageManagerCapabilities None = new();
}

/// <summary>One project as seen by the package-management layer — what manager applies to it and
/// what that manager can actually do for it right now (e.g. tool not installed ⇒ every capability
/// false even though the ecosystem itself was detected).</summary>
public sealed record PackageProject(
    string ProjectPath,
    ProjectType ProjectType,
    string PackageManagerId,
    string PackageManagerDisplayName,
    PackageManagerCapabilities Capabilities,
    string? UnavailableReason = null);

public enum PackageOperation
{
    ListInstalled,
    ListDependencies,
    ListOutdated,
    Search,
    Add,
    Remove,
    Update,
    Restore,
}

/// <summary>Structured result of one real package-manager invocation (SKILL.md §26 — never a raw
/// exception surfaced to the user). <see cref="ChangedFiles"/> lists real files the operation
/// modified (e.g. the project file, a lockfile) as observed, never assumed.</summary>
public sealed record PackageOperationResult(
    bool Success,
    PackageOperation Operation,
    string ProjectPath,
    string? PackageId,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<string> Diagnostics,
    int? ExitCode,
    bool WasCancelled,
    string? FailureReason,
    string? RawOutput = null)
{
    public static PackageOperationResult Cancelled(PackageOperation operation, string projectPath, string? packageId = null) =>
        new(false, operation, projectPath, packageId, Array.Empty<string>(), Array.Empty<string>(), null, true, "The operation was cancelled.");

    public static PackageOperationResult Unavailable(PackageOperation operation, string projectPath, string reason, string? packageId = null) =>
        new(false, operation, projectPath, packageId, Array.Empty<string>(), Array.Empty<string>(), null, false, reason);
}
