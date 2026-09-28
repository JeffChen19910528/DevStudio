namespace DevStudio.Core.Projects;

/// <summary>
/// A solution-like file's raw reference list, as read directly off disk. Referenced paths are
/// not yet resolved to the <see cref="ProjectInfo.Id"/> values of whichever projects turn up
/// elsewhere in the scan — <see cref="ProjectDetectionService"/> does that cross-referencing
/// once the full tree walk is done, since a solution detector only sees its own directory and
/// cannot know the other projects' assigned ids yet.
/// </summary>
public sealed record RawSolutionDetection(
    string Id,
    string Name,
    string SolutionFilePath,
    IReadOnlyList<string> ReferencedProjectFilePaths,
    DetectionConfidence DetectionConfidence = DetectionConfidence.Full,
    string? DetectionWarning = null);

/// <summary>One directory's detection result from a single <see cref="IProjectDetector"/>.</summary>
public sealed record ProjectDetectionResult(ProjectInfo? Project, RawSolutionDetection? Solution);

/// <summary>
/// Recognizes one project ecosystem from the marker files present in a single directory
/// (SKILL.md §2). Never invokes the ecosystem's own tooling (no MSBuild, npm, cargo, cmake,
/// pip, maven/gradle, go) — metadata discovery only (SKILL.md §4, §19). A detector MAY read the
/// text content of the config files it recognizes (e.g. to pull a project name out of
/// package.json) but must do so defensively: bounded read size, try/catch around parsing, and a
/// parse failure becomes <see cref="DetectionConfidence.Partial"/> or <see
/// cref="DetectionConfidence.Failed"/>, never an unhandled exception (SKILL.md §18, §31).
/// </summary>
public interface IProjectDetector
{
    string Name { get; }

    /// <param name="directoryPath">Absolute path of the directory being inspected.</param>
    /// <param name="fileNamesInDirectory">Names (not full paths) of the files directly in this directory.</param>
    Task<ProjectDetectionResult?> DetectAsync(
        string directoryPath,
        IReadOnlyList<string> fileNamesInDirectory,
        CancellationToken cancellationToken = default);
}
