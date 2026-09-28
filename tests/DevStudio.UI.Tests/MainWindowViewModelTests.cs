using DevStudio.Core.Build;
using DevStudio.Core.Debug;
using DevStudio.Core.Extensions;
using DevStudio.Core.Git;
using DevStudio.Core.Language;
using DevStudio.Core.Projects;
using DevStudio.Core.Run;
using DevStudio.Core.Settings;
using DevStudio.Core.Testing;
using DevStudio.UI.Localization;
using DevStudio.UI.Services;
using DevStudio.UI.Tests.Fakes;
using DevStudio.UI.ViewModels;
using Xunit;

namespace DevStudio.UI.Tests;

public class MainWindowViewModelTests
{
    private static (MainWindowViewModel ViewModel, FakeTextFileService TextFiles, FakeFilePickerService FilePicker, FakeDialogService Dialogs, FakeFolderPickerService FolderPicker, FakeTerminalSessionFactory Terminals, FakeSettingsService Settings, FakeWorkspaceStateStore WorkspaceState, FakeToolchainRegistry Toolchains, ImmediateFakeBuildAdapter BuildAdapter, FakeRunAdapter RunAdapter, FakeDebuggerAdapter DebuggerAdapter, FakeLanguageAdapter LanguageAdapter, FakeTestAdapter TestAdapter, FakeGitAdapter GitAdapter, FakeExtensionDiscovery ExtensionDiscovery) Build()
    {
        var textFiles = new FakeTextFileService();
        var watcher = new FakeFileChangeWatcher();
        var folderPicker = new FakeFolderPickerService();
        var filePicker = new FakeFilePickerService();
        var dialogs = new FakeDialogService();
        var terminals = new FakeTerminalSessionFactory();
        var settings = new FakeSettingsService();
        var workspaceState = new FakeWorkspaceStateStore();
        var workspaceScanner = new FakeWorkspaceScanner(new());
        var projectDetection = new ProjectDetectionService(workspaceScanner, Array.Empty<IProjectDetector>());
        var toolchains = new FakeToolchainRegistry();
        var visualStudio = new FakeVisualStudioDetector();
        var buildAdapter = new ImmediateFakeBuildAdapter();
        var buildService = new BuildService(new[] { buildAdapter });
        var runAdapter = new FakeRunAdapter();
        var runService = new RunService(new[] { runAdapter }, buildService);
        var debuggerAdapter = new FakeDebuggerAdapter();
        var debugService = new DebugService(new[] { debuggerAdapter }, buildService);
        var languageAdapter = new FakeLanguageAdapter();
        var languageService = new LanguageService(new[] { languageAdapter });
        var testAdapter = new FakeTestAdapter();
        var testService = new TestService(new[] { testAdapter }, buildService);
        var gitAdapter = new FakeGitAdapter();
        var gitService = new GitService(gitAdapter);
        var extensionDiscovery = new FakeExtensionDiscovery();
        var commandRegistry = new CommandRegistry();
        var extensionManager = new ExtensionManager(extensionDiscovery, new FakeExtensionLoader(), commandRegistry, Array.Empty<string>());
        var localizationService = new LocalizationService();

        var vm = new MainWindowViewModel(
            new WorkspaceAppService(workspaceScanner),
            new DocumentAppService(textFiles, watcher),
            terminals,
            folderPicker,
            filePicker,
            dialogs,
            settings,
            projectDetection,
            workspaceState,
            toolchains,
            visualStudio,
            buildService,
            runService,
            debugService,
            languageService,
            testService,
            gitService,
            extensionManager,
            commandRegistry,
            localizationService);

        return (vm, textFiles, filePicker, dialogs, folderPicker, terminals, settings, workspaceState, toolchains, buildAdapter, runAdapter, debuggerAdapter, languageAdapter, testAdapter, gitAdapter, extensionDiscovery);
    }

    [Fact]
    public void NewFileCommand_adds_an_untitled_document_and_activates_it()
    {
        var (vm, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _) = Build();

        vm.NewFileCommand.Execute(null);

        Assert.Single(vm.Documents);
        Assert.Same(vm.Documents[0], vm.ActiveDocument);
        Assert.Equal("Untitled", vm.ActiveDocument!.DisplayName);
    }

