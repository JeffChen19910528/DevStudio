namespace DevStudio.Core.Language;

/// <summary>Normalized <c>textDocument/completion</c> item (SKILL.md §27) — never assumes
/// <see cref="InsertText"/> is present; a real item's insertion may instead come entirely from
/// <see cref="TextEdit"/>, per real LSP semantics.</summary>
public sealed record CompletionItem(
    string Label,
    string? Kind,
    string? Detail,
    string? Documentation,
    string? InsertText,
    LspTextEdit? TextEdit);

public sealed record LspTextEdit(LspRange Range, string NewText);

/// <summary>Normalized <c>textDocument/hover</c> result (SKILL.md §29).</summary>
public sealed record HoverResult(string Content, LspRange? Range);
