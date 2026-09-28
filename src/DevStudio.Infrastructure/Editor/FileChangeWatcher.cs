using DevStudio.Core.Editor;
using DevStudio.Core.Platform;

namespace DevStudio.Infrastructure.Editor;

/// <summary>One <see cref="FileSystemWatcher"/> per watched directory, filtered down to the
/// specific files DevStudio has open, so an external editor touching sibling files doesn't
/// generate noise. Keyed by <see cref="PathComparer.Comparer"/> (SKILL.md §10 [Phase 11]) — on a
/// case-sensitive Linux filesystem, <c>Foo.cs</c> and <c>foo.cs</c> are two real, different
/// files and must never collide in these collections.</summary>
public sealed class FileChangeWatcher : IFileChangeWatcher
{
    private readonly Dictionary<string, FileSystemWatcher> _watchersByDirectory = new(PathComparer.Comparer);
    private readonly HashSet<string> _watchedFiles = new(PathComparer.Comparer);

    public event EventHandler<FileChangedEventArgs>? Changed;

    public void Watch(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (directory is null) return;

        _watchedFiles.Add(filePath);

        if (_watchersByDirectory.ContainsKey(directory)) return;

        var watcher = new FileSystemWatcher(directory)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };

        watcher.Changed += (_, e) =>
        {
            if (_watchedFiles.Contains(e.FullPath))
            {
                Changed?.Invoke(this, new FileChangedEventArgs(e.FullPath));
            }
        };

        _watchersByDirectory[directory] = watcher;
    }

    public void Unwatch(string filePath)
    {
        _watchedFiles.Remove(filePath);
    }

    public void Dispose()
    {
        foreach (var watcher in _watchersByDirectory.Values)
        {
            watcher.Dispose();
        }
        _watchersByDirectory.Clear();
    }
}
