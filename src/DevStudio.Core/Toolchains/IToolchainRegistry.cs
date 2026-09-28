using DevStudio.Core.Projects;

namespace DevStudio.Core.Toolchains;

/// <summary>Central registry of known toolchain detectors and their last detection results
/// (SKILL.md §19). Never executes a build — <see cref="RefreshAsync"/> only re-runs detection.</summary>
public interface IToolchainRegistry
{
    void Register(IToolchainDetector detector);

    void Unregister(string toolchainId);

    ToolchainInfo? Get(string toolchainId);

    IReadOnlyList<ToolchainInfo> GetAll();

    IReadOnlyList<ToolchainInfo> FindByCapability(ToolchainCapability capability);

    /// <summary>Toolchains relevant to a project type, per <see
    /// cref="ToolchainRequirements.GetRequiredToolchainIds"/> — present or not (SKILL.md §19).</summary>
    IReadOnlyList<ToolchainInfo> FindForProjectType(ProjectType projectType);

    /// <summary>Re-runs every registered detector concurrently and replaces the cached results
    /// (SKILL.md §20–§21, §26). Never modifies installed software.</summary>
    Task<IReadOnlyList<ToolchainInfo>> RefreshAsync(CancellationToken cancellationToken = default);
}
