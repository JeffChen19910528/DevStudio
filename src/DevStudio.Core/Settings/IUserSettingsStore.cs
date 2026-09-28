namespace DevStudio.Core.Settings;

/// <summary>
/// Persists <see cref="AppSettings"/> across restarts (SKILL.md §25). A missing or corrupted
/// store MUST NOT throw or crash the application — <see cref="LoadAsync"/> returns null and the
/// caller falls back to defaults (mirrors the workspace-state recovery contract in SKILL.md §13).
/// </summary>
public interface IUserSettingsStore
{
    Task<AppSettings?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
