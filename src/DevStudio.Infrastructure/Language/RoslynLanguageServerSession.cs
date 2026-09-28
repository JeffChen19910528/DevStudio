using System.Text.Json.Nodes;
using DevStudio.Core.Diagnostics;
using DevStudio.Core.Language;
using DevStudio.Core.Lsp;
using DevStudio.Core.Platform;
using DevStudio.Core.Processes;

namespace DevStudio.Infrastructure.Language;

/// <summary>
/// One real LSP session against a launched Roslyn language server process (SKILL.md §15–§31,
/// §38). Two real, verified findings shaped this implementation (see ADR-008 for the full
/// investigation):
/// <list type="bullet">
/// <item>Launching requires <c>--autoLoadProjects</c> as a process argument (not an LSP
/// parameter) — without it, the server never discovers/loads any <c>.csproj</c>, and every file
/// stays classified as a standalone "miscellaneous file" with only generic keyword completions.</item>
/// <item>This server does not push <c>textDocument/publishDiagnostics</c> — it uses the LSP 3.17
/// <em>pull</em> diagnostics model exclusively (<c>textDocument/diagnostic</c>). This session
/// pulls diagnostics itself after every <c>didOpen</c>/<c>didChange</c> and raises <see
/// cref="DiagnosticsPublished"/> either way, so the rest of DevStudio's architecture never needs
/// to know which model the underlying server actually uses.</item>
/// </list>
/// </summary>
internal sealed class RoslynLanguageServerSession : ILanguageServerSession
{
    private readonly JsonRpcClient _client;
    private readonly IRunningProcess _process;
    private readonly Dictionary<string, int> _documentVersions = new(PathComparer.Comparer);
    private bool _terminatedFired;

    public RoslynLanguageServerSession(JsonRpcClient client, IRunningProcess process)
    {
        _client = client;
        _process = process;
        _client.Faulted += ex => Faulted?.Invoke(ex);

        // Real servers send these as genuine requests and will stall waiting for a response if
        // none is ever sent (SKILL.md §7) — safe, conservative defaults for capability
        // negotiation DevStudio doesn't have a specific opinion about.
        _client.RegisterServerRequestHandler("workspace/configuration", _ => new JsonArray());
        _client.RegisterServerRequestHandler("client/registerCapability", _ => null);
        _client.RegisterServerRequestHandler("client/unregisterCapability", _ => null);
        _client.RegisterServerRequestHandler("window/workDoneProgress/create", _ => null);
    }

    public event Action<string, IReadOnlyList<Diagnostic>>? DiagnosticsPublished;
    public event Action<Exception>? Faulted;

