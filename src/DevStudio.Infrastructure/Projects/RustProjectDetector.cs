using System.Text.RegularExpressions;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Projects;

public sealed class RustProjectDetector : IProjectDetector
{
    public string Name => "Rust";

    private static readonly Regex PackageNameRegex = new(
        "(?m)^\\s*name\\s*=\\s*\"([^\"]+)\"", RegexOptions.Compiled);

    public async Task<ProjectDetectionResult?> DetectAsync(string directoryPath, IReadOnlyList<string> fileNamesInDirectory, CancellationToken cancellationToken = default)
    {
        var cargoToml = fileNamesInDirectory.FirstOrDefault(f => f.Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase));
        if (cargoToml is null) return null;

        var cargoPath = Path.Combine(directoryPath, cargoToml);
        var name = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar));
        var confidence = DetectionConfidence.Full;
        string? warning = null;

        var content = await ConfigFileReading.TryReadHeadAsync(cargoPath, cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            confidence = DetectionConfidence.Partial;
            warning = "Cargo.toml was detected but could not be read.";
        }
        else
        {
            var match = PackageNameRegex.Match(content);
            if (match.Success) name = match.Groups[1].Value;
        }

        var project = new ProjectInfo(
            Id: Guid.NewGuid().ToString("N"),
            Name: name,
            RootPath: directoryPath,
            ProjectType: ProjectType.Rust,
            ProjectFile: cargoPath,
            Languages: new[] { "Rust" },
            ConfigurationFiles: new[] { cargoPath },
            Capabilities: Array.Empty<ProjectCapability>(),
            DetectionConfidence: confidence,
            DetectionWarning: warning);

        return new ProjectDetectionResult(project, Solution: null);
    }
}
