using DevStudio.Core.Diagnostics;

namespace DevStudio.Core.Language;

/// <summary>
/// Orchestrates one real language-server session (SKILL.md §14): resolves which
/// <see cref="ILanguageAdapter"/> handles a file, lazily starts a session the first time a
/// relevant document opens (never one server per file — SKILL.md §14's explicit "not File A →
/// Server A, File B → Server B"), and converts the adapter's raw LSP-driven callbacks into
/// normalized state (<see cref="LanguageServerState"/>, per-file diagnostics) a ViewModel can
/// bind to without ever seeing LSP JSON. Workspace Trust is enforced by the ViewModel before
/// calling <see cref="EnsureStartedForFileAsync"/>/<see cref="OpenDocumentAsync"/> — exactly like
/// <see cref="Build.BuildService"/>/<see cref="Run.RunService"/>/<see cref="Debug.DebugService"/>
/// — Core has no UI/dialog dependency to check it here.
/// </summary>
public sealed class LanguageService
{
    private readonly IReadOnlyList<ILanguageAdapter> _adapters;
    private ILanguageServerSession? _session;

    public LanguageService(IEnumerable<ILanguageAdapter> adapters) => _adapters = adapters.ToList();

    public string? WorkspaceRootPath { get; private set; }
    public LanguageServerState State { get; private set; } = LanguageServerState.NotStarted;

    public event EventHandler<LanguageServerState>? StateChanged;
    public event EventHandler<(string FilePath, IReadOnlyList<Diagnostic> Diagnostics)>? DiagnosticsPublished;

    /// <summary>The message from the most recent failure, if <see cref="State"/> is <see
    /// cref="LanguageServerState.Failed"/>.</summary>
    public string? LastFailureMessage { get; private set; }

    public bool IsActive => State is LanguageServerState.Starting or LanguageServerState.Initializing or LanguageServerState.Running;

    public void SetWorkspace(string? workspaceRootPath) => WorkspaceRootPath = workspaceRootPath;

    public bool SupportsFile(string filePath) => _adapters.Any(a => a.SupportsFile(filePath));

    /// <summary>Starts the real language server for this file's language if one isn't already
    /// running. A no-op if a session is already active, or if no adapter supports this file
    /// (not an error — most files in a workspace aren't source code DevStudio has a language
    /// server for).</summary>
    public async Task EnsureStartedForFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (IsActive) return;
        if (WorkspaceRootPath is null) throw new InvalidOperationException("No workspace is open.");

        var adapter = _adapters.FirstOrDefault(a => a.SupportsFile(filePath));
        if (adapter is null) return;

        SetState(LanguageServerState.Starting);
        try
        {
            var session = await adapter.StartAsync(WorkspaceRootPath, cancellationToken).ConfigureAwait(false);
            session.DiagnosticsPublished += OnDiagnosticsPublished;
            session.Faulted += OnSessionFaulted;
            _session = session;
            SetState(LanguageServerState.Running);
        }
        catch (Exception ex)
        {
            LastFailureMessage = ex.Message;
            SetState(LanguageServerState.Failed);
        }
    }

    public async Task OpenDocumentAsync(string filePath, string text, CancellationToken cancellationToken = default)
    {
        await EnsureStartedForFileAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (_session is null) return;
        await _session.DidOpenAsync(filePath, text, cancellationToken).ConfigureAwait(false);
    }

    public Task ChangeDocumentAsync(string filePath, string text, CancellationToken cancellationToken = default) =>
        _session?.DidChangeAsync(filePath, text, cancellationToken) ?? Task.CompletedTask;

    public Task CloseDocumentAsync(string filePath, CancellationToken cancellationToken = default) =>
        _session?.DidCloseAsync(filePath, cancellationToken) ?? Task.CompletedTask;

    public Task RefreshDiagnosticsAsync(string filePath, CancellationToken cancellationToken = default) =>
        _session?.RefreshDiagnosticsAsync(filePath, cancellationToken) ?? Task.CompletedTask;

    public Task<IReadOnlyList<CompletionItem>> CompletionAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default) =>
        _session?.CompletionAsync(filePath, position, cancellationToken) ?? Task.FromResult<IReadOnlyList<CompletionItem>>(Array.Empty<CompletionItem>());

    public Task<HoverResult?> HoverAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default) =>
        _session?.HoverAsync(filePath, position, cancellationToken) ?? Task.FromResult<HoverResult?>(null);

    public Task<IReadOnlyList<LspLocation>> DefinitionAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default) =>
        _session?.DefinitionAsync(filePath, position, cancellationToken) ?? Task.FromResult<IReadOnlyList<LspLocation>>(Array.Empty<LspLocation>());

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_session is null) return;
        SetState(LanguageServerState.Stopping);
        var session = _session;
        _session = null;
        await session.ShutdownAsync(cancellationToken).ConfigureAwait(false);
        SetState(LanguageServerState.Stopped);
    }

    /// <summary>Manual restart (SKILL.md §37) — no automatic retry loop; a crashed server stays
    /// <see cref="LanguageServerState.Failed"/> until the user explicitly asks to restart it.</summary>
    public async Task RestartAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (_session is not null)
        {
            await StopAsync(cancellationToken).ConfigureAwait(false);
        }
        SetState(LanguageServerState.NotStarted);
        await EnsureStartedForFileAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    private void OnDiagnosticsPublished(string filePath, IReadOnlyList<Diagnostic> diagnostics) =>
        DiagnosticsPublished?.Invoke(this, (filePath, diagnostics));

    private void OnSessionFaulted(Exception exception)
    {
        _session = null;
        LastFailureMessage = exception.Message;
        SetState(LanguageServerState.Failed);
    }

    private void SetState(LanguageServerState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }
}
