using DevStudio.Core.Projects;

namespace DevStudio.Core.Adapters;

/// <summary>
/// Recognizes and models one project type (e.g. a .csproj, a CMakeLists.txt project) from
/// its marker files. Detection MUST be read-only (SKILL.md §9).
/// </summary>
public interface IProjectAdapter
{
    /// <summary>Project marker filenames/patterns this adapter recognizes, e.g. "*.csproj".</summary>
    IReadOnlyList<string> ProjectMarkers { get; }

    bool CanHandle(string projectFilePath);

    Task<ProjectInfo> LoadAsync(string projectFilePath, CancellationToken cancellationToken = default);
}
