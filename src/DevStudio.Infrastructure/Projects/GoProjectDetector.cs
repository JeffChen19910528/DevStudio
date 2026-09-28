using System.Text.RegularExpressions;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Projects;

public sealed class GoProjectDetector : IProjectDetector
{
    public string Name => "Go";

    private static readonly Regex ModuleRegex = new(
        @"^\s*module\s+(\S+)", RegexOptions.Multiline | RegexOptions.Compiled);

    public async Task<ProjectDetectionResult?> DetectAsync(string directoryPath, IReadOnlyList<string> fileNamesInDirectory, CancellationToken cancellationToken = default)
    {
        var goMod = fileNamesInDirectory.FirstOrDefault(f => f.Equals("go.mod", StringComparison.OrdinalIgnoreCase));
        if (goMod is null) return null;

        var goModPath = Path.Combine(directoryPath, goMod);
        var name = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar));
        var confidence = DetectionConfidence.Full;
        string? warning = null;

        var content = await ConfigFileReading.TryReadHeadAsync(goModPath, cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            confidence = DetectionConfidence.Partial;
            warning = "go.mod was detected but could not be read.";
        }
        else
        {
            var match = ModuleRegex.Match(content);
            if (match.Success)
            {
                var modulePath = match.Groups[1].Value;
                name = modulePath.Split('/').LastOrDefault(segment => segment.Length > 0) ?? name;
            }
        }

        var project = new ProjectInfo(
            Id: Guid.NewGuid().ToString("N"),
            Name: name,
            RootPath: directoryPath,
            ProjectType: ProjectType.Go,
            ProjectFile: goModPath,
            Languages: new[] { "Go" },
            ConfigurationFiles: new[] { goModPath },
            Capabilities: Array.Empty<ProjectCapability>(),
            DetectionConfidence: confidence,
            DetectionWarning: warning);

        return new ProjectDetectionResult(project, Solution: null);
    }
}
