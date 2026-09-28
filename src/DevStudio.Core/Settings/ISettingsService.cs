namespace DevStudio.Core.Settings;

/// <summary>
/// Holds the current <see cref="AppSettings"/>. The Phase 1 implementation is in-memory only —
/// settings do not yet survive a restart (SKILL.md §22 explicitly makes persistence optional for
/// this phase). The interface is shaped so a persisted implementation is a drop-in replacement.
/// </summary>
public interface ISettingsService
{
    AppSettings Current { get; }

    event EventHandler<AppSettings>? Changed;

    void Update(AppSettings settings);
}
