namespace DevStudio.Core.Toolchains;

/// <summary>
/// Result of detecting one toolchain on the current machine (SKILL.md §4). Pure data — nothing
/// here executes anything. <see cref="ExecutablePath"/>/<see cref="InstallationRoot"/> are
/// treated as data throughout DevStudio, never interpolated into a shell command (SKILL.md §33).
/// </summary>
public sealed record ToolchainInfo(
    string Id,
    string Name,
    ToolchainDetectionState State,
    string? Version = null,
    string? Vendor = null,
    string? Platform = null,
    string? Architecture = null,
    string? ExecutablePath = null,
    string? InstallationRoot = null,
    IReadOnlyList<ToolchainCapability> Capabilities = null!,
    string? DetectionWarning = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public IReadOnlyList<ToolchainCapability> Capabilities { get; init; } = Capabilities ?? Array.Empty<ToolchainCapability>();

    public bool IsUsable => State is ToolchainDetectionState.Installed or ToolchainDetectionState.Detected;
}
