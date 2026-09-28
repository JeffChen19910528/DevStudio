namespace DevStudio.Core.Editor;

public enum LineEndingKind
{
    Lf,
    CrLf,

    /// <summary>The file mixes LF and CRLF; the original per-line endings are preserved on save
    /// rather than normalized, so DevStudio never silently rewrites a file's line endings.</summary>
    Mixed
}
