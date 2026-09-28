using System.Text.Json;
using DevStudio.Core.Settings;

namespace DevStudio.Infrastructure.Settings;

/// <summary>
/// Persists <see cref="AppSettings"/> as JSON under the user's application-data directory
/// (SKILL.md §25). A missing or corrupted file is treated as "no saved settings yet" — it is
/// never allowed to crash startup (SKILL.md §13's recovery contract, applied here too).
/// </summary>
public sealed class JsonUserSettingsStore : IUserSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _filePath;

    public JsonUserSettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DevStudio",
            "settings.json");
    }

    public async Task<AppSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_filePath)) return null;

            using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, Options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (directory is not null) Directory.CreateDirectory(directory);

        using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken).ConfigureAwait(false);
    }
}
