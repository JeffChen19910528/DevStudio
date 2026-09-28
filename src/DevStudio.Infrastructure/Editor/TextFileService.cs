using System.Text;
using DevStudio.Core.Editor;

namespace DevStudio.Infrastructure.Editor;

/// <summary>
/// Detects encoding by BOM sniffing and line endings by scanning the decoded text, then
/// preserves both on save (SKILL.md §11 [encoding requirements]). Never silently rewrites a
/// file's encoding or normalizes mixed line endings.
/// </summary>
public sealed class TextFileService : ITextFileService
{
    public Task<long> GetFileSizeAsync(string path, CancellationToken cancellationToken = default)
        => Task.Run(() => new FileInfo(path).Length, cancellationToken);

    public async Task<TextFileContent> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var encoding = DetectEncoding(bytes, out var preambleLength);

        var decoder = encoding switch
        {
            TextEncodingKind.Utf8 or TextEncodingKind.Utf8Bom => Encoding.UTF8,
            TextEncodingKind.Utf16LittleEndian => Encoding.Unicode,
            TextEncodingKind.Utf16BigEndian => Encoding.BigEndianUnicode,
            _ => Encoding.UTF8,
        };

        var text = decoder.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        var lineEnding = DetectLineEnding(text);

        return new TextFileContent(text, encoding, lineEnding);
    }

    public async Task WriteAsync(string path, string text, TextEncodingKind encoding, LineEndingKind lineEnding, CancellationToken cancellationToken = default)
    {
        var normalized = lineEnding switch
        {
            LineEndingKind.CrLf => text.Replace("\r\n", "\n").Replace("\n", "\r\n"),
            LineEndingKind.Lf => text.Replace("\r\n", "\n"),
            // Mixed: write exactly what the editor holds, byte for byte, no normalization.
            _ => text,
        };

        Encoding writeEncoding = encoding switch
        {
            TextEncodingKind.Utf8Bom => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            TextEncodingKind.Utf16LittleEndian => Encoding.Unicode,
            TextEncodingKind.Utf16BigEndian => Encoding.BigEndianUnicode,
            _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        await File.WriteAllTextAsync(path, normalized, writeEncoding, cancellationToken).ConfigureAwait(false);
    }

    private static TextEncodingKind DetectEncoding(byte[] bytes, out int preambleLength)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            preambleLength = 3;
            return TextEncodingKind.Utf8Bom;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            preambleLength = 2;
            return TextEncodingKind.Utf16LittleEndian;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            preambleLength = 2;
            return TextEncodingKind.Utf16BigEndian;
        }

        preambleLength = 0;
        return TextEncodingKind.Utf8;
    }

    private static LineEndingKind DetectLineEnding(string text)
    {
        var hasCrLf = text.Contains("\r\n");
        var hasLoneLf = text.Replace("\r\n", "").Contains('\n');

        if (hasCrLf && hasLoneLf) return LineEndingKind.Mixed;
        if (hasCrLf) return LineEndingKind.CrLf;
        return LineEndingKind.Lf;
    }
}
