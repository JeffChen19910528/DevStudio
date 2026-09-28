namespace DevStudio.Core.Toolchains;

/// <summary>One capability's known-or-not status for a project (SKILL.md §23–§24), e.g. "Build:
/// Unavailable because Rust is not installed." <see cref="ToolchainId"/> names which toolchain
/// this judgement is based on, for display and debugging.</summary>
public sealed record ProjectCapability(
    ToolchainCapability Capability,
    CapabilityAvailability Availability,
    string ToolchainId,
    string? Explanation = null);
