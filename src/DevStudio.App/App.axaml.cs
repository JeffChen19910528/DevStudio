using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DevStudio.App.Services;
using DevStudio.App.Views;
using DevStudio.Core.Build;
using DevStudio.Core.Debug;
using DevStudio.Core.Extensions;
using DevStudio.Core.Git;
using DevStudio.Core.Language;
using DevStudio.Core.Projects;
using DevStudio.Core.Run;
using DevStudio.Core.Terminal;
using DevStudio.Core.Testing;
using DevStudio.Core.Toolchains;
using DevStudio.Infrastructure.Build;
using DevStudio.Infrastructure.Debug;
using DevStudio.Infrastructure.Editor;
using DevStudio.Infrastructure.Extensions;
using DevStudio.Infrastructure.Git;
using DevStudio.Infrastructure.Language;
using DevStudio.Infrastructure.Processes;
using DevStudio.Infrastructure.Projects;
using DevStudio.Infrastructure.Run;
using DevStudio.Infrastructure.Settings;
using DevStudio.Infrastructure.Terminal;
using DevStudio.Infrastructure.Testing;
using DevStudio.Infrastructure.Toolchains;
using DevStudio.Infrastructure.Workspace;
using DevStudio.UI.Localization;
using DevStudio.UI.Services;
using DevStudio.UI.ViewModels;

namespace DevStudio.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Composition root (SKILL.md §6 [MVVM]): concrete Infrastructure implementations of
            // the Core abstractions are wired here, and nowhere else. ViewModels only ever see
            // the Core interfaces / DevStudio.UI.Services abstractions.
            var processRunner = new ProcessRunner();
            var workspaceScanner = new WorkspaceScanner();
            var textFileService = new TextFileService();
            var fileChangeWatcher = new FileChangeWatcher();
            var settingsService = new InMemorySettingsService();
            var userSettingsStore = new JsonUserSettingsStore();
            var workspaceStateStore = new JsonWorkspaceStateStore();
            ITerminalSessionFactory terminalSessionFactory = new TerminalSessionFactory(processRunner);

            var projectDetectors = new IProjectDetector[]
            {
                new DotNetSolutionDetector(),
                new DotNetProjectDetector(),
                new CMakeProjectDetector(),
                new NodeProjectDetector(),
                new PythonProjectDetector(),
                new JavaProjectDetector(),
                new RustProjectDetector(),
                new GoProjectDetector(),
            };
            var projectDetectionService = new ProjectDetectionService(workspaceScanner, projectDetectors);

            var visualStudioDetector = new VisualStudioDetector(processRunner);
            var toolchainRegistry = new ToolchainRegistry(new IToolchainDetector[]
            {
                new DotNetToolchainDetector(processRunner),
                new MsvcToolchainDetector(visualStudioDetector),
                new PythonToolchainDetector(processRunner),
                new NodeToolchainDetector(processRunner),
                new NodePackageManagerToolchainDetector(processRunner, WellKnownToolchainIds.Npm, "npm"),
                new NodePackageManagerToolchainDetector(processRunner, WellKnownToolchainIds.Pnpm, "pnpm"),
                new NodePackageManagerToolchainDetector(processRunner, WellKnownToolchainIds.Yarn, "yarn"),
                new JavaToolchainDetector(processRunner),
                new CMakeToolchainDetector(processRunner),
                new GccToolchainDetector(processRunner),
                new ClangToolchainDetector(processRunner),
                new RustToolchainDetector(processRunner),
                new GoToolchainDetector(processRunner),
                new GitToolchainDetector(processRunner),
                new DockerToolchainDetector(processRunner),
            });

            var buildService = new BuildService(new IBuildAdapter[]
            {
                new DotNetBuildAdapter(processRunner, toolchainRegistry),
            });

            var runService = new RunService(new IRunAdapter[]
            {
                new DotNetRunAdapter(processRunner, toolchainRegistry),
            }, buildService);

            var debugService = new DebugService(new IDebuggerAdapter[]
            {
                new NetCoreDebuggerAdapter(processRunner, new NetCoreDebuggerResolver()),
            }, buildService);

            var languageService = new LanguageService(new ILanguageAdapter[]
            {
                new CSharpLanguageAdapter(processRunner, new RoslynLanguageServerResolver()),
            });

            var testService = new TestService(new ITestAdapter[]
            {
                new DotNetTestAdapter(processRunner, toolchainRegistry),
            }, buildService);

            var gitService = new GitService(new GitCliAdapter(processRunner, toolchainRegistry));

            // Loading the small user-settings JSON file synchronously at startup keeps the
            // composition root simple; the file is a few hundred bytes at most.
            var persistedSettings = userSettingsStore.LoadAsync().GetAwaiter().GetResult();
            if (persistedSettings is not null)
            {
                settingsService.Update(persistedSettings);
            }
            settingsService.Changed += (_, settings) => _ = userSettingsStore.SaveAsync(settings);

            // A controlled, bounded extension root (SKILL.md §10) — never the whole filesystem,
            // never a workspace-local directory, never scanned recursively beyond one level.
            var extensionsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DevStudio",
                "extensions");
            var commandRegistry = new CommandRegistry();
            var extensionManager = new ExtensionManager(
                new FileSystemExtensionDiscovery(),
                new AssemblyLoadContextExtensionLoader(),
                commandRegistry,
                new[] { extensionsRoot },
                settingsService.Current.DisabledExtensionIds);

            var localizationService = new LocalizationService();

            var window = new MainWindow();

            IFolderPickerService folderPickerService = new FolderPickerService(window, localizationService);
            IFilePickerService filePickerService = new FilePickerService(window, localizationService);
            IDialogService dialogService = new DialogService(window, localizationService);

            var workspaceAppService = new WorkspaceAppService(workspaceScanner);
            var documentAppService = new DocumentAppService(textFileService, fileChangeWatcher);

            var viewModel = new MainWindowViewModel(
                workspaceAppService,
                documentAppService,
                terminalSessionFactory,
                folderPickerService,
                filePickerService,
                dialogService,
                settingsService,
                projectDetectionService,
                workspaceStateStore,
                toolchainRegistry,
                visualStudioDetector,
                buildService,
                runService,
                debugService,
                languageService,
                testService,
                gitService,
                extensionManager,
                commandRegistry,
                localizationService);

            window.DataContext = viewModel;
            window.ApplyTheme(viewModel.CurrentTheme);
            viewModel.ThemeChanged += (_, theme) => window.ApplyTheme(theme);
            window.SetWorkspaceStateSaveHook(() => viewModel.SaveWorkspaceStateAsync());

            desktop.MainWindow = window;

            // Never runs a project-defined command (SKILL.md §27): this only re-runs the same
            // read-only workspace-open pipeline a user click would trigger, against the most
            // recently opened workspace, and only when the user has opted in.
            if (settingsService.Current.ReopenLastWorkspaceOnStartup
                && settingsService.Current.RecentWorkspaces.FirstOrDefault() is { } lastWorkspace
                && Directory.Exists(lastWorkspace.Path))
            {
                _ = viewModel.OpenWorkspaceAsync(lastWorkspace.Path);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
