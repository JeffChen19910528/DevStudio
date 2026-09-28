using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevStudio.Core.Build;
using DevStudio.Core.Debug;
using DevStudio.Core.Diagnostics;
using DevStudio.Core.Editor;
using DevStudio.Core.Extensions;
using DevStudio.Core.Git;
using DevStudio.Core.Language;
using DevStudio.Core.Packages;
using DevStudio.Core.Platform;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Run;
using DevStudio.Core.Settings;
using DevStudio.Core.Terminal;
using DevStudio.Core.Testing;
using DevStudio.Core.Toolchains;
using DevStudio.Core.Workspace;
using DevStudio.UI.Localization;
using DevStudio.UI.Services;

namespace DevStudio.UI.ViewModels;

/// <summary>One selectable Settings language entry (SKILL.md §7 [Phase 12]). <see
/// cref="DisplayName"/> is each language's own native self-name ("English", "繁體中文") — always
/// shown the same way regardless of which language is currently active, never translated into
/// the *other* language (SKILL.md §7's explicit "do not translate zh-TW into an ambiguous label
/// such as 'Chinese'" — the correct fix is to never translate it at all, the same convention
/// virtually every real application's language picker uses).</summary>
public sealed record LanguageOption(string Code, string DisplayName);

/// <summary>Composition root for the application shell (SKILL.md Phase 1). Coordinates the
/// sub-viewmodels and application services; Views bind to this but contain no file/process/
/// workspace logic of their own (SKILL.md §6 [MVVM]).</summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly WorkspaceAppService _workspaceAppService;
    private readonly DocumentAppService _documentAppService;
    private readonly ITerminalSessionFactory _terminalSessionFactory;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IFilePickerService _filePickerService;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;
    private readonly ProjectDetectionService _projectDetectionService;
    private readonly IWorkspaceStateStore _workspaceStateStore;
    private readonly BuildService _buildService;
    private readonly RunService _runService;
    private readonly DebugService _debugService;
    private readonly LanguageService _languageService;
    private readonly TestService _testService;
    private readonly GitService _gitService;
    private readonly ILocalizationService _localizationService;
    private readonly Dictionary<string, CancellationTokenSource> _pendingDocumentChangeDebounce = new(PathComparer.Comparer);
    private ProjectGraphLookup _projectLookup = ProjectGraphLookup.Empty;
    private WorkspaceProjectGraph? _lastDetectionGraph;

    public WorkspaceExplorerViewModel Explorer { get; }
    public OutputPanelViewModel Output { get; }
    public ProblemsPanelViewModel Problems { get; }
    public ToolchainsPanelViewModel Toolchains { get; }
    public SourceControlViewModel SourceControl { get; }
    public ExtensionsPanelViewModel Extensions { get; }
    public PackageManagerViewModel Packages { get; }
    public StatusBarViewModel StatusBar { get; } = new();

    /// <summary>The single source of localized UI text (SKILL.md §4 [Phase 12]) — bound from
    /// XAML as <c>{Binding Loc[SomeKey]}</c>. Exposed here rather than injected into every
    /// individual View, mirroring how <see cref="Toolchains"/>/<see cref="SourceControl"/>/
    /// <see cref="Extensions"/> are already reached through this same composition root.</summary>
    public ILocalizationService Loc => _localizationService;

    public IReadOnlyList<DevStudio.Core.Workspace.BuildConfiguration> AvailableBuildConfigurations { get; } =
        new[] { DevStudio.Core.Workspace.BuildConfiguration.Debug, DevStudio.Core.Workspace.BuildConfiguration.Release };

    [ObservableProperty]
    private DevStudio.Core.Workspace.BuildConfiguration _selectedBuildConfiguration = DevStudio.Core.Workspace.BuildConfiguration.Debug;

    [ObservableProperty]
    private bool _isBuildRunning;

    [ObservableProperty]
    private BuildResult? _lastBuildResult;

    [ObservableProperty]
    private BuildStatus _lastBuildStatus = BuildStatus.NotStarted;

    // --- Run (SKILL.md Phase 5) ---------------------------------------------------------

    /// <summary>Auto-discovered from detected runnable (.NET, <c>OutputType=Exe</c>/<c>WinExe</c>,
    /// or ASP.NET Core Web SDK) projects (SKILL.md §29) — never hand-authored launch profiles in
    /// this phase. Rebuilt whenever the project graph is (re)computed.</summary>
    public ObservableCollection<RunConfiguration> RunConfigurations { get; } = new();

    [ObservableProperty]
    private RunConfiguration? _selectedRunConfiguration;

    [ObservableProperty]
    private RunStatus _runStatus = RunStatus.NotStarted;

    [ObservableProperty]
    private RunResult? _lastRunResult;

    public bool CanRun => !IsRunActive;
    public bool CanStopOrRestart => RunStatus is RunStatus.Running;

    private bool IsRunActive => RunStatus is RunStatus.Starting or RunStatus.Running or RunStatus.Stopping;

    partial void OnRunStatusChanged(RunStatus value)
    {
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(CanStopOrRestart));
    }

    // --- Debug (SKILL.md Phase 6) -------------------------------------------------------

    /// <summary>In-memory only this phase (SKILL.md §22): source path + line, never persisted,
    /// never containing anything secret-shaped. Re-sent to the live session whenever it changes
    /// while a session is active.</summary>
    public ObservableCollection<Breakpoint> Breakpoints { get; } = new();

    public ObservableCollection<ThreadInfo> DebugThreads { get; } = new();
    public ObservableCollection<StackFrameInfo> CallStackFrames { get; } = new();
    public ObservableCollection<Scope> DebugScopes { get; } = new();
    public ObservableCollection<Variable> DebugVariables { get; } = new();

    [ObservableProperty]
    private DebugSessionState _debugState = DebugSessionState.NotStarted;

    [ObservableProperty]
    private DebugResult? _lastDebugResult;

    [ObservableProperty]
    private StackFrameInfo? _selectedStackFrame;

    [ObservableProperty]
    private Scope? _selectedScope;

    public bool CanStartDebugging => DebugState is DebugSessionState.NotStarted or DebugSessionState.Terminated or DebugSessionState.Failed;
    public bool CanStopDebugging => DebugState is DebugSessionState.Starting or DebugSessionState.Running or DebugSessionState.Paused;
    public bool CanContinueDebugging => DebugState == DebugSessionState.Paused;
    public bool CanPauseDebugging => DebugState == DebugSessionState.Running;
    public bool CanStepDebugging => DebugState == DebugSessionState.Paused;

    partial void OnDebugStateChanged(DebugSessionState value)
    {
        OnPropertyChanged(nameof(CanStartDebugging));
        OnPropertyChanged(nameof(CanStopDebugging));
        OnPropertyChanged(nameof(CanContinueDebugging));
        OnPropertyChanged(nameof(CanPauseDebugging));
        OnPropertyChanged(nameof(CanStepDebugging));
    }

    partial void OnSelectedStackFrameChanged(StackFrameInfo? value) => _ = LoadScopesForSelectedFrameAsync();

    partial void OnSelectedScopeChanged(Scope? value) => _ = LoadVariablesForSelectedScopeAsync();

    // --- Language Server / LSP (SKILL.md Phase 7) ---------------------------------------

    public ObservableCollection<CompletionItem> CompletionItems { get; } = new();

    [ObservableProperty]
    private LanguageServerState _languageServerState = LanguageServerState.NotStarted;

    [ObservableProperty]
    private HoverResult? _hoverResult;

    [ObservableProperty]
    private string? _lastLanguageServerFailure;

    public bool CanRestartLanguageServer => LanguageServerState is LanguageServerState.Running or LanguageServerState.Failed or LanguageServerState.Stopped;

    partial void OnLanguageServerStateChanged(LanguageServerState value) => OnPropertyChanged(nameof(CanRestartLanguageServer));

    // --- Tests (SKILL.md Phase 8) --------------------------------------------------------

    public ObservableCollection<TestNodeViewModel> TestNodes { get; } = new();

    [ObservableProperty]
    private TestRunState _testRunState = TestRunState.NotStarted;

    [ObservableProperty]
    private TestNodeViewModel? _selectedTestNode;

    private bool IsTestRunActive => TestRunState is TestRunState.Starting or TestRunState.Discovering or TestRunState.Running;
    public bool CanRunTests => !IsTestRunActive;
    public bool CanStopTests => IsTestRunActive;

    partial void OnTestRunStateChanged(TestRunState value)
    {
        OnPropertyChanged(nameof(CanRunTests));
        OnPropertyChanged(nameof(CanStopTests));
    }

    public ObservableCollection<DocumentViewModel> Documents { get; } = new();
    public ObservableCollection<TerminalViewModel> Terminals { get; } = new();

    [ObservableProperty]
    private DocumentViewModel? _activeDocument;

    [ObservableProperty]
    private TerminalViewModel? _activeTerminal;

    [ObservableProperty]
    private bool _isFindPanelVisible;

    [ObservableProperty]
    private string _findText = string.Empty;

    [ObservableProperty]
    private string _replaceText = string.Empty;

    [ObservableProperty]
    private bool _matchCase;

    [ObservableProperty]
    private WorkspaceModel? _currentWorkspace;

    [ObservableProperty]
    private string? _activeProjectId;

    [ObservableProperty]
    private bool _isWorkspaceTrusted;

    public string TrustMenuLabel => IsWorkspaceTrusted ? "Untrust Workspace" : "Trust Workspace";

    partial void OnIsWorkspaceTrustedChanged(bool value) => OnPropertyChanged(nameof(TrustMenuLabel));

    /// <summary>Refreshed straight from <see cref="ISettingsService.Current"/> on every access,
    /// so it's never stale relative to what's actually persisted (SKILL.md §26).</summary>
    public IReadOnlyList<RecentWorkspaceEntry> RecentWorkspaces => _settingsService.Current.RecentWorkspaces;

    public bool ReopenLastWorkspaceOnStartup
    {
        get => _settingsService.Current.ReopenLastWorkspaceOnStartup;
        set
        {
            _settingsService.Update(_settingsService.Current with { ReopenLastWorkspaceOnStartup = value });
            OnPropertyChanged();
        }
    }

    /// <summary>Bridges "move the caret/selection" to the View, which owns the actual TextBox
    /// control — this is presentation, not business logic, so it stays out of the ViewModel.</summary>
    public event EventHandler<(int Start, int Length)>? CaretMoveRequested;

    /// <summary>Bridges "apply this theme" to the View, since only the Avalonia
    /// <c>Application</c> object (owned by DevStudio.App) can actually set it (SKILL.md §23).</summary>
    public event EventHandler<AppTheme>? ThemeChanged;

    public AppTheme CurrentTheme => _settingsService.Current.Theme;

    /// <summary>Every language a user could pick in Settings (SKILL.md §7 [Phase 12]) — always
    /// exactly <see cref="ILocalizationService.SupportedCultures"/>, never a hard-coded list that
    /// could drift out of sync with what the service actually supports. Each option's own
    /// display name is itself localized (e.g. shows "English"/"繁體中文" regardless of which
    /// language is currently active) via its own resource key, per SKILL.md §7's explicit "do
    /// not translate zh-TW as an ambiguous 'Chinese'" requirement.</summary>
    public IReadOnlyList<LanguageOption> AvailableLanguages { get; }

    /// <summary>Changing this immediately switches the live UI (SKILL.md §9 — no restart
    /// required, since every bound string re-reads through <see cref="Loc"/>'s indexer
    /// notification) and persists the choice through the existing settings store (SKILL.md §8) —
    /// never a second persistence mechanism.</summary>
    public LanguageOption? SelectedLanguageOption
    {
        get => AvailableLanguages.FirstOrDefault(l => l.Code == _settingsService.Current.Language);
        set
        {
            if (value is null || string.Equals(value.Code, _settingsService.Current.Language, StringComparison.Ordinal)) return;

            _settingsService.Update(_settingsService.Current with { Language = value.Code });
            _localizationService.SetCulture(new CultureInfo(value.Code));
            OnPropertyChanged();
        }
    }

    public MainWindowViewModel(
        WorkspaceAppService workspaceAppService,
        DocumentAppService documentAppService,
        ITerminalSessionFactory terminalSessionFactory,
        IFolderPickerService folderPickerService,
        IFilePickerService filePickerService,
        IDialogService dialogService,
        ISettingsService settingsService,
        ProjectDetectionService projectDetectionService,
        IWorkspaceStateStore workspaceStateStore,
        IToolchainRegistry toolchainRegistry,
        IVisualStudioDetector visualStudioDetector,
        BuildService buildService,
        RunService runService,
        DebugService debugService,
        LanguageService languageService,
        TestService testService,
        GitService gitService,
        ExtensionManager extensionManager,
        ICommandRegistry commandRegistry,
        PackageService packageService,
        ILocalizationService localizationService)
    {
        _buildService = buildService;
        _runService = runService;
        _debugService = debugService;
        _languageService = languageService;
        _testService = testService;
        _workspaceAppService = workspaceAppService;
        _documentAppService = documentAppService;
        _terminalSessionFactory = terminalSessionFactory;
        _folderPickerService = folderPickerService;
        _filePickerService = filePickerService;
        _dialogService = dialogService;
        _settingsService = settingsService;
        _projectDetectionService = projectDetectionService;
        _workspaceStateStore = workspaceStateStore;
        _localizationService = localizationService;

        Explorer = new WorkspaceExplorerViewModel(_workspaceAppService);
        Explorer.FileActivated += async (_, path) => await OpenFileAsync(path).ConfigureAwait(true);
        Explorer.PropertyChanged += OnExplorerPropertyChanged;

        Output = new OutputPanelViewModel();
        Problems = new ProblemsPanelViewModel();
        Toolchains = new ToolchainsPanelViewModel(toolchainRegistry, visualStudioDetector);
        Toolchains.RefreshCompleted += async (_, _) => await RefreshProjectCapabilitiesAsync().ConfigureAwait(true);

        _gitService = gitService;
        SourceControl = new SourceControlViewModel(gitService, dialogService, EnsureGitWorkspaceTrustedAsync, _localizationService);

        Extensions = new ExtensionsPanelViewModel(extensionManager, commandRegistry, settingsService);

        Packages = new PackageManagerViewModel(packageService, dialogService, EnsurePackageWorkspaceTrustedAsync, _localizationService);

        // The persisted language preference is applied once at startup (SKILL.md §8) — settings
        // are already loaded into settingsService.Current by the composition root before this
        // constructor ever runs (the same ordering CurrentTheme already relies on).
        _localizationService.SetCulture(new CultureInfo(_settingsService.Current.Language));
        AvailableLanguages = _localizationService.SupportedCultures
            .Select(culture => new LanguageOption(culture.Name, culture.Name == "zh-TW" ? "繁體中文" : "English"))
            .ToList();

        _runService.StatusChanged += (_, status) => RunStatus = status;
        _runService.Completed += (_, result) =>
        {
            LastRunResult = result;
            Output.Log("Run", DescribeRunResult(result), result.Status == RunStatus.FailedToStart ? OutputEntrySeverity.Error : OutputEntrySeverity.Info);
        };

        _debugService.StateChanged += (_, state) => DebugState = state;
        _debugService.Stopped += (_, info) =>
        {
            Output.Log("Debug", $"Stopped: {info.Reason}" + (info.Description is { } description ? $" — {description}" : string.Empty));
            _ = RefreshThreadsAndStackAsync();
        };
        _debugService.Continued += (_, _) => Output.Log("Debug", "Continuing...");
        _debugService.OutputReceived += (_, entry) =>
            Output.Log("Debug", entry.Text, string.Equals(entry.Category, "stderr", StringComparison.OrdinalIgnoreCase) ? OutputEntrySeverity.Warning : OutputEntrySeverity.Info);
        _debugService.Completed += (_, result) =>
        {
            LastDebugResult = result;
            Output.Log("Debug", DescribeDebugResult(result), result.State == DebugSessionState.Failed ? OutputEntrySeverity.Error : OutputEntrySeverity.Info);
            DebugThreads.Clear();
            CallStackFrames.Clear();
            DebugScopes.Clear();
            DebugVariables.Clear();
            SelectedStackFrame = null;
            SelectedScope = null;
        };

        // Not marshaled to the UI thread: matches the existing, established pattern for
        // RunService/DebugService's own event handlers above, which update UI-bound state
        // directly from whatever thread raises the event.
        _languageService.StateChanged += (_, state) =>
        {
            LanguageServerState = state;
            if (state == LanguageServerState.Failed) LastLanguageServerFailure = _languageService.LastFailureMessage;
        };
        _languageService.DiagnosticsPublished += (_, args) => Problems.ReplaceLanguageDiagnostics(args.FilePath, args.Diagnostics);

        _testService.StateChanged += (_, state) => TestRunState = state;
        _testService.TestsDiscovered += (_, tests) =>
        {
            TestNodes.Clear();
            foreach (var test in tests) TestNodes.Add(new TestNodeViewModel(test));
        };
        _testService.Completed += (_, result) =>
        {
            Output.Log("Test", DescribeTestRunResult(result), result.State is TestRunState.Failed or TestRunState.BlockedByBuildFailure ? OutputEntrySeverity.Error : OutputEntrySeverity.Info);
            var resultsByName = result.Results.ToDictionary(r => r.TestCaseId, StringComparer.Ordinal);
            foreach (var node in TestNodes)
            {
                if (resultsByName.TryGetValue(node.TestCase.FullyQualifiedName, out var testResult))
                {
                    node.ApplyResult(testResult);
                }
            }

            var failureDiagnostics = result.Results
                .Where(r => r.Outcome == TestOutcome.Failed && r.SourceFile is not null && r.Line is not null)
                .Select(r => new Diagnostic(DiagnosticSeverity.Error, "Test", r.ErrorMessage ?? $"'{r.TestCaseId}' failed.", r.SourceFile!, r.Line!.Value, 1, DiagnosticSource.TestRunner))
                .ToList();
            Problems.ReplaceTestDiagnostics(failureDiagnostics);
        };

        _documentAppService.ExternalChangeDetected += OnExternalChangeDetected;
        _settingsService.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(RecentWorkspaces));
            OnPropertyChanged(nameof(ReopenLastWorkspaceOnStartup));
        };

        Output.Log("Application", "DevStudio Phase 1 shell started.");

        // Fire-and-forget: toolchain detection runs known, fixed version probes with timeouts
        // (SKILL.md §6, §27) and must never block application startup (SKILL.md §20).
        _ = Toolchains.RefreshAsync();

        // Extensions are user/global-level, not workspace-scoped (SKILL.md §10) — discovered and
        // activated once at startup rather than per-workspace; a broken extension is caught and
        // recorded as Failed by ExtensionManager itself and must never block startup (SKILL.md §45).
        _ = Extensions.RefreshAsync();
    }

    private void OnExplorerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WorkspaceExplorerViewModel.SelectedNode)) return;

        var node = Explorer.SelectedNode;
        ActiveProjectId = node?.Project?.Id ?? node?.Solution?.ProjectIds.FirstOrDefault();
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var folder = await _folderPickerService.PickFolderAsync().ConfigureAwait(true);
        if (folder is null) return;

        await OpenWorkspaceAsync(folder).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task OpenRecentWorkspace(string path) => OpenWorkspaceAsync(path);

    /// <summary>
    /// Full workspace-open pipeline (SKILL.md §14): scan → detect projects/solutions → build
    /// the Explorer → resolve the recent-workspaces list → restore whatever was open last time.
    /// Everything here is read-only metadata discovery — nothing executes a project-defined
    /// command (SKILL.md §4, §25, §31).
    /// </summary>
    public async Task OpenWorkspaceAsync(string path)
    {
        if (!Directory.Exists(path))
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.WorkspaceNotFound.Title"), _localizationService.Format("Dialog.WorkspaceNotFound.Message", path)).ConfigureAwait(true);
            return;
        }

        if (Explorer.RootPath is not null)
        {
            await SaveWorkspaceStateAsync().ConfigureAwait(true);
        }

        var graph = await _projectDetectionService.DetectAsync(path, _workspaceAppService.ExclusionRules).ConfigureAwait(true);
        _lastDetectionGraph = graph;
        var displayGraph = ProjectCapabilityMatcher.ApplyToGraph(graph, Toolchains.Toolchains.ToList());
        _projectLookup = ProjectGraphLookup.FromGraph(displayGraph);
        Packages.SetProjects(displayGraph.AllProjects);

        await Explorer.LoadRootAsync(path, _projectLookup).ConfigureAwait(true);

        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } n ? n : path;
        StatusBar.WorkspaceName = name;
        IsWorkspaceTrusted = false;
        CurrentWorkspace = new WorkspaceModel(
            Id: Guid.NewGuid().ToString("N"),
            RootPath: path,
            Name: name,
            Projects: displayGraph.TopLevelProjects,
            Solutions: displayGraph.Solutions,
            BuildConfigurations: new[] { BuildConfiguration.Debug, BuildConfiguration.Release },
            IsTrusted: false);

        DiscoverRunConfigurations(displayGraph);
        LogDetectionDiagnostics(graph);
        UpdateRecentWorkspaces(path, name);

        // Switching workspaces means any previous workspace's language server is talking about
        // the wrong project entirely — stop it rather than leave it running against stale state.
        if (_languageService.IsActive) await _languageService.StopAsync().ConfigureAwait(true);
        _languageService.SetWorkspace(path);

        await RestoreWorkspaceStateAsync(path).ConfigureAwait(true);

        // Real repository detection/status/log/branch listing are always read-only (SKILL.md
        // §8), so this runs unconditionally on every workspace open — never gated by trust.
        await SourceControl.SetWorkspaceRootAsync(path).ConfigureAwait(true);

        Output.Log("Workspace", $"Opened folder: {path}");
    }

    private void LogDetectionDiagnostics(WorkspaceProjectGraph graph)
    {
        foreach (var project in graph.AllProjects.Where(p => p.DetectionConfidence != DetectionConfidence.Full))
        {
            Output.Log("Project Detection", project.DetectionWarning ?? $"'{project.Name}' was only partially detected.", OutputEntrySeverity.Warning);
        }

        foreach (var solution in graph.Solutions.Where(s => s.DetectionConfidence != DetectionConfidence.Full))
        {
            Output.Log("Project Detection", solution.DetectionWarning ?? $"'{solution.Name}' was only partially detected.", OutputEntrySeverity.Warning);
        }
    }

    private void UpdateRecentWorkspaces(string path, string displayName)
    {
        var existing = _settingsService.Current.RecentWorkspaces
            .Where(e => !string.Equals(e.Path, path, PathComparer.Comparison));

        var updated = new List<RecentWorkspaceEntry> { new(path, displayName, DateTimeOffset.UtcNow) };
        updated.AddRange(existing);

        _settingsService.Update(_settingsService.Current with { RecentWorkspaces = updated.Take(10).ToList() });
    }

    /// <summary>Re-runs capability matching over the already-detected project graph against
    /// whatever toolchains are now known (SKILL.md §23–§24) — called after Tools → Refresh
    /// Toolchains, since a project's Build/Run/Test availability can change without any new
    /// project detection happening.</summary>
    private async Task RefreshProjectCapabilitiesAsync()
    {
        if (_lastDetectionGraph is null || Explorer.RootPath is not { } rootPath) return;

        var displayGraph = ProjectCapabilityMatcher.ApplyToGraph(_lastDetectionGraph, Toolchains.Toolchains.ToList());
        _projectLookup = ProjectGraphLookup.FromGraph(displayGraph);
        Packages.SetProjects(displayGraph.AllProjects);

        await Explorer.LoadRootAsync(rootPath, _projectLookup).ConfigureAwait(true);

        if (CurrentWorkspace is not null)
        {
            CurrentWorkspace = CurrentWorkspace with { Projects = displayGraph.TopLevelProjects, Solutions = displayGraph.Solutions };
        }

        DiscoverRunConfigurations(displayGraph);
    }

    /// <summary>Rebuilds <see cref="RunConfigurations"/> from every detected project with <see
    /// cref="ProjectInfo.IsExecutable"/> true (SKILL.md §29) — never for a class library, and
    /// never by guessing; the flag comes from the project's own real content. Preserves the
    /// current selection by name across a refresh where possible.</summary>
    private void DiscoverRunConfigurations(WorkspaceProjectGraph graph)
    {
        var previouslySelectedName = SelectedRunConfiguration?.Name;

        RunConfigurations.Clear();
        foreach (var project in graph.AllProjects.Where(p => p.IsExecutable))
        {
            var target = new BuildTarget(BuildTargetKind.Project, project.Name, project.ProjectFile ?? project.RootPath, project.RootPath, project.ProjectType);
            RunConfigurations.Add(new RunConfiguration(project.Name, target, SelectedBuildConfiguration));
        }

        SelectedRunConfiguration = RunConfigurations.FirstOrDefault(c => c.Name == previouslySelectedName) ?? RunConfigurations.FirstOrDefault();
    }

    [RelayCommand]
    private void ToggleWorkspaceTrust()
    {
        IsWorkspaceTrusted = !IsWorkspaceTrusted;
        if (CurrentWorkspace is not null)
        {
            CurrentWorkspace = CurrentWorkspace with { IsTrusted = IsWorkspaceTrusted };
        }
    }

    // --- Build (SKILL.md Phase 4) ---------------------------------------------------------

    [RelayCommand]
    private Task Build() => ExecuteBuildOperationAsync(BuildOperation.Build);

    [RelayCommand]
    private Task Rebuild() => ExecuteBuildOperationAsync(BuildOperation.Rebuild);

    [RelayCommand]
    private Task Clean() => ExecuteBuildOperationAsync(BuildOperation.Clean);

    [RelayCommand]
    private Task Restore() => ExecuteBuildOperationAsync(BuildOperation.Restore);

    [RelayCommand]
    private void CancelBuild() => _buildService.CancelCurrentBuild();

    /// <summary>Picks what "Build" means right now (SKILL.md §9): the Explorer's selected
    /// solution/project if there is one, else the workspace's first solution, else its first
    /// project. Never the workspace directory itself. <paramref name="unavailableReason"/> is
    /// set when a project was resolved but its own Phase 3 capability check already knows Build
    /// is unavailable for it (e.g. its toolchain isn't installed) — surfaced before ever calling
    /// <see cref="BuildService"/>.</summary>
    private BuildTarget? ResolveBuildTarget(out string? unavailableReason)
    {
        unavailableReason = null;
        var node = Explorer.SelectedNode;

        if (node?.Solution is { } selectedSolution)
        {
            return SolutionTarget(selectedSolution);
        }

        if (node?.Project is { } selectedProject)
        {
            return ProjectTarget(selectedProject, out unavailableReason);
        }

        if (CurrentWorkspace?.Solutions.FirstOrDefault() is { } firstSolution)
        {
            return SolutionTarget(firstSolution);
        }

        if (CurrentWorkspace?.Projects.FirstOrDefault() is { } firstProject)
        {
            return ProjectTarget(firstProject, out unavailableReason);
        }

        return null;
    }

    private BuildTarget SolutionTarget(SolutionInfo solution) => new(
        BuildTargetKind.Solution,
        solution.Name,
        solution.SolutionFilePath,
        Path.GetDirectoryName(solution.SolutionFilePath) ?? Explorer.RootPath ?? Directory.GetCurrentDirectory(),
        ProjectType.DotNet);

    private static BuildTarget ProjectTarget(ProjectInfo project, out string? unavailableReason)
    {
        unavailableReason = null;
        var buildCapability = project.Capabilities.FirstOrDefault(c => c.Capability == ToolchainCapability.Build);
        if (buildCapability is { Availability: CapabilityAvailability.Unavailable })
        {
            unavailableReason = buildCapability.Explanation ?? $"Build is not available for '{project.Name}'.";
        }

        return new BuildTarget(BuildTargetKind.Project, project.Name, project.ProjectFile ?? project.RootPath, project.RootPath, project.ProjectType);
    }

    private async Task ExecuteBuildOperationAsync(BuildOperation operation)
    {
        if (IsBuildRunning) return; // toolbar/menu also guard on this; this is the backstop.

        var target = ResolveBuildTarget(out var unavailableReason);
        if (target is null)
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.CannotBuild.Title"), _localizationService.GetString("Dialog.CannotBuild.NoTarget")).ConfigureAwait(true);
            return;
        }

        if (unavailableReason is not null)
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.CannotBuild.Title"), unavailableReason).ConfigureAwait(true);
            return;
        }

        if (!_buildService.HasAdapterFor(target.ProjectType))
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.BuildNotSupported.Title"), _localizationService.Format("Dialog.BuildNotSupported.Message", target.ProjectType)).ConfigureAwait(true);
            return;
        }

        if (CurrentWorkspace is { IsTrusted: false })
        {
            var trust = await _dialogService.ConfirmAsync(
                _localizationService.GetString("Dialog.WorkspaceNotTrusted.Title"),
                _localizationService.Format("Dialog.WorkspaceNotTrusted.Build", target.Name, operation.ToString().ToLowerInvariant())).ConfigureAwait(true);
            if (!trust) return;
            ToggleWorkspaceTrust();
        }

        Problems.ReplaceBuildDiagnostics(Array.Empty<Diagnostic>());
        Output.Log("Build", $"Starting {operation}: {target.Name} ({SelectedBuildConfiguration.Name})");
        IsBuildRunning = true;
        LastBuildStatus = BuildStatus.Running;

        var sink = new DelegateProcessOutputSink(
            line => Output.Log("Build", line),
            line => Output.Log("Build", line, OutputEntrySeverity.Warning));

        try
        {
            var result = await _buildService.ExecuteAsync(target, SelectedBuildConfiguration, operation, sink).ConfigureAwait(true);
            LastBuildResult = result;
            LastBuildStatus = result.Status;
            Problems.ReplaceBuildDiagnostics(result.Diagnostics);
            Output.Log("Build", DescribeBuildResult(result), result.Status is BuildStatus.Failed or BuildStatus.Unavailable ? OutputEntrySeverity.Error : OutputEntrySeverity.Info);
        }
        catch (InvalidOperationException ex)
        {
            // Defense-in-depth guard in BuildService tripped despite the IsBuildRunning check above.
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.BuildAlreadyRunning.Title"), ex.Message).ConfigureAwait(true);
        }
        finally
        {
            IsBuildRunning = false;
        }
    }

    private static string DescribeBuildResult(BuildResult result) => result.Status switch
    {
        BuildStatus.Succeeded => $"Build succeeded. Duration: {result.Duration.TotalSeconds:0.00}s",
        BuildStatus.Failed => $"Build failed (exit code {result.ExitCode}). Duration: {result.Duration.TotalSeconds:0.00}s. {result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error)} error(s), {result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning)} warning(s).",
        BuildStatus.Cancelled => "Build cancelled.",
        BuildStatus.TimedOut => "Build timed out.",
        BuildStatus.Unavailable => result.Message ?? "Build unavailable.",
        _ => result.Status.ToString(),
    };

    [RelayCommand]
    private async Task NavigateToDiagnosticAsync(Diagnostic? diagnostic)
    {
        if (diagnostic is null || string.IsNullOrEmpty(diagnostic.File))
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.NavigationUnavailable.Title"), _localizationService.GetString("Dialog.NavigationUnavailable.NoLocation")).ConfigureAwait(true);
            return;
        }

        if (!File.Exists(diagnostic.File))
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.NavigationUnavailable.Title"), _localizationService.GetString("Dialog.NavigationUnavailable.CannotNavigate")).ConfigureAwait(true);
            return;
        }

        await OpenFileAsync(diagnostic.File).ConfigureAwait(true);
        if (ActiveDocument is null) return;

        var lineStartIndex = TextSearchService.GetIndexForLine(ActiveDocument.Text, diagnostic.Line);
        var index = Math.Min(lineStartIndex + Math.Max(diagnostic.Column - 1, 0), ActiveDocument.Text.Length);
        CaretMoveRequested?.Invoke(this, (index, 0));
    }

    // --- Run (SKILL.md Phase 5) -----------------------------------------------------------

    [RelayCommand]
    private Task Run() => ExecuteRunAsync(buildBeforeRunOverride: null);

    [RelayCommand]
    private Task RunWithoutBuild() => ExecuteRunAsync(buildBeforeRunOverride: false);

    [RelayCommand]
    private void Stop() => _runService.Stop();

    [RelayCommand]
    private async Task RestartAsync()
    {
        if (SelectedRunConfiguration is null) return;

        var buildOutputSink = new DelegateProcessOutputSink(line => Output.Log("Build", line), line => Output.Log("Build", line, OutputEntrySeverity.Warning));
        var runOutputSink = new DelegateProcessOutputSink(line => Output.Log("Run", line), line => Output.Log("Run", line, OutputEntrySeverity.Warning));

        Output.Log("Run", $"Restarting: {SelectedRunConfiguration.Name}");
        await _runService.RestartAsync(SelectedRunConfiguration, buildOutputSink, runOutputSink).ConfigureAwait(true);
    }

    private async Task ExecuteRunAsync(bool? buildBeforeRunOverride)
    {
        if (!CanRun) return; // toolbar/menu also guard on this; this is the backstop.

        if (SelectedRunConfiguration is null)
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.CannotRun.Title"), _localizationService.GetString("Dialog.CannotRun.Message")).ConfigureAwait(true);
            return;
        }

        if (!_runService.HasAdapterFor(SelectedRunConfiguration.Target.ProjectType))
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.RunNotSupported.Title"), _localizationService.Format("Dialog.RunNotSupported.Message", SelectedRunConfiguration.Target.ProjectType)).ConfigureAwait(true);
            return;
        }

        // Run executes real application code — the same trust gate Build uses, and it applies
        // even when BuildBeforeRun is false (SKILL.md §34–§35): skipping the build never skips
        // the fact that a real process is about to start.
        if (CurrentWorkspace is { IsTrusted: false })
        {
            var trust = await _dialogService.ConfirmAsync(
                _localizationService.GetString("Dialog.WorkspaceNotTrusted.Title"),
                _localizationService.Format("Dialog.WorkspaceNotTrusted.Run", SelectedRunConfiguration.Name)).ConfigureAwait(true);
            if (!trust) return;
            ToggleWorkspaceTrust();
        }

        var configuration = buildBeforeRunOverride is { } overrideValue
            ? SelectedRunConfiguration with { BuildBeforeRun = overrideValue }
            : SelectedRunConfiguration with { BuildConfiguration = SelectedBuildConfiguration };

        var buildOutputSink = new DelegateProcessOutputSink(line => Output.Log("Build", line), line => Output.Log("Build", line, OutputEntrySeverity.Warning));
        var runOutputSink = new DelegateProcessOutputSink(line => Output.Log("Run", line), line => Output.Log("Run", line, OutputEntrySeverity.Warning));

        Output.Log("Run", $"Starting: {configuration.Name} ({configuration.BuildConfiguration.Name}){(configuration.BuildBeforeRun ? "" : " [no build]")}");

        try
        {
            await _runService.StartAsync(configuration, buildOutputSink, runOutputSink).ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            // Defense-in-depth guard in RunService tripped despite the CanRun check above.
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.ApplicationAlreadyRunning.Title"), ex.Message).ConfigureAwait(true);
        }
    }

    private static string DescribeRunResult(RunResult result) => result.Status switch
    {
        RunStatus.Exited => $"'{result.Configuration.Name}' exited with code {result.ExitCode}.",
        RunStatus.Terminated => $"'{result.Configuration.Name}' stopped.",
        RunStatus.FailedToStart => result.Message ?? $"'{result.Configuration.Name}' failed to start.",
        RunStatus.Cancelled => $"'{result.Configuration.Name}' cancelled.",
        _ => result.Status.ToString(),
    };

    // --- Debug (SKILL.md Phase 6) -----------------------------------------------------------

    [RelayCommand]
    private async Task StartDebuggingAsync()
    {
        if (!CanStartDebugging) return;

        if (SelectedRunConfiguration is null)
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.CannotDebug.Title"), _localizationService.GetString("Dialog.CannotDebug.Message")).ConfigureAwait(true);
            return;
        }

        if (!_debugService.HasAdapterFor(SelectedRunConfiguration.Target.ProjectType))
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.DebugNotSupported.Title"), _localizationService.Format("Dialog.DebugNotSupported.Message", SelectedRunConfiguration.Target.ProjectType)).ConfigureAwait(true);
            return;
        }

        // Debugging executes both the real application and real debugger code — the trust gate
        // must occur before Build/launch, exactly like Run (SKILL.md §44), and must apply even
        // though the debugger has its own BuildBeforeDebug flag.
        if (CurrentWorkspace is { IsTrusted: false })
        {
            var trust = await _dialogService.ConfirmAsync(
                _localizationService.GetString("Dialog.WorkspaceNotTrusted.Title"),
                _localizationService.Format("Dialog.WorkspaceNotTrusted.Debug", SelectedRunConfiguration.Name)).ConfigureAwait(true);
            if (!trust) return;
            ToggleWorkspaceTrust();
        }

        foreach (var sourceGroup in Breakpoints.GroupBy(b => b.SourcePath))
        {
            await _debugService.SetBreakpointsAsync(sourceGroup.Key, sourceGroup.ToList()).ConfigureAwait(true);
        }

        var configuration = new DebugConfiguration(SelectedRunConfiguration with { BuildConfiguration = SelectedBuildConfiguration });
        Output.Log("Debug", $"Starting debug session: {configuration.RunConfiguration.Name}");

        try
        {
            await _debugService.StartAsync(configuration).ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            // Defense-in-depth guard in DebugService tripped despite the CanStartDebugging check above.
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.DebugSessionAlreadyActive.Title"), ex.Message).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private Task ContinueDebuggingAsync() => _debugService.ContinueAsync();

    [RelayCommand]
    private Task PauseDebuggingAsync() => _debugService.PauseAsync();

    [RelayCommand]
    private Task StepOverDebuggingAsync() => _debugService.StepOverAsync();

    [RelayCommand]
    private Task StepIntoDebuggingAsync() => _debugService.StepIntoAsync();

    [RelayCommand]
    private Task StepOutDebuggingAsync() => _debugService.StepOutAsync();

    [RelayCommand]
    private Task StopDebuggingAsync() => _debugService.StopAsync();

    /// <summary>Toggles a breakpoint at a line number the user supplies (SKILL.md §23: the
    /// plain-<c>TextBox</c> editor from ADR-002 has no gutter to click, so this reuses the same
    /// "Go To Line"-style number-entry pattern rather than adding a new editor framework just
    /// for Phase 6 — a documented UI limitation, not an oversight).</summary>
    [RelayCommand]
    private async Task ToggleBreakpointAsync(int lineNumber)
    {
        if (ActiveDocument?.FilePath is not { } path) return;

        var existing = Breakpoints.FirstOrDefault(b => string.Equals(b.SourcePath, path, PathComparer.Comparison) && b.Line == lineNumber);
        if (existing is not null)
        {
            Breakpoints.Remove(existing);
        }
        else
        {
            Breakpoints.Add(new Breakpoint(Guid.NewGuid(), path, lineNumber));
        }

        if (_debugService.IsActive)
        {
            var forSource = Breakpoints.Where(b => string.Equals(b.SourcePath, path, PathComparer.Comparison)).ToList();
            await _debugService.SetBreakpointsAsync(path, forSource).ConfigureAwait(true);
        }
    }

    private async Task RefreshThreadsAndStackAsync()
    {
        DebugThreads.Clear();
        foreach (var thread in await _debugService.GetThreadsAsync().ConfigureAwait(true)) DebugThreads.Add(thread);

        CallStackFrames.Clear();
        if (_debugService.CurrentThreadId is { } threadId)
        {
            foreach (var frame in await _debugService.GetStackTraceAsync(threadId).ConfigureAwait(true)) CallStackFrames.Add(frame);
        }
        SelectedStackFrame = CallStackFrames.FirstOrDefault();
    }

    private async Task LoadScopesForSelectedFrameAsync()
    {
        DebugScopes.Clear();
        DebugVariables.Clear();
        if (SelectedStackFrame is null) return;

        foreach (var scope in await _debugService.GetScopesAsync(SelectedStackFrame.Id).ConfigureAwait(true)) DebugScopes.Add(scope);
        SelectedScope = DebugScopes.FirstOrDefault();

        if (SelectedStackFrame.SourcePath is { } sourcePath && File.Exists(sourcePath))
        {
            await NavigateToSourceLocationAsync(sourcePath, SelectedStackFrame.Line, SelectedStackFrame.Column).ConfigureAwait(true);
        }
    }

    private async Task LoadVariablesForSelectedScopeAsync()
    {
        DebugVariables.Clear();
        if (SelectedScope is null) return;

        foreach (var variable in await _debugService.GetVariablesAsync(SelectedScope.VariablesReference).ConfigureAwait(true)) DebugVariables.Add(variable);
    }

    /// <summary>Expands a variable on demand (SKILL.md §30) — children are never fetched
    /// automatically just because a parent variable loaded.</summary>
    public Task<IReadOnlyList<Variable>> GetChildVariablesAsync(int variablesReference) =>
        _debugService.GetVariablesAsync(variablesReference);

    private async Task NavigateToSourceLocationAsync(string path, int line, int column)
    {
        await OpenFileAsync(path).ConfigureAwait(true);
        if (ActiveDocument is null) return;

        var lineStartIndex = TextSearchService.GetIndexForLine(ActiveDocument.Text, line);
        var index = Math.Min(lineStartIndex + Math.Max(column - 1, 0), ActiveDocument.Text.Length);
        CaretMoveRequested?.Invoke(this, (index, 0));
    }

    private static string DescribeDebugResult(DebugResult result) => result.State switch
    {
        DebugSessionState.Terminated => result.ExitCode is { } code ? $"Debuggee exited with code {code}." : "Debug session stopped.",
        DebugSessionState.Failed => result.Message ?? "Debug session failed.",
        _ => result.State.ToString(),
    };

    // --- Language Server / LSP (SKILL.md Phase 7) -------------------------------------------

    [RelayCommand]
    private async Task RequestCompletionAsync()
    {
        CompletionItems.Clear();
        if (ActiveDocument?.FilePath is not { } path) return;

        var items = await _languageService.CompletionAsync(path, CurrentCaretPosition(ActiveDocument)).ConfigureAwait(true);
        foreach (var item in items) CompletionItems.Add(item);
    }

    [RelayCommand]
    private void ApplyCompletion(CompletionItem? item)
    {
        if (item is null || ActiveDocument is null) return;

        // A real completion's insertion may come entirely from its TextEdit rather than
        // InsertText (SKILL.md §27–§28) — never assume InsertText is populated.
        if (item.TextEdit is { } edit)
        {
            var startIndex = TextSearchService.GetIndexForLine(ActiveDocument.Text, edit.Range.Start.Line + 1) + edit.Range.Start.Character;
            var endIndex = TextSearchService.GetIndexForLine(ActiveDocument.Text, edit.Range.End.Line + 1) + edit.Range.End.Character;
            startIndex = Math.Clamp(startIndex, 0, ActiveDocument.Text.Length);
            endIndex = Math.Clamp(endIndex, startIndex, ActiveDocument.Text.Length);
            ActiveDocument.Text = ActiveDocument.Text[..startIndex] + edit.NewText + ActiveDocument.Text[endIndex..];
            CaretMoveRequested?.Invoke(this, (startIndex + edit.NewText.Length, 0));
        }
        else if (item.InsertText is { } insertText)
        {
            var index = ActiveDocument.CaretIndex;
            ActiveDocument.Text = ActiveDocument.Text[..index] + insertText + ActiveDocument.Text[index..];
            CaretMoveRequested?.Invoke(this, (index + insertText.Length, 0));
        }

        CompletionItems.Clear();
        // ActiveDocument.Text just changed, which already re-triggers the debounced didChange
        // via OnDocumentPropertyChanged — the language server's document state stays in sync,
        // never left stale after applying an edit (SKILL.md §28).
    }

    [RelayCommand]
    private async Task RequestHoverAsync()
    {
        if (ActiveDocument?.FilePath is not { } path) { HoverResult = null; return; }
        HoverResult = await _languageService.HoverAsync(path, CurrentCaretPosition(ActiveDocument)).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task GoToDefinitionAsync()
    {
        if (ActiveDocument?.FilePath is not { } path) return;

        var locations = await _languageService.DefinitionAsync(path, CurrentCaretPosition(ActiveDocument)).ConfigureAwait(true);
        var location = locations.FirstOrDefault();
        if (location is null)
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.NoDefinitionFound.Title"), _localizationService.GetString("Dialog.NoDefinitionFound.Message")).ConfigureAwait(true);
            return;
        }

        await OpenFileAsync(location.FilePath).ConfigureAwait(true);
        if (ActiveDocument is null) return;

        var lineStartIndex = TextSearchService.GetIndexForLine(ActiveDocument.Text, location.Range.Start.Line + 1);
        var index = Math.Min(lineStartIndex + location.Range.Start.Character, ActiveDocument.Text.Length);
        CaretMoveRequested?.Invoke(this, (index, 0));
    }

    [RelayCommand]
    private async Task RestartLanguageServerAsync()
    {
        if (!CanRestartLanguageServer) return;
        var path = ActiveDocument?.FilePath ?? Documents.FirstOrDefault(d => d.FilePath is not null && _languageService.SupportsFile(d.FilePath))?.FilePath;
        if (path is null) return;

        Output.Log("Language Server", "Restarting C# language server...");
        await _languageService.RestartAsync(path).ConfigureAwait(true);
    }

    private static LspPosition CurrentCaretPosition(DocumentViewModel document) => new(document.Line - 1, document.Column - 1);

    // --- Tests (SKILL.md Phase 8) -----------------------------------------------------------

    /// <summary>Prefers the Explorer's selected project if it's a real, evidence-based test
    /// project (SKILL.md §11); otherwise falls back to the first test project anywhere in the
    /// workspace. Never assumes every project is testable.</summary>
    private ProjectInfo? ResolveTestProject()
    {
        if (Explorer.SelectedNode?.Project is { IsTestProject: true } selected) return selected;
        return _projectLookup.ProjectsByRootPath.Values.FirstOrDefault(p => p.IsTestProject);
    }

    private static BuildTarget TestTarget(ProjectInfo project) =>
        new(BuildTargetKind.Project, project.Name, project.ProjectFile ?? project.RootPath, project.RootPath, project.ProjectType);

    /// <summary>Test discovery runs a real <c>dotnet test --list-tests</c>, which performs a
    /// real build of the test project — the same class of risk Build/Run/Debug/Language's trust
    /// gates exist for (SKILL.md §21). Execution is gated the same way, at the same call site.</summary>
    private async Task<bool> EnsureTestWorkspaceTrustedAsync(string actionDescription)
    {
        if (CurrentWorkspace is not { IsTrusted: false }) return true;

        var trust = await _dialogService.ConfirmAsync(
            _localizationService.GetString("Dialog.WorkspaceNotTrusted.Title"),
            _localizationService.Format("Dialog.WorkspaceNotTrusted.TestOrGit", actionDescription)).ConfigureAwait(true);
        if (!trust) return false;

        ToggleWorkspaceTrust();
        return true;
    }

    /// <summary>Every Git operation that mutates repository state (Stage/Unstage/Discard/Commit/
    /// CheckoutBranch/CreateBranch/DeleteBranch) is gated the same way (SKILL.md §29 [Phase 9])
    /// — real Git commit/checkout can trigger repository-defined hooks, the same execution-risk
    /// class Build/Run/Debug/Test/Language's gates exist for. Read-only inspection (status/log/
    /// diff/branch listing/repository detection) is never gated, exactly like every prior
    /// phase's read-only operations.</summary>
    private async Task<bool> EnsureGitWorkspaceTrustedAsync(string actionDescription)
    {
        if (CurrentWorkspace is not { IsTrusted: false }) return true;

        var trust = await _dialogService.ConfirmAsync(
            _localizationService.GetString("Dialog.WorkspaceNotTrusted.Title"),
            _localizationService.Format("Dialog.WorkspaceNotTrusted.GitHooks", actionDescription)).ConfigureAwait(true);
        if (!trust) return false;

        ToggleWorkspaceTrust();
        return true;
    }

    /// <summary>Gates every Package Manager mutation (Add/Remove/Update/Restore) — installing or
    /// restoring a package can run that ecosystem's own arbitrary install/build scripts (npm
    /// lifecycle scripts, Python build hooks, MSBuild targets triggered by `dotnet restore`),
    /// the same execution-risk class as Build/Run/Debug/Test/Git (SKILL.md §17). Read-only
    /// inspection (installed/dependencies/outdated/search) is never gated.</summary>
    private async Task<bool> EnsurePackageWorkspaceTrustedAsync(string actionDescription)
    {
        if (CurrentWorkspace is not { IsTrusted: false }) return true;

        var trust = await _dialogService.ConfirmAsync(
            _localizationService.GetString("Dialog.WorkspaceNotTrusted.Title"),
            _localizationService.Format("Dialog.WorkspaceNotTrusted.PackageManager", actionDescription)).ConfigureAwait(true);
        if (!trust) return false;

        ToggleWorkspaceTrust();
        return true;
    }

    [RelayCommand]
    private async Task RefreshTestsAsync()
    {
        var project = ResolveTestProject();
        if (project is null)
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.NoTestProjectFound.Title"), _localizationService.GetString("Dialog.NoTestProjectFound.MessageDetailed")).ConfigureAwait(true);
            return;
        }

        if (!_testService.HasAdapterFor(project.ProjectType))
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.TestDiscoveryNotSupported.Title"), _localizationService.Format("Dialog.TestDiscoveryNotSupported.Message", project.ProjectType)).ConfigureAwait(true);
            return;
        }

        if (!await EnsureTestWorkspaceTrustedAsync(_localizationService.GetString("Trust.Action.DiscoveringTests")).ConfigureAwait(true)) return;

        Output.Log("Test", $"Discovering tests in '{project.Name}'...");
        try
        {
            await _testService.DiscoverAsync(project, SelectedBuildConfiguration).ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.TestDiscoveryAlreadyActive.Title"), ex.Message).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private Task RunAllTestsAsync() => ExecuteTestRunAsync(filter: null);

    [RelayCommand]
    private Task RunSelectedTestAsync(TestNodeViewModel? node) =>
        ExecuteTestRunAsync(node is null ? null : new TestFilter(FullyQualifiedNames: new[] { node.TestCase.FullyQualifiedName }));

    [RelayCommand]
    private void StopTests() => _testService.Cancel();

    private async Task ExecuteTestRunAsync(TestFilter? filter)
    {
        if (!CanRunTests) return;

        var project = ResolveTestProject();
        if (project is null)
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.NoTestProjectFound.Title"), _localizationService.GetString("Dialog.NoTestProjectFound.Message")).ConfigureAwait(true);
            return;
        }

        if (!_testService.HasAdapterFor(project.ProjectType))
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.TestExecutionNotSupported.Title"), _localizationService.Format("Dialog.TestExecutionNotSupported.Message", project.ProjectType)).ConfigureAwait(true);
            return;
        }

        if (!await EnsureTestWorkspaceTrustedAsync(_localizationService.GetString("Trust.Action.RunningTests")).ConfigureAwait(true)) return;

        foreach (var node in TestNodes) node.SetRunning();

        var buildOutputSink = new DelegateProcessOutputSink(line => Output.Log("Build", line), line => Output.Log("Build", line, OutputEntrySeverity.Warning));
        var testOutputSink = new DelegateProcessOutputSink(line => Output.Log("Test", line), line => Output.Log("Test", line, OutputEntrySeverity.Warning));

        Output.Log("Test", $"Running tests in '{project.Name}'...");
        try
        {
            await _testService.RunAsync(TestTarget(project), project, SelectedBuildConfiguration, filter, buildBeforeTest: true, buildOutputSink, testOutputSink).ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.TestRunAlreadyActive.Title"), ex.Message).ConfigureAwait(true);
        }
    }

    /// <summary>Only navigates when a real source location exists (SKILL.md §14) — real .NET
    /// test discovery never reports one, and only a real failure's parsed stack trace does; a
    /// passing/not-yet-run test simply has nowhere to navigate to, and none is guessed.</summary>
    [RelayCommand]
    private async Task NavigateToTestNodeAsync(TestNodeViewModel? node)
    {
        if (node?.SourceFile is not { } sourceFile || node.Line is not { } line) return;
        if (!File.Exists(sourceFile)) return;

        await OpenFileAsync(sourceFile).ConfigureAwait(true);
        if (ActiveDocument is null) return;

        var index = Math.Min(TextSearchService.GetIndexForLine(ActiveDocument.Text, line), ActiveDocument.Text.Length);
        CaretMoveRequested?.Invoke(this, (index, 0));
    }

    private static string DescribeTestRunResult(TestRunResult result)
    {
        if (result.State is TestRunState.BlockedByBuildFailure or TestRunState.Failed or TestRunState.Cancelled)
        {
            return result.Message ?? result.State.ToString();
        }

        var passed = result.Results.Count(r => r.Outcome == TestOutcome.Passed);
        var failed = result.Results.Count(r => r.Outcome == TestOutcome.Failed);
        var skipped = result.Results.Count(r => r.Outcome == TestOutcome.Skipped);
        return $"{result.Results.Count} test(s): {passed} passed, {failed} failed, {skipped} skipped.";
    }

    // ----------------------------------------------------------------------------------------

    private async Task RestoreWorkspaceStateAsync(string workspaceRootPath)
    {
        var result = await _workspaceStateStore.LoadAsync(workspaceRootPath).ConfigureAwait(true);

        if (result.WasCorrupted)
        {
            Output.Log("Workspace", "Unable to load workspace configuration. Starting with a fresh workspace state.", OutputEntrySeverity.Warning);
            return;
        }

        if (result.State is null) return;

        foreach (var documentPath in result.State.OpenDocumentPaths)
        {
            if (File.Exists(documentPath))
            {
                await OpenFileAsync(documentPath).ConfigureAwait(true);
            }
            else
            {
                Output.Log("Workspace", $"Previously open file is missing: {documentPath}", OutputEntrySeverity.Warning);
            }
        }

        if (result.State.ActiveDocumentPath is { } activePath)
        {
            var match = Documents.FirstOrDefault(d => string.Equals(d.FilePath, activePath, PathComparer.Comparison));
            if (match is not null) ActiveDocument = match;
        }

        ActiveProjectId = result.State.ActiveProjectId;
    }

    public async Task SaveWorkspaceStateAsync()
    {
        if (Explorer.RootPath is not { } rootPath) return;

        var state = new WorkspaceState(
            WorkspaceState.CurrentVersion,
            rootPath,
            Documents.Where(d => d.FilePath is not null).Select(d => d.FilePath!).ToList(),
            ActiveDocument?.FilePath,
            ActiveProjectId);

        await _workspaceStateStore.SaveAsync(rootPath, state).ConfigureAwait(true);
    }

    [RelayCommand]
    private void NewFile()
    {
        var document = new DocumentViewModel(null, "Untitled", string.Empty, TextEncodingKind.Utf8, LineEndingKind.Lf);
        Documents.Add(document);
        ActiveDocument = document;
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var path = await _filePickerService.PickFileToOpenAsync().ConfigureAwait(true);
        if (path is null) return;

        await OpenFileAsync(path).ConfigureAwait(true);
    }

    public async Task OpenFileAsync(string path)
    {
        var existing = Documents.FirstOrDefault(d => string.Equals(d.FilePath, path, PathComparer.Comparison));
        if (existing is not null)
        {
            ActiveDocument = existing;
            return;
        }

        var size = await _documentAppService.GetFileSizeAsync(path).ConfigureAwait(true);
        if (size > DocumentAppService.DefaultLargeFileThresholdBytes)
        {
            var proceed = await _dialogService
                .ConfirmAsync("Large File", $"'{Path.GetFileName(path)}' is {size / (1024 * 1024)} MB. Open anyway?")
                .ConfigureAwait(true);
            if (!proceed) return;
        }

        try
        {
            var content = await _documentAppService.OpenAsync(path).ConfigureAwait(true);
            var document = new DocumentViewModel(path, Path.GetFileName(path), content.Text, content.Encoding, content.LineEnding)
            {
                ProjectContext = _projectLookup.FindOwningProject(path)?.Name ?? "Unknown"
            };
            Documents.Add(document);
            ActiveDocument = document;
            Output.Log("Editor", $"Opened {path}");

            document.PropertyChanged += OnDocumentPropertyChanged;
            await EnsureLanguageServerAndOpenDocumentAsync(path, content.Text).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.UnableToOpenFile.Title"), _localizationService.Format("Dialog.UnableToOpenFile.Message", path, ex.Message)).ConfigureAwait(true);
        }
    }

    /// <summary>Reading/editing source code stays allowed in an untrusted workspace, but
    /// starting the language server does not: Roslyn evaluates the owning project through
    /// MSBuild to get real compiler arguments, which can execute custom SDK/target/task logic
    /// from the project file itself — the same class of risk Workspace Trust exists for (SKILL.md
    /// §13, ADR-008). The prompt is only shown once per workspace, not once per file.</summary>
    private async Task EnsureLanguageServerAndOpenDocumentAsync(string path, string text)
    {
        // A loose file opened with no workspace (e.g. File > Open on a standalone .cs file) has
        // no project context to give the language server — same as a real IDE, it just doesn't
        // get language features until a workspace is open.
        if (_languageService.WorkspaceRootPath is null) return;
        if (!_languageService.SupportsFile(path)) return;

        if (!_languageService.IsActive && CurrentWorkspace is { IsTrusted: false })
        {
            var trust = await _dialogService.ConfirmAsync(
                _localizationService.GetString("Dialog.WorkspaceNotTrusted.Title"),
                _localizationService.GetString("Dialog.WorkspaceNotTrusted.LanguageServer")).ConfigureAwait(true);
            if (!trust) return; // the file stays open as plain text; no language server starts.
            ToggleWorkspaceTrust();
        }

        await _languageService.OpenDocumentAsync(path, text).ConfigureAwait(true);
    }

    private void OnDocumentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DocumentViewModel.Text)) return;
        if (sender is not DocumentViewModel { FilePath: { } path } document) return;
        if (!_languageService.SupportsFile(path)) return;

        DebounceDocumentChange(path, document.Text);
    }

    /// <summary>Coalesces rapid keystrokes into one real <c>textDocument/didChange</c> per pause
    /// (SKILL.md §49–§50) rather than sending one per keystroke.</summary>
    private void DebounceDocumentChange(string path, string text)
    {
        if (_pendingDocumentChangeDebounce.TryGetValue(path, out var previous)) previous.Cancel();

        var cts = new CancellationTokenSource();
        _pendingDocumentChangeDebounce[path] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400), cts.Token).ConfigureAwait(false);
                await _languageService.ChangeDocumentAsync(path, text, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer edit — expected, not an error.
            }
        });
    }

    [RelayCommand]
    private Task SaveAsync() => SaveDocumentAsync(ActiveDocument);

    [RelayCommand]
    private Task SaveAsAsync() => SaveDocumentAsAsync(ActiveDocument);

    private async Task SaveDocumentAsync(DocumentViewModel? document)
    {
        if (document is null) return;

        if (document.FilePath is null)
        {
            await SaveDocumentAsAsync(document).ConfigureAwait(true);
            return;
        }

        try
        {
            await _documentAppService.SaveAsync(document.FilePath, document.Text, document.Encoding, document.LineEnding).ConfigureAwait(true);
            document.MarkSaved();
            Output.Log("Editor", $"Saved {document.FilePath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.UnableToSaveFile.Title"), _localizationService.Format("Dialog.UnableToSaveFile.Message", document.FilePath, ex.Message)).ConfigureAwait(true);
        }
    }

    private async Task SaveDocumentAsAsync(DocumentViewModel? document)
    {
        if (document is null) return;

        var path = await _filePickerService.PickFileToSaveAsAsync(document.FilePath is null ? document.DisplayName : Path.GetFileName(document.FilePath)).ConfigureAwait(true);
        if (path is null) return;

        document.FilePath = path;
        document.DisplayName = Path.GetFileName(path);
        await SaveDocumentAsync(document).ConfigureAwait(true);
    }

    /// <summary>Saves a document and reports whether it ended up clean, for callers (like the
    /// window's Closing handler) that need to know whether to abort (SKILL.md §13).</summary>
    public async Task<bool> TrySaveDocumentAsync(DocumentViewModel document)
    {
        await SaveDocumentAsync(document).ConfigureAwait(true);
        return !document.IsModified;
    }

    [RelayCommand]
    private async Task CloseDocumentAsync(DocumentViewModel? document)
    {
        document ??= ActiveDocument;
        if (document is null) return;

        if (document.IsModified)
        {
            var result = await _dialogService.AskSaveChangesAsync(document.DisplayName).ConfigureAwait(true);
            switch (result)
            {
                case SaveChangesResult.Cancel:
                    return;
                case SaveChangesResult.Save:
                    await SaveDocumentAsync(document).ConfigureAwait(true);
                    if (document.IsModified) return; // save failed or was cancelled (e.g. Save As dismissed)
                    break;
            }
        }

        document.PropertyChanged -= OnDocumentPropertyChanged;
        if (document.FilePath is not null)
        {
            _documentAppService.StopWatching(document.FilePath);
            if (_pendingDocumentChangeDebounce.TryGetValue(document.FilePath, out var pending))
            {
                pending.Cancel();
                _pendingDocumentChangeDebounce.Remove(document.FilePath);
            }
            await _languageService.CloseDocumentAsync(document.FilePath).ConfigureAwait(true);
        }

        Documents.Remove(document);
        if (ActiveDocument == document)
        {
            ActiveDocument = Documents.LastOrDefault();
        }
    }

    private void OnExternalChangeDetected(object? sender, FileChangedEventArgs e)
    {
        var document = Documents.FirstOrDefault(d => string.Equals(d.FilePath, e.FilePath, PathComparer.Comparison));
        if (document is not null)
        {
            document.HasExternalChangePending = true;
        }
    }

    [RelayCommand]
    private async Task ReloadFromDiskAsync(DocumentViewModel? document)
    {
        document ??= ActiveDocument;
        if (document?.FilePath is null) return;

        var content = await _documentAppService.OpenAsync(document.FilePath).ConfigureAwait(true);
        document.ReloadFromDisk(content.Text);
        Output.Log("Editor", $"Reloaded {document.FilePath} from disk.");
    }

    [RelayCommand]
    private void FindNext()
    {
        if (ActiveDocument is null || string.IsNullOrEmpty(FindText)) return;

        var index = TextSearchService.FindNext(ActiveDocument.Text, FindText, ActiveDocument.CaretIndex, MatchCase);
        if (index >= 0)
        {
            CaretMoveRequested?.Invoke(this, (index, FindText.Length));
        }
    }

    [RelayCommand]
    private void ReplaceAll()
    {
        if (ActiveDocument is null || string.IsNullOrEmpty(FindText)) return;

        ActiveDocument.Text = TextSearchService.ReplaceAll(ActiveDocument.Text, FindText, ReplaceText, MatchCase);
    }

    [RelayCommand]
    private void GoToLine(int lineNumber)
    {
        if (ActiveDocument is null) return;

        var index = TextSearchService.GetIndexForLine(ActiveDocument.Text, lineNumber);
        CaretMoveRequested?.Invoke(this, (index, 0));
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        var newTheme = _settingsService.Current.Theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        _settingsService.Update(_settingsService.Current with { Theme = newTheme });
        OnPropertyChanged(nameof(CurrentTheme));
        ThemeChanged?.Invoke(this, newTheme);
    }

    [RelayCommand]
    private async Task OpenTerminalAsync()
    {
        var workingDirectory = Explorer.RootPath ?? Directory.GetCurrentDirectory();
        var terminal = new TerminalViewModel(_terminalSessionFactory.Create(), workingDirectory);
        Terminals.Add(terminal);
        ActiveTerminal = terminal;
        await terminal.StartAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CloseTerminalAsync(TerminalViewModel? terminal)
    {
        terminal ??= ActiveTerminal;
        if (terminal is null) return;

        terminal.Terminate();
        Terminals.Remove(terminal);
        await terminal.DisposeAsync().ConfigureAwait(true);

        if (ActiveTerminal == terminal)
        {
            ActiveTerminal = Terminals.LastOrDefault();
        }
    }

    partial void OnActiveDocumentChanged(DocumentViewModel? oldValue, DocumentViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnActiveDocumentPropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnActiveDocumentPropertyChanged;
        }

        UpdateStatusBarFromActiveDocument();
    }

    private void OnActiveDocumentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => UpdateStatusBarFromActiveDocument();

    public void UpdateStatusBarFromActiveDocument()
    {
        if (ActiveDocument is null)
        {
            StatusBar.CurrentFileName = string.Empty;
            StatusBar.ProjectContext = string.Empty;
            StatusBar.EncodingLabel = string.Empty;
            StatusBar.LineEndingLabel = string.Empty;
            StatusBar.IsModified = false;
            return;
        }

        StatusBar.CurrentFileName = ActiveDocument.DisplayName;
        StatusBar.ProjectContext = ActiveDocument.ProjectContext;
        StatusBar.Line = ActiveDocument.Line;
        StatusBar.Column = ActiveDocument.Column;
        StatusBar.EncodingLabel = ActiveDocument.Encoding.ToString();
        StatusBar.LineEndingLabel = ActiveDocument.LineEnding.ToString();
        StatusBar.IsModified = ActiveDocument.IsModified;
    }
}
