namespace DevStudio.Core.Extensions;

public sealed record CommandDefinition(string Id, string Title, ExtensionId OwnerExtensionId);

/// <summary>
/// The global command table (SKILL.md §18 [Phase 10]). Command ids are globally unique across
/// every extension — an extension may only register commands under an id it declared in its own
/// manifest's <c>contributions.commands</c>, and it may never register a command id another
/// extension (or DevStudio itself) already owns; a later registration attempt for an
/// already-taken id fails rather than silently overwriting the earlier one (SKILL.md §35).
/// </summary>
public interface ICommandRegistry
{
    IReadOnlyList<CommandDefinition> Commands { get; }

    /// <summary>Registers one command owned by <paramref name="ownerExtensionId"/>. Returns
    /// <c>false</c> (never throws) if <paramref name="commandId"/> is already registered by any
    /// extension — the caller (<see cref="ExtensionManager"/>) turns a <c>false</c> here into a
    /// real, recorded activation failure for the offending extension, never a crash.</summary>
    bool TryRegisterCommand(ExtensionId ownerExtensionId, string commandId, string title, Func<CancellationToken, Task> handler);

    /// <summary>Removes every command owned by this extension — called by
    /// <see cref="ExtensionManager"/> after deactivation, not left to the extension's own
    /// (possibly buggy or malicious) cooperation.</summary>
    void UnregisterCommandsFor(ExtensionId ownerExtensionId);

    Task<bool> InvokeAsync(string commandId, CancellationToken cancellationToken = default);
}
