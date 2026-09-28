namespace DevStudio.Core.Workspace;

/// <summary>
/// Directory names hidden from the Explorer by default (SKILL.md §9). Purely a display filter —
/// excluded directories are never deleted, modified, or skipped during any future build/toolchain
/// operation. Configurable so no single project layout is assumed.
/// </summary>
public sealed class WorkspaceExclusionRules
{
    public static IReadOnlyList<string> DefaultExcludedDirectoryNames { get; } = new[]
    {
        ".git", "bin", "obj", "node_modules", ".vscode", ".idea", ".vs"
    };

    // Must be declared after DefaultExcludedDirectoryNames: static field initializers run in
    // textual declaration order, and this constructor reads that field as its fallback.
    public static WorkspaceExclusionRules Default { get; } = new();

    private readonly HashSet<string> _excludedDirectoryNames;

    public WorkspaceExclusionRules(IEnumerable<string>? excludedDirectoryNames = null)
    {
        _excludedDirectoryNames = new HashSet<string>(
            excludedDirectoryNames ?? DefaultExcludedDirectoryNames,
            StringComparer.OrdinalIgnoreCase);
    }

    public bool IsExcluded(string directoryName) => _excludedDirectoryNames.Contains(directoryName);

    public IReadOnlyCollection<string> ExcludedDirectoryNames => _excludedDirectoryNames;
}
