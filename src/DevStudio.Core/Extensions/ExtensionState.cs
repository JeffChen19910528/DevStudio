namespace DevStudio.Core.Extensions;

/// <summary>An extension's real, observed lifecycle state (SKILL.md §12–§13 [Phase 10]) — never
/// inferred, always set by <see cref="ExtensionManager"/> as each real step actually
/// completes.</summary>
public enum ExtensionState
{
    Discovered,
    Valid,
    Invalid,
    Incompatible,
    Disabled,
    Enabled,
    Loading,
    Loaded,
    Active,
    Failed,
    Unloaded,
}

/// <summary>One discovered extension's full, current status (SKILL.md §12). <see
/// cref="Manifest"/> is <c>null</c> only when the manifest itself failed to parse at all (in
/// which case <see cref="State"/> is <see cref="ExtensionState.Invalid"/> and <see
/// cref="Id"/> is <c>null</c> — there is no identity to key on yet). <see
/// cref="RootDirectory"/> is always present; it — not the id — is what discovery/registry use to
/// tell two manifest-less-invalid entries apart.</summary>
public sealed record ExtensionDescriptor(
    string RootDirectory,
    ExtensionManifest? Manifest,
    ExtensionState State,
    IReadOnlyList<string> ValidationErrors,
    string? FailureReason = null)
{
    public ExtensionId? Id => Manifest?.Id;
    public string DisplayName => Manifest?.DisplayName ?? Path.GetFileName(RootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
}
