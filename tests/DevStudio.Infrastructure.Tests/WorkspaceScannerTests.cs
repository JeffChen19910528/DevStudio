using DevStudio.Core.Workspace;
using DevStudio.Infrastructure.Workspace;
using Xunit;

namespace DevStudio.Infrastructure.Tests;

public class WorkspaceScannerTests : IDisposable
{
    private readonly string _tempDirectory;

    public WorkspaceScannerTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "DevStudioTests_" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(_tempDirectory, "src"));
        Directory.CreateDirectory(Path.Combine(_tempDirectory, ".git"));
        File.WriteAllText(Path.Combine(_tempDirectory, "README.md"), "hello");
    }

    public void Dispose() => Directory.Delete(_tempDirectory, recursive: true);

    [Fact]
    public async Task GetChildrenAsync_lists_one_level_and_hides_excluded_directories()
    {
        var scanner = new WorkspaceScanner();

        var children = await scanner.GetChildrenAsync(_tempDirectory, WorkspaceExclusionRules.Default);

        Assert.Contains(children, c => c.Name == "src" && c.IsDirectory);
        Assert.Contains(children, c => c.Name == "README.md" && !c.IsDirectory);
        Assert.DoesNotContain(children, c => c.Name == ".git");
    }

    [Fact]
    public async Task GetChildrenAsync_never_recurses_into_subdirectories()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, "src", "Program.cs"), "// nested");
        var scanner = new WorkspaceScanner();

        var children = await scanner.GetChildrenAsync(_tempDirectory, WorkspaceExclusionRules.Default);

        Assert.DoesNotContain(children, c => c.Name == "Program.cs");
    }

    [Fact]
    public async Task GetChildrenAsync_returns_empty_for_an_inaccessible_or_missing_directory_instead_of_throwing()
    {
        var scanner = new WorkspaceScanner();

        var children = await scanner.GetChildrenAsync(
            Path.Combine(_tempDirectory, "does-not-exist"), WorkspaceExclusionRules.Default);

        Assert.Empty(children);
    }
}