    public async Task InitializeAsync(string workspaceRootPath, CancellationToken cancellationToken)
    {
        _client.Start(cancellationToken);
        var rootUri = ToFileUri(workspaceRootPath.TrimEnd('\\', '/') + "/");

        var initializeParams = new JsonObject
        {
            ["processId"] = Environment.ProcessId,
            ["rootUri"] = rootUri,
            ["capabilities"] = new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["publishDiagnostics"] = new JsonObject(),
                    ["completion"] = new JsonObject { ["completionItem"] = new JsonObject() },
                    ["hover"] = new JsonObject(),
                    ["definition"] = new JsonObject(),
                    ["synchronization"] = new JsonObject { ["didSave"] = true },
                },
                ["workspace"] = new JsonObject { ["configuration"] = true, ["workspaceFolders"] = true },
            },
            ["workspaceFolders"] = new JsonArray(new JsonObject { ["uri"] = rootUri, ["name"] = Path.GetFileName(workspaceRootPath.TrimEnd('\\', '/')) }),
        };

        var response = await _client.SendRequestAsync("initialize", initializeParams, cancellationToken).ConfigureAwait(false);
        RequireSuccess(response, "initialize");

        await _client.SendNotificationAsync("initialized", new JsonObject(), cancellationToken).ConfigureAwait(false);
    }

    public async Task DidOpenAsync(string filePath, string text, CancellationToken cancellationToken = default)
    {
        _documentVersions[filePath] = 1;
        await _client.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = ToFileUri(filePath),
                ["languageId"] = "csharp",
                ["version"] = 1,
                ["text"] = text,
            },
        }, cancellationToken).ConfigureAwait(false);

        await PullDiagnosticsAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    public async Task DidChangeAsync(string filePath, string fullText, CancellationToken cancellationToken = default)
    {
        var version = _documentVersions.TryGetValue(filePath, out var current) ? current + 1 : 1;
        _documentVersions[filePath] = version;

        // A content-change entry with no `range` means "replace the whole document" — a valid
        // degenerate case of incremental sync per the LSP spec, so this is safe to send
        // regardless of the server's declared `textDocumentSync.change` capability (SKILL.md
        // §19: full synchronization is acceptable for Phase 7).
        await _client.SendNotificationAsync("textDocument/didChange", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = ToFileUri(filePath), ["version"] = version },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = fullText }),
        }, cancellationToken).ConfigureAwait(false);

        await PullDiagnosticsAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    public async Task DidCloseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        _documentVersions.Remove(filePath);
        await _client.SendNotificationAsync("textDocument/didClose", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = ToFileUri(filePath) },
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task RefreshDiagnosticsAsync(string filePath, CancellationToken cancellationToken = default) =>
        PullDiagnosticsAsync(filePath, cancellationToken);

    private async Task PullDiagnosticsAsync(string filePath, CancellationToken cancellationToken)
    {
        JsonRpcResponse response;
        try
        {
            response = await _client.SendRequestAsync("textDocument/diagnostic", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = ToFileUri(filePath) },
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return; // best-effort — a failed diagnostic pull should not crash document sync
        }

        if (!response.Success) return;

        var items = (response.Result as JsonObject)?["items"] as JsonArray;
        var diagnostics = items?.Select(item => MapDiagnostic(filePath, item as JsonObject)).ToList()
            ?? new List<Diagnostic>();
        DiagnosticsPublished?.Invoke(filePath, diagnostics);
    }

    private static Diagnostic MapDiagnostic(string filePath, JsonObject? item)
    {
        var range = item?["range"] as JsonObject;
        var start = range?["start"] as JsonObject;
        var severity = item?["severity"]?.GetValue<int>() ?? 1;
        return new Diagnostic(
            MapSeverity(severity),
            item?["code"]?.GetValue<string>() ?? string.Empty,
            item?["message"]?.GetValue<string>() ?? string.Empty,
            filePath,
            (start?["line"]?.GetValue<int>() ?? 0) + 1, // LSP is 0-based; DevStudio's Diagnostic is 1-based
            (start?["character"]?.GetValue<int>() ?? 0) + 1,
            DiagnosticSource.LanguageServer);
    }

    private static DiagnosticSeverity MapSeverity(int lspSeverity) => lspSeverity switch
    {
        1 => DiagnosticSeverity.Error,
        2 => DiagnosticSeverity.Warning,
        3 => DiagnosticSeverity.Info,
        _ => DiagnosticSeverity.Trace,
    };

    public async Task<IReadOnlyList<CompletionItem>> CompletionAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default)
    {
        var response = await _client.SendRequestAsync("textDocument/completion", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = ToFileUri(filePath) },
            ["position"] = new JsonObject { ["line"] = position.Line, ["character"] = position.Character },
        }, cancellationToken).ConfigureAwait(false);
        RequireSuccess(response, "textDocument/completion");

        var items = response.Result is JsonObject obj ? obj["items"] as JsonArray : response.Result as JsonArray;
        return items?.Select(item => MapCompletionItem(item as JsonObject)).ToList() ?? new List<CompletionItem>();
    }

    private static CompletionItem MapCompletionItem(JsonObject? item)
    {
        var textEditObj = item?["textEdit"] as JsonObject;
        LspTextEdit? textEdit = textEditObj is null ? null : new LspTextEdit(MapRange(textEditObj["range"] as JsonObject), textEditObj["newText"]?.GetValue<string>() ?? string.Empty);
        return new CompletionItem(
            item?["label"]?.GetValue<string>() ?? string.Empty,
            MapCompletionKind(item?["kind"]?.GetValue<int>()),
            item?["detail"]?.GetValue<string>(),
            (item?["documentation"] as JsonObject)?["value"]?.GetValue<string>() ?? item?["documentation"]?.GetValue<string>(),
            item?["insertText"]?.GetValue<string>(),
            textEdit);
    }

    private static string? MapCompletionKind(int? kind) => kind switch
    {
        null => null,
        3 => "Function",
        5 => "Field",
        6 => "Variable",
        7 => "Class",
        8 => "Interface",
        9 => "Module",
        10 => "Property",
        13 => "Enum",
        14 => "Keyword",
        _ => kind.Value.ToString(),
    };

    public async Task<HoverResult?> HoverAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default)
    {
        var response = await _client.SendRequestAsync("textDocument/hover", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = ToFileUri(filePath) },
            ["position"] = new JsonObject { ["line"] = position.Line, ["character"] = position.Character },
        }, cancellationToken).ConfigureAwait(false);
        RequireSuccess(response, "textDocument/hover");

        if (response.Result is not JsonObject obj) return null;
        var contents = obj["contents"];
        var text = contents switch
        {
            JsonObject contentsObj => contentsObj["value"]?.GetValue<string>(),
            JsonValue value when value.TryGetValue(out string? s) => s,
            _ => null,
        };
        if (text is null) return null;

        var range = obj["range"] as JsonObject;
        return new HoverResult(text, range is null ? null : MapRange(range));
    }

    public async Task<IReadOnlyList<LspLocation>> DefinitionAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default)
    {
        var response = await _client.SendRequestAsync("textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = ToFileUri(filePath) },
            ["position"] = new JsonObject { ["line"] = position.Line, ["character"] = position.Character },
        }, cancellationToken).ConfigureAwait(false);
        RequireSuccess(response, "textDocument/definition");

        var array = response.Result as JsonArray;
        return array?.Select(item =>
        {
            var obj = item as JsonObject;
            var uri = obj?["uri"]?.GetValue<string>() ?? string.Empty;
            return new LspLocation(FromFileUri(uri), MapRange(obj?["range"] as JsonObject));
        }).ToList() ?? new List<LspLocation>();
    }

    private static LspRange MapRange(JsonObject? range)
    {
        var start = range?["start"] as JsonObject;
        var end = range?["end"] as JsonObject;
        return new LspRange(
            new LspPosition(start?["line"]?.GetValue<int>() ?? 0, start?["character"]?.GetValue<int>() ?? 0),
            new LspPosition(end?["line"]?.GetValue<int>() ?? 0, end?["character"]?.GetValue<int>() ?? 0));
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SendRequestAsync("shutdown", null, cancellationToken).ConfigureAwait(false);
            if (response.Success)
            {
                await _client.SendNotificationAsync("exit", null, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // Best-effort graceful shutdown — the fallback Kill() below always runs regardless.
        }
        finally
        {
            _terminatedFired = true;
            await _client.DisposeAsync().ConfigureAwait(false);
            if (!_process.HasExited)
            {
                _process.Kill();
            }
            await _process.DisposeAsync().ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync() => _terminatedFired ? ValueTask.CompletedTask : new ValueTask(ShutdownAsync());

    private static void RequireSuccess(JsonRpcResponse response, string method)
    {
        if (!response.Success)
        {
            throw new InvalidOperationException($"Language server '{method}' request failed: {response.Error?.Message ?? "(no message)"}");
        }
    }

    private static string ToFileUri(string path) => new Uri(path).AbsoluteUri;

    private static string FromFileUri(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? parsed.LocalPath : uri;
}
