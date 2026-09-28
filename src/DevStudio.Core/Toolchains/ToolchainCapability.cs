namespace DevStudio.Core.Toolchains;

/// <summary>
/// What a toolchain is documented to be able to do (SKILL.md §4). <see
/// cref="Debug"/> is deliberately never assigned by any Phase 3 detector — a version probe
/// succeeding is not "a justified capability source" for debugging; that requires an actual
/// <see cref="Adapters.IDebuggerAdapter"/> implementation, which doesn't exist until Phase 6.
/// </summary>
public enum ToolchainCapability
{
    Detect,
    Restore,
    Build,
    Run,
    Test,
    Debug,
    Format,
    Package,
    Publish
}
