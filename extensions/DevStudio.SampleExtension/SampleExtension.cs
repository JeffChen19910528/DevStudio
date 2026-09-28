using DevStudio.Core.Extensions;

namespace DevStudio.SampleExtension;

/// <summary>
/// The real Phase 10 proof-of-lifecycle extension (SKILL.md §3, §17): contributes exactly one
/// harmless command, <c>sample.hello</c>, which logs a fixed greeting through the extension
/// context — nothing that touches the filesystem, a process, or the network. Its purpose is to
/// prove Discovery → Validation → Load → Activate → Command Registration → Invocation →
/// Deactivation actually works end to end against a real, compiled, external assembly — not to
/// demonstrate functionality.
/// </summary>
public sealed class SampleExtension : IDevStudioExtension
{
    public Task ActivateAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        context.Commands.TryRegisterCommand("sample.hello", "Sample: Hello", _ =>
        {
            context.Log("Hello from DevStudio Sample Extension");
            return Task.CompletedTask;
        });

        return Task.CompletedTask;
    }

    public Task DeactivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
