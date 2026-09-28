using DevStudio.Core.Extensions;

namespace DevStudio.SampleExtension;

/// <summary>
/// A real extension that activates successfully but whose one contributed command always throws
/// when invoked — used only by <c>ExtensionManagerIntegrationTests</c> to prove a real command
/// invocation failure is isolated by <see cref="CommandRegistry.InvokeAsync"/> rather than
/// propagating to the caller (SKILL.md §26, §39).
/// </summary>
public sealed class CommandFailingExtension : IDevStudioExtension
{
    public Task ActivateAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        context.Commands.TryRegisterCommand("sample.failing", "Sample: Failing Command", _ =>
            throw new InvalidOperationException("Deliberate command failure for real-integration testing."));

        return Task.CompletedTask;
    }

    public Task DeactivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
