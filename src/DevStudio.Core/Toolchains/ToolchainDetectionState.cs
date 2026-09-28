namespace DevStudio.Core.Toolchains;

/// <summary>Explicit detection states (SKILL.md §5) — richer than a bare bool because future
/// detection needs to distinguish *why* something isn't usable, not just whether it is.</summary>
public enum ToolchainDetectionState
{
    NotInstalled,
    Installed,
    Detected,
    PartiallyDetected,
    Invalid,
    Unsupported,
    DetectionTimedOut,
    DetectionFailed
}
