using DevStudio.Core.Projects;

namespace DevStudio.Core.Build;

public enum BuildTargetKind
{
    Project,
    Solution
}

/// <summary>What to build (SKILL.md §9): a specific project or a specific solution, never "the
/// workspace directory" itself — a workspace is not automatically buildable.</summary>
public sealed record BuildTarget(
    BuildTargetKind Kind,
    string Name,
    string FilePath,
    string WorkingDirectory,
    ProjectType ProjectType);
