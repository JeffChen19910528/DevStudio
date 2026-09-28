using DevStudio.Core.Extensions;

namespace DevStudio.Infrastructure.Extensions;

/// <summary>
/// Real filesystem extension discovery (SKILL.md §10–§11 [Phase 10]): each given root's
/// immediate subdirectories only (never recursive, never the whole filesystem) are checked for a
/// real <c>devstudio.extension.json</c>; a subdirectory with none is not even reported as
/// Invalid — it simply isn't an extension. Never activates anything itself.
/// </summary>
public sealed class FileSystemExtensionDiscovery : IExtensionDiscovery
{
    public const string ManifestFileName = "devstudio.extension.json";

    public async Task<IReadOnlyList<ExtensionDescriptor>> DiscoverAsync(IReadOnlyList<string> roots, CancellationToken cancellationToken = default)
    {
        var results = new List<ExtensionDescriptor>();

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root)) continue;

            IEnumerable<string> subdirectories;
            try
            {
                subdirectories = Directory.EnumerateDirectories(root).ToList();
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var directory in subdirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var descriptor = await DiscoverOneAsync(directory, cancellationToken).ConfigureAwait(false);
                if (descriptor is not null) results.Add(descriptor);
            }
        }

        return results;
    }

    private static async Task<ExtensionDescriptor?> DiscoverOneAsync(string directory, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(directory, ManifestFileName);
        if (!File.Exists(manifestPath)) return null;

        string json;
        try
        {
            json = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ExtensionDescriptor(directory, null, ExtensionState.Invalid, new[] { $"Unable to read manifest: {ex.Message}" });
        }

        var parseResult = ExtensionManifestParser.Parse(json, directory);
        if (!parseResult.Succeeded)
        {
            return new ExtensionDescriptor(directory, null, ExtensionState.Invalid, parseResult.Errors);
        }

        var manifest = parseResult.Manifest!;
        if (!manifest.HostVersionRange.IsSatisfiedBy(ExtensionHostInfo.HostVersion))
        {
            return new ExtensionDescriptor(directory, manifest, ExtensionState.Incompatible, new[]
            {
                $"This extension requires host version range '{manifest.HostVersionRange}', but DevStudio is running host version {ExtensionHostInfo.HostVersion}.",
            });
        }

        if (!File.Exists(manifest.EntryPoint))
        {
            return new ExtensionDescriptor(directory, manifest, ExtensionState.Invalid, new[]
            {
                $"Entry point '{manifest.EntryPoint}' does not exist.",
            });
        }

        return new ExtensionDescriptor(directory, manifest, ExtensionState.Valid, Array.Empty<string>());
    }
}
