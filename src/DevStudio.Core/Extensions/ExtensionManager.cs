namespace DevStudio.Core.Extensions;

/// <summary>
/// Orchestrates the real extension lifecycle: Discover → Validate → Enable/Disable → Load →
/// Activate → Deactivate → Unload (SKILL.md §13 [Phase 10]). Mirrors the existing
/// <c>BuildService</c>/<c>TestService</c>/<c>GitService</c> shape (a plain, no-UI-dependency
/// orchestrator over injected abstractions) rather than introducing a new pattern. A failing
/// extension is always isolated: one extension's discovery/load/activation/command failure never
/// prevents another extension from loading, and never crashes DevStudio (SKILL.md §13, §26).
/// </summary>
public sealed class ExtensionManager
{
    private readonly IExtensionDiscovery _discovery;
    private readonly IExtensionLoader _loader;
    private readonly ICommandRegistry _commandRegistry;
    private readonly IReadOnlyList<string> _roots;
    private readonly HashSet<string> _disabledIds;
    private readonly Dictionary<string, ILoadedExtension> _loadedByIdValue = new(StringComparer.Ordinal);

    private List<ExtensionDescriptor> _extensions = new();

    public ExtensionManager(
        IExtensionDiscovery discovery,
        IExtensionLoader loader,
        ICommandRegistry commandRegistry,
        IReadOnlyList<string> roots,
        IEnumerable<string>? initiallyDisabledExtensionIds = null)
    {
        _discovery = discovery;
        _loader = loader;
        _commandRegistry = commandRegistry;
        _roots = roots;
        _disabledIds = new HashSet<string>(initiallyDisabledExtensionIds ?? Array.Empty<string>(), StringComparer.Ordinal);
    }

    public IReadOnlyList<ExtensionDescriptor> Extensions => _extensions;

    public event EventHandler? Changed;

    /// <summary>Extension-specific log lines (SKILL.md §25), each tagged with the emitting
    /// extension's id — never routed through a generic, extension-writable file API.</summary>
    public event EventHandler<(ExtensionId ExtensionId, string Message)>? ExtensionLogged;

    /// <summary>Re-runs discovery/validation only (SKILL.md §11, §36) — never unloads or
    /// reactivates an already-<see cref="ExtensionState.Active"/> extension; picking up a change
    /// to an active extension's code/manifest requires restarting DevStudio, a documented
    /// limitation, not unsafe hot-reload behavior.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var discovered = await _discovery.DiscoverAsync(_roots, cancellationToken).ConfigureAwait(false);
        _extensions = ApplyDuplicateAndEnabledState(discovered);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private List<ExtensionDescriptor> ApplyDuplicateAndEnabledState(IReadOnlyList<ExtensionDescriptor> discovered)
    {
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ExtensionDescriptor>(discovered.Count);

        foreach (var descriptor in discovered)
        {
            if (descriptor.Id is { } id)
            {
                if (!seenIds.Add(id.Value))
                {
                    result.Add(descriptor with
                    {
                        State = ExtensionState.Invalid,
                        ValidationErrors = descriptor.ValidationErrors
                            .Append($"Duplicate extension id '{id}' — another extension already claims this id; this one was rejected, not silently merged or overridden.")
                            .ToList(),
                    });
                    continue;
                }

                if (_loadedByIdValue.ContainsKey(id.Value))
                {
                    // Already active from a previous refresh — never regress it back to
                    // Enabled/Disabled just because discovery ran again.
                    var existing = _extensions.FirstOrDefault(d => d.Id == id);
                    result.Add(existing ?? descriptor with { State = ExtensionState.Active });
                    continue;
                }
            }

            if (descriptor.State == ExtensionState.Valid)
            {
                var disabled = descriptor.Id is { } enabledId && _disabledIds.Contains(enabledId.Value);
                result.Add(descriptor with { State = disabled ? ExtensionState.Disabled : ExtensionState.Enabled });
            }
            else
            {
                result.Add(descriptor);
            }
        }

        return result;
    }

