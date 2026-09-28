namespace DevStudio.Core.Extensions;

/// <summary>
/// Finds and validates real extension manifests under a bounded set of known roots (SKILL.md
/// §10–§11 [Phase 10]) — never the whole filesystem, never recursive beyond one directory level
/// per root, and never workspace-local unless a caller explicitly opts a workspace root in.
/// Discovery only ever produces structured metadata (<see cref="ExtensionDescriptor"/>); it never
/// loads or activates an extension assembly. <c>Discover ≠ Load</c> (SKILL.md §11).
/// </summary>
public interface IExtensionDiscovery
{
    Task<IReadOnlyList<ExtensionDescriptor>> DiscoverAsync(IReadOnlyList<string> roots, CancellationToken cancellationToken = default);
}
