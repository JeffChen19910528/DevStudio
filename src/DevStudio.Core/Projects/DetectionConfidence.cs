namespace DevStudio.Core.Projects;

/// <summary>SKILL.md §18: detection problems are surfaced as diagnostics, not crashes.</summary>
public enum DetectionConfidence
{
    Full,
    Partial,
    Failed
}
