using DevStudio.Core.Extensions;
using DevStudio.Infrastructure.Extensions;
using DevStudio.Infrastructure.Tests.TestSupport;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Extensions;

/// <summary>
/// SKILL.md §38–§40 (Phase 10): a real, compiled external extension assembly
/// (<c>DevStudio.SampleExtension.dll</c>, built by the sibling <c>extensions/</c> project) is
/// copied into a real temporary directory alongside a real manifest and driven through the real
/// <c>ExtensionManager → FileSystemExtensionDiscovery/AssemblyLoadContextExtensionLoader →
/// real AssemblyLoadContext</c> pipeline. No fake loader, no fake discovery, no simulated
/// activation anywhere in this file.
/// </summary>
public class ExtensionManagerIntegrationTests
{
    private static readonly string SampleAssemblyPath = typeof(SampleExtension.SampleExtension).Assembly.Location;

    private static string WriteExtension(TempDirectory temp, string folderName, string manifestJson)
    {
        var directory = Path.Combine(temp.Path, folderName);
        Directory.CreateDirectory(directory);
        File.Copy(SampleAssemblyPath, Path.Combine(directory, "DevStudio.SampleExtension.dll"), overwrite: true);
        File.WriteAllText(Path.Combine(directory, FileSystemExtensionDiscovery.ManifestFileName), manifestJson);
        return directory;
    }

    private static string Manifest(
        string id = "devstudio.sample-extension",
        string version = "1.0.0",
        string hostVersionRange = ">=1.0.0 <2.0.0",
        string entryPoint = "DevStudio.SampleExtension.dll",
        string? entryType = "DevStudio.SampleExtension.SampleExtension",
        string capabilities = "\"command\"",
        string commandsJson = """[{"id":"sample.hello","title":"Sample: Hello"}]""")
    {
        var entryTypeJson = entryType is null ? "" : $""", "entryType": "{entryType}" """;
        return $$"""
            {
              "id": "{{id}}",
              "name": "SampleExtension",
              "displayName": "Sample Extension",
              "version": "{{version}}",
              "publisher": "DevStudio",
              "description": "Real Phase 10 sample extension.",
              "hostVersionRange": "{{hostVersionRange}}",
              "entryPoint": "{{entryPoint}}"{{entryTypeJson}},
              "capabilities": [{{capabilities}}],
              "contributions": { "commands": {{commandsJson}} }
            }
            """;
    }

    private static (ExtensionManager Manager, CommandRegistry Commands) CreateManager(IReadOnlyList<string> roots, IEnumerable<string>? disabled = null)
    {
        var discovery = new FileSystemExtensionDiscovery();
        var loader = new AssemblyLoadContextExtensionLoader();
        var commands = new CommandRegistry();
        var manager = new ExtensionManager(discovery, loader, commands, roots, disabled);
        return (manager, commands);
    }

    // --- Full real lifecycle: discover -> validate -> load -> activate -> invoke -> deactivate --

    [Fact]
    public async Task Real_full_lifecycle_discovers_loads_activates_invokes_and_deactivates_the_sample_extension()
    {
        using var temp = new TempDirectory();
        WriteExtension(temp, "sample", Manifest());
        var (manager, commands) = CreateManager(new[] { temp.Path });

        await manager.RefreshAsync();
        var discovered = Assert.Single(manager.Extensions);
        Assert.Equal(ExtensionState.Enabled, discovered.State);

        string? loggedMessage = null;
        manager.ExtensionLogged += (_, e) => loggedMessage = e.Message;

        await manager.LoadAndActivateEnabledAsync();
        var active = Assert.Single(manager.Extensions);
        Assert.Equal(ExtensionState.Active, active.State);

        var command = Assert.Single(commands.Commands);
        Assert.Equal("sample.hello", command.Id);

        var invoked = await commands.InvokeAsync("sample.hello");
        Assert.True(invoked);
        Assert.Equal("Hello from DevStudio Sample Extension", loggedMessage);

        await manager.DeactivateAsync(discovered.Id!.Value);
        Assert.Empty(commands.Commands);
        Assert.Equal(ExtensionState.Unloaded, Assert.Single(manager.Extensions).State);
    }

    // --- Activation failure isolation ---------------------------------------------------------