    /// <summary>Loads and activates every currently <see cref="ExtensionState.Enabled"/>
    /// extension. One extension's load/activation failure is caught and recorded as
    /// <see cref="ExtensionState.Failed"/> — every other extension still gets its own attempt
    /// (SKILL.md §13, §26, §45).</summary>
    public async Task LoadAndActivateEnabledAsync(CancellationToken cancellationToken = default)
    {
        foreach (var descriptor in _extensions.ToArray())
        {
            if (descriptor.State != ExtensionState.Enabled || descriptor.Manifest is null) continue;
            await LoadAndActivateAsync(descriptor, cancellationToken).ConfigureAwait(false);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadAndActivateAsync(ExtensionDescriptor descriptor, CancellationToken cancellationToken)
    {
        var manifest = descriptor.Manifest!;
        SetState(manifest.Id, ExtensionState.Loading);

        ILoadedExtension loaded;
        try
        {
            loaded = await _loader.LoadAsync(manifest, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SetState(manifest.Id, ExtensionState.Failed, $"Load failed: {ex.Message}");
            return;
        }

        SetState(manifest.Id, ExtensionState.Loaded);

        var declaredCommandIds = manifest.Contributions.Commands.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var registrar = new ScopedCommandRegistrar(_commandRegistry, manifest.Id, declaredCommandIds);
        var context = new ExtensionContext(manifest.Id, registrar, message => ExtensionLogged?.Invoke(this, (manifest.Id, message)));

        try
        {
            await loaded.Instance.ActivateAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _commandRegistry.UnregisterCommandsFor(manifest.Id);
            await SafeDisposeAsync(loaded).ConfigureAwait(false);
            SetState(manifest.Id, ExtensionState.Failed, $"Activation failed: {ex.Message}");
            return;
        }

        _loadedByIdValue[manifest.Id.Value] = loaded;
        SetState(manifest.Id, ExtensionState.Active);
    }

    /// <summary>Deactivates and unloads one active extension — its own
    /// <see cref="IDevStudioExtension.DeactivateAsync"/> failing is caught and recorded, never
    /// allowed to prevent its commands from being unregistered or to affect any other extension
    /// (SKILL.md §26).</summary>
    public async Task DeactivateAsync(ExtensionId id, CancellationToken cancellationToken = default)
    {
        if (!_loadedByIdValue.TryGetValue(id.Value, out var loaded)) return;

        try
        {
            await loaded.Instance.DeactivateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ExtensionLogged?.Invoke(this, (id, $"Deactivation threw: {ex.Message}"));
        }

        _commandRegistry.UnregisterCommandsFor(id);
        await SafeDisposeAsync(loaded).ConfigureAwait(false);
        _loadedByIdValue.Remove(id.Value);
        SetState(id, ExtensionState.Unloaded);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>An extension enabled while already discovered as <see
    /// cref="ExtensionState.Disabled"/> is loaded and activated immediately; enabling a
    /// currently-Invalid/Incompatible extension has no effect until it becomes real, valid,
    /// discovered content.</summary>
    public async Task EnableAsync(ExtensionId id, CancellationToken cancellationToken = default)
    {
        _disabledIds.Remove(id.Value);
        var descriptor = _extensions.FirstOrDefault(d => d.Id == id);
        if (descriptor is null || descriptor.State != ExtensionState.Disabled) return;

        SetState(id, ExtensionState.Enabled);
        var updated = _extensions.First(d => d.Id == id);
        await LoadAndActivateAsync(updated, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task DisableAsync(ExtensionId id, CancellationToken cancellationToken = default)
    {
        _disabledIds.Add(id.Value);

        if (_loadedByIdValue.ContainsKey(id.Value))
        {
            await DeactivateAsync(id, cancellationToken).ConfigureAwait(false);
        }

        SetState(id, ExtensionState.Disabled);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyCollection<string> DisabledExtensionIds => _disabledIds;

    private void SetState(ExtensionId id, ExtensionState state, string? failureReason = null)
    {
        var index = _extensions.FindIndex(d => d.Id == id);
        if (index < 0) return;
        _extensions[index] = _extensions[index] with { State = state, FailureReason = failureReason };
    }

    private static async Task SafeDisposeAsync(ILoadedExtension loaded)
    {
        try
        {
            await loaded.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best-effort unload (SKILL.md §21) — a loader-side disposal failure must not
            // propagate and must not be treated as an activation/command failure of its own.
        }
    }

    private sealed class ExtensionContext : IExtensionContext
    {
        private readonly Action<string> _log;

        public ExtensionContext(ExtensionId extensionId, IExtensionCommandRegistrar commands, Action<string> log)
        {
            ExtensionId = extensionId;
            Commands = commands;
            _log = log;
        }

        public ExtensionId ExtensionId { get; }
        public IExtensionCommandRegistrar Commands { get; }
        public void Log(string message) => _log(message);
    }

    private sealed class ScopedCommandRegistrar : IExtensionCommandRegistrar
    {
        private readonly ICommandRegistry _inner;
        private readonly ExtensionId _ownerExtensionId;
        private readonly IReadOnlySet<string> _declaredCommandIds;

        public ScopedCommandRegistrar(ICommandRegistry inner, ExtensionId ownerExtensionId, IReadOnlySet<string> declaredCommandIds)
        {
            _inner = inner;
            _ownerExtensionId = ownerExtensionId;
            _declaredCommandIds = declaredCommandIds;
        }

        public bool TryRegisterCommand(string commandId, string title, Func<CancellationToken, Task> handler)
        {
            // An extension may only ever bring to life a command id its own manifest already
            // declared (SKILL.md §18) — this is enforced here, not merely by convention.
            if (!_declaredCommandIds.Contains(commandId)) return false;
            return _inner.TryRegisterCommand(_ownerExtensionId, commandId, title, handler);
        }
    }
}
