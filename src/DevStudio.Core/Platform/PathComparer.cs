namespace DevStudio.Core.Platform;

/// <summary>
/// The single, centralized answer to "does this OS's conventional filesystem treat two paths
/// differing only by case as the same path?" (SKILL.md §10 [Phase 11]) — every place in the
/// codebase that compares or keys a collection by a real filesystem path uses this rather than
/// each scattering its own <c>OperatingSystem.IsWindows()</c> check or, worse, a hard-coded
/// <see cref="StringComparer.OrdinalIgnoreCase"/> that silently assumes Windows/macOS semantics
/// everywhere, including on a case-sensitive Linux filesystem where <c>Foo.cs</c> and
/// <c>foo.cs</c> are two genuinely different real files.
///
/// Windows and macOS's conventional filesystems (NTFS, APFS in its default configuration) are
/// case-insensitive; Linux's conventional filesystem (ext4) is case-sensitive. This is a
/// deliberate, documented simplification: APFS *can* be configured case-sensitive, and NTFS
/// *can* enable POSIX case-sensitivity per-directory, but treating the OS's own conventional
/// default is the same "safe default over exotic configuration" judgment call
/// <c>ShellLocator</c>/<c>ExecutableLocator</c> already make elsewhere in this codebase — see
/// ADR-012.
/// </summary>
public static class PathComparer
{
    public static bool IsCaseInsensitive { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public static StringComparer Comparer { get; } = IsCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static StringComparison Comparison { get; } = IsCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool Equals(string? a, string? b) => string.Equals(a, b, Comparison);
}
