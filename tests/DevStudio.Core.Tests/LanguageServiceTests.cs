using DevStudio.Core.Diagnostics;
using DevStudio.Core.Language;
using Xunit;

namespace DevStudio.Core.Tests;

file sealed class FakeLanguageServerSession : ILanguageServerSession
{
    public List<(string FilePath, string Text)> Opened { get; } = new();
    public List<(string FilePath, string Text)> Changed { get; } = new();
    public List<string> Closed { get; } = new();
    public List<string> RefreshedDiagnostics { get; } = new();
    public bool ShutdownCalled { get; private set; }

    public event Action<string, IReadOnlyList<Diagnostic>>? DiagnosticsPublished;
    public event Action<Exception>? Faulted;

    public void RaiseDiagnostics(string filePath, IReadOnlyList<Diagnostic> diagnostics) => DiagnosticsPublished?.Invoke(filePath, diagnostics);
    public void RaiseFaulted(Exception ex) => Faulted?.Invoke(ex);

    public Task DidOpenAsync(string filePath, string text, CancellationToken cancellationToken = default)
    {
        Opened.Add((filePath, text));
        return Task.CompletedTask;
    }

    public Task DidChangeAsync(string filePath, string fullText, CancellationToken cancellationToken = default)
    {
        Changed.Add((filePath, fullText));
        return Task.CompletedTask;
    }

    public Task DidCloseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        Closed.Add(filePath);
        return Task.CompletedTask;
    }

    public Task RefreshDiagnosticsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        RefreshedDiagnostics.Add(filePath);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CompletionItem>> CompletionAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CompletionItem>>(new List<CompletionItem> { new("Name", "Property", null, null, null, null) });

    public Task<HoverResult?> HoverAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default) =>
        Task.FromResult<HoverResult?>(new HoverResult("Person person", null));

    public Task<IReadOnlyList<LspLocation>> DefinitionAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LspLocation>>(new List<LspLocation> { new(filePath, new LspRange(new LspPosition(0, 0), new LspPosition(0, 5))) });

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        ShutdownCalled = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

file sealed class FakeLanguageAdapter : ILanguageAdapter
{
    private readonly string _extension;
    public List<string> StartedWorkspaces { get; } = new();
    public List<FakeLanguageServerSession> CreatedSessions { get; } = new();
    public bool ThrowOnStart { get; set; }

    public FakeLanguageAdapter(string extension = ".cs") => _extension = extension;

    public string LanguageId => "fake";
    public bool SupportsFile(string filePath) => filePath.EndsWith(_extension, StringComparison.OrdinalIgnoreCase);
    public LanguageServerResolution ResolveLanguageServer() => new(true, "/fake/server", "1.0", "fake", null);

    public Task<ILanguageServerSession> StartAsync(string workspaceRootPath, CancellationToken cancellationToken = default)
    {
        StartedWorkspaces.Add(workspaceRootPath);
        if (ThrowOnStart) throw new InvalidOperationException("simulated language server failure");

        var session = new FakeLanguageServerSession();
        CreatedSessions.Add(session);
        return Task.FromResult<ILanguageServerSession>(session);
    }
}

public class LanguageServiceTests
{
    [Fact]
    public async Task Opening_a_supported_file_starts_the_server_exactly_once()
    {
        var adapter = new FakeLanguageAdapter();
        var service = new LanguageService(new[] { adapter });
        service.SetWorkspace("/repo");

        await service.OpenDocumentAsync("/repo/Program.cs", "class C {}");
        await service.OpenDocumentAsync("/repo/Other.cs", "class D {}");

        Assert.Single(adapter.StartedWorkspaces);
        Assert.Equal(LanguageServerState.Running, service.State);
        Assert.Equal(2, adapter.CreatedSessions[0].Opened.Count);
    }

    [Fact]
    public async Task Opening_an_unsupported_file_does_not_start_any_server()
    {
        var adapter = new FakeLanguageAdapter();
        var service = new LanguageService(new[] { adapter });
        service.SetWorkspace("/repo");

        await service.OpenDocumentAsync("/repo/readme.md", "# hi");

        Assert.Empty(adapter.StartedWorkspaces);
        Assert.Equal(LanguageServerState.NotStarted, service.State);
    }

