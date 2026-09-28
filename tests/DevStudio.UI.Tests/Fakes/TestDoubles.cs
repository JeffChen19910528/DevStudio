using DevStudio.Core.Build;
using DevStudio.Core.Debug;
using DevStudio.Core.Diagnostics;
using DevStudio.Core.Editor;
using DevStudio.Core.Extensions;
using DevStudio.Core.Git;
using DevStudio.Core.Language;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Run;
using DevStudio.Core.Settings;
using DevStudio.Core.Terminal;
using DevStudio.Core.Testing;
using DevStudio.Core.Toolchains;
using DevStudio.Core.Workspace;
using DevStudio.UI.Services;

namespace DevStudio.UI.Tests.Fakes;

/// <summary>In-memory test doubles (SKILL.md §40) for exercising <see
/// cref="DevStudio.UI.ViewModels.MainWindowViewModel"/> without any real disk, process, or UI
/// dependency.</summary>
public sealed class FakeTextFileService : ITextFileService
{
    public Dictionary<string, TextFileContent> Files { get; } = new();
    public long ReportedSize { get; set; }
    public string? LastWrittenPath { get; private set; }
    public string? LastWrittenText { get; private set; }

    public Task<long> GetFileSizeAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(ReportedSize);

    public Task<TextFileContent> ReadAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult(Files[path]);

    public Task WriteAsync(string path, string text, TextEncodingKind encoding, LineEndingKind lineEnding, CancellationToken cancellationToken = default)
    {
        LastWrittenPath = path;
        LastWrittenText = text;
        Files[path] = new TextFileContent(text, encoding, lineEnding);
        return Task.CompletedTask;
    }
}

public sealed class FakeFileChangeWatcher : IFileChangeWatcher
{
    public event EventHandler<FileChangedEventArgs>? Changed;
    public List<string> Watched { get; } = new();

    public void Watch(string filePath) => Watched.Add(filePath);
    public void Unwatch(string filePath) => Watched.Remove(filePath);
    public void RaiseChanged(string filePath) => Changed?.Invoke(this, new FileChangedEventArgs(filePath));
    public void Dispose() { }
}

public sealed class FakeFolderPickerService : IFolderPickerService
{
    public string? PathToReturn { get; set; }
    public Task<string?> PickFolderAsync() => Task.FromResult(PathToReturn);
}

public sealed class FakeFilePickerService : IFilePickerService
{
    public string? OpenPathToReturn { get; set; }
    public string? SaveAsPathToReturn { get; set; }
    public Task<string?> PickFileToOpenAsync() => Task.FromResult(OpenPathToReturn);
    public Task<string?> PickFileToSaveAsAsync(string? suggestedFileName) => Task.FromResult(SaveAsPathToReturn);
}

public sealed class FakeDialogService : IDialogService
{
    public SaveChangesResult SaveChangesResultToReturn { get; set; } = SaveChangesResult.DontSave;
    public bool ConfirmResultToReturn { get; set; } = true;
    public List<string> ErrorsShown { get; } = new();

    public Task<SaveChangesResult> AskSaveChangesAsync(string documentName) => Task.FromResult(SaveChangesResultToReturn);
    public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(ConfirmResultToReturn);
    public Task ShowErrorAsync(string title, string message)
    {
        ErrorsShown.Add(message);
        return Task.CompletedTask;
    }
}

public sealed class FakeTerminalSession : ITerminalSession
{
    public bool IsRunning { get; private set; }
    public event EventHandler<TerminalOutputEventArgs>? OutputReceived;
    public event EventHandler? Exited;

    public Task StartAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        IsRunning = true;
        return Task.CompletedTask;
    }

    public Task SendInputAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Resize(int columns, int rows) { }

    public void Terminate()
    {
        IsRunning = false;
        Exited?.Invoke(this, EventArgs.Empty);
    }
    public Task RestartAsync(CancellationToken cancellationToken = default) => StartAsync(string.Empty, cancellationToken);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void RaiseOutput(string text) => OutputReceived?.Invoke(this, new TerminalOutputEventArgs(text, TerminalOutputStream.StandardOutput));
}