    [Fact]
    public async Task OpenFileAsync_reads_through_the_document_service_and_opens_a_tab()
    {
        var (vm, textFiles, _, _, _, _, _, _, _, _, _, _, _, _, _, _) = Build();
        textFiles.Files["C:/repo/a.cs"] = new DevStudio.Core.Editor.TextFileContent(
            "class A {}", DevStudio.Core.Editor.TextEncodingKind.Utf8, DevStudio.Core.Editor.LineEndingKind.Lf);

        await vm.OpenFileAsync("C:/repo/a.cs");

        Assert.Single(vm.Documents);
        Assert.Equal("class A {}", vm.ActiveDocument!.Text);
        Assert.Equal("C:/repo/a.cs", vm.ActiveDocument.FilePath);
    }

    [Fact]
    public async Task OpenFileAsync_reopens_the_existing_tab_instead_of_duplicating_it()
    {
        var (vm, textFiles, _, _, _, _, _, _, _, _, _, _, _, _, _, _) = Build();
        textFiles.Files["C:/repo/a.cs"] = new("content", DevStudio.Core.Editor.TextEncodingKind.Utf8, DevStudio.Core.Editor.LineEndingKind.Lf);

        await vm.OpenFileAsync("C:/repo/a.cs");
        vm.NewFileCommand.Execute(null); // switch active document away
        await vm.OpenFileAsync("C:/repo/a.cs");

        Assert.Equal(2, vm.Documents.Count);
    }

    [Fact]
    public async Task Saving_an_untitled_document_routes_through_SaveAs()
    {
        var (vm, textFiles, filePicker, _, _, _, _, _, _, _, _, _, _, _, _, _) = Build();
        filePicker.SaveAsPathToReturn = "C:/repo/new.txt";
        vm.NewFileCommand.Execute(null);
        vm.ActiveDocument!.Text = "hello";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("C:/repo/new.txt", vm.ActiveDocument.FilePath);
        Assert.False(vm.ActiveDocument.IsModified);
        Assert.Equal("hello", textFiles.LastWrittenText);
    }

    [Fact]
    public async Task CloseDocumentAsync_with_Cancel_keeps_the_modified_document_open()
    {
        var (vm, _, _, dialogs, _, _, _, _, _, _, _, _, _, _, _, _) = Build();
        dialogs.SaveChangesResultToReturn = SaveChangesResult.Cancel;
        vm.NewFileCommand.Execute(null);
        vm.ActiveDocument!.Text = "unsaved change";

        await vm.CloseDocumentCommand.ExecuteAsync(vm.ActiveDocument);

        Assert.Single(vm.Documents);
    }

    [Fact]
    public async Task CloseDocumentAsync_with_DontSave_discards_changes_and_closes()
    {
        var (vm, _, _, dialogs, _, _, _, _, _, _, _, _, _, _, _, _) = Build();
        dialogs.SaveChangesResultToReturn = SaveChangesResult.DontSave;
        vm.NewFileCommand.Execute(null);
        var document = vm.ActiveDocument!;
        document.Text = "unsaved change";

        await vm.CloseDocumentCommand.ExecuteAsync(document);

        Assert.Empty(vm.Documents);
    }

    [Fact]
    public async Task OpenFolderAsync_does_nothing_when_the_picker_is_cancelled()
    {
        var (vm, _, _, _, folderPicker, _, _, _, _, _, _, _, _, _, _, _) = Build();
        folderPicker.PathToReturn = null;

        await vm.OpenFolderCommand.ExecuteAsync(null);

        Assert.Empty(vm.Explorer.RootNodes);
    }

    [Fact]
    public async Task OpenTerminalAsync_creates_and_starts_a_terminal_session()
    {
        var (vm, _, _, _, _, terminals, _, _, _, _, _, _, _, _, _, _) = Build();

        await vm.OpenTerminalCommand.ExecuteAsync(null);

        Assert.Single(vm.Terminals);
        Assert.Single(terminals.Created);
        Assert.True(terminals.Created[0].IsRunning);
        Assert.Same(vm.Terminals[0], vm.ActiveTerminal);
    }

    [Fact]
    public void ToggleThemeCommand_flips_between_dark_and_light_and_raises_ThemeChanged()
    {
        var (vm, _, _, _, _, _, settings, _, _, _, _, _, _, _, _, _) = Build();
        Assert.Equal(AppTheme.Dark, vm.CurrentTheme);

        AppTheme? raised = null;
        vm.ThemeChanged += (_, theme) => raised = theme;

        vm.ToggleThemeCommand.Execute(null);

        Assert.Equal(AppTheme.Light, vm.CurrentTheme);
        Assert.Equal(AppTheme.Light, raised);
        Assert.Equal(AppTheme.Light, settings.Current.Theme);
    }

