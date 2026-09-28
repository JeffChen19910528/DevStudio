using DevStudio.Core.Workspace;

namespace DevStudio.Infrastructure.Workspace;

/// <summary>
/// Lists one directory level at a time (SKILL.md §26) rather than walking an entire repository
/// up front. Read-only: never creates, deletes, renames, or modifies anything.
/// </summary>
public sealed class WorkspaceScanner : IWorkspaceScanner
{
    public Task<IReadOnlyList<FileSystemNode>> GetChildrenAsync(
        string directoryPath,
        WorkspaceExclusionRules exclusionRules,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var nodes = new List<FileSystemNode>();

            IEnumerable<string> directories;
            IEnumerable<string> files;
            try
            {
                directories = Directory.EnumerateDirectories(directoryPath);
                files = Directory.EnumerateFiles(directoryPath);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                return (IReadOnlyList<FileSystemNode>)nodes;
            }

            foreach (var dir in directories.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(dir);
                if (exclusionRules.IsExcluded(name)) continue;
                nodes.Add(new FileSystemNode(name, dir, IsDirectory: true));
            }

            foreach (var file in files.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                nodes.Add(new FileSystemNode(Path.GetFileName(file), file, IsDirectory: false));
            }

            return (IReadOnlyList<FileSystemNode>)nodes;
        }, cancellationToken);
    }
}