    [Fact]
    public async Task A_real_activation_failure_is_isolated_and_does_not_prevent_another_extension_from_activating()
    {
        using var temp = new TempDirectory();
        WriteExtension(temp, "good", Manifest(id: "devstudio.good-extension"));
        WriteExtension(temp, "bad", Manifest(
            id: "devstudio.bad-extension",
            entryType: "DevStudio.SampleExtension.ActivationFailingExtension",
            commandsJson: "[]",
            capabilities: ""));

        var (manager, commands) = CreateManager(new[] { temp.Path });
        await manager.RefreshAsync();
        await manager.LoadAndActivateEnabledAsync();

        var good = manager.Extensions.Single(e => e.Id!.Value.Value == "devstudio.good-extension");
        var bad = manager.Extensions.Single(e => e.Id!.Value.Value == "devstudio.bad-extension");

        Assert.Equal(ExtensionState.Active, good.State);
        Assert.Equal(ExtensionState.Failed, bad.State);
        Assert.Contains("Activation failed", bad.FailureReason);
        Assert.Single(commands.Commands); // only the good extension's command survives
    }

    // --- Command invocation failure isolation --------------------------------------------------

    [Fact]
    public async Task A_real_command_failure_is_isolated_and_the_extension_stays_Active()
    {
        using var temp = new TempDirectory();
        WriteExtension(temp, "failing", Manifest(
            id: "devstudio.failing-command",
            entryType: "DevStudio.SampleExtension.CommandFailingExtension",
            commandsJson: """[{"id":"sample.failing","title":"Sample: Failing Command"}]"""));

        var (manager, commands) = CreateManager(new[] { temp.Path });
        await manager.RefreshAsync();
        await manager.LoadAndActivateEnabledAsync();

        (Exception Exception, string CommandId)? failure = null;
        commands.CommandInvocationFailed += (_, e) => failure = (e.Exception, e.CommandId);

        var invoked = await commands.InvokeAsync("sample.failing");

        Assert.False(invoked);
        Assert.NotNull(failure);
        Assert.Equal("sample.failing", failure!.Value.CommandId);
        Assert.Equal(ExtensionState.Active, Assert.Single(manager.Extensions).State);
    }

    // --- Invalid manifest -----------------------------------------------------------------------

