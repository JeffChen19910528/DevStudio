using DevStudio.Core.Workspace;
using DevStudio.UI.Services;
using DevStudio.UI.Tests.Fakes;
using DevStudio.UI.ViewModels;
using Xunit;

namespace DevStudio.UI.Tests;

public class WorkspaceExplorerViewModelTests
{
    [Fact]
    public async Task LoadRootAsync_auto_expands_the_root_and_applies_default_exclusions()
    {
        var scanner = new FakeWorkspaceScanner(new()
        {
            ["/repo"] = new List<FileSystemNode>
            {
                new("src", "/repo/src", IsDirectory: true),
                new(".git", "/repo/.git", IsDirectory: true),
                new("README.md", "/repo/README.md", IsDirectory: false),
            }
        });
        var explorer = new WorkspaceExplorerViewModel(new WorkspaceAppService(scanner));

        await explorer.LoadRootAsync("/repo");

        Assert.Single(explorer.RootNodes);
        var root = explorer.RootNodes[0];

        // LoadRootAsync sets IsExpanded = true itself, so the root's own children are already
        // loaded (via the fake's synchronously-completed Task) without a separate expand step.
        Assert.Equal(2, root.Children.Count); // README.md + src; .git is excluded by default
        Assert.Contains(root.Children, c => c.Name == "src" && c.IsDirectory);
        Assert.Contains(root.Children, c => c.Name == "README.md" && !c.IsDirectory);
        Assert.DoesNotContain(root.Children, c => c.Name == ".git");
    }

    [Fact]
    public async Task Child_directory_only_loads_its_own_children_when_expanded()
    {
        var scanner = new FakeWorkspaceScanner(new()
        {
            ["/repo"] = new List<FileSystemNode> { new("src", "/repo/src", IsDirectory: true) },
            ["/repo/src"] = new List<FileSystemNode> { new("Program.cs", "/repo/src/Program.cs", IsDirectory: false) },
        });
        var explorer = new WorkspaceExplorerViewModel(new WorkspaceAppService(scanner));
        await explorer.LoadRootAsync("/repo");

        var srcNode = explorer.RootNodes[0].Children.Single();
        Assert.Single(srcNode.Children); // still just the "Loading..." placeholder

        srcNode.IsExpanded = true;

        Assert.Single(srcNode.Children);
        Assert.Equal("Program.cs", srcNode.Children[0].Name);
    }

    [Fact]
    public async Task ActivateFile_raises_FileActivated_only_for_files_not_directories()
    {
        var scanner = new FakeWorkspaceScanner(new());
        var explorer = new WorkspaceExplorerViewModel(new WorkspaceAppService(scanner));
        await explorer.LoadRootAsync("/repo");

        string? activatedPath = null;
        explorer.FileActivated += (_, path) => activatedPath = path;

        var directoryNode = new FileTreeNodeViewModel("src", "/repo/src", isDirectory: true, new WorkspaceAppService(scanner));
        explorer.ActivateFile(directoryNode);
        Assert.Null(activatedPath);

        var fileNode = new FileTreeNodeViewModel("a.cs", "/repo/a.cs", isDirectory: false, new WorkspaceAppService(scanner));
        explorer.ActivateFile(fileNode);
        Assert.Equal("/repo/a.cs", activatedPath);
    }
}
