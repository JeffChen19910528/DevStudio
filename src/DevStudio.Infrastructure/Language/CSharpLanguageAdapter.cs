using DevStudio.Core.Language;
using DevStudio.Core.Lsp;
using DevStudio.Core.Processes;
using DevStudio.Infrastructure.Lsp;

namespace DevStudio.Infrastructure.Language;

/// <summary>
/// Launches the real Roslyn language server and drives it over LSP (SKILL.md §9, ADR-008).
/// Its raw stdin/stdout are obtained via <c>ProcessStartRequest.RawStdio</c> (the same
/// <see cref="IProcessRunner"/> everything else uses — no second process-execution path,
/// reusing the exact mechanism Phase 6 added for DAP) and wrapped in a
/// <see cref="StreamJsonRpcTransport"/>.
/// </summary>
public sealed class CSharpLanguageAdapter : ILanguageAdapter
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".cs" };

    private readonly IProcessRunner _processRunner;
    private readonly RoslynLanguageServerResolver _resolver;

    public CSharpLanguageAdapter(IProcessRunner processRunner, RoslynLanguageServerResolver resolver)
    {
        _processRunner = processRunner;
        _resolver = resolver;
    }

    public string LanguageId => "csharp";

    public bool SupportsFile(string filePath) => SupportedExtensions.Contains(Path.GetExtension(filePath));

    public LanguageServerResolution ResolveLanguageServer() => _resolver.Resolve();

    public async Task<ILanguageServerSession> StartAsync(string workspaceRootPath, CancellationToken cancellationToken = default)
    {
        var resolution = _resolver.Resolve();
        if (!resolution.Found)
        {
            throw new InvalidOperationException(resolution.Message ?? "C# language server not found.");
        }

        if (!Directory.Exists(workspaceRootPath))
        {
            throw new InvalidOperationException($"Workspace directory does not exist: {workspaceRootPath}");
        }

        // --autoLoadProjects is required for the server to discover/load any .csproj at all —
        // a real, verified finding (see ADR-008): without it, every file stays classified as a
        // standalone "miscellaneous file" and only offers generic keyword completions, never
        // real project-aware members.
        var request = new ProcessStartRequest(
            resolution.ExecutablePath!,
            new[] { "--stdio", "--logLevel", "Information", "--autoLoadProjects" },
            workspaceRootPath,
            RawStdio: true);
        var process = _processRunner.Start(request);
        if (process.StandardInput is null || process.StandardOutput is null)
        {
            await process.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException("The language server did not expose raw stdio (RawStdio request was not honored).");
        }

        var transport = new StreamJsonRpcTransport(process.StandardOutput, process.StandardInput);
        var client = new JsonRpcClient(transport);
        var session = new RoslynLanguageServerSession(client, process);

        try
        {
            await session.InitializeAsync(workspaceRootPath, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return session;
    }
}