    [Fact]
    public void FindNextCommand_moves_the_caret_to_the_next_match()
    {
        var (vm, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _) = Build();
        vm.NewFileCommand.Execute(null);
        vm.ActiveDocument!.Text = "foo bar foo";
        vm.FindText = "foo";

        (int Start, int Length)? requested = null;
        vm.CaretMoveRequested += (_, e) => requested = e;

        vm.FindNextCommand.Execute(null);

        Assert.NotNull(requested);
        Assert.Equal(0, requested!.Value.Start);
    }

    // These use Path.GetTempPath()/Directory.GetCurrentDirectory() as workspace roots — real,
    // guaranteed-to-exist directories — because MainWindowViewModel.OpenWorkspaceAsync checks
    // Directory.Exists before doing anything else (a defensive, real filesystem check; see
    // ARCHITECTURE.md). The FakeWorkspaceScanner behind WorkspaceAppService still returns no
    // children for any path not explicitly registered with it, so nothing here touches real
    // project files.

    [Fact]
    public async Task OpenWorkspaceAsync_runs_project_detection_and_populates_the_workspace_model()
    {
        var (vm, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _) = Build();
        var root = Path.GetTempPath();

        await vm.OpenWorkspaceAsync(root);

        Assert.NotNull(vm.CurrentWorkspace);
        Assert.Equal(root, vm.CurrentWorkspace!.RootPath);
        Assert.False(vm.CurrentWorkspace.IsTrusted);
        Assert.Single(vm.Explorer.RootNodes);
    }

    [Fact]
    public async Task OpenWorkspaceAsync_records_the_workspace_in_RecentWorkspaces_most_recent_first()
    {
        var (vm, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _) = Build();
        var rootA = Path.GetTempPath();
        var rootB = Directory.GetCurrentDirectory();

        await vm.OpenWorkspaceAsync(rootA);
        await vm.OpenWorkspaceAsync(rootB);

        Assert.Equal(rootB, vm.RecentWorkspaces[0].Path);
        Assert.Equal(rootA, vm.RecentWorkspaces[1].Path);
    }

    [Fact]
    public async Task OpenWorkspaceAsync_reports_a_missing_folder_without_throwing()
    {
        var (vm, _, _, dialogs, _, _, _, _, _, _, _, _, _, _, _, _) = Build();
        var missingPath = Path.Combine(Path.GetTempPath(), "DevStudioTests_definitely_missing_" + Guid.NewGuid());

        await vm.OpenWorkspaceAsync(missingPath);

        Assert.Null(vm.CurrentWorkspace);
        Assert.Single(dialogs.ErrorsShown);
    }

    [Fact]
    public async Task ToggleWorkspaceTrustCommand_flips_trust_on_the_current_workspace()
    {
        var (vm, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _) = Build();
        await vm.OpenWorkspaceAsync(Path.GetTempPath());

        vm.ToggleWorkspaceTrustCommand.Execute(null);

        Assert.True(vm.IsWorkspaceTrusted);
        Assert.True(vm.CurrentWorkspace!.IsTrusted);
        Assert.Equal("Untrust Workspace", vm.TrustMenuLabel);
    }

    [Fact]
    public async Task SaveWorkspaceStateAsync_persists_open_documents_and_the_active_one()
    {
        var (vm, textFiles, _, _, _, _, _, workspaceState, _, _, _, _, _, _, _, _) = Build();
        var root = Path.GetTempPath();
        var docPath = Path.GetTempFileName();
        textFiles.Files[docPath] = new("a", DevStudio.Core.Editor.TextEncodingKind.Utf8, DevStudio.Core.Editor.LineEndingKind.Lf);

        try
        {
            await vm.OpenWorkspaceAsync(root);
            await vm.OpenFileAsync(docPath);

            await vm.SaveWorkspaceStateAsync();

            var saved = workspaceState.StatesByRoot[root];
            Assert.Contains(docPath, saved.OpenDocumentPaths);
            Assert.Equal(docPath, saved.ActiveDocumentPath);
        }
        finally
        {
            File.Delete(docPath);
        }
    }

