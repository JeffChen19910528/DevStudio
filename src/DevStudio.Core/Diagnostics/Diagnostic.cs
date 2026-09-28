namespace DevStudio.Core.Diagnostics;

/// <summary>
/// Unified diagnostic model per SKILL.md §34. Every compiler/linter/LSP/debugger/test-runner
/// output funnels into this shape so the UI can render and navigate diagnostics uniformly
/// regardless of which tool produced them.
/// </summary>
public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    string File,
    int Line,
    int Column,
    DiagnosticSource Source,
    IReadOnlyList<Diagnostic>? RelatedInformation = null);