public sealed class FakeTerminalSessionFactory : ITerminalSessionFactory
{
    public List<FakeTerminalSession> Created { get; } = new();

    public ITerminalSession Create()
    {
        var session = new FakeTerminalSession();
        Created.Add(session);
        return session;
    }
}

public sealed class FakeWorkspaceStateStore : IWorkspaceStateStore
{
    public Dictionary<string, WorkspaceState> StatesByRoot { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool ReturnCorrupted { get; set; }

    public Task<WorkspaceStateLoadResult> LoadAsync(string workspaceRootPath, CancellationToken cancellationToken = default)
    {
        if (ReturnCorrupted) return Task.FromResult(new WorkspaceStateLoadResult(null, WasCorrupted: true));

        var state = StatesByRoot.TryGetValue(workspaceRootPath, out var found) ? found : null;
        return Task.FromResult(new WorkspaceStateLoadResult(state, WasCorrupted: false));
    }

    public Task SaveAsync(string workspaceRootPath, WorkspaceState state, CancellationToken cancellationToken = default)
    {
        StatesByRoot[workspaceRootPath] = state;
        return Task.CompletedTask;
    }
}

public sealed class FakeToolchainRegistry : IToolchainRegistry
{
    private readonly List<IToolchainDetector> _detectors = new();
    public Dictionary<string, ToolchainInfo> ResultsToReturn { get; } = new();

    public void Register(IToolchainDetector detector) => _detectors.Add(detector);
    public void Unregister(string toolchainId) => _detectors.RemoveAll(d => d.ToolchainId == toolchainId);
    public ToolchainInfo? Get(string toolchainId) => ResultsToReturn.GetValueOrDefault(toolchainId);
    public IReadOnlyList<ToolchainInfo> GetAll() => ResultsToReturn.Values.ToList();
    public IReadOnlyList<ToolchainInfo> FindByCapability(ToolchainCapability capability) =>
        ResultsToReturn.Values.Where(t => t.Capabilities.Contains(capability)).ToList();
    public IReadOnlyList<ToolchainInfo> FindForProjectType(ProjectType projectType) =>
        ResultsToReturn.Values.Where(t => ToolchainRequirements.GetRequiredToolchainIds(projectType).Contains(t.Id)).ToList();
    public Task<IReadOnlyList<ToolchainInfo>> RefreshAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult((IReadOnlyList<ToolchainInfo>)ResultsToReturn.Values.ToList());
}

public sealed class FakeVisualStudioDetector : IVisualStudioDetector
{
    public List<VisualStudioInstance> InstancesToReturn { get; } = new();

    public Task<IReadOnlyList<VisualStudioInstance>> DetectAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult((IReadOnlyList<VisualStudioInstance>)InstancesToReturn);
}

public sealed class FakeBuildAdapter : IBuildAdapter
{
    public ProjectType SupportedProjectType { get; set; } = ProjectType.DotNet;
    public Func<BuildRequest, BuildResult>? ResultFactory { get; set; }
    public List<BuildRequest> Requests { get; } = new();
    public TaskCompletionSource<bool>? StartedSignal { get; set; }
    public bool ThrowOnExecute { get; set; }

    public bool SupportsProjectType(ProjectType projectType) => projectType == SupportedProjectType;

