namespace DevStudio.Core.Editor;

public sealed record FileChangedEventArgs(string FilePath);

/// <summary>Watches open files for changes made outside DevStudio (SKILL.md §12 [external
/// file changes]), so the editor can prompt instead of silently reloading or overwriting.</summary>
public interface IFileChangeWatcher : IDisposable
{
    event EventHandler<FileChangedEventArgs>? Changed;

    void Watch(string filePath);
    void Unwatch(string filePath);
}
