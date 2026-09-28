namespace DevStudio.Core.Extensions;

/// <summary>
/// Loads one already-validated extension's real entry-point assembly and produces its
/// <see cref="IDevStudioExtension"/> instance (SKILL.md §14, §21 [Phase 10]). Implemented
/// in-process via a collectible <see cref="System.Runtime.Loader.AssemblyLoadContext"/> (see
/// ADR-011) — this is explicitly NOT a security sandbox; a malicious extension's code runs with
/// the same privileges as DevStudio itself. <see cref="ILoadedExtension.DisposeAsync"/> requests
/// unload, which is advisory (garbage-collector-driven), never a guaranteed-immediate unload.
/// </summary>
public interface IExtensionLoader
{
    Task<ILoadedExtension> LoadAsync(ExtensionManifest manifest, CancellationToken cancellationToken = default);
}

public interface ILoadedExtension : IAsyncDisposable
{
    IDevStudioExtension Instance { get; }
}
