namespace DevStudio.Infrastructure.Projects;

/// <summary>
/// Shared defensive file-content reading for project detectors (SKILL.md §31): bounded read
/// size so a huge or maliciously crafted config file cannot exhaust memory, and every failure
/// mode returns null instead of throwing — detectors decide what a missing read means for their
/// own <see cref="DevStudio.Core.Projects.DetectionConfidence"/>.
/// </summary>
internal static class ConfigFileReading
{
    public const int MaxReadBytes = 64 * 1024;

    public static async Task<string?> TryReadHeadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var length = (int)Math.Min(stream.Length, MaxReadBytes);
            var buffer = new byte[length];
            var read = await stream.ReadAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            return System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }
}
