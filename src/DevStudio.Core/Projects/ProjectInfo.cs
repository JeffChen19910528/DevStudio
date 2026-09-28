using DevStudio.Core.Toolchains;

namespace DevStudio.Core.Projects;

/// <summary>
/// A single detected project (SKILL.md §5). Produced entirely by read-only metadata discovery —
/// filenames and, where a detector chooses to, the text content of known config files — never by
/// executing a build script, package manager, or compiler (SKILL.md §4, §31). This is
/// information, not an execution surface: it has no methods that run anything.
/// <see cref="Capabilities"/> starts empty at detection time (Phase 2) — a project detector has
/// no toolchain knowledge — and is only populated once <see cref="ProjectCapabilityMatcher"/>
/// runs against actual detected toolchains (Phase 3).
/// </summary>
public sealed record ProjectInfo(
    string Id,
    string Name,
    string RootPath,
    ProjectType ProjectType,
    string? ProjectFile,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> ConfigurationFiles,
    IReadOnlyList<ProjectCapability> Capabilities,
    DetectionConfidence DetectionConfidence = DetectionConfidence.Full,
    string? DetectionWarning = null,
    string? ParentProjectId = null,
    IReadOnlyList<ProjectInfo> ChildProjects = null!,
    /// <summary>Whether this project actually produces a runnable application (SKILL.md §8–§9
    /// [Phase 5]) — e.g. a .NET project's real <c>&lt;OutputType&gt;Exe&lt;/OutputType&gt;</c>,
    /// not assumed from its project type. A class library is never runnable. Defaults to false
    /// so every detector predating this field (everything but <c>DotNetProjectDetector</c>)
    /// compiles unchanged and correctly reports "not runnable" by omission.</summary>
    bool IsExecutable = false,
    /// <summary>Whether this project's own file contains real evidence of a .NET test SDK/
    /// framework reference (SKILL.md §11 [Phase 8]) — e.g. a real
    /// <c>Microsoft.NET.Test.Sdk</c>/<c>xunit</c>/<c>NUnit3TestAdapter</c>/
    /// <c>MSTest.TestAdapter</c> package reference, never assumed from <see cref="ProjectType"/>
    /// alone (a normal application or class library is not automatically a test project).
    /// Defaults to false so every detector predating this field compiles unchanged and
    /// correctly reports "not a test project" by omission — conservative by design: when the
    /// evidence is inconclusive, this stays false rather than guessing.</summary>
    bool IsTestProject = false)
{
    public IReadOnlyList<ProjectInfo> ChildProjects { get; init; } = ChildProjects ?? Array.Empty<ProjectInfo>();
}
