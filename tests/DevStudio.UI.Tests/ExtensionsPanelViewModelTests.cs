using DevStudio.Core.Extensions;
using DevStudio.UI.Tests.Fakes;
using DevStudio.UI.ViewModels;
using Xunit;

namespace DevStudio.UI.Tests;

file sealed class FakeLoadedExtension : ILoadedExtension
{
    public IDevStudioExtension Instance { get; }
    public FakeLoadedExtension(IDevStudioExtension instance) => Instance = instance;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

file sealed class FakeExtension : IDevStudioExtension
{
    public Task ActivateAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        context.Commands.TryRegisterCommand("sample.hello", "Sample: Hello", _ => Task.CompletedTask);
        return Task.CompletedTask;
    }

    public Task DeactivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

file sealed class WorkingLoader : IExtensionLoader
{
    public Task<ILoadedExtension> LoadAsync(ExtensionManifest manifest, CancellationToken cancellationToken = default) =>
        Task.FromResult<ILoadedExtension>(new FakeLoadedExtension(new FakeExtension()));
}

public class ExtensionsPanelViewModelTests
{
    private static ExtensionManifest MakeManifest(string id) =>
        new(
            Parse(id), "Name", "Display", new ExtensionVersion(1, 0, 0), "Pub", "Desc",
            ParseRange(">=1.0.0"), "ext.dll", null,
            new[] { ExtensionCapability.Command },
            new ExtensionContributions(new[] { new ExtensionCommandContribution("sample.hello", "Sample: Hello") }));

    private static ExtensionId Parse(string value) { ExtensionId.TryParse(value, out var id); return id; }
    private static ExtensionVersionRange ParseRange(string value) { ExtensionVersionRange.TryParse(value, out var range); return range!; }

    private static (ExtensionsPanelViewModel ViewModel, FakeExtensionDiscovery Discovery, CommandRegistry Commands, FakeSettingsService Settings) Build()
    {
        var discovery = new FakeExtensionDiscovery();
        var commands = new CommandRegistry();
        var settings = new FakeSettingsService();
        var manager = new ExtensionManager(discovery, new WorkingLoader(), commands, Array.Empty<string>());
        var viewModel = new ExtensionsPanelViewModel(manager, commands, settings);
        return (viewModel, discovery, commands, settings);
    }

    [Fact]
    public async Task RefreshAsync_populates_discovered_extensions_and_loads_enabled_ones()
    {
        var (viewModel, discovery, commands, _) = Build();
        discovery.DescriptorsToReturn.Add(new ExtensionDescriptor("/ext", MakeManifest("devstudio.sample"), ExtensionState.Valid, Array.Empty<string>()));

        await viewModel.RefreshAsync();

        var descriptor = Assert.Single(viewModel.Extensions);
        Assert.Equal(ExtensionState.Active, descriptor.State);
        Assert.Single(viewModel.ContributedCommands);
        Assert.Single(commands.Commands);
    }

    [Fact]
    public async Task DisableAsync_deactivates_the_extension_and_persists_the_disabled_id()
    {
        var (viewModel, discovery, commands, settings) = Build();
        discovery.DescriptorsToReturn.Add(new ExtensionDescriptor("/ext", MakeManifest("devstudio.sample"), ExtensionState.Valid, Array.Empty<string>()));
        await viewModel.RefreshAsync();
        var descriptor = viewModel.Extensions.Single();

        await viewModel.DisableCommand.ExecuteAsync(descriptor);

        Assert.Equal(ExtensionState.Disabled, viewModel.Extensions.Single().State);
        Assert.Empty(commands.Commands);
        Assert.Contains("devstudio.sample", settings.Current.DisabledExtensionIds);
    }

    [Fact]
    public async Task EnableAsync_reactivates_a_disabled_extension_and_removes_the_persisted_disabled_id()
    {
        var (viewModel, discovery, commands, settings) = Build();
        discovery.DescriptorsToReturn.Add(new ExtensionDescriptor("/ext", MakeManifest("devstudio.sample"), ExtensionState.Valid, Array.Empty<string>()));
        await viewModel.RefreshAsync();
        await viewModel.DisableCommand.ExecuteAsync(viewModel.Extensions.Single());

        await viewModel.EnableCommand.ExecuteAsync(viewModel.Extensions.Single());

        Assert.Equal(ExtensionState.Active, viewModel.Extensions.Single().State);
        Assert.Single(commands.Commands);
        Assert.DoesNotContain("devstudio.sample", settings.Current.DisabledExtensionIds);
    }

    [Fact]
    public async Task An_invalid_manifest_is_shown_with_its_validation_errors_and_contributes_no_command()
    {
        var (viewModel, discovery, commands, _) = Build();
        discovery.DescriptorsToReturn.Add(new ExtensionDescriptor("/broken", null, ExtensionState.Invalid, new[] { "Missing required field 'id'." }));

        await viewModel.RefreshAsync();

        var descriptor = Assert.Single(viewModel.Extensions);
        Assert.Equal(ExtensionState.Invalid, descriptor.State);
        Assert.NotEmpty(descriptor.ValidationErrors);
        Assert.Empty(commands.Commands);
    }

    [Fact]
    public async Task InvokeCommandAsync_runs_the_real_registered_handler_and_reports_success()
    {
        var (viewModel, discovery, commands, _) = Build();
        discovery.DescriptorsToReturn.Add(new ExtensionDescriptor("/ext", MakeManifest("devstudio.sample"), ExtensionState.Valid, Array.Empty<string>()));
        await viewModel.RefreshAsync();
        var command = viewModel.ContributedCommands.Single();

        await viewModel.InvokeCommandCommand.ExecuteAsync(command);

        Assert.Contains("completed", viewModel.LastInvocationResult);
    }
}
