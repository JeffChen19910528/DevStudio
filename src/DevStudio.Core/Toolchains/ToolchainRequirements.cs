using DevStudio.Core.Projects;

namespace DevStudio.Core.Toolchains;

/// <summary>
/// Which toolchain(s) a project type needs before any build capability can be considered
/// available (SKILL.md §24). Each inner list is an "any of these" alternative group — e.g. CMake
/// needs CMake itself *and* at least one C/C++ compiler, so it has two groups; everything else
/// has exactly one toolchain in exactly one group.
/// </summary>
public static class ToolchainRequirements
{
    public static IReadOnlyList<IReadOnlyList<string>> GetRequiredToolchainGroups(ProjectType projectType) => projectType switch
    {
        ProjectType.DotNet => new[] { new[] { WellKnownToolchainIds.DotNet } },
        ProjectType.Node => new[] { new[] { WellKnownToolchainIds.Node } },
        ProjectType.Python => new[] { new[] { WellKnownToolchainIds.Python } },
        ProjectType.Java => new[] { new[] { WellKnownToolchainIds.Java } },
        ProjectType.Rust => new[] { new[] { WellKnownToolchainIds.Rust } },
        ProjectType.Go => new[] { new[] { WellKnownToolchainIds.Go } },
        ProjectType.CMake => new IReadOnlyList<string>[]
        {
            new[] { WellKnownToolchainIds.CMake },
            new[] { WellKnownToolchainIds.Msvc, WellKnownToolchainIds.Gcc, WellKnownToolchainIds.Clang },
        },
        _ => Array.Empty<IReadOnlyList<string>>(),
    };

    public static IReadOnlyList<string> GetRequiredToolchainIds(ProjectType projectType) =>
        GetRequiredToolchainGroups(projectType).SelectMany(group => group).Distinct().ToList();
}
