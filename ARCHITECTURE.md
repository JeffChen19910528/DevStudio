# DevStudio Architecture

Status: Phase 11 (Cross-Platform Support) complete. This document reflects the
architecture actually scaffolded so far, not the full long-term vision — see `SKILL.md` for the
complete specification this project follows.

## Guiding Principle

DevStudio is an **orchestrator**, not a replacement for compilers, debuggers, or build systems
(SKILL.md §47):

```
DevStudio UI/UX
      │
   IDE Core (Workspace / Project / Build / Run / Debug / Test / Diagnostics)
      │
 ┌────┴────┬─────────┐
 Toolchain Debugger   LSP
 Adapter   Adapter    Adapter
      │        │        │
 GCC/MSVC/  GDB/LLDB/  Language
 .NET/...   vsdbg/...  Servers
```

Nothing in `DevStudio.Core` may depend on a specific language, compiler, debugger, or build
tool. All of that lives behind the adapter interfaces below and is implemented in later phases.

## Technology

.NET + Avalonia, chosen in `docs/adr/ADR-001-ui-framework.md`. Phase 1's specific UI dependencies
(CommunityToolkit.Mvvm; a plain `TextBox` editor instead of AvaloniaEdit; `Grid`/`GridSplitter`
instead of a docking library) are justified in `docs/adr/ADR-002-phase1-ui-dependencies.md`.
Phase 2's workspace/settings persistence format and file locations are justified in
`docs/adr/ADR-003-workspace-persistence.md`. Phase 3's toolchain-detector/registry/capability
architecture is justified in `docs/adr/ADR-004-toolchain-detection.md`. Phase 4's Build System
architecture (including the per-request output-encoding decision) is justified in
`docs/adr/ADR-005-build-system.md`. Phase 5's Run System architecture (including the
`dotnet run --no-build` decision and the Exited-vs-Terminated distinction) is justified in
`docs/adr/ADR-006-run-system.md`. Phase 6's Debug System architecture (the DAP transport, the
real debugger discovery finding, and the verified handshake ordering) is justified in
`docs/adr/ADR-007-debug-system.md`. Phase 7's LSP architecture (what was shared with DAP vs. kept
separate, the real Roslyn language server discovery, and the `--autoLoadProjects`/pull-diagnostics
findings) is justified in `docs/adr/ADR-008-lsp-language-intelligence.md`. Phase 8's Test Explorer
architecture (the `--list-tests`/TRX discovery and result strategy, the Cancelled-vs-Failed bug
found and fixed via real testing, and which test frameworks were actually real-environment tested)
is justified in `docs/adr/ADR-009-test-explorer-and-runner.md`. Phase 9's Git integration
architecture (`GitService`'s real per-repository — not global — single-flight deviation, the
NUL-delimited porcelain v2/log separator-framed parsing strategies, and the real rename-record
parsing bug found and fixed via testing) is justified in `docs/adr/ADR-010-git-integration.md`.
Phase 10's Extension System architecture (the manifest/discovery/lifecycle/capability model, why
collectible `AssemblyLoadContext` loading is explicitly not a security sandbox, and the structural
— not merely policy — reason an extension cannot bypass Workspace Trust) is justified in
`docs/adr/ADR-011-extension-system.md`. Phase 11's cross-platform audit (what was already correct
from Phase 0 onward, the one real case-sensitivity bug found and centralized behind
`Core.Platform.PathComparer`, and the real Windows/Linux validation results) is justified in
`docs/adr/ADR-012-cross-platform-support.md`. `DevStudio.Core` (and every service/adapter library)
is a plain class library with no UI dependency, so it is usable from both the Avalonia shell and
a future `devstudio` CLI (SKILL.md §33) without duplicating logic.

## Repository Layout (current)

