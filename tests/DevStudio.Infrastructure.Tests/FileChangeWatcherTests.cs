using DevStudio.Core.Editor;
using DevStudio.Infrastructure.Editor;
using Xunit;

namespace DevStudio.Infrastructure.Tests;

public class FileChangeWatcherTests : IDisposable
{
    private readonly string _tempDirectory;

    public FileChangeWatcherTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "DevStudioTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose() => Directory.Delete(_tempDirectory, recursive: true);

    [Fact]
    public async Task Raises_Changed_when_a_watched_file_is_modified_on_disk()
    {
        var path = Path.Combine(_tempDirectory, "watched.txt");
        await File.WriteAllTextAsync(path, "initial");

        using var watcher = new FileChangeWatcher();
        var tcs = new TaskCompletionSource<string>();
        watcher.Changed += (_, e) => tcs.TrySetResult(e.FilePath);
        watcher.Watch(path);

        await Task.Delay(100); // let the FileSystemWatcher finish attaching before the write
        await File.WriteAllTextAsync(path, "changed externally");

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(tcs.Task, completed);
        Assert.Equal(path, await tcs.Task);
    }

    [Fact]
    public async Task Unwatch_stops_further_notifications_for_that_file()
    {
        var path = Path.Combine(_tempDirectory, "ignored.txt");
        await File.WriteAllTextAsync(path, "initial");

        using var watcher = new FileChangeWatcher();
        var changeCount = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref changeCount);
        watcher.Watch(path);
        watcher.Unwatch(path);

        await Task.Delay(100);
        await File.WriteAllTextAsync(path, "changed after unwatch");
        await Task.Delay(500);

        Assert.Equal(0, changeCount);
    }
}
