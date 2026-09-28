namespace DevStudio.Core.Toolchains;

/// <summary>Tri-state rather than <c>bool CanBuild</c> (SKILL.md §23): a required toolchain
/// might genuinely not be known yet (no detection has run), which is a different fact from
/// "known to be missing."</summary>
public enum CapabilityAvailability
{
    Unknown,
    Available,
    Unavailable
}
