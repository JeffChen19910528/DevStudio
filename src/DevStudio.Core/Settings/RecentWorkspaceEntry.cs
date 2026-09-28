namespace DevStudio.Core.Settings;

/// <summary>SKILL.md §26: only the path, a display name, and when it was last opened —
/// never anything about what the workspace contains.</summary>
public sealed record RecentWorkspaceEntry(string Path, string DisplayName, DateTimeOffset LastOpenedUtc);
