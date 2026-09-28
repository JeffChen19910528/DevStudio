namespace DevStudio.Core.Toolchains;

/// <summary>
/// One discovered Visual Studio installation (SKILL.md §8–§9). Multiple instances (e.g. VS 2026
/// Enterprise alongside VS 2022 Build Tools) are represented independently — the registry never
/// silently picks one; a future project/toolchain selection step does that.
/// </summary>
public sealed record VisualStudioInstance(
    string InstanceId,
    string DisplayName,
    string Edition,
    string Version,
    string InstallationPath,
    ToolchainDetectionState State,
    string? MsBuildPath = null,
    string? MsvcToolsetPath = null,
    string? WindowsSdkPath = null,
    string? DetectionWarning = null);