    [Fact]
    public async Task OpenWorkspaceAsync_restores_previously_open_documents_from_saved_state()
    {
        var (vm, textFiles, _, _, _, _, _, workspaceState, _, _, _, _, _, _, _, _) = Build();
        var root = Path.GetTempPath();
        var docPath = Path.GetTempFileName();
        textFiles.Files[docPath] = new("saved content", DevStudio.Core.Editor.TextEncodingKind.Utf8, DevStudio.Core.Editor.LineEndingKind.Lf);
        workspaceState.StatesByRoot[root] = new DevStudio.Core.Workspace.WorkspaceState(
            DevStudio.Core.Workspace.WorkspaceState.CurrentVersion, root, new[] { docPath }, docPath, null);

        try
        {
            await vm.OpenWorkspaceAsync(root);

            Assert.Single(vm.Documents);
            Assert.Equal("saved content", vm.ActiveDocument!.Text);
        }
        finally
        {
            File.Delete(docPath);
        }
    }

    [Fact]
    public async Task OpenWorkspaceAsync_with_a_corrupted_saved_state_starts_fresh_without_throwing()
    {
        var (vm, _, _, _, _, _, _, workspaceState, _, _, _, _, _, _, _, _) = Build();
        workspaceState.ReturnCorrupted = true;

        await vm.OpenWorkspaceAsync(Path.GetTempPath());

        Assert.Empty(vm.Documents);
        Assert.NotNull(vm.CurrentWorkspace);
    }

    // --- Language Server / LSP (SKILL.md Phase 7) -------------------------------------------

    [Fact]
    public async Task Opening_a_cs_file_in_an_untrusted_workspace_prompts_for_trust_before_starting_the_language_server()
    {
        var (vm, textFiles, _, dialogs, _, _, _, _, _, _, _, _, languageAdapter, _, _, _) = Build();
        dialogs.ConfirmResultToReturn = true;
        await vm.OpenWorkspaceAsync(Path.GetTempPath());
        var path = Path.Combine(Path.GetTempPath(), "Program.cs");
        textFiles.Files[path] = new("class C {}", DevStudio.Core.Editor.TextEncodingKind.Utf8, DevStudio.Core.Editor.LineEndingKind.Lf);

        await vm.OpenFileAsync(path);

        Assert.Single(languageAdapter.StartedWorkspaces);
        Assert.True(vm.IsWorkspaceTrusted);
        Assert.Equal(LanguageServerState.Running, vm.LanguageServerState);
    }

    [Fact]
    public async Task Declining_trust_leaves_the_file_open_as_plain_text_without_starting_the_language_server()
    {
        var (vm, textFiles, _, dialogs, _, _, _, _, _, _, _, _, languageAdapter, _, _, _) = Build();
        dialogs.ConfirmResultToReturn = false;
        await vm.OpenWorkspaceAsync(Path.GetTempPath());
        var path = Path.Combine(Path.GetTempPath(), "Program.cs");
        textFiles.Files[path] = new("class C {}", DevStudio.Core.Editor.TextEncodingKind.Utf8, DevStudio.Core.Editor.LineEndingKind.Lf);

        await vm.OpenFileAsync(path);

        Assert.Empty(languageAdapter.StartedWorkspaces);
        Assert.False(vm.IsWorkspaceTrusted);
        Assert.Single(vm.Documents); // still opened as plain text
    }

    [Fact]
    public async Task Opening_a_non_cs_file_never_starts_the_language_server()
    {
        var (vm, textFiles, _, dialogs, _, _, _, _, _, _, _, _, languageAdapter, _, _, _) = Build();
        dialogs.ConfirmResultToReturn = true;
        await vm.OpenWorkspaceAsync(Path.GetTempPath());
        var path = Path.Combine(Path.GetTempPath(), "readme.md");
        textFiles.Files[path] = new("# hi", DevStudio.Core.Editor.TextEncodingKind.Utf8, DevStudio.Core.Editor.LineEndingKind.Lf);

        await vm.OpenFileAsync(path);

        Assert.Empty(languageAdapter.StartedWorkspaces);
    }

