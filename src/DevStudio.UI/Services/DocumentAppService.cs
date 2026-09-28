using DevStudio.Core.Editor;

namespace DevStudio.UI.Services;

/// <summary>Application-service layer for opening/saving files and tracking external changes,
/// sitting between ViewModels and the <see cref="ITextFileService"/>/<see
/// cref="IFileChangeWatcher"/> Core abstractions (SKILL.md §11 [editor], §12 [encoding], §12
/// [external changes]).</summary>
public sealed class DocumentAppService
{
    /// <summary>Configurable later (SKILL.md §27); 5 MB is a reasonable Phase 1 default.</summary>
    public const long DefaultLargeFileThresholdBytes = 5 * 1024 * 1024;

    private readonly ITextFileService _textFileService;
    private readonly IFileChangeWatcher _fileChangeWatcher;

    public DocumentAppService(ITextFileService textFileService, IFileChangeWatcher fileChangeWatcher)
    {
        _textFileService = textFileService;
        _fileChangeWatcher = fileChangeWatcher;
    }

    public event EventHandler<FileChangedEventArgs>? ExternalChangeDetected
    {
        add => _fileChangeWatcher.Changed += value;
        remove => _fileChangeWatcher.Changed -= value;
    }

    public Task<long> GetFileSizeAsync(string path, CancellationToken cancellationToken = default)
        => _textFileService.GetFileSizeAsync(path, cancellationToken);

    public async Task<TextFileContent> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        var content = await _textFileService.ReadAsync(path, cancellationToken).ConfigureAwait(false);
        _fileChangeWatcher.Watch(path);
        return content;
    }

    public Task SaveAsync(string path, string text, TextEncodingKind encoding, LineEndingKind lineEnding, CancellationToken cancellationToken = default)
        => _textFileService.WriteAsync(path, text, encoding, lineEnding, cancellationToken);

    public void StopWatching(string path) => _fileChangeWatcher.Unwatch(path);
}
