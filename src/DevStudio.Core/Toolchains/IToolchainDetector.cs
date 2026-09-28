namespace DevStudio.Core.Toolchains;

/// <summary>
/// Detects whether one specific toolchain (SKILL.md §2) is installed and usable. Detection may
/// run a known, fixed version-probe command (e.g. <c>dotnet --version</c>) through <see
/// cref="Processes.IProcessRunner"/> with a timeout — it must never install anything, modify
/// PATH/environment/registry, or execute a project-defined command (SKILL.md §6, §30). A
/// detector must fail independently: any error becomes a <see
/// cref="ToolchainDetectionState.DetectionFailed"/>/<see
/// cref="ToolchainDetectionState.DetectionTimedOut"/> result, never an unhandled exception
/// (SKILL.md §28), so one broken detector can't stop the others from running.
/// </summary>
public interface IToolchainDetector
{
    string ToolchainId { get; }

    Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default);
}
