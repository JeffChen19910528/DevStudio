using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevStudio.Core.Extensions;
using DevStudio.Core.Settings;

namespace DevStudio.UI.ViewModels;

/// <summary>
/// Extensions panel state (SKILL.md §24 [Phase 10]): discovered extensions with their real
/// validation/compatibility/activation status, and the live set of contributed commands. Mirrors
/// <see cref="ToolchainsPanelViewModel"/>'s shape (an async <c>RefreshAsync</c>/
/// <c>IsRefreshing</c> pair over an injected service) rather than introducing a new pattern.
/// Enable/disable state is persisted through the existing <see cref="ISettingsService"/> —
/// never a new, ad-hoc storage location, and never anything beyond the enabled/disabled id list
/// itself (no extension instance, delegate, or runtime object is ever persisted).
/// </summary>
public sealed partial class ExtensionsPanelViewModel : ObservableObject
{
    private readonly ExtensionManager _manager;
    private readonly ICommandRegistry _commandRegistry;
    private readonly ISettingsService _settingsService;

    public ObservableCollection<ExtensionDescriptor> Extensions { get; } = new();
    public ObservableCollection<CommandDefinition> ContributedCommands { get; } = new();

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private ExtensionDescriptor? _selectedExtension;

    [ObservableProperty]
    private CommandDefinition? _selectedCommand;

    [ObservableProperty]
    private string? _lastInvocationResult;

    public ExtensionsPanelViewModel(ExtensionManager manager, ICommandRegistry commandRegistry, ISettingsService settingsService)
    {
        _manager = manager;
        _commandRegistry = commandRegistry;
        _settingsService = settingsService;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            await _manager.RefreshAsync().ConfigureAwait(true);
            await _manager.LoadAndActivateEnabledAsync().ConfigureAwait(true);
        }
        finally
        {
            IsRefreshing = false;
        }

        SyncFromManager();
    }

    [RelayCommand]
    public async Task EnableAsync(ExtensionDescriptor? descriptor)
    {
        if (descriptor?.Id is not { } id) return;
        await _manager.EnableAsync(id).ConfigureAwait(true);
        PersistDisabledIds();
        SyncFromManager();
    }

    [RelayCommand]
    public async Task DisableAsync(ExtensionDescriptor? descriptor)
    {
        if (descriptor?.Id is not { } id) return;
        await _manager.DisableAsync(id).ConfigureAwait(true);
        PersistDisabledIds();
        SyncFromManager();
    }

    /// <summary>Manually invokes a contributed command (SKILL.md §17, §48's manual acceptance
    /// step 13) — the same real <see cref="ICommandRegistry.InvokeAsync"/> path a future menu
    /// contribution would use; a failing command is reported here, never left silently
    /// swallowed, but never crashes the host either (SKILL.md §26).</summary>
    [RelayCommand]
    public async Task InvokeCommandAsync(CommandDefinition? command)
    {
        if (command is null) return;
        var succeeded = await _commandRegistry.InvokeAsync(command.Id).ConfigureAwait(true);
        LastInvocationResult = succeeded
            ? $"'{command.Title}' completed."
            : $"'{command.Title}' failed — see the Output log for details.";
    }

    private void PersistDisabledIds() =>
        _settingsService.Update(_settingsService.Current with { DisabledExtensionIds = _manager.DisabledExtensionIds.ToList() });

    private void SyncFromManager()
    {
        Extensions.Clear();
        foreach (var descriptor in _manager.Extensions.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            Extensions.Add(descriptor);
        }

        ContributedCommands.Clear();
        foreach (var command in _commandRegistry.Commands)
        {
            ContributedCommands.Add(command);
        }
    }
}
