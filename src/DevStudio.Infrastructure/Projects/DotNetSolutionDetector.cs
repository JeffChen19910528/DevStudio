using System.Text.RegularExpressions;
using System.Xml.Linq;
using DevStudio.Core.Projects;

namespace DevStudio.Infrastructure.Projects;

/// <summary>
/// Parses .sln (classic text format) and .slnx (XML) solution files for their referenced
/// project paths (SKILL.md §7) — a conservative, string/XML-based read, never an MSBuild
/// invocation. Solution-folder entries (no known project extension) are ignored. A parse
/// failure is reported as <see cref="DetectionConfidence.Partial"/> rather than thrown.
/// </summary>
public sealed class DotNetSolutionDetector : IProjectDetector
{
    public string Name => "DotNetSolution";

    private static readonly Regex SlnProjectLine = new(
        "Project\\(\"\\{[^}]+\\}\"\\)\\s*=\\s*\"([^\"]+)\"\\s*,\\s*\"([^\"]+)\"\\s*,\\s*\"\\{[^}]+\\}\"",
        RegexOptions.Compiled);

    private static readonly string[] KnownProjectExtensions = { ".csproj", ".fsproj", ".vbproj" };

    public async Task<ProjectDetectionResult?> DetectAsync(string directoryPath, IReadOnlyList<string> fileNamesInDirectory, CancellationToken cancellationToken = default)
    {
        var solutionFile = fileNamesInDirectory.FirstOrDefault(f =>
            f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase));
        if (solutionFile is null) return null;

        var solutionPath = Path.Combine(directoryPath, solutionFile);
        var isSlnx = solutionFile.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

        var content = await ConfigFileReading.TryReadHeadAsync(solutionPath, cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            var unreadable = new RawSolutionDetection(
                Guid.NewGuid().ToString("N"),
                Path.GetFileNameWithoutExtension(solutionFile),
                solutionPath,
                Array.Empty<string>(),
                DetectionConfidence.Partial,
                $"'{solutionFile}' was detected but could not be read.");
            return new ProjectDetectionResult(null, unreadable);
        }

        List<string> referencedPaths;
        string? warning = null;
        var confidence = DetectionConfidence.Full;

        try
        {
            referencedPaths = isSlnx ? ParseSlnx(content, directoryPath) : ParseSln(content, directoryPath);
        }
        catch (Exception)
        {
            referencedPaths = new List<string>();
            confidence = DetectionConfidence.Partial;
            warning = $"'{solutionFile}' was detected but could not be fully parsed.";
        }

        var solution = new RawSolutionDetection(
            Guid.NewGuid().ToString("N"),
            Path.GetFileNameWithoutExtension(solutionFile),
            solutionPath,
            referencedPaths,
            confidence,
            warning);

        return new ProjectDetectionResult(null, solution);
    }

    private static List<string> ParseSln(string content, string solutionDirectory)
    {
        var paths = new List<string>();

        foreach (Match match in SlnProjectLine.Matches(content))
        {
            var relativePath = match.Groups[2].Value;
            if (!KnownProjectExtensions.Contains(Path.GetExtension(relativePath), StringComparer.OrdinalIgnoreCase)) continue;

            var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar);
            paths.Add(Path.GetFullPath(Path.Combine(solutionDirectory, normalized)));
        }

        return paths;
    }

    private static List<string> ParseSlnx(string content, string solutionDirectory)
    {
        var paths = new List<string>();
        var document = XDocument.Parse(content);

        foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "Project"))
        {
            var relativePath = element.Attribute("Path")?.Value;
            if (relativePath is null) continue;
            if (!KnownProjectExtensions.Contains(Path.GetExtension(relativePath), StringComparer.OrdinalIgnoreCase)) continue;

            var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar);
            paths.Add(Path.GetFullPath(Path.Combine(solutionDirectory, normalized)));
        }

        return paths;
    }
}
