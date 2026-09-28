using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevStudio.UI.Services;

namespace DevStudio.UI.ViewModels;

public partial class WorkspaceExplorerViewModel : ObservableObject
{
    private readonly WorkspaceAppService _workspaceAppService;

    [ObservableProperty]
    private string? _rootPath;

    [ObservableProperty]
    private FileTreeNodeViewModel? _selectedNode;

    public ObservableCollection<FileTreeNodeViewModel> RootNodes { get; } = new();

    /// <summary>Raised when the user double-clicks a file node; the shell owns what "open" means.</summary>
    public event EventHandler<string>? FileActivated;

    public WorkspaceExplorerViewModel(WorkspaceAppService workspaceAppService)
    {
        _workspaceAppService = workspaceAppService;
    }

    public async Task LoadRootAsync(string rootPath, ProjectGraphLookup? lookup = null)
    {
        RootPath = rootPath;
        RootNodes.Clear();

        var rootNode = new FileTreeNodeViewModel(Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : rootPath, rootPath, isDirectory: true, _workspaceAppService, lookup)
        {
            IsExpanded = true
        };
        RootNodes.Add(rootNode);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        foreach (var node in RootNodes)
        {
            await node.RefreshAsync().ConfigureAwait(true);
        }
    }

    public void ActivateFile(FileTreeNodeViewModel node)
    {
        if (!node.IsDirectory)
        {
            FileActivated?.Invoke(this, node.FullPath);
        }
    }
}