    [Fact]
    public async Task A_real_malformed_manifest_is_rejected_with_a_structured_diagnostic_and_never_loaded()
    {
        using var temp = new TempDirectory();
        var directory = Path.Combine(temp.Path, "broken");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, FileSystemExtensionDiscovery.ManifestFileName), "{ this is not valid json");

        var (manager, _) = CreateManager(new[] { temp.Path });
        await manager.RefreshAsync();

        var descriptor = Assert.Single(manager.Extensions);
        Assert.Equal(ExtensionState.Invalid, descriptor.State);
        Assert.NotEmpty(descriptor.ValidationErrors);
        Assert.Null(descriptor.Manifest);
    }

    [Fact]
    public async Task A_manifest_missing_required_fields_is_rejected()
    {
        using var temp = new TempDirectory();
        var directory = Path.Combine(temp.Path, "incomplete");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, FileSystemExtensionDiscovery.ManifestFileName), """{ "id": "devstudio.incomplete" }""");

        var (manager, _) = CreateManager(new[] { temp.Path });
        await manager.RefreshAsync();

        var descriptor = Assert.Single(manager.Extensions);
        Assert.Equal(ExtensionState.Invalid, descriptor.State);
        Assert.Contains(descriptor.ValidationErrors, e => e.Contains("version", StringComparison.OrdinalIgnoreCase));
    }

    // --- Incompatible host version ---------------------------------------------------------------

    [Fact]
    public async Task A_real_incompatible_host_version_range_is_rejected_and_never_loaded()
    {
        using var temp = new TempDirectory();
        WriteExtension(temp, "future", Manifest(hostVersionRange: ">=99.0.0"));

        var (manager, _) = CreateManager(new[] { temp.Path });
        await manager.RefreshAsync();
        await manager.LoadAndActivateEnabledAsync();

        var descriptor = Assert.Single(manager.Extensions);
        Assert.Equal(ExtensionState.Incompatible, descriptor.State);
    }

    [Fact]
    public async Task A_malformed_host_version_range_is_rejected_as_invalid()
    {
        using var temp = new TempDirectory();
        WriteExtension(temp, "malformed-range", Manifest(hostVersionRange: "not-a-version-range"));

        var (manager, _) = CreateManager(new[] { temp.Path });
        await manager.RefreshAsync();

        Assert.Equal(ExtensionState.Invalid, Assert.Single(manager.Extensions).State);
    }

    // --- Duplicate extension ID -------------------------------------------------------------------

    [Fact]
    public async Task A_duplicate_extension_id_is_rejected_and_never_silently_overrides_the_first()
    {
        using var temp = new TempDirectory();
        WriteExtension(temp, "first", Manifest());
        WriteExtension(temp, "second", Manifest());

        var (manager, _) = CreateManager(new[] { temp.Path });
        await manager.RefreshAsync();
        await manager.LoadAndActivateEnabledAsync();

        Assert.Equal(2, manager.Extensions.Count);
        Assert.Single(manager.Extensions, e => e.State == ExtensionState.Active);
        Assert.Single(manager.Extensions, e => e.State == ExtensionState.Invalid);
    }

    // --- Path traversal -----------------------------------------------------------------------

    [Fact]
    public async Task A_real_path_traversal_entry_point_is_rejected()
    {
        using var temp = new TempDirectory();
        WriteExtension(temp, "traversal", Manifest(entryPoint: "../outside.dll", entryType: null));

        var (manager, _) = CreateManager(new[] { temp.Path });
        await manager.RefreshAsync();

        var descriptor = Assert.Single(manager.Extensions);
        Assert.Equal(ExtensionState.Invalid, descriptor.State);
        Assert.Contains(descriptor.ValidationErrors, e => e.Contains("traversal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_real_absolute_entry_point_is_rejected()
    {
        using var temp = new TempDirectory();
        var absolute = Path.Combine(temp.Path, "outside.dll").Replace("\\", "\\\\");
        WriteExtension(temp, "absolute", Manifest(entryPoint: absolute, entryType: null));

        var (manager, _) = CreateManager(new[] { temp.Path });
        await manager.RefreshAsync();

        Assert.Equal(ExtensionState.Invalid, Assert.Single(manager.Extensions).State);
    }

    // --- Enable / Disable --------------------------------------------------------------------

    [Fact]
    public async Task Disabling_an_active_extension_deactivates_it_and_unregisters_its_commands()
    {
        using var temp = new TempDirectory();
        WriteExtension(temp, "sample", Manifest());
        var (manager, commands) = CreateManager(new[] { temp.Path });
        await manager.RefreshAsync();
        await manager.LoadAndActivateEnabledAsync();
        var id = manager.Extensions.Single().Id!.Value;

        await manager.DisableAsync(id);

        Assert.Equal(ExtensionState.Disabled, manager.Extensions.Single().State);
        Assert.Empty(commands.Commands);
        Assert.Contains(id.Value, manager.DisabledExtensionIds);
    }

    [Fact]
    public async Task A_previously_disabled_extension_is_discovered_as_Disabled_not_Enabled()
    {
        using var temp = new TempDirectory();
        WriteExtension(temp, "sample", Manifest());
        var (manager, _) = CreateManager(new[] { temp.Path }, disabled: new[] { "devstudio.sample-extension" });

        await manager.RefreshAsync();

        Assert.Equal(ExtensionState.Disabled, Assert.Single(manager.Extensions).State);
    }

    [Fact]
    public async Task Enabling_a_disabled_extension_loads_and_activates_it()
    {
        using var temp = new TempDirectory();
        WriteExtension(temp, "sample", Manifest());
        var (manager, commands) = CreateManager(new[] { temp.Path }, disabled: new[] { "devstudio.sample-extension" });
        await manager.RefreshAsync();
        var id = manager.Extensions.Single().Id!.Value;

        await manager.EnableAsync(id);

        Assert.Equal(ExtensionState.Active, manager.Extensions.Single().State);
        Assert.Single(commands.Commands);
    }

    // --- Refresh never disturbs an already-active extension ------------------------------------

    [Fact]
    public async Task Refreshing_again_does_not_reset_an_already_active_extension()
    {
        using var temp = new TempDirectory();
        WriteExtension(temp, "sample", Manifest());
        var (manager, commands) = CreateManager(new[] { temp.Path });
        await manager.RefreshAsync();
        await manager.LoadAndActivateEnabledAsync();

        await manager.RefreshAsync();

        Assert.Equal(ExtensionState.Active, Assert.Single(manager.Extensions).State);
        Assert.Single(commands.Commands);
    }
}