```
DevStudio.slnx
src/
  DevStudio.Core/            Domain models + adapter interfaces, no I/O, no UI dependency
    Adapters/                 IProjectAdapter (the unreferenced Phase 0 IDebuggerAdapter/Breakpoint stubs were removed in Phase 6 — see ADR-007 — the equally-unreferenced ILanguageAdapter stub was removed in Phase 7 — see ADR-008 — and the equally-unreferenced ITestAdapter stub was removed in Phase 8 — see ADR-009)
    Build/                     BuildOperation, BuildStatus, BuildTarget, BuildRequest, BuildResult, IBuildAdapter, BuildService, MsBuildDiagnosticParser
    Dap/                       DapProtocolMessage/DapRequest/DapResponse/DapEvent, DapMessageSerializer, IDapTransport, DapClient
    Debug/                     DebugSessionState, Breakpoint, BreakpointVerification, ThreadInfo, StackFrameInfo, Scope, Variable, StoppedInfo, DebugConfiguration, DebugResult, IDebuggerAdapter, IActiveDebugSession, DebugService
    Diagnostics/               Diagnostic, DiagnosticSeverity, DiagnosticSource
    Editor/                    ITextFileService, TextEncodingKind, LineEndingKind, IFileChangeWatcher
    Errors/                    DevStudioErrorKind, DevStudioException
    Extensions/                ExtensionId, ExtensionVersion/ExtensionVersionRange/ExtensionHostInfo, ExtensionManifest/ExtensionManifestParser/ExtensionContributions/ExtensionCommandContribution/ExtensionCapability, ExtensionState/ExtensionDescriptor, IDevStudioExtension/IExtensionContext/IExtensionCommandRegistrar, ICommandRegistry/CommandRegistry, IExtensionDiscovery/IExtensionLoader/ILoadedExtension, ExtensionManager
    Git/                       GitChangeType, GitFileStatus, GitRepositoryStatus, GitBranch, GitCommit, GitDiffLineKind/GitDiffLine/GitDiffHunk/GitDiff, GitErrorKind, GitOperationResult, IGitAdapter, GitService
    Language/                  LanguageServerState, LspPosition/LspRange/LspLocation, CompletionItem/LspTextEdit, HoverResult, LanguageServerResolution, ILanguageAdapter, ILanguageServerSession, LanguageService
    Lsp/                       JsonRpcMessage/JsonRpcRequest/JsonRpcResponse/JsonRpcNotification/JsonRpcError, JsonRpcMessageSerializer, IJsonRpcTransport, JsonRpcClient
    Packages/                  PackageReference/PackageVersion/PackageDependency/PackageSource/PackageProject, PackageManagerCapabilities, PackageOperation/PackageOperationResult, PackageSearchResult, WellKnownPackageManagerIds, IPackageManagerAdapter + IPackageInspector/IPackageSearcher/IPackageInstaller/IPackageRemover/IPackageUpdater/IPackageSourceManager, PackageManagerRegistry, PackageService
    Platform/                  PathComparer (the one centralized cross-platform case-sensitivity fact — SKILL.md §10 [Phase 11])
    Processes/                 IProcessRunner, ProcessStartRequest (incl. OutputEncoding, RawStdio), ProcessResult, DelegateProcessOutputSink
    Projects/                  ProjectInfo (incl. IsExecutable, IsTestProject), SolutionInfo, ProjectType, IProjectDetector, ProjectDetectionService, WorkspaceProjectGraph
    Rpc/                       RpcFramingException (shared by Dap/ and Lsp/'s Infrastructure framing wrappers)
    Run/                       RunStatus, RunConfiguration, RunResult, IRunAdapter, IRunningApplication, RunService
    Settings/                  AppSettings, AppTheme, ISettingsService, IUserSettingsStore, RecentWorkspaceEntry
    Terminal/                  ITerminalSession, ITerminalSessionFactory
    Testing/                   TestOutcome, TestRunState, TestCase, TestResult, TestRunResult, TestFilter, ITestAdapter, TestService
    Toolchains/                ToolchainInfo, ToolchainDetectionState, ToolchainCapability, IToolchainDetector, IToolchainRegistry, VisualStudioInstance, IVisualStudioDetector, WellKnownToolchainIds, ToolchainRequirements, CapabilityAvailability, ProjectCapability, ProjectCapabilityMatcher, EnvironmentSnapshot
    Workspace/                 WorkspaceModel, BuildConfiguration, IWorkspaceScanner, FileSystemNode, WorkspaceExclusionRules, WorkspaceState, IWorkspaceStateStore
  DevStudio.Infrastructure/  Concrete, platform-touching implementations of the Core abstractions
    Processes/                 ProcessRunner (System.Diagnostics.Process, executable+args only)
    Workspace/                 WorkspaceScanner (one directory level at a time), JsonWorkspaceStateStore
    Editor/                    TextFileService (encoding/line-ending detection), FileChangeWatcher (FileSystemWatcher)
    Terminal/                  TerminalSession, TerminalSessionFactory, ShellLocator
    Settings/                  InMemorySettingsService, JsonUserSettingsStore
    Projects/                  DotNetProjectDetector, DotNetSolutionDetector, CMakeProjectDetector, NodeProjectDetector, PythonProjectDetector, JavaProjectDetector, RustProjectDetector, GoProjectDetector, ConfigFileReading
    Toolchains/                ToolchainProbe, ExecutableLocator, ToolchainRegistry, VisualStudioDetector, MsvcToolchainDetector, DotNetToolchainDetector, PythonToolchainDetector, NodeToolchainDetector, NodePackageManagerToolchainDetector, JavaToolchainDetector, MavenToolchainDetector, GradleToolchainDetector, CMakeToolchainDetector, GccToolchainDetector, ClangToolchainDetector, RustToolchainDetector, GoToolchainDetector, VcpkgToolchainDetector, ConanToolchainDetector, GitToolchainDetector, DockerToolchainDetector
    Build/                     DotNetBuildAdapter
    Run/                       DotNetRunAdapter, DotNetRunningApplication
    Dap/                       DapFrameReader, DapFrameWriter, StreamDapTransport (the framing classes now delegate to Rpc/)
    Debug/                     NetCoreDebuggerResolver, NetCoreDebuggerAdapter, NetCoreDebugSession
    Rpc/                       ContentLengthFrameReader, ContentLengthFrameWriter (shared Content-Length framing, extracted from Dap/ in Phase 7)
    Lsp/                       StreamJsonRpcTransport
    Language/                  RoslynLanguageServerResolver, CSharpLanguageAdapter, RoslynLanguageServerSession
    Testing/                   DotNetTestAdapter
    Git/                       GitCliAdapter
    Extensions/                FileSystemExtensionDiscovery, AssemblyLoadContextExtensionLoader
    Packages/                  NuGetPackageAdapter, PythonPackageAdapter (incl. PythonDependencyStyle detection), NpmPackageAdapter, MavenPackageAdapter, GradlePackageAdapter, CargoPackageAdapter, GoModulePackageAdapter, VcpkgPackageAdapter, ConanPackageAdapter
  DevStudio.UI/              ViewModels + application services; Avalonia + CommunityToolkit.Mvvm, no other platform dependency
    ViewModels/                MainWindowViewModel, WorkspaceExplorerViewModel, FileTreeNodeViewModel, DocumentViewModel, TerminalViewModel, OutputPanelViewModel, ProblemsPanelViewModel, ToolchainsPanelViewModel, StatusBarViewModel, ProjectGraphLookup, TestNodeViewModel, SourceControlViewModel, ExtensionsPanelViewModel, PackageManagerViewModel
    Services/                  WorkspaceAppService, DocumentAppService, TextSearchService, IFolderPickerService, IFilePickerService, IDialogService
  DevStudio.App/             Avalonia executable: composition root, Views, Program.cs
    Views/                     MainWindow.axaml(.cs), DialogWindow.axaml(.cs)
    Services/                  FolderPickerService, FilePickerService, DialogService (Avalonia-specific implementations of the DevStudio.UI service interfaces)
extensions/
  DevStudio.SampleExtension/ A real, separately-compiled sample extension proving the Phase 10 lifecycle: SampleExtension (harmless sample.hello command), ActivationFailingExtension/CommandFailingExtension (real-integration-test-only failure fixtures)
tests/
  DevStudio.Core.Tests/       xUnit tests for Core models + ProjectDetectionService (with local fake detectors) + ProjectCapabilityMatcher
  DevStudio.Infrastructure.Tests/  Integration tests against real temp directories/processes, incl. real-filesystem project/solution/monorepo detection, workspace/settings persistence round-trips, real toolchain/Visual Studio detection, real .NET build/rebuild/clean/restore/cancellation, real .NET run/stop/restart/exit-code/build-before-run/run-without-build, byte-level DAP/JSON-RPC framing against adversarial-chunked streams, a real netcoredbg debug session (breakpoint hit, locals, call stack, stepping, stop), a real Roslyn language server session (diagnostics incl. unsaved edits, completion, hover, definition, shutdown, restart), real `dotnet test` discovery/execution (passing/failing/skipped outcomes, single/selected/filtered runs, real cancellation with no orphan process, build-failure blocking, multi-project test-project isolation) against temporary xUnit projects, real Git repository detection/status/stage/unstage/discard/diff/commit/branch-create/checkout/delete against temporary `git init`-created repositories, incl. Unicode filenames and checkout/branch-deletion safety, and a real extension lifecycle (discovery/validation/load/activate/command-registration/invocation/deactivation, plus real activation/command failure isolation, duplicate-id rejection, incompatible-host-version rejection, and path-traversal rejection) against the real, separately-compiled `extensions/DevStudio.SampleExtension` assembly (nothing here touches the DevStudio repo itself)
  DevStudio.UI.Tests/         ViewModel/service tests using in-memory fakes (SKILL.md §40)
docs/
  adr/                        Architecture Decision Records
```

SKILL.md §5's target tree lists `DevStudio.Toolchains` as its own top-level project; Phase 3
instead added toolchain detection as a `Toolchains/` namespace inside the existing
`DevStudio.Infrastructure`, matching how project detectors were added to that same project in
Phase 2 rather than a separate `DevStudio.Projects`. Both are process-touching, Core-interface-
implementing detector collections with no reason to be physically separate assemblies yet;
splitting them out becomes worth it if/when extension loading (Phase 10) needs to load toolchain
or project detectors independently of the rest of Infrastructure.

