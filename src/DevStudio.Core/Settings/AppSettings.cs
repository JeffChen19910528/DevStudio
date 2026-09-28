namespace DevStudio.Core.Settings;

/// <summary>
/// Configuration surface (SKILL.md §22, extended by §25–§27 in Phase 2). Persisted at the user
/// level via <see cref="IUserSettingsStore"/> (Phase 2); per-workspace state (open tabs, active
/// project) is a separate concern — see <see cref="Workspace.WorkspaceState"/> — so a workspace's
/// tab layout never lives in this record. Never holds secrets: nothing here is a credential.
/// </summary>
public sealed record AppSettings(
    AppTheme Theme = AppTheme.Dark,
    string FontFamily = "Consolas,Cascadia Mono,monospace",
    double FontSize = 13,
    IReadOnlyList<string>? ExcludedDirectories = null,
    string? DefaultShell = null,
    bool ReopenLastWorkspaceOnStartup = false,
    IReadOnlyList<RecentWorkspaceEntry> RecentWorkspaces = null!,
    /// <summary>Extension ids the user has explicitly disabled (SKILL.md §23 [Phase 10]) — never
    /// extension runtime state/instances/delegates, only the persisted enable/disable choice
    /// itself.</summary>
    IReadOnlyList<string>? DisabledExtensionIds = null,
    /// <summary>The user's selected UI culture name (e.g. <c>"en-US"</c>, <c>"zh-TW"</c> — SKILL.md
    /// §8 [Phase 12]), a user-level application preference like <see cref="Theme"/>, never a
    /// workspace-specific setting. Defaults to <c>"en-US"</c> on first launch. Stored as a plain
    /// string (not a <see cref="System.Globalization.CultureInfo"/>) so this record — and the JSON
    /// it round-trips through — never depends on the localization layer at all.</summary>
    string Language = "en-US")
{
    public IReadOnlyList<RecentWorkspaceEntry> RecentWorkspaces { get; init; } = RecentWorkspaces ?? Array.Empty<RecentWorkspaceEntry>();
    public IReadOnlyList<string> DisabledExtensionIds { get; init; } = DisabledExtensionIds ?? Array.Empty<string>();
}
