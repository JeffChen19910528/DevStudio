using DevStudio.Core.Extensions;

namespace DevStudio.SampleExtension;

/// <summary>
/// A real extension that always throws during activation — used only by
/// <c>ExtensionManagerIntegrationTests</c> to prove a real activation failure is caught, recorded
/// as <see cref="ExtensionState.Failed"/>, and never crashes the host or blocks any other
/// extension (SKILL.md §26, §39). Selected via a manifest's <c>entryType</c> field, since this
/// assembly deliberately hosts more than one <see cref="IDevStudioExtension"/> implementation.
/// </summary>
public sealed class ActivationFailingExtension : IDevStudioExtension
{
    public Task ActivateAsync(IExtensionContext context, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Deliberate activation failure for real-integration testing.");

    public Task DeactivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
