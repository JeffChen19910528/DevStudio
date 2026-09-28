using System.Text.RegularExpressions;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Projects;

public sealed class JavaProjectDetector : IProjectDetector
{
    public string Name => "Java";

    private static readonly Regex ArtifactIdRegex = new(
        "<artifactId>([^<]+)</artifactId>", RegexOptions.Compiled);

    private static readonly string[] GradleFiles =
    {
        "build.gradle", "build.gradle.kts", "settings.gradle", "settings.gradle.kts"
    };

    public async Task<ProjectDetectionResult?> DetectAsync(string directoryPath, IReadOnlyList<string> fileNamesInDirectory, CancellationToken cancellationToken = default)
    {
        var pomXml = fileNamesInDirectory.FirstOrDefault(f => f.Equals("pom.xml", StringComparison.OrdinalIgnoreCase));
        var gradleFile = fileNamesInDirectory.FirstOrDefault(f => GradleFiles.Contains(f, StringComparer.OrdinalIgnoreCase));

        if (pomXml is null && gradleFile is null) return null;

        var configFiles = new List<string>();
        var name = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar));
        var confidence = DetectionConfidence.Full;
        string? warning = null;
        var languages = new List<string> { "Java" };

        if (pomXml is not null)
        {
            var pomPath = Path.Combine(directoryPath, pomXml);
            configFiles.Add(pomPath);

            var content = await ConfigFileReading.TryReadHeadAsync(pomPath, cancellationToken).ConfigureAwait(false);
            if (content is null)
            {
                confidence = DetectionConfidence.Partial;
                warning = "pom.xml was detected but could not be read.";
            }
            else
            {
                var match = ArtifactIdRegex.Match(content);
                if (match.Success) name = match.Groups[1].Value;
            }
        }

        if (gradleFile is not null)
        {
            configFiles.Add(Path.Combine(directoryPath, gradleFile));
            if (gradleFile.EndsWith(".kts", StringComparison.OrdinalIgnoreCase))
            {
                languages.Add("Kotlin");
            }
        }

        var project = new ProjectInfo(
            Id: Guid.NewGuid().ToString("N"),
            Name: name,
            RootPath: directoryPath,
            ProjectType: ProjectType.Java,
            ProjectFile: pomXml is not null ? Path.Combine(directoryPath, pomXml) : Path.Combine(directoryPath, gradleFile!),
            Languages: languages,
            ConfigurationFiles: configFiles,
            Capabilities: Array.Empty<ProjectCapability>(),
            DetectionConfidence: confidence,
            DetectionWarning: warning);

        return new ProjectDetectionResult(project, Solution: null);
    }
}
