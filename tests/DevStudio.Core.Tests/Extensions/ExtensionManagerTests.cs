using DevStudio.Core.Extensions;
using Xunit;

namespace DevStudio.Core.Tests.Extensions;

file sealed class FakeExtensionDiscovery : IExtensionDiscovery
{
    public List<ExtensionDescriptor> DescriptorsToReturn { get; set; } = new();

    public Task<IReadOnlyList<ExtensionDescriptor>> DiscoverAsync(IReadOnlyList<string> roots, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ExtensionDescriptor>>(DescriptorsToReturn);
}

file sealed class FakeLoadedExtension : ILoadedExtension
{
    public IDevStudioExtension Instance { get; }
    public bool Disposed { get; private set; }

    public FakeLoadedExtension(IDevStudioExtension instance) => Instance = instance;

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

file sealed class FakeExtension : IDevStudioExtension
{
    public bool ThrowOnActivate { get; set; }
    public bool Activated { get; private set; }
    public bool Deactivated { get; private set; }

    public Task ActivateAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        if (ThrowOnActivate) throw new InvalidOperationException("simulated activation failure");
        Activated = true;
        context.Commands.TryRegisterCommand("fake.command", "Fake Command", _ => Task.CompletedTask);
        return Task.CompletedTask;
    }

    public Task DeactivateAsync(CancellationToken cancellationToken)
    {
        Deactivated = true;
        return Task.CompletedTask;
    }
}

file sealed class FakeExtensionLoader : IExtensionLoader
{
    public Dictionary<string, FakeExtension> ExtensionsByEntryPoint { get; } = new();

    public Task<ILoadedExtension> LoadAsync(ExtensionManifest manifest, CancellationToken cancellationToken = default)
    {
        var extension = ExtensionsByEntryPoint.TryGetValue(manifest.EntryPoint, out var existing) ? existing : new FakeExtension();
        ExtensionsByEntryPoint[manifest.EntryPoint] = extension;
        return Task.FromResult<ILoadedExtension>(new FakeLoadedExtension(extension));
    }
}

public class ExtensionManagerTests
{
    private static ExtensionManifest MakeManifest(string id, string entryPoint = "ext.dll") =>
        new(
            Id: Parse(id),
            Name: "Name",
            DisplayName: "Display",
            Version: new ExtensionVersion(1, 0, 0),
            Publisher: "Pub",
            Description: "Desc",
            HostVersionRange: ParseRange(">=1.0.0"),
            EntryPoint: entryPoint,
            EntryType: null,
            Capabilities: new[] { ExtensionCapability.Command },
            Contributions: new ExtensionContributions(new[] { new ExtensionCommandContribution("fake.command", "Fake Command") }));

    private static ExtensionId Parse(string value)
    {
        ExtensionId.TryParse(value, out var id);
        return id;
    }

    private static ExtensionVersionRange ParseRange(string value)
    {
        ExtensionVersionRange.TryParse(value, out var range);
        return range!;
    }

    [Fact]
    public async Task RefreshAsync_marks_a_second_duplicate_id_Invalid_and_keeps_the_first()
    {
        var discovery = new FakeExtensionDiscovery
        {
            DescriptorsToReturn =
            {
                new ExtensionDescriptor("/a", MakeManifest("dup.one"), ExtensionState.Valid, Array.Empty<string>()),
                new ExtensionDescriptor("/b", MakeManifest("dup.one"), ExtensionState.Valid, Array.Empty<string>()),
            },
        };
        var manager = new ExtensionManager(discovery, new FakeExtensionLoader(), new CommandRegistry(), new[] { "/root" });

        await manager.RefreshAsync();

        Assert.Equal(2, manager.Extensions.Count);
        Assert.Single(manager.Extensions, e => e.State == ExtensionState.Enabled);
        Assert.Single(manager.Extensions, e => e.State == ExtensionState.Invalid);
    }

    [Fact]
    public async Task A_disabled_extension_is_never_loaded_by_LoadAndActivateEnabledAsync()
    {
        var discovery = new FakeExtensionDiscovery { DescriptorsToReturn = { new ExtensionDescriptor("/a", MakeManifest("some.ext"), ExtensionState.Valid, Array.Empty<string>()) } };
        var loader = new FakeExtensionLoader();
        var manager = new ExtensionManager(discovery, loader, new CommandRegistry(), new[] { "/root" }, initiallyDisabledExtensionIds: new[] { "some.ext" });

        await manager.RefreshAsync();
        await manager.LoadAndActivateEnabledAsync();

        Assert.Equal(ExtensionState.Disabled, manager.Extensions.Single().State);
        Assert.Empty(loader.ExtensionsByEntryPoint);
    }

    [Fact]
    public async Task An_activation_exception_marks_only_that_extension_Failed()
    {
        var discovery = new FakeExtensionDiscovery
        {
            DescriptorsToReturn =
            {
                new ExtensionDescriptor("/a", MakeManifest("good.ext", "good.dll"), ExtensionState.Valid, Array.Empty<string>()),
                new ExtensionDescriptor("/b", MakeManifest("bad.ext", "bad.dll"), ExtensionState.Valid, Array.Empty<string>()),
            },
        };
        var loader = new FakeExtensionLoader();
        loader.ExtensionsByEntryPoint["bad.dll"] = new FakeExtension { ThrowOnActivate = true };
        var commandRegistry = new CommandRegistry();
        var manager = new ExtensionManager(discovery, loader, commandRegistry, new[] { "/root" });

        await manager.RefreshAsync();
        await manager.LoadAndActivateEnabledAsync();

        Assert.Equal(ExtensionState.Active, manager.Extensions.Single(e => e.Id!.Value.Value == "good.ext").State);
        var bad = manager.Extensions.Single(e => e.Id!.Value.Value == "bad.ext");
        Assert.Equal(ExtensionState.Failed, bad.State);
        Assert.Contains("Activation failed", bad.FailureReason);
        Assert.Single(commandRegistry.Commands); // only the good extension's
    }

    [Fact]
    public async Task DeactivateAsync_unregisters_commands_and_disposes_the_loaded_extension()
    {
        var discovery = new FakeExtensionDiscovery { DescriptorsToReturn = { new ExtensionDescriptor("/a", MakeManifest("some.ext"), ExtensionState.Valid, Array.Empty<string>()) } };
        var loader = new FakeExtensionLoader();
        var commandRegistry = new CommandRegistry();
        var manager = new ExtensionManager(discovery, loader, commandRegistry, new[] { "/root" });
        await manager.RefreshAsync();
        await manager.LoadAndActivateEnabledAsync();

        await manager.DeactivateAsync(Parse("some.ext"));

        Assert.Empty(commandRegistry.Commands);
        Assert.Equal(ExtensionState.Unloaded, manager.Extensions.Single().State);
        Assert.True(loader.ExtensionsByEntryPoint["ext.dll"].Deactivated);
    }

    [Fact]
    public async Task RefreshAsync_does_not_regress_an_already_Active_extension_back_to_Enabled()
    {
        var discovery = new FakeExtensionDiscovery { DescriptorsToReturn = { new ExtensionDescriptor("/a", MakeManifest("some.ext"), ExtensionState.Valid, Array.Empty<string>()) } };
        var manager = new ExtensionManager(discovery, new FakeExtensionLoader(), new CommandRegistry(), new[] { "/root" });
        await manager.RefreshAsync();
        await manager.LoadAndActivateEnabledAsync();

        await manager.RefreshAsync();

        Assert.Equal(ExtensionState.Active, manager.Extensions.Single().State);
    }
}
