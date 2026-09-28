namespace DevStudio.Core.Extensions;

/// <summary>
/// The entire real API surface an extension implements (SKILL.md §14 [Phase 10]). Deliberately
/// minimal: two lifecycle methods, nothing else. An extension receives no reference to <c>
/// MainWindow</c>, <c>Application</c>, the raw filesystem, arbitrary process execution, or any
/// internal DevStudio service — only what <see cref="IExtensionContext"/> exposes.
/// </summary>
public interface IDevStudioExtension
{
    Task ActivateAsync(IExtensionContext context, CancellationToken cancellationToken);

    Task DeactivateAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The controlled set of host services an activating extension receives (SKILL.md §15). Every
/// member is an interface, never a concrete DevStudio type — an extension cannot reach past this
/// surface into the rest of the application (SKILL.md §2, §29, §42).
/// </summary>
public interface IExtensionContext
{
    ExtensionId ExtensionId { get; }

    /// <summary>Scoped strictly to this extension's own declared command contributions — not the
    /// full <see cref="ICommandRegistry"/> (SKILL.md §15, §18: an extension must not be able to
    /// unregister another extension's commands or invoke arbitrary ones through its own
    /// context).</summary>
    IExtensionCommandRegistrar Commands { get; }

    /// <summary>Logs a message under this extension's own isolated log category (SKILL.md §25) —
    /// never written to an arbitrary file the extension names itself.</summary>
    void Log(string message);
}

/// <summary>The narrow, extension-facing slice of <see cref="ICommandRegistry"/> — registration
/// only, always attributed to the extension that owns this context, and only for a command id
/// that extension's own manifest actually declared in <c>contributions.commands</c>.</summary>
public interface IExtensionCommandRegistrar
{
    bool TryRegisterCommand(string commandId, string title, Func<CancellationToken, Task> handler);
}
