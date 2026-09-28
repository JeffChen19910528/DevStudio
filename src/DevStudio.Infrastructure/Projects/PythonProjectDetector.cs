using System.Text.RegularExpressions;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Projects;

/// <summary>requirements.txt alone is a weaker signal than pyproject.toml/setup.py — plenty of
/// non-project folders (a docs build, a notebook scratch folder) carry just a requirements
/// file, so that case is reported as <see cref="DetectionConfidence.Partial"/> (SKILL.md §18).</summary>
public sealed class PythonProjectDetector : IProjectDetector
{
    public string Name => "Python";

    private static readonly Regex TomlNameRegex = new(
        "(?m)^\\s*name\\s*=\\s*\"([^\"]+)\"", RegexOptions.Compiled);

    public async Task<ProjectDetectionResult?> DetectAsync(string directoryPath, IReadOnlyList<string> fileNamesInDirectory, CancellationToken cancellationToken = default)
    {
        var pyproject = fileNamesInDirectory.FirstOrDefault(f => f.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase));
        var setupPy = fileNamesInDirectory.FirstOrDefault(f => f.Equals("setup.py", StringComparison.OrdinalIgnoreCase));
        var requirements = fileNamesInDirectory.FirstOrDefault(f => f.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase));

        if (pyproject is null && setupPy is null && requirements is null) return null;

        var configFiles = new List<string>();
        var name = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar));
        var confidence = DetectionConfidence.Full;
        string? warning = null;

        if (pyproject is not null)
        {
            configFiles.Add(Path.Combine(directoryPath, pyproject));
            var content = await ConfigFileReading.TryReadHeadAsync(Path.Combine(directoryPath, pyproject), cancellationToken).ConfigureAwait(false);
            var match = content is not null ? TomlNameRegex.Match(content) : Match.Empty;
            if (match.Success) name = match.Groups[1].Value;
        }

        if (setupPy is not null) configFiles.Add(Path.Combine(directoryPath, setupPy));

        if (requirements is not null)
        {
            configFiles.Add(Path.Combine(directoryPath, requirements));
            if (pyproject is null && setupPy is null)
            {
                confidence = DetectionConfidence.Partial;
                warning = "requirements.txt found without pyproject.toml or setup.py; project boundaries may be approximate.";
            }
        }

        var project = new ProjectInfo(
            Id: Guid.NewGuid().ToString("N"),
            Name: name,
            RootPath: directoryPath,
            ProjectType: ProjectType.Python,
            ProjectFile: pyproject is not null ? Path.Combine(directoryPath, pyproject) : setupPy is not null ? Path.Combine(directoryPath, setupPy) : null,
            Languages: new[] { "Python" },
            ConfigurationFiles: configFiles,
            Capabilities: Array.Empty<ProjectCapability>(),
            DetectionConfidence: confidence,
            DetectionWarning: warning);

        return new ProjectDetectionResult(project, Solution: null);
    }
}
