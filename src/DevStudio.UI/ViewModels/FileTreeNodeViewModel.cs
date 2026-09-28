using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DevStudio.Core.Projects;
using DevStudio.Core.Workspace;
using DevStudio.UI.Services;

namespace DevStudio.UI.ViewModels;

/// <summary>Visual/semantic category of an Explorer node (SKILL.md §8, §20): Workspace/Solution/
/// Project distinction layered on top of the plain file tree where project detection found
/// something at that path, Folder/File otherwise.</summary>
public enum FileTreeNodeKind
{
    Folder,
    File,
    Project,
    Solution
}

/// <summary>One Explorer tree node. Directories load their children lazily, only when first
/// expanded (SKILL.md §26), never the whole subtree up front.</summary>
public partial class FileTreeNodeViewModel : ObservableObject
{
    private readonly WorkspaceAppService? _workspaceAppService;
    private readonly ProjectGraphLookup _lookup;
    private readonly bool _isPlaceholder;

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }

    public ProjectInfo? Project { get; }
    public SolutionInfo? Solution { get; }
    public FileTreeNodeKind Kind { get; }

    /// <summary>Plain-text kind marker (SKILL.md §20: "do not require complex icons if they add
    /// unnecessary dependencies") so Workspace/Solution/Project/Folder/File stay visually
    /// distinguishable without a new icon-font dependency.</summary>
    public string DisplayLabel => Kind switch
    {
        FileTreeNodeKind.Solution => $"[Solution] {Name}",
        FileTreeNodeKind.Project => $"[{Project!.ProjectType}] {Name}",
        _ => Name,
    };

    [ObservableProperty]
    private bool _isExpanded;

    private bool _childrenLoaded;

    public ObservableCollection<FileTreeNodeViewModel> Children { get; } = new();

    public FileTreeNodeViewModel(string name, string fullPath, bool isDirectory, WorkspaceAppService workspaceAppService, ProjectGraphLookup? lookup = null)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        _workspaceAppService = workspaceAppService;
        _lookup = lookup ?? ProjectGraphLookup.Empty;

        if (isDirectory)
        {
            Solution = _lookup.FindSolution(fullPath);
            Project = Solution is null ? _lookup.FindProject(fullPath) : null;
            Kind = Solution is not null ? FileTreeNodeKind.Solution : Project is not null ? FileTreeNodeKind.Project : FileTreeNodeKind.Folder;

            Children.Add(CreatePlaceholder());
        }
        else
        {
            Kind = FileTreeNodeKind.File;
        }
    }

    private FileTreeNodeViewModel(bool isPlaceholder)
    {
        _isPlaceholder = isPlaceholder;
        _lookup = ProjectGraphLookup.Empty;
        Name = "Loading...";
        FullPath = string.Empty;
        IsDirectory = false;
    }

    private static FileTreeNodeViewModel CreatePlaceholder() => new(isPlaceholder: true);

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_childrenLoaded && IsDirectory)
        {
            _ = LoadChildrenAsync();
        }
    }

    public async Task RefreshAsync()
    {
        _childrenLoaded = false;
        if (IsExpanded)
        {
            await LoadChildrenAsync().ConfigureAwait(true);
        }
        else
        {
            Children.Clear();
            Children.Add(CreatePlaceholder());
        }
    }

    private async Task LoadChildrenAsync()
    {
        if (_workspaceAppService is null) return;

        IReadOnlyList<FileSystemNode> nodes;
        try
        {
            nodes = await _workspaceAppService.GetChildrenAsync(FullPath).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Leave _childrenLoaded false so collapsing and re-expanding retries the scan
            // instead of leaving the "Loading..." placeholder stuck forever (SKILL.md §26).
            Children.Clear();
            Children.Add(CreatePlaceholder());
            return;
        }

        _childrenLoaded = true;
        Children.Clear();
        foreach (var node in nodes)
        {
            Children.Add(new FileTreeNodeViewModel(node.Name, node.FullPath, node.IsDirectory, _workspaceAppService, _lookup));
        }
    }
}
