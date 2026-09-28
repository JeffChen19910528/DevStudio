namespace DevStudio.Core.Editor;

/// <summary>
/// Reads/writes text files, detecting and preserving encoding and line endings (SKILL.md §11
/// [editor requirements], §12). Never silently corrupts a file's original byte-level shape.
/// </summary>
public interface ITextFileService
{
    Task<long> GetFileSizeAsync(string path, CancellationToken cancellationToken = default);

    Task<TextFileContent> ReadAsync(string path, CancellationToken cancellationToken = default);

    Task WriteAsync(string path, string text, TextEncodingKind encoding, LineEndingKind lineEnding, CancellationToken cancellationToken = default);
}