    public async Task<BuildResult> ExecuteAsync(BuildRequest request, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        StartedSignal?.TrySetResult(true);

        if (ThrowOnExecute) throw new InvalidOperationException("simulated adapter crash");

        if (cancellationToken.IsCancellationRequested)
        {
            return new BuildResult(BuildStatus.Cancelled, -1, TimeSpan.Zero, request.Target, request.Operation, Array.Empty<Diagnostic>(), string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        }

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new BuildResult(BuildStatus.Cancelled, -1, TimeSpan.Zero, request.Target, request.Operation, Array.Empty<Diagnostic>(), string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        }

        return ResultFactory?.Invoke(request) ?? new BuildResult(BuildStatus.Succeeded, 0, TimeSpan.Zero, request.Target, request.Operation, Array.Empty<Diagnostic>(), string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    }
}

public sealed class ImmediateFakeBuildAdapter : IBuildAdapter
{
    public ProjectType SupportedProjectType { get; set; } = ProjectType.DotNet;
    public Func<BuildRequest, BuildResult>? ResultFactory { get; set; }
    public List<BuildRequest> Requests { get; } = new();

    public bool SupportsProjectType(ProjectType projectType) => projectType == SupportedProjectType;

    public Task<BuildResult> ExecuteAsync(BuildRequest request, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        var result = ResultFactory?.Invoke(request) ?? new BuildResult(BuildStatus.Succeeded, 0, TimeSpan.Zero, request.Target, request.Operation, Array.Empty<Diagnostic>(), string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        return Task.FromResult(result);
    }
}

public sealed class FakeRunningApplication : IRunningApplication
{
    private readonly TaskCompletionSource<RunResult> _exitSource = new();
    private readonly RunConfiguration _configuration;

    public bool StopCalled { get; private set; }
    public bool HasExited { get; private set; }

    public FakeRunningApplication(RunConfiguration configuration) => _configuration = configuration;

    public void CompleteWith(RunStatus status, int? exitCode)
    {
        HasExited = true;
        _exitSource.TrySetResult(new RunResult(status, exitCode, TimeSpan.Zero, _configuration, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    public void Stop()
    {
        StopCalled = true;
        if (!HasExited) CompleteWith(RunStatus.Terminated, null);
    }

    public Task<RunResult> WaitForExitAsync(CancellationToken cancellationToken = default) => _exitSource.Task;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class FakeRunAdapter : IRunAdapter
{
    public ProjectType SupportedProjectType { get; set; } = ProjectType.DotNet;
    public List<RunConfiguration> StartedConfigurations { get; } = new();
    public List<FakeRunningApplication> CreatedApplications { get; } = new();
    public bool ThrowOnStart { get; set; }
    public string ThrowMessage { get; set; } = "simulated adapter failure";

    /// <summary>When true (the default), the started application "exits" immediately with code
    /// 0 — enough for most command tests. Set false and drive <see
    /// cref="FakeRunningApplication.CompleteWith"/>/<see cref="FakeRunningApplication.Stop"/>
    /// manually for Stop/Restart/exit-code tests.</summary>
    public bool AutoExitImmediately { get; set; } = true;

    public bool SupportsProjectType(ProjectType projectType) => projectType == SupportedProjectType;

    public Task<IRunningApplication> StartAsync(RunConfiguration configuration, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        StartedConfigurations.Add(configuration);
        if (ThrowOnStart) throw new InvalidOperationException(ThrowMessage);

        var application = new FakeRunningApplication(configuration);
        CreatedApplications.Add(application);
        if (AutoExitImmediately)
        {
            application.CompleteWith(RunStatus.Exited, 0);
        }

        return Task.FromResult<IRunningApplication>(application);
    }
}

public sealed class FakeActiveDebugSession : IActiveDebugSession
{
    public bool StopCalled { get; private set; }
    public int? ProcessId => 1234;
    public event Action<StoppedInfo>? Stopped;
#pragma warning disable CS0067 // required by IActiveDebugSession; not exercised by these ViewModel-level tests
    public event Action? Continued;
    public event Action<string, string>? OutputReceived;
#pragma warning restore CS0067
    public event Action<DebugResult>? Terminated;

    public void RaiseStopped(StoppedInfo info) => Stopped?.Invoke(info);

    public Task<IReadOnlyList<BreakpointVerification>> SetBreakpointsAsync(string sourcePath, IReadOnlyList<Breakpoint> breakpoints, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BreakpointVerification>>(breakpoints.Select(b => new BreakpointVerification(b.Id, true, null, b.Line, b.Column)).ToList());

    public Task ConfigurationDoneAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<ThreadInfo>> GetThreadsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ThreadInfo>>(new List<ThreadInfo>());

    public Task<IReadOnlyList<StackFrameInfo>> GetStackTraceAsync(int threadId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StackFrameInfo>>(new List<StackFrameInfo>());

    public Task<IReadOnlyList<Scope>> GetScopesAsync(int frameId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Scope>>(new List<Scope>());

    public Task<IReadOnlyList<Variable>> GetVariablesAsync(int variablesReference, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Variable>>(new List<Variable>());

    public Task ContinueAsync(int threadId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task PauseAsync(int threadId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StepOverAsync(int threadId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StepIntoAsync(int threadId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StepOutAsync(int threadId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DisconnectAsync(bool terminateDebuggee, CancellationToken cancellationToken = default)
    {
        StopCalled = true;
        Terminated?.Invoke(new DebugResult(DebugSessionState.Terminated, null!, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class FakeDebuggerAdapter : IDebuggerAdapter
{
    public ProjectType SupportedProjectType { get; set; } = ProjectType.DotNet;
    public List<DebugConfiguration> StartedConfigurations { get; } = new();
    public List<FakeActiveDebugSession> CreatedSessions { get; } = new();
    public bool ThrowOnStart { get; set; }

    public bool SupportsProjectType(ProjectType projectType) => projectType == SupportedProjectType;

    public Task<IActiveDebugSession> StartAsync(DebugConfiguration configuration, CancellationToken cancellationToken = default)
    {
        StartedConfigurations.Add(configuration);
        if (ThrowOnStart) throw new InvalidOperationException("simulated debugger failure");

        var session = new FakeActiveDebugSession();
        CreatedSessions.Add(session);
        return Task.FromResult<IActiveDebugSession>(session);
    }
}

public sealed class FakeLanguageServerSession : ILanguageServerSession
{
    public bool ShutdownCalled { get; private set; }
    public event Action<string, IReadOnlyList<Diagnostic>>? DiagnosticsPublished;
#pragma warning disable CS0067 // required by ILanguageServerSession; not exercised by these ViewModel-level tests
    public event Action<Exception>? Faulted;
#pragma warning restore CS0067

    public void RaiseDiagnostics(string filePath, IReadOnlyList<Diagnostic> diagnostics) => DiagnosticsPublished?.Invoke(filePath, diagnostics);

    public Task DidOpenAsync(string filePath, string text, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DidChangeAsync(string filePath, string fullText, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DidCloseAsync(string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RefreshDiagnosticsAsync(string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<CompletionItem>> CompletionAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CompletionItem>>(new List<CompletionItem>());

    public Task<HoverResult?> HoverAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default) =>
        Task.FromResult<HoverResult?>(null);

    public Task<IReadOnlyList<LspLocation>> DefinitionAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LspLocation>>(new List<LspLocation>());

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        ShutdownCalled = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class FakeLanguageAdapter : ILanguageAdapter
{
    public string Extension { get; set; } = ".cs";
    public List<string> StartedWorkspaces { get; } = new();
    public List<FakeLanguageServerSession> CreatedSessions { get; } = new();
    public bool ThrowOnStart { get; set; }

    public string LanguageId => "csharp";
    public bool SupportsFile(string filePath) => filePath.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);
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

public sealed class FakeTestAdapter : ITestAdapter
{
    public ProjectType SupportedProjectType { get; set; } = ProjectType.DotNet;
    public List<TestCase> TestsToReturn { get; set; } = new();
    public List<TestResult> ResultsToReturn { get; set; } = new();
    public List<ProjectInfo> DiscoverCalls { get; } = new();
    public List<(ProjectInfo Project, TestFilter? Filter, bool SkipBuild)> RunCalls { get; } = new();
    public bool ThrowOnRun { get; set; }
    public TaskCompletionSource? HangUntilCancelled { get; set; }

    public bool SupportsProjectType(ProjectType projectType) => projectType == SupportedProjectType;

    public Task<IReadOnlyList<TestCase>> DiscoverTestsAsync(ProjectInfo project, BuildConfiguration configuration, CancellationToken cancellationToken = default)
    {
        DiscoverCalls.Add(project);
        return Task.FromResult<IReadOnlyList<TestCase>>(TestsToReturn);
    }

    public async Task<IReadOnlyList<TestResult>> RunTestsAsync(
        ProjectInfo project,
        BuildConfiguration configuration,
        TestFilter? filter,
        bool skipBuild,
        IProcessOutputSink? outputSink = null,
        CancellationToken cancellationToken = default)
    {
        RunCalls.Add((project, filter, skipBuild));
        if (ThrowOnRun) throw new InvalidOperationException("simulated adapter failure");

        if (HangUntilCancelled is not null)
        {
            using var registration = cancellationToken.Register(() => HangUntilCancelled.TrySetResult());
            await HangUntilCancelled.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return ResultsToReturn;
    }
}

public sealed class FakeGitAdapter : IGitAdapter
{
    public string? RepositoryRootToReturn { get; set; }
    public GitRepositoryStatus? StatusToReturn { get; set; }
    public List<GitBranch> BranchesToReturn { get; } = new();
    public List<GitCommit> LogToReturn { get; } = new();
    public GitOperationResult ResultToReturn { get; set; } = GitOperationResult.Success();
    public List<string> StageCalls { get; } = new();
    public List<string> CommitMessages { get; } = new();

    public Task<string?> FindRepositoryRootAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(RepositoryRootToReturn);

    public Task<GitRepositoryStatus> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
        Task.FromResult(StatusToReturn ?? new GitRepositoryStatus(repositoryRoot, "main", null, 0, 0, Array.Empty<GitFileStatus>()));

    public Task<IReadOnlyList<GitBranch>> GetBranchesAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GitBranch>>(BranchesToReturn);

    public Task<IReadOnlyList<GitCommit>> GetLogAsync(string repositoryRoot, int maxCount = 100, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GitCommit>>(LogToReturn);

    public Task<GitDiff?> GetDiffAsync(string repositoryRoot, string relativePath, bool staged, CancellationToken cancellationToken = default) =>
        Task.FromResult<GitDiff?>(null);

    public Task<GitOperationResult> StageAsync(string repositoryRoot, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
    {
        StageCalls.AddRange(relativePaths);
        return Task.FromResult(ResultToReturn);
    }

    public Task<GitOperationResult> UnstageAsync(string repositoryRoot, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default) =>
        Task.FromResult(ResultToReturn);

    public Task<GitOperationResult> DiscardChangesAsync(string repositoryRoot, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default) =>
        Task.FromResult(ResultToReturn);

    public Task<GitOperationResult> CommitAsync(string repositoryRoot, string message, CancellationToken cancellationToken = default)
    {
        CommitMessages.Add(message);
        return Task.FromResult(ResultToReturn);
    }

    public Task<GitOperationResult> CheckoutBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default) =>
        Task.FromResult(ResultToReturn);

    public Task<GitOperationResult> CreateBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default) =>
        Task.FromResult(ResultToReturn);

    public Task<GitOperationResult> DeleteBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default) =>
        Task.FromResult(ResultToReturn);
}

public sealed class FakeExtensionDiscovery : IExtensionDiscovery
{
    public List<ExtensionDescriptor> DescriptorsToReturn { get; } = new();

    public Task<IReadOnlyList<ExtensionDescriptor>> DiscoverAsync(IReadOnlyList<string> roots, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ExtensionDescriptor>>(DescriptorsToReturn);
}

public sealed class FakeExtensionLoader : IExtensionLoader
{
    public Task<ILoadedExtension> LoadAsync(ExtensionManifest manifest, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("No fake extension descriptor is ever Valid/Enabled in these ViewModel-level tests, so this should never be called.");
}

public sealed class FakeSettingsService : ISettingsService
{
    public AppSettings Current { get; private set; } = new();
    public event EventHandler<AppSettings>? Changed;

    public void Update(AppSettings settings)
    {
        Current = settings;
        Changed?.Invoke(this, settings);
    }
}
