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

            // Directory.EnumerateDirectories/EnumerateFiles are lazily evaluated: a permission
            // error, reparse-point cycle, or too-long path on any individual entry only
            // surfaces once the sequence is actually walked below, not on the call itself. The
            // whole materialization (call + enumeration) must therefore share one try/catch, or
            // an exception thrown mid-walk escapes uncaught and faults this Task, which leaves
            // the Explorer's "Loading..." placeholder stuck forever (SKILL.md §26).
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(directoryPath)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(dir);
                    if (exclusionRules.IsExcluded(name)) continue;
                    nodes.Add(new FileSystemNode(name, dir, IsDirectory: true));
                }

                foreach (var file in Directory.EnumerateFiles(directoryPath)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    nodes.Add(new FileSystemNode(Path.GetFileName(file), file, IsDirectory: false));
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                return (IReadOnlyList<FileSystemNode>)nodes;
            }

            return (IReadOnlyList<FileSystemNode>)nodes;
        }, cancellationToken);
    }
}
