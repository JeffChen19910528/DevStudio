using DevStudio.Core.Diagnostics;

namespace DevStudio.Core.Language;

/// <summary>Where a real language server was found, or why not (SKILL.md §11) — a project's
/// SDK being present is never evidence a language server exists; every candidate is verified
/// with a real file-existence/version check before being reported <see cref="Found"/>.</summary>
public sealed record LanguageServerResolution(bool Found, string? ExecutablePath, string? Version, string? Source, string? Message);

/// <summary>
/// Evolves the Phase 0 <c>Adapters.ILanguageAdapter</c> stub (a handful of properties, no
/// lifecycle) into a real contract capable of driving an actual language server (SKILL.md §8–§9
/// [Phase 7]): <c>LanguageService → ILanguageAdapter → JsonRpcClient → real language server</c>.
/// The Phase 0 stub was unreferenced anywhere and is replaced outright, per the same precedent
/// ADR-005/ADR-007 used for the equally-unreferenced Phase 0 <c>IBuildAdapter</c>/
/// <c>IDebuggerAdapter</c> stubs.
/// </summary>
public interface ILanguageAdapter
{
    string LanguageId { get; }

    bool SupportsFile(string filePath);

    /// <summary>Verifies a real language server is actually installed and usable — never assumed
    /// from an SDK/toolchain being present.</summary>
    LanguageServerResolution ResolveLanguageServer();

    /// <summary>Launches the real language server and performs its <c>initialize</c>/
    /// <c>initialized</c> handshake, returning a handle for everything after that (document
    /// sync, completion, hover, definition). Throws if the server could not be resolved/started.</summary>
    Task<ILanguageServerSession> StartAsync(string workspaceRootPath, CancellationToken cancellationToken = default);
}

/// <summary>One live language server session (SKILL.md §18, §25, §29, §31). Every method
/// corresponds to one real LSP request/notification; <see cref="LanguageService"/> is the only
/// thing that calls it, and never exposes this interface (or raw LSP JSON) to a ViewModel.</summary>
public interface ILanguageServerSession : IAsyncDisposable
{
    /// <summary>Raised for a real <c>textDocument/publishDiagnostics</c> notification — the
    /// given list always replaces this file's previous LSP diagnostics (SKILL.md §23), never
    /// appends.</summary>
    event Action<string, IReadOnlyList<Diagnostic>>? DiagnosticsPublished;

    /// <summary>Raised once if the session terminates abnormally (server crash, malformed
    /// message, unexpected exit).</summary>
    event Action<Exception>? Faulted;

    Task DidOpenAsync(string filePath, string text, CancellationToken cancellationToken = default);
    Task DidChangeAsync(string filePath, string fullText, CancellationToken cancellationToken = default);
    Task DidCloseAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>Re-pulls diagnostics for a file and raises <see cref="DiagnosticsPublished"/>
    /// again — useful when the server's project load was still in progress at the time of the
    /// original <c>didOpen</c>/<c>didChange</c> pull.</summary>
    Task RefreshDiagnosticsAsync(string filePath, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompletionItem>> CompletionAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default);
    Task<HoverResult?> HoverAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LspLocation>> DefinitionAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default);

    /// <summary>Graceful LSP <c>shutdown</c>/<c>exit</c> (SKILL.md §38); falls back to
    /// <c>IRunningProcess.Kill(entireProcessTree: true)</c> only if the server doesn't exit on
    /// its own promptly.</summary>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