Later phases still split out `DevStudio.Editor` (real syntax highlighting/LSP-backed editing —
currently just `Core.Editor`/`Infrastructure.Editor`), `DevStudio.Build`, `DevStudio.Debug`,
`DevStudio.Testing`, `DevStudio.Languages`, `DevStudio.Packaging`, and `DevStudio.Extensions`,
following SKILL.md §5 and the phase order in §38. `DevStudio.Git` (SKILL.md §5's target tree)
was likewise added as a `Git/` namespace inside the existing `Core`/`Infrastructure` projects in
Phase 9, matching the same precedent as `Toolchains/`/`Testing/` above.

## MVVM Boundary (Phase 1)

```
View (DevStudio.App/Views/*.axaml)
  ↓ data binding only — no file IO, process execution, or workspace logic
ViewModel (DevStudio.UI/ViewModels/*)
  ↓
Application Service (DevStudio.UI/Services/WorkspaceAppService, DocumentAppService)
  ↓
Core abstraction (DevStudio.Core/{Workspace,Editor,Terminal,Processes}/I*)
  ↓
Infrastructure (DevStudio.Infrastructure/* — the only layer touching System.IO/System.Diagnostics directly)
```

`DevStudio.App`'s `App.axaml.cs` is the single composition root: it is the only place that
constructs concrete `DevStudio.Infrastructure` types and hands them to `DevStudio.UI`
ViewModels/services through the Core interfaces. Two things Avalonia data binding cannot express
are bridged with plain C# events rather than being pushed into the ViewModel: moving the editor's
caret/selection for Find/Replace/Go To Line (`MainWindowViewModel.CaretMoveRequested`), and
applying a theme to the `Application` object (`MainWindowViewModel.ThemeChanged`) — both handled
in `MainWindow.axaml.cs`, which otherwise contains no business logic.

## Core Interfaces (this phase)

- **`IProcessRunner`** (`Processes/`): the *only* sanctioned way to launch an external tool.
  Executable + argument array, explicit working directory/environment, cancellation, timeout,
  and process-tree kill — never a raw shell command string (SKILL.md §12).
- **`IToolchainDetector` / `IToolchainRegistry`** (`Toolchains/`): read-only detection of
  installed toolchains (SKILL.md §9, §10, §44). No detector may install software or execute
  project-defined commands. Implemented since Phase 3 — see the Toolchain Detection Architecture
  section below.
- **`IProjectAdapter`**: recognizes project marker files and produces a `ProjectInfo` (SKILL.md
  §8–§9). Still unimplemented.
- **`IBuildAdapter`** (`Build/`): runs one build operation (`Restore`/`Clean`/`Build`/`Rebuild`)
  for a target via `BuildService → IBuildAdapter → IProcessRunner → external process` (SKILL.md
  §3–§4). Redesigned and implemented in Phase 4 — the Phase 0 version (`BuildOperation`/
  `BuildOutcome` under `Adapters/`) was never referenced anywhere and was replaced outright; see
  the Build System Architecture section below and `docs/adr/ADR-005-build-system.md`.
- **`IRunAdapter`** (`Run/`): launches one runnable target via
  `RunService → IRunAdapter → IProcessRunner → real application` (SKILL.md §7, §10). Implemented
  in Phase 5 — see the Run System Architecture section below and
  `docs/adr/ADR-006-run-system.md`.
- **`ITestAdapter`** (`Testing/`): discovers and runs tests via the real .NET test infrastructure
  through `TestService → ITestAdapter → DotNetTestAdapter → IProcessRunner → real dotnet test`
  (SKILL.md §7–§10 [Phase 8]). Implemented in Phase 8 — see the Test Explorer Architecture section
  below and `docs/adr/ADR-009-test-explorer-and-runner.md`. (This superseded and replaced the
  Phase 0 `Adapters.ITestAdapter` stub, which was never referenced anywhere.)
- **`IDebuggerAdapter`** (`Debug/`): drives one real debugger through DAP via
  `DebugService → IDebuggerAdapter → DAP Client → real debugger → real application` (SKILL.md
  §2). Implemented in Phase 6 — see the Debug System Architecture section below and
  `docs/adr/ADR-007-debug-system.md`. (This superseded and replaced the Phase 0
  `Adapters.IDebuggerAdapter` stub, which was never referenced anywhere.)
- **`ILanguageAdapter`** (`Language/`): drives one real language server over LSP via
  `LanguageService → ILanguageAdapter → JsonRpcClient → real language server` (SKILL.md §8–§9).
  Implemented in Phase 7 — see the LSP / Language Intelligence Architecture section below and
  `docs/adr/ADR-008-lsp-language-intelligence.md`. (This superseded and replaced the Phase 0
  `Adapters.ILanguageAdapter` stub — a handful of properties with no lifecycle — which was never
  referenced anywhere.)
- **`IGitAdapter`** (`Git/`): drives the real `git` executable via
  `GitService → IGitAdapter → GitCliAdapter → IProcessRunner → real git` (SKILL.md §2, §6
  [Phase 9]). Implemented in Phase 9 — see the Git Integration Architecture section below and
  `docs/adr/ADR-010-git-integration.md`. There was no Phase 0 `Adapters.IGitAdapter` stub to
  supersede — Git integration is entirely new this phase.
- **`Diagnostic`**: the single diagnostic shape used across compiler/linter/LSP/debugger/test
  output, enabling uniform click-to-navigate error handling (SKILL.md §34).
- **`DevStudioException` / `DevStudioErrorKind`**: classified errors so the UI can render
  actionable messages instead of "Operation failed." (SKILL.md §31).
- **`WorkspaceModel` / `BuildConfiguration`**: the workspace/build-config shape from SKILL.md §8,
  including an explicit `IsTrusted` flag for Workspace Trust (§26). `ProjectInfo` moved to
  `Projects/` in Phase 2 — see below.

`IProjectAdapter` still has no concrete implementation — that remains a later phase per SKILL.md
§38 (distinct from `IProjectDetector`, which is fully implemented — see the Project Detection
Architecture section below). `IToolchainDetector`/`IToolchainRegistry` were implemented in
Phase 3; `IBuildAdapter` (.NET only) in Phase 4 — see below. Phase 1 added and implemented three
more Core abstractions that the shell itself needs:

- **`IWorkspaceScanner`** (`Workspace/`): lists one directory level at a time — never the whole
  tree — so the Explorer stays responsive on large repositories (SKILL.md §26). Implemented by
  `Infrastructure.Workspace.WorkspaceScanner`.
- **`ITextFileService`** (`Editor/`): reads/writes text files, detecting and preserving encoding
  (UTF-8/UTF-8 BOM/UTF-16) and line endings (LF/CRLF/Mixed) rather than normalizing them away
  (SKILL.md §11 editor requirements, §12 [encoding]). Implemented by
  `Infrastructure.Editor.TextFileService`.
- **`IFileChangeWatcher`** (`Editor/`): reports when an open file changes on disk outside
  DevStudio, so the shell can prompt instead of silently reloading or overwriting (SKILL.md
  §12 [external changes]). Implemented by `Infrastructure.Editor.FileChangeWatcher`
  (`System.IO.FileSystemWatcher`).
- **`ITerminalSession`/`ITerminalSessionFactory`** (`Terminal/`): a real shell child process
  with piped stdin/stdout, built strictly on top of `IProcessRunner` (SKILL.md §14–§15, §24).
  There is no ConPTY/pty allocation — see Known Limitations in the Phase 1 report for what that
  means in practice. Implemented by `Infrastructure.Terminal.TerminalSession`/
  `TerminalSessionFactory`, with per-platform shell selection in `Infrastructure.Terminal.ShellLocator`.

## Project Detection Architecture (Phase 2)

`IProjectDetector` (SKILL.md §2) is deliberately narrow: given one directory's immediate file
names, recognize whether a known ecosystem (`.NET`, CMake/Make/Meson, Node, Python, Java/Gradle,
Rust, Go) lives there, and optionally read that ecosystem's config file content (defensively —
see `Infrastructure.Projects.ConfigFileReading`, bounded to 64 KB, never throwing) to pull out a
real project name. It never invokes the ecosystem's own tooling — no `dotnet`, `npm`, `cmake`,
`pip`, `mvn`/`gradle`, `cargo`, or `go` process is ever started during detection (SKILL.md §4,
§19); whether those tools are even installed is Phase 3's concern, and `ProjectInfo.Capabilities`
is deliberately left empty until then rather than guessed at.

`ProjectDetectionService` (`DevStudio.Core.Projects`) is the orchestrator:

1. Walks the workspace via the same `IWorkspaceScanner` the Explorer uses — one directory level
   at a time, exclusion-aware, depth-bounded (6 levels) — so a huge repository doesn't get fully
   indexed just to find projects (SKILL.md §30).
2. Runs every registered `IProjectDetector` against each directory's file names.
3. Resolves solution references in a second pass: a solution detector (e.g.
   `DotNetSolutionDetector`) can only see its own directory, so it returns a
   `RawSolutionDetection` carrying *resolved absolute paths*, not project ids; the service then
   matches each path against every `ProjectInfo` found anywhere in the scan. A reference that
   doesn't match anything found gets a stub `ProjectInfo` with `DetectionConfidence.Partial`
   instead of being silently dropped (SKILL.md §18) — a stale/moved project reference stays
   visible.
4. Builds the parent/child hierarchy by directory containment (`ProjectInfo.ChildProjects`), so a
   project detected inside another detected project's directory nests under it; everything else
   is top-level.
5. Falls back to one `Generic Folder Project` (SKILL.md §3, §17) only when nothing was detected
   anywhere in the whole scan — a `docs/` folder next to a real project stays a plain folder, it
   does not get its own Generic wrapper.

**This is a distinct abstraction from `IProjectAdapter`** (Phase 0). `IProjectDetector` answers
"what project ecosystems exist in this workspace" cheaply and recursively, for the Explorer and
for file-to-project mapping. `IProjectAdapter.LoadAsync(projectFilePath)` is reserved for a
heavier, on-demand, toolchain-aware load of one specific project file — building the real MSBuild
project graph, for instance — which is Phase 3+ work once there's a build system to serve. They
are expected to eventually cooperate (a build command would call an adapter for the project a
detector already found), not compete.

`DevStudio.UI.ViewModels.ProjectGraphLookup` turns a `WorkspaceProjectGraph` into two lookups the
Explorer and editor need at interactive speed: "does this directory have a project/solution"
(annotates `FileTreeNodeViewModel.Kind`/`DisplayLabel`) and "which project owns this file"
(`FindOwningProject`, SKILL.md §22–§23 — deepest matching project root wins; no match means
`Unknown`, never a guess).

## Workspace Persistence (Phase 2)

`WorkspaceState` (open document paths, active document, active project, a `Version` int) is
saved to `<root>/.devstudio/workspace.json` by `IWorkspaceStateStore` and restored the next time
that folder is opened (SKILL.md §10). `AppSettings` (theme, recent workspaces, reopen-on-startup)
is saved to a user-level JSON file by `IUserSettingsStore`. Both are justified in
`docs/adr/ADR-003-workspace-persistence.md`; both treat a missing or corrupted file as "start
fresh," never as a crash (SKILL.md §13).

