namespace DevStudio.Core.Debug;

public sealed record ThreadInfo(int Id, string Name);

public sealed record StackFrameInfo(int Id, string Name, string? SourcePath, int Line, int Column);

public sealed record Scope(string Name, int VariablesReference, bool Expensive);

public sealed record Variable(string Name, string Value, string? Type, int VariablesReference);

/// <summary>Normalized <c>stopped</c> event body (SKILL.md §24) — DAP's <c>reason</c> is kept as
/// the real string the adapter sent (<c>"breakpoint"</c>/<c>"step"</c>/<c>"pause"</c>/
/// <c>"exception"</c>/<c>"entry"</c>/...) rather than forced into a closed enum, since adapters
/// are free to send other real reasons DevStudio should still display faithfully.</summary>
public sealed record StoppedInfo(string Reason, int? ThreadId, bool AllThreadsStopped, string? Description);
