namespace DevStudio.Core.Projects;

/// <summary>
/// A multi-project grouping discovered from a solution-like file (e.g. .sln/.slnx) (SKILL.md
/// §7). <see cref="ProjectIds"/> references <see cref="ProjectInfo.Id"/> values in the same
/// detection result; a referenced project that could not be found on disk still gets an entry
/// there, pointing at a stub <see cref="ProjectInfo"/> with <see
/// cref="DetectionConfidence.Partial"/>, so a stale solution reference is visible rather than
/// silently dropped.
/// </summary>
public sealed record SolutionInfo(
    string Id,
    string Name,
    string SolutionFilePath,
    IReadOnlyList<string> ProjectIds,
    DetectionConfidence DetectionConfidence = DetectionConfidence.Full,
    string? DetectionWarning = null);
