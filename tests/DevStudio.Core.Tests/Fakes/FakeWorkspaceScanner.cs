using DevStudio.Core.Workspace;

namespace DevStudio.Core.Tests.Fakes;

/// <summary>In-memory directory tree for testing <see cref="Projects.ProjectDetectionService"/>
/// without real disk I/O (SKILL.md §40).</summary>
public sealed class FakeWorkspaceScanner : IWorkspaceScanner
{
    private readonly Dictionary<string, IReadOnlyList<FileSystemNode>> _childrenByDirectory;

    public FakeWorkspaceScanner(Dictionary<string, IReadOnlyList<FileSystemNode>> childrenByDirectory)
    {
        _childrenByDirectory = childrenByDirectory;
    }

    public Task<IReadOnlyList<FileSystemNode>> GetChildrenAsync(string directoryPath, WorkspaceExclusionRules exclusionRules, CancellationToken cancellationToken = default)
    {
        var children = _childrenByDirectory.TryGetValue(directoryPath, out var found) ? found : Array.Empty<FileSystemNode>();
        var filtered = children.Where(n => !n.IsDirectory || !exclusionRules.IsExcluded(n.Name)).ToList();
        return Task.FromResult((IReadOnlyList<FileSystemNode>)filtered);
    }
}
