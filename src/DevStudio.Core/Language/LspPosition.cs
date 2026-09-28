namespace DevStudio.Core.Language;

/// <summary>Zero-based line/character, exactly as LSP defines <c>Position</c> (SKILL.md §30) —
/// conversion to/from DevStudio's own 1-based line numbers happens at the
/// <c>LanguageService</c>/adapter boundary, never silently inside a shared model.</summary>
public sealed record LspPosition(int Line, int Character);

public sealed record LspRange(LspPosition Start, LspPosition End);

public sealed record LspLocation(string FilePath, LspRange Range);
