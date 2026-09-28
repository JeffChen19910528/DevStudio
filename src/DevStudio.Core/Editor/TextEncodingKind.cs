namespace DevStudio.Core.Editor;

public enum TextEncodingKind
{
    Utf8,
    Utf8Bom,
    Utf16LittleEndian,
    Utf16BigEndian,

    /// <summary>No BOM and no encoding-specific byte pattern was found; treated as UTF-8 for
    /// editing, but the file's original bytes are re-checked before a silent overwrite.</summary>
    Unknown
}
