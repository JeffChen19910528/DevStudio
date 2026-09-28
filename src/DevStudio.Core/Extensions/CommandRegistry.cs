using System.Collections.Concurrent;

namespace DevStudio.Core.Extensions;

/// <summary>
/// The real, in-memory <see cref="ICommandRegistry"/> implementation. A command invocation
/// failure is isolated here (SKILL.md §26) — <see cref="InvokeAsync"/> never lets a handler's
/// exception propagate to its caller; it is caught, reported through
/// <see cref="CommandInvocationFailed"/>, and reported back as <c>false</c>.
/// </summary>
public sealed class CommandRegistry : ICommandRegistry
{
    private readonly ConcurrentDictionary<string, (CommandDefinition Definition, Func<CancellationToken, Task> Handler)> _commands = new(StringComparer.Ordinal);

    public event EventHandler<(string CommandId, Exception Exception)>? CommandInvocationFailed;

    public IReadOnlyList<CommandDefinition> Commands => _commands.Values.Select(v => v.Definition).ToList();

    public bool TryRegisterCommand(ExtensionId ownerExtensionId, string commandId, string title, Func<CancellationToken, Task> handler)
    {
        var definition = new CommandDefinition(commandId, title, ownerExtensionId);
        return _commands.TryAdd(commandId, (definition, handler));
    }

    public void UnregisterCommandsFor(ExtensionId ownerExtensionId)
    {
        foreach (var (commandId, entry) in _commands.ToArray())
        {
            if (entry.Definition.OwnerExtensionId == ownerExtensionId)
            {
                _commands.TryRemove(commandId, out _);
            }
        }
    }

    public async Task<bool> InvokeAsync(string commandId, CancellationToken cancellationToken = default)
    {
        if (!_commands.TryGetValue(commandId, out var entry)) return false;

        try
        {
            await entry.Handler(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            CommandInvocationFailed?.Invoke(this, (commandId, ex));
            return false;
        }
    }
}
