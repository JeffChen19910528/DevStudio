namespace DevStudio.Core.Extensions;

/// <summary>Capability types a manifest may declare (SKILL.md §16 [Phase 10]). Only
/// <see cref="Command"/> is actually wired to any real behavior this phase — the rest are
/// recognized as valid, known capability strings (so a manifest declaring them is not rejected as
/// malformed) but declaring them contributes nothing; an extension does not gain any of that
/// behavior merely by naming it (SKILL.md §16's "a manifest must declare what the extension
/// intends to contribute" — declaring is never the same as the host actually granting it).</summary>
public enum ExtensionCapability
{
    Command,
    Panel,
    Language,
    Debugger,
    TestAdapter,
    BuildAdapter,
    SourceControl,
    Toolchain,
}

public sealed record ExtensionCommandContribution(string Id, string Title);

public sealed record ExtensionContributions(IReadOnlyList<ExtensionCommandContribution> Commands)
{
    public static readonly ExtensionContributions Empty = new(Array.Empty<ExtensionCommandContribution>());
}

/// <summary>A validated, real extension manifest (SKILL.md §6 [Phase 10]). Every field here has
/// already passed schema/identity/version/capability/contribution validation —
/// <see cref="ExtensionManifestParser"/> is the only place one of these is ever constructed.
/// <see cref="EntryType"/> is an adaptation beyond the prompt's example schema: an optional
/// fully-qualified type name, used only when more than one <see cref="IDevStudioExtension"/>
/// implementation exists in the same entry-point assembly (e.g. this codebase's own test
/// fixtures); when absent, the loader requires the assembly to expose exactly one such type.</summary>
public sealed record ExtensionManifest(
    ExtensionId Id,
    string Name,
    string DisplayName,
    ExtensionVersion Version,
    string Publisher,
    string Description,
    ExtensionVersionRange HostVersionRange,
    string EntryPoint,
    string? EntryType,
    IReadOnlyList<ExtensionCapability> Capabilities,
    ExtensionContributions Contributions);

public sealed record ExtensionManifestParseResult(ExtensionManifest? Manifest, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Manifest is not null && Errors.Count == 0;

    public static ExtensionManifestParseResult Success(ExtensionManifest manifest) => new(manifest, Array.Empty<string>());
    public static ExtensionManifestParseResult Failure(IReadOnlyList<string> errors) => new(null, errors);
}