`MainWindowViewModel.OpenWorkspaceAsync` is the single pipeline behind File → Open Folder, a
Recent Workspaces click, and startup auto-reopen: check the folder still exists → save the
previous workspace's state (if one was open) → run `ProjectDetectionService` → populate the
Explorer → update the Recent Workspaces list → restore the new workspace's saved state. Nothing
in that pipeline executes a project-defined command (SKILL.md §25) — it is metadata discovery and
JSON I/O only, whether triggered by a user click or by the startup auto-reopen setting.

## Toolchain Detection Architecture (Phase 3)

Full rationale in `docs/adr/ADR-004-toolchain-detection.md`; summary:

- **Strict separation from project detection.** "I found Cargo.toml" (Phase 2) and "cargo is
  installed at X, version Y" (Phase 3) are different facts produced by different subsystems —
  `IProjectDetector` never runs a process; `IToolchainDetector` never inspects a project file.
  Neither implies build success, which remains Phase 4+.
- **15 real detectors** in `Infrastructure.Toolchains`, each probing one known, fixed command
  (`dotnet --version`, `go version`, etc.) through the existing `IProcessRunner` via a shared
  `ToolchainProbe` helper — every failure mode (missing executable, timeout, non-zero exit)
  becomes a typed result, never an exception, so one broken detector can't stop the others
  (`ToolchainRegistry.RefreshAsync` also runs them concurrently with its own isolating try/catch
  as a second layer of defense).
- **Visual Studio is its own richer model** (`VisualStudioInstance`/`IVisualStudioDetector`),
  discovered via `vswhere.exe`, because SKILL.md §9 requires representing multiple VS
  installations independently — a plain `ToolchainInfo` only holds one instance per id.
  `MsvcToolchainDetector` adapts it into an ordinary `ToolchainInfo` (id `"msvc"`) purely so
  capability matching can treat MSVC/GCC/Clang uniformly.
- **Capability matching is tri-state and never guesses Debug.** `ProjectCapabilityMatcher.Match`
  turns a project type's required toolchain(s) (`ToolchainRequirements` — CMake needs CMake
  *and* a compiler; everything else needs exactly one toolchain) plus current detection results
  into `Available`/`Unavailable`/`Unknown` judgements. `Debug` capability is never copied from a
  toolchain's declared capabilities into a project's — that requires an actual `IDebuggerAdapter`,
  now implemented in Phase 6 (`DebugService.HasAdapterFor`), which a successful *toolchain*
  version probe was never evidence of and still isn't; debugger availability is checked through
  `DebugService`/`NetCoreDebuggerResolver`, a separate mechanism from `ProjectCapabilityMatcher`.
- **Toolchains panel + Refresh** (`ToolchainsPanelViewModel`, Tools → Refresh Toolchains):
  detection is asynchronous and runs once at startup plus on demand; `MainWindowViewModel` keeps
  the pre-capability `WorkspaceProjectGraph` around so a refresh re-applies capability matching
  (and reloads the Explorer) without re-scanning the filesystem.

## Build System Architecture (Phase 4)

Full rationale in `docs/adr/ADR-005-build-system.md`; summary:

```
View → ViewModel → BuildService → IBuildAdapter → DotNetBuildAdapter → IProcessRunner → dotnet
```

- **`BuildService`** (`Core.Build`) dispatches to whichever `IBuildAdapter` supports the
  target's `ProjectType`, enforces one build at a time (throws on overlap — the UI guards its
  own Build button; this is the backstop), and keeps `LastResult` as minimal build history
  (SKILL.md §17, §28).
- **`BuildResult.Status`** is a 7-value enum (`NotStarted/Running/Succeeded/Failed/Cancelled/
  TimedOut/Unavailable`) — never a bool, and never "Succeeded" merely because a process started
  (SKILL.md §6, §38).
- **`DotNetBuildAdapter`** (`Infrastructure.Build`) resolves `dotnet`'s path from the live
  `IToolchainRegistry` at call time (not a cached startup assumption); Restore has no `-c` flag,
  Build/Clean do, Rebuild is one real `dotnet build ... --no-incremental` invocation rather than
  a two-step Clean+Build (verified against the real CLI, not invented).
- **`MsBuildDiagnosticParser`** (`Core.Build`, pure logic) parses real MSBuild/Roslyn console
  lines, including the location-less project-level form (e.g. `MSB4025`); `DotNetBuildAdapter`
  deduplicates by `(File, Line, Column, Code)` because the MSBuild console logger genuinely
  repeats every diagnostic in its end-of-build summary — caught by a real integration test, not
  anticipated.
- **Cancellation** reuses Phase 1's `IRunningProcess.Kill(entireProcessTree: true)` unchanged —
  no new process-management code was needed; verified with a real `dotnet build` cancellation
  test, not just a fake.
- **Workspace Trust has its first real gate here**: `MainWindowViewModel` prompts via
  `IDialogService.ConfirmAsync` before building an untrusted workspace, and only proceeds on
  explicit confirmation (SKILL.md §42). Reading project/solution metadata to resolve a build
  target is unaffected by trust.
- **Output encoding is opt-in per request** (`ProcessStartRequest.OutputEncoding`, default
  `null`): a real integration test caught `dotnet`'s output being mis-decoded under the OS's
  legacy codepage; forcing UTF-8 globally was tried and broke `vswhere.exe`'s real-environment
  test, so only `DotNetBuildAdapter` opts in, with its choice verified against the real CLI.
- Solution/project/CMake/Rust/Go/Java/Node/Python build adapters remain unimplemented by
  design; `BuildService.HasAdapterFor` reports this and the UI says so rather than attempting or
  faking anything for them.

## Run System Architecture (Phase 5)

Full rationale in `docs/adr/ADR-006-run-system.md`; summary:

```
View → ViewModel → RunService → IRunAdapter → DotNetRunAdapter → IProcessRunner → real application
                       │
                       └──────────────► BuildService (build-before-run only; never calls
                                          `dotnet build` directly)
```

- **`RunService`** (`Core.Run`) is structurally identical to `BuildService`: it dispatches to
  whichever `IRunAdapter` supports the target's `ProjectType`, enforces one running application
  at a time (throws on overlap), and exposes `Status`/`LastResult`/`StatusChanged`/`Completed`.
  It never touches `IProcessRunner` directly and never invokes `dotnet build` itself — when
  `RunConfiguration.BuildBeforeRun` is true it calls the injected `BuildService.ExecuteAsync`.
- **`RunStatus`** is its own 8-value enum
  (`NotStarted/Starting/Running/Stopping/Exited/FailedToStart/Cancelled/Terminated`) — never
  `BuildStatus` reused. `Exited` (process ended on its own) and `Terminated` (a deliberate `Stop`)
  are kept distinct so the UI never reports a user-requested stop as a failure.
- **Runnability is never assumed.** `ProjectInfo.IsExecutable` (default `false`) is computed by
  `DotNetProjectDetector` from the real `<OutputType>` (`Exe`/`WinExe`) or an `Sdk="...Web"`
  project file, never from `ProjectType.DotNet` alone. `MainWindowViewModel.DiscoverRunConfigurations`
  only creates a `RunConfiguration` for projects where this is true — a class library is simply
  never offered as a run target.
- **`DotNetRunAdapter`** (`Infrastructure.Run`) launches
  `dotnet run --project <target> -c <configuration> --no-build [-- <args>]` rather than resolving
  a built artifact's path directly, avoiding any cross-platform guess at an output binary's name.
  Before launching, it checks the real, name-agnostic signal of whether `bin/<Configuration>` is
  non-empty, raising a clear "has not been built" error instead of a confusing raw `dotnet run`
  failure — and `--no-build` itself is what makes a skipped build-before-run fail honestly rather
  than silently building.
- **Stop/Restart reuse Phase 1/4's `IRunningProcess.Kill(entireProcessTree: true)` unchanged** —
  no new process-killing code. Because `dotnet run` launches the real application as a child of
  the `dotnet` process it starts, killing the whole tree (not just the wrapper) is what actually
  stops the application; verified with a real process that records its own PID and is confirmed
  dead after `Stop()`. `RestartAsync` always waits for the previous application's real exit
  before starting the next, verified with a real single-instance-lock console app that would
  report a conflict if it ever raced.
- **Workspace Trust gates Run before the build-before-run branch**, so it applies even when
  `BuildBeforeRun` is false (SKILL.md's explicit requirement that trust gates executing
  application code, not just building it) — the same `IDialogService.ConfirmAsync` pattern as
  Phase 4's Build gate.
