using System.Text.RegularExpressions;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Projects;

/// <summary>Detects CMake/Make/Meson-based C/C++ projects (SKILL.md §3). Detection is metadata
/// discovery only — it never runs cmake, make, or meson (SKILL.md §4, §19); whether those
/// toolchains are even installed is Phase 3's concern.</summary>
public sealed class CMakeProjectDetector : IProjectDetector
{
    public string Name => "CMake";

    private static readonly Regex ProjectNameRegex = new(
        @"project\s*\(\s*([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<ProjectDetectionResult?> DetectAsync(string directoryPath, IReadOnlyList<string> fileNamesInDirectory, CancellationToken cancellationToken = default)
    {
        var cmakeLists = fileNamesInDirectory.FirstOrDefault(f => f.Equals("CMakeLists.txt", StringComparison.OrdinalIgnoreCase));
        var makefile = fileNamesInDirectory.FirstOrDefault(f => f.Equals("Makefile", StringComparison.OrdinalIgnoreCase));
        var mesonBuild = fileNamesInDirectory.FirstOrDefault(f => f.Equals("meson.build", StringComparison.OrdinalIgnoreCase));

        var marker = cmakeLists ?? makefile ?? mesonBuild;
        if (marker is null) return null;

        var configFiles = new List<string> { Path.Combine(directoryPath, marker) };
        var name = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar));
        var confidence = DetectionConfidence.Full;
        string? warning = null;

        if (cmakeLists is not null)
        {
            var content = await ConfigFileReading.TryReadHeadAsync(Path.Combine(directoryPath, cmakeLists), cancellationToken).ConfigureAwait(false);
            if (content is null)
            {
                confidence = DetectionConfidence.Partial;
                warning = "CMakeLists.txt was detected but could not be read. The project will be opened as a partial CMake project.";
            }
            else
            {
                var match = ProjectNameRegex.Match(content);
                if (match.Success)
                {
                    name = match.Groups[1].Value;
                }
                else
                {
                    confidence = DetectionConfidence.Partial;
                    warning = "CMakeLists.txt was detected but could not be fully parsed. The project will be opened as a partial CMake project.";
                }
            }
        }

        var project = new ProjectInfo(
            Id: Guid.NewGuid().ToString("N"),
            Name: name,
            RootPath: directoryPath,
            ProjectType: ProjectType.CMake,
            ProjectFile: cmakeLists is not null ? Path.Combine(directoryPath, cmakeLists) : null,
            Languages: new[] { "C/C++" },
            ConfigurationFiles: configFiles,
            Capabilities: Array.Empty<ProjectCapability>(),
            DetectionConfidence: confidence,
            DetectionWarning: warning);

        return new ProjectDetectionResult(project, Solution: null);
    }
}
