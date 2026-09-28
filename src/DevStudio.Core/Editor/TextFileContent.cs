namespace DevStudio.Core.Editor;

public sealed record TextFileContent(string Text, TextEncodingKind Encoding, LineEndingKind LineEnding);
