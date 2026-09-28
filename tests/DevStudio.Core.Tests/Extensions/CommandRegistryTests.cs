using DevStudio.Core.Extensions;
using Xunit;

namespace DevStudio.Core.Tests.Extensions;

public class CommandRegistryTests
{
    private static ExtensionId Id(string value)
    {
        ExtensionId.TryParse(value, out var id);
        return id;
    }

    [Fact]
    public void Registering_a_new_command_id_succeeds()
    {
        var registry = new CommandRegistry();
        var registered = registry.TryRegisterCommand(Id("a.one"), "cmd.one", "One", _ => Task.CompletedTask);

        Assert.True(registered);
        Assert.Equal("cmd.one", Assert.Single(registry.Commands).Id);
    }

    [Fact]
    public void A_second_extension_cannot_register_an_already_taken_command_id()
    {
        var registry = new CommandRegistry();
        registry.TryRegisterCommand(Id("a.one"), "cmd.shared", "First", _ => Task.CompletedTask);

        var overridden = registry.TryRegisterCommand(Id("b.two"), "cmd.shared", "Second", _ => Task.CompletedTask);

        Assert.False(overridden);
        Assert.Equal("First", Assert.Single(registry.Commands).Title); // never silently overwritten
    }

    [Fact]
    public void UnregisterCommandsFor_only_removes_that_extensions_own_commands()
    {
        var registry = new CommandRegistry();
        registry.TryRegisterCommand(Id("a.one"), "cmd.a", "A", _ => Task.CompletedTask);
        registry.TryRegisterCommand(Id("b.two"), "cmd.b", "B", _ => Task.CompletedTask);

        registry.UnregisterCommandsFor(Id("a.one"));

        Assert.Equal("cmd.b", Assert.Single(registry.Commands).Id);
    }

    [Fact]
    public async Task InvokeAsync_returns_true_and_runs_the_handler_for_a_known_command()
    {
        var registry = new CommandRegistry();
        var ran = false;
        registry.TryRegisterCommand(Id("a.one"), "cmd.a", "A", _ => { ran = true; return Task.CompletedTask; });

        var invoked = await registry.InvokeAsync("cmd.a");

        Assert.True(invoked);
        Assert.True(ran);
    }

    [Fact]
    public async Task InvokeAsync_returns_false_for_an_unknown_command_id()
    {
        var registry = new CommandRegistry();
        Assert.False(await registry.InvokeAsync("does.not.exist"));
    }

    [Fact]
    public async Task InvokeAsync_isolates_a_handler_exception_never_propagating_it()
    {
        var registry = new CommandRegistry();
        registry.TryRegisterCommand(Id("a.one"), "cmd.a", "A", _ => throw new InvalidOperationException("boom"));

        Exception? seen = null;
        registry.CommandInvocationFailed += (_, e) => seen = e.Exception;

        var invoked = await registry.InvokeAsync("cmd.a");

        Assert.False(invoked);
        Assert.NotNull(seen);
        Assert.Equal("boom", seen!.Message);
    }
}
