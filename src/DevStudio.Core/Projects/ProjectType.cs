namespace DevStudio.Core.Projects;

/// <summary>
/// Normalized project ecosystem (SKILL.md §6). Deliberately not tied to an executable name —
/// "ProjectType = DotNet", never "ProjectType = dotnet.exe". Which toolchain, if any, is
/// actually installed for a given type is a Phase 3 concern.
/// </summary>
public enum ProjectType
{
    Unknown,
    Generic,
    DotNet,
    CMake,
    Node,
    Python,
    Java,
    Rust,
    Go
}