    [Fact]
    public async Task RequestCompletionCommand_populates_CompletionItems_from_the_language_server()
    {
        var (vm, textFiles, _, dialogs, _, _, _, _, _, _, _, _, _, _, _, _) = Build();
        dialogs.ConfirmResultToReturn = true;
        await vm.OpenWorkspaceAsync(Path.GetTempPath());
        var path = Path.Combine(Path.GetTempPath(), "Program.cs");
        textFiles.Files[path] = new("class C {}", DevStudio.Core.Editor.TextEncodingKind.Utf8, DevStudio.Core.Editor.LineEndingKind.Lf);
        await vm.OpenFileAsync(path);

        await vm.RequestCompletionCommand.ExecuteAsync(null);

        Assert.Empty(vm.CompletionItems); // the fake session returns no items by default; verifies the plumbing doesn't throw
    }

    [Fact]
    public async Task DiagnosticsPublished_by_the_language_server_flow_into_the_Problems_panel()
    {
        var (vm, textFiles, _, dialogs, _, _, _, _, _, _, _, _, languageAdapter, _, _, _) = Build();
        dialogs.ConfirmResultToReturn = true;
        await vm.OpenWorkspaceAsync(Path.GetTempPath());
        var path = Path.Combine(Path.GetTempPath(), "Program.cs");
        textFiles.Files[path] = new("class C {}", DevStudio.Core.Editor.TextEncodingKind.Utf8, DevStudio.Core.Editor.LineEndingKind.Lf);
        await vm.OpenFileAsync(path);

        languageAdapter.CreatedSessions[0].RaiseDiagnostics(path, new[]
        {
            new DevStudio.Core.Diagnostics.Diagnostic(DevStudio.Core.Diagnostics.DiagnosticSeverity.Warning, "IDE0002", "simplify", path, 1, 1, DevStudio.Core.Diagnostics.DiagnosticSource.LanguageServer),
        });

        var diagnostic = Assert.Single(vm.Problems.Diagnostics);
        Assert.Equal(DevStudio.Core.Diagnostics.DiagnosticSource.LanguageServer, diagnostic.Source);
    }

    [Fact]
    public async Task RestartLanguageServerCommand_stops_the_old_session_and_starts_a_new_one()
    {
        var (vm, textFiles, _, dialogs, _, _, _, _, _, _, _, _, languageAdapter, _, _, _) = Build();
        dialogs.ConfirmResultToReturn = true;
        await vm.OpenWorkspaceAsync(Path.GetTempPath());
        var path = Path.Combine(Path.GetTempPath(), "Program.cs");
        textFiles.Files[path] = new("class C {}", DevStudio.Core.Editor.TextEncodingKind.Utf8, DevStudio.Core.Editor.LineEndingKind.Lf);
        await vm.OpenFileAsync(path);
        var firstSession = languageAdapter.CreatedSessions[0];

        await vm.RestartLanguageServerCommand.ExecuteAsync(null);

        Assert.True(firstSession.ShutdownCalled);
        Assert.Equal(2, languageAdapter.StartedWorkspaces.Count);
        Assert.Equal(LanguageServerState.Running, vm.LanguageServerState);
    }

    [Fact]
    public void AvailableLanguages_exposes_exactly_en_US_and_zh_TW_with_native_display_names()
    {
        var (vm, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _) = Build();

        Assert.Equal(2, vm.AvailableLanguages.Count);
        Assert.Contains(vm.AvailableLanguages, l => l.Code == "en-US" && l.DisplayName == "English");
        Assert.Contains(vm.AvailableLanguages, l => l.Code == "zh-TW" && l.DisplayName == "繁體中文");
    }

    [Fact]
    public void SelectedLanguageOption_round_trips_through_settings_and_the_real_localization_service()
    {
        var (vm, _, _, _, _, _, settings, _, _, _, _, _, _, _, _, _) = Build();
        Assert.Equal("en-US", vm.SelectedLanguageOption!.Code);

        vm.SelectedLanguageOption = vm.AvailableLanguages.Single(l => l.Code == "zh-TW");

        Assert.Equal("zh-TW", settings.Current.Language);
        Assert.Equal("zh-TW", vm.Loc.CurrentCulture.Name);
        Assert.Equal("_檔案", vm.Loc.GetString("Menu.File"));

        vm.SelectedLanguageOption = vm.AvailableLanguages.Single(l => l.Code == "en-US");

        Assert.Equal("en-US", settings.Current.Language);
        Assert.Equal("_File", vm.Loc.GetString("Menu.File"));
    }
}
