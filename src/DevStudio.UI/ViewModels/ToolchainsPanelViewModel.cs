using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevStudio.Core.Toolchains;

namespace DevStudio.UI.ViewModels;

/// <summary>
/// Basic Toolchains view (SKILL.md §22): what's installed, its version/path, and its status.
/// Detection is asynchronous and re-entrant-safe — the UI stays responsive during a refresh
/// (SKILL.md §20, §34), and <see cref="RefreshCompleted"/> lets the shell re-run project
/// capability matching once fresh results are in.
/// </summary>
public partial class ToolchainsPanelViewModel : ObservableObject
{
    private readonly IToolchainRegistry _registry;
    private readonly IVisualStudioDetector _visualStudioDetector;

    public ObservableCollection<ToolchainInfo> Toolchains { get; } = new();
    public ObservableCollection<VisualStudioInstance> VisualStudioInstances { get; } = new();

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private DateTimeOffset? _lastRefreshedUtc;

    public event EventHandler? RefreshCompleted;

    public bool HasNoVisualStudioInstances => VisualStudioInstances.Count == 0;

    public ToolchainsPanelViewModel(IToolchainRegistry registry, IVisualStudioDetector visualStudioDetector)
    {
        _registry = registry;
        _visualStudioDetector = visualStudioDetector;
        VisualStudioInstances.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoVisualStudioInstances));
    }

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsRefreshing = true;
        try
        {
            var toolchainsTask = _registry.RefreshAsync(cancellationToken);
            var vsTask = _visualStudioDetector.DetectAllAsync(cancellationToken);
            await Task.WhenAll(toolchainsTask, vsTask).ConfigureAwait(true);

            Toolchains.Clear();
            foreach (var toolchain in toolchainsTask.Result.OrderBy(t => t.Name))
            {
                Toolchains.Add(toolchain);
            }

            VisualStudioInstances.Clear();
            foreach (var instance in vsTask.Result)
            {
                VisualStudioInstances.Add(instance);
            }

            LastRefreshedUtc = DateTimeOffset.UtcNow;
        }
        finally
        {
            IsRefreshing = false;
        }

        RefreshCompleted?.Invoke(this, EventArgs.Empty);
    }
}
