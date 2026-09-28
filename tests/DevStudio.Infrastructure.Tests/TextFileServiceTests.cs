using DevStudio.Core.Editor;
using DevStudio.Infrastructure.Editor;
using Xunit;

namespace DevStudio.Infrastructure.Tests;

public class TextFileServiceTests : IDisposable
{
    private readonly string _tempDirectory;

    public TextFileServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "DevStudioTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose() => Directory.Delete(_tempDirectory, recursive: true);

    [Theory]
    [InlineData(TextEncodingKind.Utf8)]
    [InlineData(TextEncodingKind.Utf8Bom)]
    [InlineData(TextEncodingKind.Utf16LittleEndian)]
    public async Task Write_then_read_round_trips_text_and_detects_the_same_encoding(TextEncodingKind encoding)
    {
        var service = new TextFileService();
        var path = Path.Combine(_tempDirectory, "file.txt");

        await service.WriteAsync(path, "hello DevStudio", encoding, LineEndingKind.Lf);
        var content = await service.ReadAsync(path);

        Assert.Equal("hello DevStudio", content.Text);
        Assert.Equal(encoding, content.Encoding);
    }

    [Fact]
    public async Task WriteAsync_normalizes_to_the_requested_line_ending()
    {
        var service = new TextFileService();
        var path = Path.Combine(_tempDirectory, "crlf.txt");

        await service.WriteAsync(path, "a\nb\nc", TextEncodingKind.Utf8, LineEndingKind.CrLf);
        var rawBytes = await File.ReadAllBytesAsync(path);
        var raw = System.Text.Encoding.UTF8.GetString(rawBytes);

        Assert.Equal("a\r\nb\r\nc", raw);
    }

    [Fact]
    public async Task ReadAsync_detects_mixed_line_endings_without_altering_the_file()
    {
        var service = new TextFileService();
        var path = Path.Combine(_tempDirectory, "mixed.txt");
        await File.WriteAllBytesAsync(path, System.Text.Encoding.UTF8.GetBytes("a\r\nb\nc"));

        var content = await service.ReadAsync(path);

        Assert.Equal(LineEndingKind.Mixed, content.LineEnding);
        Assert.Equal("a\r\nb\nc", content.Text);
    }

    [Fact]
    public async Task GetFileSizeAsync_returns_the_actual_file_size_in_bytes()
    {
        var service = new TextFileService();
        var path = Path.Combine(_tempDirectory, "sized.txt");
        await File.WriteAllTextAsync(path, "12345");

        var size = await service.GetFileSizeAsync(path);

        Assert.Equal(5, size);
    }
}