    [Fact]
    public async Task Starting_without_a_workspace_throws()
    {
        var adapter = new FakeLanguageAdapter();
        var service = new LanguageService(new[] { adapter });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureStartedForFileAsync("/repo/Program.cs"));
    }

    [Fact]
    public async Task An_adapter_exception_is_reported_as_Failed_not_thrown()
    {
        var adapter = new FakeLanguageAdapter { ThrowOnStart = true };
        var service = new LanguageService(new[] { adapter });
        service.SetWorkspace("/repo");

        await service.OpenDocumentAsync("/repo/Program.cs", "class C {}");

        Assert.Equal(LanguageServerState.Failed, service.State);
        Assert.Equal("simulated language server failure", service.LastFailureMessage);
    }

    [Fact]
    public async Task DiagnosticsPublished_by_the_session_are_forwarded_by_the_service()
    {
        var adapter = new FakeLanguageAdapter();
        var service = new LanguageService(new[] { adapter });
        service.SetWorkspace("/repo");
        (string FilePath, IReadOnlyList<Diagnostic> Diagnostics)? received = null;
        service.DiagnosticsPublished += (_, args) => received = args;

        await service.OpenDocumentAsync("/repo/Program.cs", "class C {}");
        var diagnostic = new Diagnostic(DiagnosticSeverity.Error, "CS1", "boom", "/repo/Program.cs", 1, 1, DiagnosticSource.LanguageServer);
        adapter.CreatedSessions[0].RaiseDiagnostics("/repo/Program.cs", new[] { diagnostic });

        Assert.NotNull(received);
        Assert.Single(received!.Value.Diagnostics);
    }

    [Fact]
    public async Task ChangeDocumentAsync_forwards_to_the_session()
    {
        var adapter = new FakeLanguageAdapter();
        var service = new LanguageService(new[] { adapter });
        service.SetWorkspace("/repo");
        await service.OpenDocumentAsync("/repo/Program.cs", "class C {}");

        await service.ChangeDocumentAsync("/repo/Program.cs", "class C { void M() {} }");

        Assert.Single(adapter.CreatedSessions[0].Changed);
    }

    [Fact]
    public async Task CompletionAsync_HoverAsync_and_DefinitionAsync_return_real_session_results()
    {
        var adapter = new FakeLanguageAdapter();
        var service = new LanguageService(new[] { adapter });
        service.SetWorkspace("/repo");
        await service.OpenDocumentAsync("/repo/Program.cs", "class C {}");

        var completions = await service.CompletionAsync("/repo/Program.cs", new LspPosition(0, 0));
        var hover = await service.HoverAsync("/repo/Program.cs", new LspPosition(0, 0));
        var definitions = await service.DefinitionAsync("/repo/Program.cs", new LspPosition(0, 0));

        Assert.Single(completions);
        Assert.NotNull(hover);
        Assert.Single(definitions);
    }

    [Fact]
    public async Task Completion_before_any_server_has_started_returns_an_empty_list_not_null()
    {
        var adapter = new FakeLanguageAdapter();
        var service = new LanguageService(new[] { adapter });

        var completions = await service.CompletionAsync("/repo/Program.cs", new LspPosition(0, 0));

        Assert.Empty(completions);
    }

    [Fact]
    public async Task StopAsync_shuts_down_the_session_and_reaches_Stopped()
    {
        var adapter = new FakeLanguageAdapter();
        var service = new LanguageService(new[] { adapter });
        service.SetWorkspace("/repo");
        await service.OpenDocumentAsync("/repo/Program.cs", "class C {}");

        await service.StopAsync();

        Assert.True(adapter.CreatedSessions[0].ShutdownCalled);
        Assert.Equal(LanguageServerState.Stopped, service.State);
    }

    [Fact]
    public async Task RestartAsync_stops_the_old_session_and_starts_a_new_one()
    {
        var adapter = new FakeLanguageAdapter();
        var service = new LanguageService(new[] { adapter });
        service.SetWorkspace("/repo");
        await service.OpenDocumentAsync("/repo/Program.cs", "class C {}");
        var firstSession = adapter.CreatedSessions[0];

        await service.RestartAsync("/repo/Program.cs");

        Assert.True(firstSession.ShutdownCalled);
        Assert.Equal(2, adapter.StartedWorkspaces.Count);
        Assert.Equal(LanguageServerState.Running, service.State);
    }

    [Fact]
    public async Task A_session_fault_transitions_to_Failed_and_records_the_message()
    {
        var adapter = new FakeLanguageAdapter();
        var service = new LanguageService(new[] { adapter });
        service.SetWorkspace("/repo");
        await service.OpenDocumentAsync("/repo/Program.cs", "class C {}");

        adapter.CreatedSessions[0].RaiseFaulted(new InvalidOperationException("server crashed"));

        Assert.Equal(LanguageServerState.Failed, service.State);
        Assert.Equal("server crashed", service.LastFailureMessage);
    }

    [Fact]
    public void SupportsFile_reflects_registered_adapters()
    {
        var adapter = new FakeLanguageAdapter();
        var service = new LanguageService(new[] { adapter });

        Assert.True(service.SupportsFile("/repo/Program.cs"));
        Assert.False(service.SupportsFile("/repo/readme.md"));
    }
}
