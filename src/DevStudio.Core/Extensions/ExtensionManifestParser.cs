using System.Text.Json;
using DevStudio.Core.Platform;

namespace DevStudio.Core.Extensions;

/// <summary>
/// Parses and validates a real <c>devstudio.extension.json</c> manifest (SKILL.md §6–§7, §30
/// [Phase 10]) — pure logic, no I/O; <c>Infrastructure.Extensions.FileSystemExtensionDiscovery</c>
/// reads the real file and hands its text here. Deterministic, strict, and safe against
/// malformed input: nothing here ever throws for bad manifest content — every failure becomes a
/// structured diagnostic in <see cref="ExtensionManifestParseResult.Errors"/>, never a silent
/// repair and never an exception escaping to the caller.
/// </summary>
public static class ExtensionManifestParser
{
    private static readonly IReadOnlyDictionary<string, ExtensionCapability> KnownCapabilities =
        Enum.GetValues<ExtensionCapability>().ToDictionary(c => c.ToString(), c => c, StringComparer.OrdinalIgnoreCase);

    public static ExtensionManifestParseResult Parse(string json, string extensionRootDirectory)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return ExtensionManifestParseResult.Failure(new[] { $"Malformed JSON: {ex.Message}" });
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ExtensionManifestParseResult.Failure(new[] { "The manifest root must be a JSON object." });
            }

            var errors = new List<string>();

            var idText = ReadString(root, "id", errors, required: true);
            ExtensionId id = default;
            if (idText is not null && !ExtensionId.TryParse(idText, out id))
            {
                errors.Add($"'id' ('{idText}') is not a valid extension id (expected lowercase, dot-separated segments, e.g. 'publisher.name', max {ExtensionId.MaxLength} characters).");
            }

            var name = ReadString(root, "name", errors, required: true) ?? string.Empty;
            var displayName = ReadString(root, "displayName", errors, required: true) ?? string.Empty;
            var publisher = ReadString(root, "publisher", errors, required: true) ?? string.Empty;
            var description = ReadString(root, "description", errors, required: false) ?? string.Empty;

            var versionText = ReadString(root, "version", errors, required: true);
            ExtensionVersion version = default;
            if (versionText is not null && !ExtensionVersion.TryParse(versionText, out version))
            {
                errors.Add($"'version' ('{versionText}') is not a valid Major.Minor.Patch version.");
            }

            var hostRangeText = ReadString(root, "hostVersionRange", errors, required: true);
            ExtensionVersionRange? hostRange = null;
            if (hostRangeText is not null && !ExtensionVersionRange.TryParse(hostRangeText, out hostRange))
            {
                errors.Add($"'hostVersionRange' ('{hostRangeText}') is not a valid version range (expected e.g. '>=1.0.0' or '>=1.0.0 <2.0.0').");
            }

            var entryPoint = ReadString(root, "entryPoint", errors, required: true);
            string? resolvedEntryPoint = null;
            if (entryPoint is not null)
            {
                resolvedEntryPoint = ValidateEntryPoint(entryPoint, extensionRootDirectory, errors);
            }

            var entryType = ReadString(root, "entryType", errors, required: false);

            var capabilities = ReadCapabilities(root, errors);
            var contributions = ReadContributions(root, capabilities, errors);

            if (errors.Count > 0)
            {
                return ExtensionManifestParseResult.Failure(errors);
            }

            var manifest = new ExtensionManifest(
                id,
                name,
                displayName,
                version,
                publisher,
                description,
                hostRange!,
                resolvedEntryPoint!,
                string.IsNullOrWhiteSpace(entryType) ? null : entryType,
                capabilities,
                contributions);

            return ExtensionManifestParseResult.Success(manifest);
        }
    }

    private static string? ReadString(JsonElement root, string propertyName, List<string> errors, bool required)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            if (required) errors.Add($"Missing required field '{propertyName}'.");
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            errors.Add($"'{propertyName}' must be a string.");
            return null;
        }

        var value = property.GetString();
        if (required && string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"'{propertyName}' must not be empty.");
            return null;
        }

        return value;
    }

    /// <summary>Rejects path traversal, absolute paths, and UNC paths (SKILL.md §30) — the
    /// manifest's own claim about its entry point is never trusted; the final resolved path must
    /// remain inside <paramref name="extensionRootDirectory"/>. Purely string/path computation —
    /// no file is opened or required to exist for this check.</summary>
    private static string? ValidateEntryPoint(string entryPoint, string extensionRootDirectory, List<string> errors)
    {
        if (Path.IsPathRooted(entryPoint) || entryPoint.StartsWith(@"\\", StringComparison.Ordinal))
        {
            errors.Add($"'entryPoint' ('{entryPoint}') must be a relative path — absolute and UNC paths are not permitted.");
            return null;
        }

        string resolvedRoot;
        string resolvedEntryPoint;
        try
        {
            resolvedRoot = Path.GetFullPath(extensionRootDirectory);
            resolvedEntryPoint = Path.GetFullPath(Path.Combine(resolvedRoot, entryPoint));
        }
        catch (ArgumentException)
        {
            errors.Add($"'entryPoint' ('{entryPoint}') is not a valid path.");
            return null;
        }

        var rootWithSeparator = resolvedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolvedEntryPoint.StartsWith(rootWithSeparator, PathComparer.Comparison))
        {
            errors.Add($"'entryPoint' ('{entryPoint}') resolves outside the extension's own directory — path traversal is not permitted.");
            return null;
        }

        if (!string.Equals(Path.GetExtension(resolvedEntryPoint), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"'entryPoint' ('{entryPoint}') must be a .dll file.");
            return null;
        }

        return resolvedEntryPoint;
    }

    private static IReadOnlyList<ExtensionCapability> ReadCapabilities(JsonElement root, List<string> errors)
    {
        if (!root.TryGetProperty("capabilities", out var property))
        {
            return Array.Empty<ExtensionCapability>();
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            errors.Add("'capabilities' must be an array of strings.");
            return Array.Empty<ExtensionCapability>();
        }

        var capabilities = new List<ExtensionCapability>();
        foreach (var element in property.EnumerateArray())
        {
            var text = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
            if (text is null || !KnownCapabilities.TryGetValue(text, out var capability))
            {
                errors.Add($"Unknown capability '{(text ?? element.ToString())}'.");
                continue;
            }
            capabilities.Add(capability);
        }
        return capabilities;
    }

    private static ExtensionContributions ReadContributions(JsonElement root, IReadOnlyList<ExtensionCapability> capabilities, List<string> errors)
    {
        if (!root.TryGetProperty("contributions", out var contributionsProperty) || contributionsProperty.ValueKind != JsonValueKind.Object)
        {
            return ExtensionContributions.Empty;
        }

        var commands = new List<ExtensionCommandContribution>();
        if (contributionsProperty.TryGetProperty("commands", out var commandsProperty))
        {
            if (commandsProperty.ValueKind != JsonValueKind.Array)
            {
                errors.Add("'contributions.commands' must be an array.");
                return ExtensionContributions.Empty;
            }

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in commandsProperty.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object ||
                    !element.TryGetProperty("id", out var idProp) || idProp.ValueKind != JsonValueKind.String ||
                    !element.TryGetProperty("title", out var titleProp) || titleProp.ValueKind != JsonValueKind.String)
                {
                    errors.Add("Each entry in 'contributions.commands' must be an object with string 'id' and 'title'.");
                    continue;
                }

                var commandId = idProp.GetString()!;
                var title = titleProp.GetString()!;
                if (string.IsNullOrWhiteSpace(commandId) || string.IsNullOrWhiteSpace(title))
                {
                    errors.Add("A command contribution's 'id' and 'title' must not be empty.");
                    continue;
                }

                if (!seenIds.Add(commandId))
                {
                    errors.Add($"Duplicate command contribution id '{commandId}' within the same manifest.");
                    continue;
                }

                commands.Add(new ExtensionCommandContribution(commandId, title));
            }

            if (commands.Count > 0 && !capabilities.Contains(ExtensionCapability.Command))
            {
                errors.Add("'contributions.commands' is declared but 'command' is missing from 'capabilities'.");
            }
        }

        return new ExtensionContributions(commands);
    }
}