- Run configurations are not persisted this phase (including any environment variables), and
  CMake/Rust/Go/Java/Node/Python run adapters remain unimplemented by design; `RunService.HasAdapterFor`
  reports this and the UI says so rather than attempting or faking anything for them.

## Debug System Architecture (Phase 6)

Full rationale in `docs/adr/ADR-007-debug-system.md`; summary:

```
View → ViewModel → DebugService → IDebuggerAdapter → NetCoreDebuggerAdapter → DAP Client → StreamDapTransport → real debugger → real application
                       │
                       └──────────────► BuildService (build-before-debug only; never calls
                                          `dotnet build` directly)
```

- **`DebugService`** (`Core.Debug`) is structurally identical to `RunService`: dispatches to
  whichever `IDebuggerAdapter` supports the target's `ProjectType`, enforces one active debug
  session at a time, reuses `BuildService` for build-before-debug, and converts the adapter's
  DAP-driven callbacks into normalized state (`DebugSessionState`, current thread/frame,
  breakpoints) — no raw DAP JSON ever reaches a ViewModel.
- **`DebugSessionState`** is its own 7-value enum
  (`NotStarted/Starting/Running/Paused/Stopping/Terminated/Failed`) — never `RunStatus` reused;
  `Paused` (stopped at a breakpoint) is a state a plain run never has.
- **The DAP boundary** (`Core.Dap`): `DapRequest`/`DapResponse`/`DapEvent` (pure data,
  `JsonNode` bodies), `IDapTransport` (an abstract read/write contract), and `DapClient`
  (assigns each request a unique `seq`, correlates the eventual response by `request_seq`
  regardless of arrival order, dispatches unsolicited events) — all unit-testable with a fake
  transport, no real process needed.
- **Framing** (`Infrastructure.Dap`): `DapFrameReader`/`DapFrameWriter` implement
  `Content-Length: <n>\r\n\r\n<n bytes>` directly over a `Stream` (never
  `StreamReader.ReadLine()`), correctly handling a header or payload split across multiple
  physical reads and several whole messages already sitting in one read — proven against a test
  `Stream` that hands back adversarially-sized chunks, no process needed.
- **`IProcessRunner` extended for raw stdio, not replaced**: `ProcessStartRequest.RawStdio`
  (default `false`) and `IRunningProcess.StandardInput`/`StandardOutput` (`Stream?`, null unless
  `RawStdio` was set) let `StreamDapTransport` read/write a debugger's stdio directly, since
  DAP's binary framing cannot be modeled as line-based text the way Terminal/Build/Run's stdout
  handling is. Every existing caller is unaffected (both default to off/null).
- **Real debugger discovery — a load-bearing finding, not an assumption.** `vsdbg` is genuinely
  present on this development machine (installed by the VS Code C# extension, *not* by either
  installed Visual Studio instance), and a real DAP `initialize` exchange with it succeeds — but
  its own license enforces a client-identity handshake that refuses a non-Visual-Studio-Code/
  Visual-Studio client at `configurationDone`. DevStudio does not attempt to bypass that.
  `NetCoreDebuggerResolver` therefore resolves **`netcoredbg`** (Samsung, MIT-licensed, no
  client-identity restriction, same `--interpreter=vscode` DAP contract) instead — installed via
  `winget install Samsung.NetCoreDbg` specifically to obtain genuine real-debugger validation.
  See ADR-007's Decision section for the full investigation.
