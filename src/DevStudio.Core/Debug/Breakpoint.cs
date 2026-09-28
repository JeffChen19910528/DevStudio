namespace DevStudio.Core.Debug;

/// <summary>A source breakpoint as DevStudio knows it — source file + line (+ optional column)
/// only, never arbitrary debugger state (SKILL.md §22 [Phase 6], mirroring how workspace state
/// never stores secrets).</summary>
public sealed record Breakpoint(Guid Id, string SourcePath, int Line, int? Column = null, bool Enabled = true);

/// <summary>What the real debugger reported back for one breakpoint after <c>setBreakpoints</c>
/// (SKILL.md §21): a breakpoint is never assumed valid just because DevStudio asked for it — the
/// adapter may report <c>verified = false</c> or adjust the line/column.</summary>
public sealed record BreakpointVerification(Guid RequestedId, bool Verified, string? Message, int? Line, int? Column);
