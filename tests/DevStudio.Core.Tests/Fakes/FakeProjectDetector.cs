using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;

namespace DevStudio.Core.Tests.Fakes;

/// <summary>Matches directories by a marker filename and produces a deterministic project/solution
/// so <see cref="ProjectDetectionService"/> tests don't depend on any real project ecosystem.</summary>
public sealed class FakeProjectDetector : IProjectDetector
{
    private readonly string _markerFileName;
    private readonly ProjectType _projectType;
    private readonly Func<string, RawSolutionDetection>? _solutionFactory;

    public string Name => "Fake";

    public FakeProjectDetector(string markerFileName, ProjectType projectType)
    {
        _markerFileName = markerFileName;
        _projectType = projectType;
    }

    public FakeProjectDetector(string markerFileName, Func<string, RawSolutionDetection> solutionFactory)
    {
        _markerFileName = markerFileName;
        _projectType = ProjectType.Unknown;
        _solutionFactory = solutionFactory;
    }

    public Task<ProjectDetectionResult?> DetectAsync(string directoryPath, IReadOnlyList<string> fileNamesInDirectory, CancellationToken cancellationToken = default)
    {
        if (!fileNamesInDirectory.Contains(_markerFileName)) return Task.FromResult<ProjectDetectionResult?>(null);

        if (_solutionFactory is not null)
        {
            return Task.FromResult<ProjectDetectionResult?>(new ProjectDetectionResult(null, _solutionFactory(directoryPath)));
        }

        var project = new ProjectInfo(
            Id: Guid.NewGuid().ToString("N"),
            Name: Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar)),
            RootPath: directoryPath,
            ProjectType: _projectType,
            ProjectFile: Path.Combine(directoryPath, _markerFileName),
            Languages: Array.Empty<string>(),
            ConfigurationFiles: new[] { Path.Combine(directoryPath, _markerFileName) },
            Capabilities: Array.Empty<ProjectCapability>());

        return Task.FromResult<ProjectDetectionResult?>(new ProjectDetectionResult(project, null));
    }
}