- **The DAP handshake ordering was verified against the real adapter, not assumed**:
  `initialize` (await) → `launch` (send, don't await yet) → wait for the real `initialized`
  event (bounded to 10 seconds — SKILL.md §48's "must not hang forever," which the vsdbg
  investigation demonstrated the real necessity of) → `setBreakpoints` → `configurationDone`
  (await) → only then confirm the deferred `launch` response actually succeeded.
- **Program path resolution never guesses an executable name**: `NetCoreDebuggerAdapter` scans
  `bin/<Configuration>/*/<ProjectName>.dll` and requires exactly one real match, mirroring Phase
  5's Run adapter — DAP's `launch` needs an explicit `program` path, unlike `dotnet run` which
  resolves its own target internally.
- **Stop/cleanup reuse Phase 1/4/5's `IRunningProcess.Kill(entireProcessTree: true)`** unchanged.
  An adapter-initiated termination (the debuggee exits on its own mid-session) escapes to a
  background `Task.Run` rather than cleaning up inline inside the DAP event-dispatch callback —
  that callback runs on the same call stack as the client's own read loop, and disposing that
  client inline would be the read loop awaiting itself. A real deadlock/orphan-file-handle bug
  this caused was caught by the real integration tests, not anticipated in advance.
- **Editor integration reuses the existing `CaretMoveRequested` bridge** (ADR-002) to navigate to
  a stack frame's real source/line. Breakpoints are toggled via a line-number prompt (reusing
  "Go To Line"'s exact `DialogWindow.ShowInputAsync` pattern) rather than a clickable gutter —
  ADR-002 already established the plain `TextBox` editor has none; this is a documented UI
  limitation, not an oversight.
- **Workspace Trust gates Debug before the build-before-debug branch**, the same placement as
  Phase 5's Run gate, so it applies regardless of whether a future UI ever exposes
  `BuildBeforeDebug = false`.
- Attach is deferred to a later phase (explicitly permitted); CMake/Rust/Go/Java/Node/Python
  debugger adapters remain unimplemented by design — `DebugService.HasAdapterFor` reports this
  and the UI says so.

## LSP / Language Intelligence Architecture (Phase 7)

Full rationale in `docs/adr/ADR-008-lsp-language-intelligence.md`; summary:

```
Editor → ViewModel → LanguageService → ILanguageAdapter → CSharpLanguageAdapter → JsonRpcClient → StreamJsonRpcTransport → real C# language server
```

- **`LanguageService`** (`Core.Language`) is structurally identical to `RunService`/
  `DebugService`: dispatches to whichever `ILanguageAdapter` supports a file, enforces one active
  server session at a time, and normalizes the adapter's raw LSP callbacks into
  `LanguageServerState`/per-file diagnostics — no LSP JSON reaches a ViewModel. It has no
  `BuildService` dependency; opening/editing source code never triggers a build.
- **Shared with DAP, not duplicated**: the protocol-agnostic `Content-Length` byte framing.
  `Infrastructure.Dap.DapFrameReader`/`DapFrameWriter` were refactored into thin adapters over
  new `Infrastructure.Rpc.ContentLengthFrameReader`/`ContentLengthFrameWriter` classes (their own
  public behavior/tests unchanged); LSP's `StreamJsonRpcTransport` uses the same classes.
- **Not shared with DAP**: the request/response/notification client itself. `Core.Dap.DapClient`
  (one global `seq` across every message kind) and the new `Core.Lsp.JsonRpcClient` (an `id` that
  exists only on requests/responses; notifications carry none) follow the same *correlation
  pattern* but are genuinely different protocols with separate domain models, per instruction not
  to blur DAP and LSP together.
- **Real C# language server discovery — investigated, not assumed.** The real, verified
  candidate is `Microsoft.CodeAnalysis.LanguageServer.exe` (the Roslyn language server bundled
  with the VS Code C# extension) — MIT-licensed, no client-identity restriction (unlike Phase 6's
  `vsdbg`). `RoslynLanguageServerResolver` mirrors `NetCoreDebuggerResolver`'s exact shape
  (override env var → PATH → newest matching extension folder, `File.Exists`-verified).
- **Two real, load-bearing findings from empirical investigation** (mirroring ADR-007's vsdbg/
  netcoredbg investigation): (1) `--autoLoadProjects` is a *process argument*, not an LSP
  parameter, and without it the server never loads any `.csproj` — every file stays a
  standalone "miscellaneous file" with only generic keyword completions. (2) This server uses
  the LSP 3.17 *pull* diagnostics model (`textDocument/diagnostic`) exclusively — it never sends
  `publishDiagnostics`. `RoslynLanguageServerSession` pulls diagnostics itself after every
  `didOpen`/`didChange` (and exposes `RefreshDiagnosticsAsync` for when the very first pull races
  the server's own project load) and raises the same normalized event either way.
- **Full-document synchronization** by design (SKILL.md's explicit permission): every
  `didChange` sends one `contentChanges` entry with no `range`, a valid degenerate case of
  incremental sync regardless of the server's declared `textDocumentSync.change` capability.
  Document versions are tracked per file entirely inside the session.
- **Workspace Trust gates starting the language server, not reading/editing source.** Roslyn's
  project load runs a real MSBuild design-time build, which can execute a project's own custom
  SDK/target/task logic — the same risk class Build/Run/Debug's trust gates exist for.
  `MainWindowViewModel.EnsureLanguageServerAndOpenDocumentAsync` prompts once per workspace
  (checked via `LanguageService.IsActive`), not once per file; declining leaves the file open as
  plain text with no server started.
- **Editor integration reuses existing infrastructure.** `DocumentViewModel.Line`/`Column`
  (already maintained live from `CaretIndex` since Phase 1) convert directly to LSP's 0-based
  `Position`; Go To Definition reuses the same `CaretMoveRequested` bridge Find/Go-To-Line/Debug
  frame navigation already use. Applying a completion prefers its real `TextEdit` and falls back
  to `InsertText` only when the server didn't supply one — never assumed present.
- **Problems panel diagnostic sources are kept separate, never erasing each other.**
  `ProblemsPanelViewModel.ReplaceBuildDiagnostics`/`ReplaceLanguageDiagnostics` merge Build and
  per-file language-server diagnostics into one displayed collection; a file's new
  language-server diagnostics replace only that file's previous ones, exactly mirroring how
  Build's diagnostics are replaced wholesale on every build.
- Find References/Document Symbols/Workspace Symbols/Signature Help are explicitly deferred this
  phase — not implemented, not faked.

## Test Explorer Architecture (Phase 8)

Full rationale in `docs/adr/ADR-009-test-explorer-and-runner.md`; summary:

```
Test Explorer (UI) → MainWindowViewModel → TestService → ITestAdapter → DotNetTestAdapter → IProcessRunner → real dotnet test
```

- **`TestService`** (`Core.Testing`) mirrors `BuildService`'s single-flight shape (one
  `CancellationTokenSource`, `IsRunning`/`State`, `Cancel()`), not `RunService`/`DebugService`'s
  long-lived-session shape — a test run is one bounded operation like a build, not an ongoing
  session.
- **Test project detection is evidence-based, never guessed by name.** `ProjectInfo.IsTestProject`
  is set by `DotNetProjectDetector` only when the `.csproj` references a real, known test package
  (`Microsoft.NET.Test.Sdk`, `xunit`/`xunit.v3`, `NUnit`/`NUnit3TestAdapter`,
  `MSTest.TestFramework`/`MSTest.TestAdapter`).
- **Discovery uses the real `dotnet test --list-tests`, never source parsing.** Verified against a
  real throwaway probe project: every real discovered test's fully-qualified name is indented with
  exactly four spaces (locale-independent), including Theory-expanded names with embedded
  parameter values. `TestCase.SourceFile`/`Line` are always `null` after discovery — `--list-tests`
  never reports a location.
- **Execution parses the real TRX file `dotnet test --logger trx` writes**, never a guessed or
  simulated result. Outcomes map from the TRX's own `outcome` attribute
  (`Passed`/`Failed`/`NotExecuted`→Skipped/other→Error). A real failure's stack trace is the only
  place a source location is ever available (`in <file>:line <n>`, extracted via regex); a passing
  test's result carries none, and none is fabricated.
- **A real bug found and fixed by testing for it**: `IProcessRunner.RunAsync` reports cancellation
  via `ProcessResult.WasCancelled` rather than throwing. `DotNetTestAdapter` initially didn't check
  this flag, so a cancelled run's TRX-missing fallback error was mapped by `TestService` to
  `Failed` instead of `Cancelled` — caught by the mandatory real cancellation integration test,
  fixed by explicitly checking `WasCancelled` and re-throwing `OperationCanceledException` before
  the TRX-existence check.
- **Build-before-test reuses `BuildService` verbatim.** A failed build aborts with
  `TestRunState.BlockedByBuildFailure` before any test executes — the adapter is never invoked.
- **Workspace Trust gates discovery as well as execution**, unlike Build/Run/Debug (which only
  gate the operation itself) — because `--list-tests` performs a real build as a side effect, the
  same risk class every other trust gate exists for.
- **`TestFilter` is a structured record, never a raw shell string.** It translates to VSTest's real
  `--filter` mini-language (`FullyQualifiedName=...`, OR-joined with `|`, or `Trait=Value`),
  escaping VSTest's own special characters.
- **Problems panel gains a third, separately-tracked diagnostic source.**
  `ProblemsPanelViewModel.ReplaceTestDiagnostics` wholesale-replaces per run (like Build's), only
  for failures with a real, TRX-parsed source location — a location-less failure is still visible
  in the Test Explorer list itself, just not click-navigable.
- **`TestNodeViewModel`** is a dedicated mutable per-row ViewModel (unlike Debug/Language's
  read-only panels) because a test's displayed status genuinely mutates across its run lifecycle
  (NotRun → Running → Passed/Failed/Skipped).
- **Only xUnit was created and exercised against real `dotnet test`/TRX output** in this
  environment; NUnit/MSTest are expected to work identically (the adapter only ever talks to
  `dotnet test`/VSTest, never a framework-specific API) but this has not been real-environment
  verified — reported honestly as Implemented, Not Real Tested.

## Git Integration Architecture (Phase 9)

Full rationale in `docs/adr/ADR-010-git-integration.md`; summary:

```
Source Control (UI) → SourceControlViewModel → GitService → IGitAdapter → GitCliAdapter → IProcessRunner → real git
```

- **`GitService`'s real deviation from `BuildService`/`TestService`**: per-repository
  `CancellationTokenSource` tracking (a `ConcurrentDictionary<string, CancellationTokenSource>`),
  not one global token — a workspace may contain multiple real repositories (SKILL.md §9) whose
  mutating operations must not block each other, unlike Build/Test where only one target is ever
  active. Read-only operations (status/log/diff/branch listing/repository discovery) are never
  gated by this lock.
- **Repository discovery is always real Git, never manual `.git`-folder walking.**
  `git rev-parse --show-toplevel` is authoritative; `SourceControlViewModel` additionally scans
  one level of immediate subdirectories for their own separate repositories (a deliberately
  shallow, documented limitation — a repository nested two or more levels deep is not discovered).
- **Status parsing uses real, NUL-delimited `git status --porcelain=v2 -z --branch` output** —
  verified byte-for-byte against a real repository (including a real rename record, whose
  original path is Git's own *next*, separate NUL-delimited record) before writing the parser.
  Empirically confirmed to survive DevStudio's existing line-oriented `IProcessRunner` unmodified
  (no newlines exist in this output for it to split on incorrectly) — no `RawStdio` mode needed.
- **Log parsing uses a real unit/record-separator (`\x1f`/`\x1e`) custom format**; a repository
  with no commits yet is treated as a real, expected empty history, never an exception.
- **Diff parsing is real unified diff**; a "Binary files ... differ" line (Git's own wording) is
  the only signal that marks a diff binary — never guessed from an extension. An untracked file
  is never diffed (`git diff` has none for it) — `GetDiffAsync` returns `null`, not a fabricated
  result.
- **Checkout and branch deletion are never forced.** A real Git refusal (conflicting uncommitted
  changes; unmerged commits) is surfaced as `GitErrorKind.CheckoutBlocked`/`BranchDeletionBlocked`
  with Git's own message — DevStudio never stashes, resets, discards, or force-deletes around it
  (verified by real integration tests that assert the real file content/branch existence are
  unchanged after a blocked operation).
- **Commit messages are one structured argument-array element**, never shell-concatenated —
  verified with a real message containing quotes, an ampersand, backticks, and Unicode, read back
  unmodified from `git log`.
- **Workspace Trust gates every mutating operation** (Stage/Unstage/Discard/Commit/
  CheckoutBranch/CreateBranch/DeleteBranch) because commit/checkout can trigger repository-defined
  hooks — the same risk class every prior phase's gate exists for. Read-only inspection is never
  gated, matching every prior phase's read-only operations.
- **A real bug found and fixed by testing**: the rename/copy status record parser initially
  assumed the same field count as an ordinary entry, undercounting by one real field
  (`<X><score>`) — caught by a real `git mv` integration test, fixed by correcting the split
  count.
- **`SourceControlViewModel`** (unlike `ToolchainsPanelViewModel`/`ProblemsPanelViewModel`) owns
  its own trust-gating and destructive-confirmation logic directly, via delegates injected from
  `MainWindowViewModel` — Git has substantially more mutating operations than any prior panel,
  and putting all of them on `MainWindowViewModel` directly would have grown that composition
  root further without a matching benefit.
- Push/pull/fetch/clone, credential/SSH/PAT handling, merge/rebase/cherry-pick UI, stash/
  submodule/worktree management, and remote hosting integration are explicitly out of scope this
  phase (SKILL.md §27, §48) — deferred, not half-implemented. Commit details show metadata only,
  not a full commit-vs-parent diff, this phase.

## Extension System Architecture (Phase 10)

Full rationale in `docs/adr/ADR-011-extension-system.md`; summary:

```
Extensions (UI) → ExtensionsPanelViewModel → ExtensionManager → IExtensionDiscovery / IExtensionLoader / ICommandRegistry
                                                    ↓                    ↓                   ↓
                                     FileSystemExtensionDiscovery  AssemblyLoadContextExtensionLoader  CommandRegistry
```

- **`Discover ≠ Load ≠ Activate`, enforced structurally.** `IExtensionDiscovery` never
  constructs an `IDevStudioExtension`; `IExtensionLoader` never calls `ActivateAsync`. Only
  `ExtensionManager` chains all three, and only for a `Discovered`/`Valid` extension the user has
  actually enabled.
- **Manifest parsing is pure logic (no I/O), deterministic, and never throws for bad input.**
  `ExtensionManifestParser.Parse(json, extensionRoot)` rejects missing/malformed fields, invalid
  id/version/host-range, unknown capabilities, duplicate command-contribution ids, and unsafe
  entry points (absolute, UNC, or `../`-traversing — the manifest's own claim is never trusted;
  the resolved path must land back inside the extension's own root) as structured diagnostics.
- **`ExtensionState` (11 values) is set only as each real step completes** — never inferred.
  `RefreshAsync` rejects a second manifest claiming an already-seen id (`Invalid`, never silently
  merged) and never regresses an already-`Active` extension back to `Enabled`.
  `LoadAndActivateEnabledAsync` isolates each extension's own load/activation exception as
  `Failed` — one broken extension never blocks another, verified with real compiled fixtures.
- **The command system enforces two real constraints, not just documented ones.**
  `CommandRegistry.TryRegisterCommand` never overwrites an already-taken id (first registration
  wins). An activating extension never receives the full `ICommandRegistry` — only a narrow
  `IExtensionCommandRegistrar` scoped to its own id and its own manifest's declared command-
  contribution ids, so it structurally cannot register outside its own declared contributions or
  reach another extension's commands at all.
- **In-process collectible `AssemblyLoadContext` loading is explicitly not a security sandbox.**
  An extension's code runs with DevStudio's own OS-level privileges. `ExtensionAssemblyLoadContext
  .Load` always returns `null` so the runtime falls back to the host's already-loaded
  `DevStudio.Core.dll` for correct `IDevStudioExtension` type identity across the load-context
  boundary — the sample extension's own project reference to `DevStudio.Core` is marked
  `Private="false" ExcludeAssets="runtime"` specifically so no private duplicate copy ever sits
  next to it.
- **An extension cannot bypass Workspace Trust or execute a process, structurally, not by
  policy.** `IExtensionContext` exposes only command registration and logging — no reference to
  `IProcessRunner`, `BuildService`, `RunService`, `DebugService`, `TestService`, `GitService`, or
  any UI type (`MainWindow`/`Application`/etc.) is reachable from anything an extension receives.
- Only user/global extensions (under `%APPDATA%/DevStudio/extensions`) are supported — workspace-
  local extension loading was not implemented at all this phase. Panel contributions, a
  marketplace, downloads, and auto-updates are explicitly out of scope.

## Cross-Platform Support Architecture (Phase 11)

Full rationale in `docs/adr/ADR-012-cross-platform-support.md`; summary:

- **A repository-wide audit found the codebase already largely cross-platform-correct** —
  `ExecutableLocator`/`ShellLocator` (Phase 1/3), `PythonToolchainDetector`'s `python`/`python3`
  fallback, every JSON settings/extension store's use of `Environment.SpecialFolder`,
  `RoslynLanguageServerResolver`'s `.vscode/extensions` convention, `DotNetSolutionDetector`'s
  `.sln` backslash normalization, and `MsBuildDiagnosticParser`'s OS-agnostic path regex were all
  already written correctly, from Phase 0 onward. `VisualStudioDetector`'s Windows-only gating is
  correct as-is — Visual Studio itself is Windows-only.
- **The one real bug found: case-sensitivity assumed everywhere a real filesystem path was keyed
  or compared.** Roughly a dozen sites (file-change watching, the open-document table,
  breakpoints-by-source, project/solution path lookup, Git's per-repository mutation tracking, the
  extension entry-point traversal check, project ancestor/containment checks) hard-coded
  `StringComparer.OrdinalIgnoreCase` — correct on Windows/macOS, wrong on Linux's case-sensitive
  ext4, where it would have silently treated `Foo.cs` and `foo.cs` as the same real file. Fixed by
  introducing one centralized `Core.Platform.PathComparer` (computed once from
  `OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()`) and updating every genuine
  path-identity site to consume it — sites comparing something *other* than a real path (file
  extensions, known project-marker filenames, JSON capability names, Git's own stderr text) were
  deliberately left case-insensitive, since those are conventions, not filesystem identity.
- **No new process/executable-resolution/shell abstraction was introduced** —
  `IProcessRunner`/`IRunningProcess` remain the sole process execution foundation;
  `ExecutableLocator`/`ShellLocator` were confirmed correct rather than duplicated.
- **`.dll` is the correct universal answer, not a Windows-only assumption** — a .NET assembly's
  extension does not vary by OS (unlike a native executable), so the Extension System needed no
  change here.
- **Real Windows validation**: the full suite (489 tests) passes unchanged; a real app-launch
  smoke test confirmed clean startup/shutdown.
- **Real Linux validation, under WSL2 Ubuntu 24.04 (explicitly not native Linux desktop)**: a
  separately installed, real .NET 10.0.401 SDK (matching the Windows machine's version exactly)
  and this Ubuntu image's own real `git` (2.43.0) — the whole solution (including the Avalonia
  app) builds unmodified; 465 of 473 runnable tests pass unmodified (excluding two long,
  environment-blocked Debug/LSP integration suites); the real Phase 10 extension lifecycle passes
  unchanged. A real GUI launch attempt under
  WSLg found a missing native `libICE.so.6` system library (an environment gap requiring `sudo`,
  unavailable this session) — not a DevStudio defect — so real GUI rendering remains unverified.
  Two real, pre-existing test-code bugs (not product bugs) were found and fixed by this run: a
  hard-coded Windows-style expected path in a Git test, and a hard-coded bare `python` (absent on
  this real Ubuntu image, which ships only `python3`) in a process-runner test.
- **macOS was not available and is not claimed as tested.**

## Localization Architecture (Phase 12)

Full rationale in `docs/adr/ADR-013-localization.md`; summary:

- **`ILocalizationService`/`LocalizationService` live in `DevStudio.UI`, not `Core`** —
  localization is a presentation concern; `Core` never needs to know which language is active.
  Constructed once in `App.axaml.cs`'s composition root and injected via constructor into
  `MainWindowViewModel`, `SourceControlViewModel`, `DialogService`, `FolderPickerService`, and
  `FilePickerService` — the same convention every other cross-cutting service already follows.
- **Resources are ordinary `.resx` files, not a hand-rolled scheme** —
  `Localization/Strings.resx` (en-US, neutral) and `Strings.zh-TW.resx` (258 keys each, kept in
  parity mechanically, verified by a real test enumerating each culture's own `ResourceSet`). The
  SDK auto-compiles the culture-suffixed file into a real satellite assembly with zero extra
  MSBuild configuration. The real `System.Resources.ResourceManager` fallback chain — not
  hand-written `try/catch`-and-guess logic — is what makes a key missing from zh-TW resolve to
  en-US.
- **Runtime language switching, no restart, via the real indexer-change-notification
  convention**: `SetCulture` raises `PropertyChangedEventArgs("Item[]")`, which every
  `{Binding Loc[Key]}` binding across `MainWindow.axaml`/`SettingsWindow.axaml` re-evaluates
  immediately — not a window recreation, not a partial/fragile refresh.
- **`LocFormatConverter`** (a small `IMultiValueConverter`) lets a parameterized localized
  template (e.g. `"Ln {0}, Col {1}"`) be bound through a `MultiBinding` instead of requiring a
  computed ViewModel property per message.
- **A language's own display name is a literal, never resolved through the active culture** —
  `MainWindowViewModel.LanguageOption.DisplayName` is `"English"`/`"繁體中文"` set directly, so
  the picker never shows the *other* language's translation of a language's own name.
- **Persistence reuses the existing `AppSettings`/`IUserSettingsStore` mechanism** —
  `AppSettings.Language` is a plain `string` (default `"en-US"`), following the same
  `with`-expression update pattern every other setting already uses; no second persistence
  system was introduced.
- **External tool output is never translated, by design** — `dotnet`/MSBuild/`git`/Roslyn/DAP/LSP/
  compiler/test-framework text is foreign data DevStudio only relays, never DevStudio's own UI.
- **Known, documented gap**: enum `ToString()` displays and `OutputPanelViewModel.Log(...)`
  message bodies remain English-only this phase — see ADR-013's Known Limitations for why this
  was deferred rather than rushed.

## Package Management Architecture (Phase 13 P0 + Phase 14 P1)

Full rationale in `docs/adr/ADR-014-package-management.md` (P0) and
`docs/adr/ADR-015-cross-language-package-ecosystems.md` (P1); summary:

```
Package Manager (UI) → PackageManagerViewModel → PackageService → PackageManagerRegistry
    → IPackageManagerAdapter (+ capability interfaces) → NuGetPackageAdapter/PythonPackageAdapter/
      NpmPackageAdapter/MavenPackageAdapter/GradlePackageAdapter/CargoPackageAdapter/
      GoModulePackageAdapter/VcpkgPackageAdapter/ConanPackageAdapter → IProcessRunner →
      real dotnet/pip/npm/mvn/gradle/cargo/go/vcpkg/conan
```

- **`Core.Packages` is entirely ecosystem-neutral** — `PackageReference`/`PackageVersion`/
  `PackageDependency`/`PackageSource`/`PackageProject`/`PackageManagerCapabilities`/
  `PackageOperation`/`PackageOperationResult`/`PackageSearchResult`/`IPackageManagerAdapter` plus
  the capability interfaces `IPackageInspector`/`IPackageSearcher`/`IPackageInstaller`/
  `IPackageRemover`/`IPackageUpdater`/`IPackageSourceManager`. Core references no package-manager
  API, executable name, or ecosystem-specific type — mirroring how `ProjectType`/
  `WellKnownToolchainIds` already keep Core free of tool-specific knowledge.
- **Capability-oriented adapters, not one mega-interface.** `IPackageManagerAdapter` is only
  `Id`/`DisplayName`/`DetectProject`; an adapter implements only the capability interfaces it
  genuinely supports, and `PackageManagerCapabilities` is re-evaluated *per project* (not just per
  tool) — e.g. `PythonPackageAdapter` reports `Add`/`Remove`/`Update` as `false` for a
  Poetry/uv/Pipenv-managed project even though pip itself is installed, to avoid silently
  diverging from that project's own declared dependency file.
- **`PackageService`'s real deviation from `BuildService`/`TestService`, matching `GitService`**:
  per-project `ConcurrentDictionary<string, CancellationTokenSource>` single-flight tracking for
  mutations (Add/Remove/Update/Restore) — a workspace can have several independent projects whose
  package operations must not block each other. Read-only inspection is never gated by this lock.
- **Project Detection integration is read-only and additive** — each adapter's own
  `DetectProject(ProjectInfo)` checks file presence/content directly (never a process), and the
  existing `IProjectDetector`/`ProjectDetectionService` pipeline was not modified.
- **Workspace Trust gates exactly Add/Remove/Update/Restore**, via the same
  `Func<string, Task<bool>>` delegate shape `SourceControlViewModel` already uses
  (`MainWindowViewModel.EnsurePackageWorkspaceTrustedAsync`) — never read-only inspection.
- **Implemented: NuGet (.NET), pip (Python), npm (Node) (Phase 13 P0, real-environment
  validated), plus Maven, Gradle, Cargo, Go Modules, vcpkg, Conan (Phase 14 P1, implemented but
  NOT real-environment validated — none of `mvn`/`gradle`/`cargo`/`rustc`/`go`/`vcpkg`/`conan` is
  installed on the development machine).** pnpm, Yarn, Poetry, and uv remain explicitly deferred —
  not implemented, not faked as supported.
- **Each Phase 14 adapter made its own capability-honesty judgment** rather than assuming every
  ecosystem behaves like NuGet/npm/pip: `GradlePackageAdapter` is Inspector-only (a Gradle build
  script is executable code no static edit can safely rewrite); `VcpkgPackageAdapter` has no
  `Update` (vcpkg's baseline/overrides version model has no safe naive per-package edit);
  `ConanPackageAdapter` only mutates `conanfile.txt` projects, never `conanfile.py` (an executable
  Python recipe). See ADR-015.
- **A real, pre-existing Phase 3 Windows bug was found and fixed** while real-testing the npm
  adapter: `NodePackageManagerToolchainDetector` probed the bare `"npm"` name, which Win32 cannot
  resolve to npm's real `.cmd` launcher; a compounding `ExecutableLocator` candidate-ordering bug
  made it worse. Both fixed — see ADR-014.
- **`PackageManagerRegistry.DetectApplicableManagers` needed no change to support two independent
  C/C++ adapters (vcpkg, Conan) on the same `ProjectType.CMake` project** — the registry already
  asks every registered adapter independently and collects whichever return non-null, proving the
  Phase 13 design's "a project can have more than one applicable manager" case without
  modification.
- **The UI (`PackageManagerViewModel` + a `MainWindow.axaml` tab) has zero per-language
  branches** — every control binds to the generic `PackageProject`/`PackageManagerCapabilities`
  contracts, mirroring `SourceControlViewModel`'s "owns its own trust-gating, injected via
  delegate" shape rather than growing `MainWindowViewModel` further. Confirmed unchanged by
  Phase 14 (zero ecosystem-name literals found anywhere in `DevStudio.UI`/`DevStudio.App/Views`
  outside compiled artifacts).

## Security Model (enforced by interface shape, not by convention)

- `IProcessRunner` has no `ExecuteCommand(string)` overload — by construction, callers cannot
  build a shell-injectable command line (SKILL.md §12).
- `WorkspaceModel.IsTrusted` exists so that later build/debug/package-install flows can gate on
  Workspace Trust before running anything project-defined (SKILL.md §26).
- `IToolchainDetector.DetectAsync`/`IProjectAdapter.LoadAsync` return data and have no
  side-effecting "install" method — but unlike project detection, toolchain detection *is*
  explicitly allowed to run a fixed version-probe command (SKILL.md §6), always through
  `IProcessRunner` with a hard-coded executable and argument array, never anything derived from
  project content, and always with a timeout (`ToolchainProbe`, SKILL.md §27, §33).
- `ITerminalSession` is built entirely on `IProcessRunner`/`IRunningProcess` — there is no second,
  parallel process-execution path, and no shell-string command is ever constructed for it
  (SKILL.md §15, §24). Opening a folder only ever calls `IWorkspaceScanner.GetChildrenAsync`
  (read-only); nothing about opening a workspace runs a project-defined command (SKILL.md §25).
- `IProjectDetector` has no method that starts a process — its only capability is reading
  filenames and, optionally, bounded config-file text (SKILL.md §4, §19, §31).
  `ProjectInfo.Capabilities` is now populated (Phase 3, via `ProjectCapabilityMatcher`) strictly
  from already-collected `ToolchainInfo` results — matching itself never executes anything.
- `WorkspaceState`/`AppSettings` persistence never stores secrets — both are plain metadata
  (paths, flags, timestamps); see `SECURITY.md` for the explicit review.
- No detector's executable/argument list is ever built from project or workspace data — every
  probe target is a literal string constant (`"dotnet"`, `"cmake"`, `"vswhere.exe"`, …).
- `DotNetBuildAdapter` is the first component that runs a project-defined build — it still
  never constructs a shell string: the target path and configuration are separate elements of
  `ProcessStartRequest.Arguments`, and Workspace Trust gates the call before it happens
  (SKILL.md §41–§42).
- `PackageService.AddAsync`/`RemoveAsync`/`UpdateAsync`/`RestoreAsync` (Phase 13, extended with six
  more ecosystems in Phase 14) reuse the exact same `IProcessRunner`/`ProcessStartRequest`
  structured-invocation path — no `PackageProcessRunner`/`CommandExecutor` was introduced — and are
  gated by Workspace Trust before `PackageService` is ever called, since installing/restoring a
  package can execute that ecosystem's own arbitrary third-party install scripts (npm lifecycle
  scripts, Python build hooks, MSBuild targets a restored package contributes, Maven/Gradle plugin
  code, Cargo/Go build scripts, vcpkg port builds, Conan recipe code).

See `SECURITY.md` for the full security model and how future phases must extend it.
