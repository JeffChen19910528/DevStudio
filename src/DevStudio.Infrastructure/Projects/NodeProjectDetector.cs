using System.Text.Json;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Projects;

public sealed class NodeProjectDetector : IProjectDetector
{
    public string Name => "Node";

    public async Task<ProjectDetectionResult?> DetectAsync(string directoryPath, IReadOnlyList<string> fileNamesInDirectory, CancellationToken cancellationToken = default)
    {
        var packageJson = fileNamesInDirectory.FirstOrDefault(f => f.Equals("package.json", StringComparison.OrdinalIgnoreCase));
        if (packageJson is null) return null;

        var hasTypeScript = fileNamesInDirectory.Any(f => f.Equals("tsconfig.json", StringComparison.OrdinalIgnoreCase));
        var configFiles = new List<string> { Path.Combine(directoryPath, packageJson) };
        if (hasTypeScript) configFiles.Add(Path.Combine(directoryPath, "tsconfig.json"));

        var name = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar));
        var confidence = DetectionConfidence.Full;
        string? warning = null;

        var content = await ConfigFileReading.TryReadHeadAsync(Path.Combine(directoryPath, packageJson), cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            confidence = DetectionConfidence.Partial;
            warning = "package.json was detected but could not be read.";
        }
        else
        {
            try
            {
                using var document = JsonDocument.Parse(content);
                if (document.RootElement.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String)
                {
                    name = nameProperty.GetString() ?? name;
                }
            }
            catch (JsonException)
            {
                confidence = DetectionConfidence.Partial;
                warning = "package.json was detected but could not be parsed as JSON.";
            }
        }

        var languages = hasTypeScript ? new[] { "TypeScript", "JavaScript" } : new[] { "JavaScript" };

        var project = new ProjectInfo(
            Id: Guid.NewGuid().ToString("N"),
            Name: name,
            RootPath: directoryPath,
            ProjectType: ProjectType.Node,
            ProjectFile: Path.Combine(directoryPath, packageJson),
            Languages: languages,
            ConfigurationFiles: configFiles,
            Capabilities: Array.Empty<ProjectCapability>(),
            DetectionConfidence: confidence,
            DetectionWarning: warning);

        return new ProjectDetectionResult(project, Solution: null);
    }
}
