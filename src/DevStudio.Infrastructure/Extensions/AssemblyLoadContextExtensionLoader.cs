using System.Reflection;
using System.Runtime.Loader;
using DevStudio.Core.Extensions;

namespace DevStudio.Infrastructure.Extensions;

/// <summary>
/// Loads a real extension assembly into its own collectible <see cref="AssemblyLoadContext"/>
/// (SKILL.md §21 [Phase 10], ADR-011). <b>This is not a security sandbox</b> — an extension's
/// code runs with the exact same OS-level privileges as DevStudio's own host process; a
/// collectible <c>AssemblyLoadContext</c> only provides load isolation (so the assembly can later
/// be released for garbage collection) and simplifies future unload, never a process/permission
/// boundary. True isolation would require an out-of-process extension host, deliberately not
/// built this phase (see ADR-011's Loading/Isolation Strategy).
/// </summary>
public sealed class AssemblyLoadContextExtensionLoader : IExtensionLoader
{
    public Task<ILoadedExtension> LoadAsync(ExtensionManifest manifest, CancellationToken cancellationToken = default)
    {
        var context = new ExtensionAssemblyLoadContext(manifest.Id.Value);
        Assembly assembly;
        try
        {
            assembly = context.LoadFromAssemblyPath(manifest.EntryPoint);
        }
        catch (Exception ex)
        {
            context.Unload();
            throw new InvalidOperationException($"Failed to load '{manifest.EntryPoint}': {ex.Message}", ex);
        }

        Type extensionType;
        try
        {
            extensionType = ResolveExtensionType(assembly, manifest);
        }
        catch (Exception)
        {
            context.Unload();
            throw;
        }

        object instance;
        try
        {
            instance = Activator.CreateInstance(extensionType) ?? throw new InvalidOperationException($"Activator.CreateInstance returned null for '{extensionType.FullName}'.");
        }
        catch (Exception ex)
        {
            context.Unload();
            throw new InvalidOperationException($"Failed to construct extension type '{extensionType.FullName}': {ex.Message}", ex);
        }

        if (instance is not IDevStudioExtension extension)
        {
            context.Unload();
            throw new InvalidOperationException($"Type '{extensionType.FullName}' does not implement IDevStudioExtension.");
        }

        return Task.FromResult<ILoadedExtension>(new LoadedExtension(context, extension));
    }

    private static Type ResolveExtensionType(Assembly assembly, ExtensionManifest manifest)
    {
        if (manifest.EntryType is { } entryType)
        {
            return assembly.GetType(entryType, throwOnError: false)
                ?? throw new InvalidOperationException($"Entry type '{entryType}' was not found in '{manifest.EntryPoint}'.");
        }

        var candidates = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IDevStudioExtension).IsAssignableFrom(t))
            .ToList();

        return candidates.Count switch
        {
            0 => throw new InvalidOperationException($"No type implementing IDevStudioExtension was found in '{manifest.EntryPoint}'."),
            1 => candidates[0],
            _ => throw new InvalidOperationException($"Multiple types implementing IDevStudioExtension were found in '{manifest.EntryPoint}'; set 'entryType' in the manifest to disambiguate."),
        };
    }

    private sealed class LoadedExtension : ILoadedExtension
    {
        private readonly ExtensionAssemblyLoadContext _context;

        public LoadedExtension(ExtensionAssemblyLoadContext context, IDevStudioExtension instance)
        {
            _context = context;
            Instance = instance;
        }

        public IDevStudioExtension Instance { get; }

        public ValueTask DisposeAsync()
        {
            // Advisory only (SKILL.md §21) — Unload() marks the context collectible-eligible;
            // it does not force an immediate, guaranteed release the way a process exit would.
            _context.Unload();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// A minimal, collectible load context. <see cref="Load"/> always returns <c>null</c> — this is
/// deliberate: it makes the runtime fall back to the default context for any assembly the
/// extension references that the host itself already loaded there (critically,
/// <c>DevStudio.Core.dll</c>, so <c>typeof(IDevStudioExtension)</c> resolves to the exact same
/// type identity on both sides of the load-context boundary — a classic .NET plugin pitfall
/// avoided here by never giving this context its own private probing path). The extension's own
/// entry-point assembly is loaded explicitly by <see cref="AssemblyLoadContextExtensionLoader"/>
/// via <c>LoadFromAssemblyPath</c>, not through this method.
/// </summary>
internal sealed class ExtensionAssemblyLoadContext : AssemblyLoadContext
{
    public ExtensionAssemblyLoadContext(string name) : base(name, isCollectible: true)
    {
    }

    protected override Assembly? Load(AssemblyName assemblyName) => null;
}
