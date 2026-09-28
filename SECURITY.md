# Security Model

DevStudio executes external programs (compilers, debuggers, build tools, package managers) on
behalf of projects it opens, so it must treat project files and their build configuration as
potentially untrusted input (SKILL.md §26).

## Process Execution

- All process launches go through `DevStudio.Core.Processes.IProcessRunner`, which takes an
  executable path plus an argument array (`ProcessStartRequest`), never a single shell command
  string. There is intentionally no `ExecuteCommand(string)` API anywhere in Core.
- Every launch has an explicit working directory and environment; nothing inherits ambient
  shell state implicitly.
- Cancellation and timeout are first-class (`ProcessStartRequest.Timeout`, `CancellationToken`
  parameters), and `IRunningProcess.Kill()` is expected to terminate the full process tree, not
  just the immediate child (SKILL.md §12, §30).

## Workspace Trust

- `DevStudio.Core.Workspace.WorkspaceModel.IsTrusted` gates any operation that would run
  project-defined code: build scripts, pre/post-build tasks, package installation, debug
  launch configurations, and external tools (SKILL.md §26).
- Opening a workspace or detecting its projects must never itself execute anything
  project-defined. `IProjectAdapter.LoadAsync` is read-only by contract (SKILL.md §9, §44).
  `IToolchainDetector.DetectAsync` is a deliberate exception: SKILL.md §6 explicitly permits it
  to run a fixed, known version-probe command (never a project-defined one) — see the Toolchain
  and Package Management section below.
- **Phase 4: the first real gate.** `DotNetBuildAdapter` runs a project's own build (`dotnet
  build`/`restore`/`clean`) — the first component in the codebase that executes project-defined
  behavior for real. `MainWindowViewModel` checks `WorkspaceModel.IsTrusted` before every build
  operation and, if untrusted, prompts for explicit confirmation before proceeding (and before
  setting `IsTrusted = true`). There is no way to trigger a build against an untrusted workspace
  without that prompt.
- **Phase 5: Run gates on trust even without a build.** `DotNetRunAdapter` launches a project's
  own compiled application via `dotnet run` — untrusted application code now executes as
  directly as an untrusted build script did in Phase 4. `MainWindowViewModel.ExecuteRunAsync`
  checks `WorkspaceModel.IsTrusted` before the `BuildBeforeRun` branch runs, so the same prompt
  and gate apply even when `BuildBeforeRun` is false (i.e. Run does not silently bypass trust by
  skipping the build step). There is no way to start a run against an untrusted workspace without
  that prompt.
- **Phase 6: Debug gates on trust before Build or the debugger ever launches.** Debugging
  executes both the real application and real debugger code — a strictly larger surface than
  Run. `MainWindowViewModel.StartDebuggingAsync` checks `WorkspaceModel.IsTrusted` before the
  `BuildBeforeDebug` branch, in the same position as Phase 5's Run gate (SKILL.md §44's explicit
  requirement that the trust prompt occur before Build *and* before the debugger/debuggee ever
  launch). There is no way to start a debug session against an untrusted workspace without that
  prompt.
- **Phase 7: reading/editing source stays allowed untrusted; starting the language server does
  not.** Opening a `.cs` file and editing it as plain text never requires trust — the same
  read-only principle project detection already established. But the real C# language server
  evaluates the owning project through MSBuild (a design-time build) to get accurate compiler
  arguments, which can execute a project's own custom SDK resolvers/imported `.targets`/inline
  tasks — the same risk class Build/Run/Debug's gates exist for. `MainWindowViewModel
  .EnsureLanguageServerAndOpenDocumentAsync` checks `WorkspaceModel.IsTrusted` before ever calling
  `LanguageService.OpenDocumentAsync` for the first relevant file in a workspace (prompted once
  per workspace, not once per file); declining leaves the file open as plain text with no
  language server started. See ADR-008.
- **Phase 8: test discovery is gated the same as test execution, not treated as read-only.**
  `dotnet test --list-tests` performs a real build of the test project as a side effect — the
  same risk class Build/Run/Debug/Language's gates exist for, even though "discovering tests"
  sounds inspection-only. `MainWindowViewModel.EnsureTestWorkspaceTrustedAsync` checks
  `WorkspaceModel.IsTrusted` before both `RefreshTestsCommand` (discovery) and
  `RunAllTestsCommand`/`RunSelectedTestCommand` (execution) ever call into `TestService`. There is
  no way to discover or run tests against an untrusted workspace without that prompt. See ADR-009.
- **Phase 9: every mutating Git operation is gated; read-only Git inspection is not.**
  Stage/Unstage/Discard/Commit/CheckoutBranch/CreateBranch/DeleteBranch can execute
  repository-defined Git hooks (`pre-commit`, `commit-msg`, `post-checkout`, etc.) — the same risk
  class every prior phase's gate exists for. `MainWindowViewModel.EnsureGitWorkspaceTrustedAsync`
  is checked by `SourceControlViewModel` before every one of those operations ever calls into
  `GitService`. Repository detection, status, log, diff, and branch listing are never gated — they
  read a repository's own metadata, no more execution risk than reading a project's source files.
  There is no way to stage/commit/checkout/create/delete a branch against an untrusted workspace
  without that prompt. See ADR-010.
- **Phase 10: extensions cannot bypass Workspace Trust, structurally.** An activating extension's
  `IExtensionContext` has no reference to `BuildService`/`RunService`/`DebugService`/
  `TestService`/`GitService`/`IProcessRunner` — every one of those services' own Workspace Trust
  gate above still applies exactly as before, because an extension has no host-provided path to
  any of them at all. This is an absent API, not a runtime check an extension's own code could
  find a way around. See ADR-011.

## Secrets

- No component may log passwords or API keys, or display secrets in build/debug output when
  avoidable. Secret masking is required in Output, Terminal, Build Logs, Debug Logs, and any
  Environment Display surface once those exist (SKILL.md §27).
- No credentials may be stored in plain text, and no generated credentials may be committed.
- Full environment-variable dumps must not be logged by default (SKILL.md §28).

## Toolchain and Package Management

- Toolchain detection is allowed to run a fixed, known version-probe command per toolchain
  (`dotnet --version`, `go version`, `vswhere.exe -all -products * -format json`, …) — SKILL.md
  §6 explicitly permits this, unlike project detection. Every probe goes through the same
  `IProcessRunner`/`ProcessStartRequest` as everything else: hard-coded executable name, a fixed
  argument array, a timeout, no project data ever in the command. DevStudio never installs
  software automatically (SKILL.md §30, §44).
- Package operations (`Restore`/`Install`/`Update`/`Remove`) are explicit user actions —
  dependencies are never updated automatically (SKILL.md §22). As of Phase 13 (P0), this is a
  real, implemented gate: `PackageService.AddAsync`/`RemoveAsync`/`UpdateAsync`/`RestoreAsync` are
  never called by anything triggered on workspace open — only a user clicking Add/Remove/Update/
  Restore in the Package Manager panel reaches them, and `MainWindowViewModel.
  EnsurePackageWorkspaceTrustedAsync` (the same Workspace Trust confirmation dialog Build/Run/
  Debug/Test/Git already use) gates every one of the four before `PackageService` is invoked.
  Read-only inspection (`ListInstalledAsync`/`ListDependenciesAsync`/`ListOutdatedAsync`/
  `SearchAsync`) is never gated, matching every prior phase's read-only-operations convention.
  Installing/restoring a package is explicitly treated as execution-capable, not merely a file
  write — see the Phase 13 Review below. Phase 14 (P1) added six more ecosystems behind the exact
  same gate, with three adapters deliberately narrowing what they even expose as mutable in the
  first place rather than relying on the gate alone: `GradlePackageAdapter` exposes no
  Add/Remove/Update at all (a Gradle build script is executable code), `VcpkgPackageAdapter`
  exposes no Update (vcpkg's baseline/overrides model has no safe naive edit), and
  `ConanPackageAdapter` exposes no mutation for `conanfile.py` projects (an executable Python
  recipe) — see the Phase 14 Review below.

## Phase 1 Review

Phase 1 added a real filesystem-reading Explorer, a real file editor with save, a real terminal
(shell child process), and external-change detection. Verified against the principles above:

- **No automatic project execution.** Opening a folder calls only `IWorkspaceScanner.GetChildrenAsync`
  (read-only directory listing). Opening a terminal launches the user's own shell with an empty
  command line — DevStudio never feeds it a project-defined command automatically. No build
  script, pre/post-build hook, or package manager is invoked anywhere in Phase 1 (none of that
  exists yet).
- **No shell-string execution added.** `TerminalSession` (`Infrastructure.Terminal`) launches the
  shell executable itself via `IProcessRunner`/`ProcessStartRequest` (executable + argument
  array); user-typed terminal input goes to the child process's stdin via the newly-added
  `IRunningProcess.WriteInputAsync`, not through a second command-execution path. `ProcessRunner`
  (`Infrastructure.Processes`) still never uses `UseShellExecute` or builds a command string.
- **No secrets logged.** The Output panel logs only DevStudio-generated messages (file paths,
  operation names); it never dumps environment variables or file contents.
- **No unsafe file operations.** `TextFileService` reads/writes exactly the file the user opened
  or saved; `WorkspaceScanner` only lists directories, never deletes/renames/creates. Large files
  (>5 MB) prompt before opening rather than silently loading (SKILL.md §27).
- **Workspace Trust remains respected.** `WorkspaceModel.IsTrusted` still gates nothing yet
  because nothing in Phase 1 runs project-defined code — there is no build/debug/package-install
  path for it to gate. This is intentional: Workspace Trust becomes load-bearing starting with
  whichever phase first runs a project's own commands (build scripts, pre/post-build tasks,
  debug launch configs), not before.

## Phase 2 Review

Phase 2 added project/solution detection and workspace/settings persistence. Verified:

- **Project detection is read-only.** Every `IProjectDetector` implementation
  (`DevStudio.Infrastructure.Projects.*`) only calls `File.OpenRead`/`ReadAsync` through the
  shared, bounded `ConfigFileReading.TryReadHeadAsync` helper (max 64 KB, catches
  `IOException`/`UnauthorizedAccessException`/`NotSupportedException` and returns null rather
  than throwing) or parses already-read text with regex/`JsonDocument`/`XDocument`. No detector
  calls `Process.Start`, and none of them can — they only implement `IProjectDetector`, which has
  no such method.
- **No project scripts, package managers, build systems, or compilers execute.** Confirmed by
  reading every detector: `dotnet`, `npm`, `cmake`, `pip`, `mvn`/`gradle`, `cargo`, `go` are
  never invoked. `ProjectDetectionService` itself only calls `IWorkspaceScanner` (already
  read-only, Phase 1) and the detectors.
- **No arbitrary commands execute from workspace configuration.** `WorkspaceState`/`AppSettings`
  are plain data records (paths, an int version, booleans, timestamps) deserialized by
  `System.Text.Json` into known record types with no custom converters — there is no code path
  from "a value in `workspace.json`" to "a process gets started." Startup auto-reopen
  (`ReopenLastWorkspaceOnStartup`) re-runs the exact same read-only `OpenWorkspaceAsync` pipeline
  a manual click would, nothing more privileged.
- **Malicious project files are parsed defensively.** Bounded read size (64 KB) guards against a
  huge file; every parse (JSON, XML, TOML-ish regex, `.sln` regex) is wrapped so a malformed file
  degrades to `DetectionConfidence.Partial`/`Failed` with a warning, not a crash. Verified with
  real malformed-file integration tests (`NodeProjectDetectorTests.Reports_partial_confidence_for_malformed_json`,
  `DotNetSolutionDetectorTests.Reports_partial_confidence_for_an_unparsable_slnx_file`, and the
  `JsonWorkspaceStateStoreTests`/`JsonUserSettingsStoreTests` corrupted-file cases).
- **No secrets persisted.** `WorkspaceState` and `AppSettings` fields are enumerated in
  `ARCHITECTURE.md`; none of them is credential-shaped, and nothing in either store's
  save/load path touches environment variables.
- **Workspace Trust remains respected.** `WorkspaceModel.IsTrusted` now has a real UI toggle
  (Project → Trust/Untrust Workspace) but still gates nothing — there is still no build/debug/
  package-install path in Phase 2 for it to gate. It becomes load-bearing starting with whichever
  phase first runs a project's own commands.

## Phase 3 Review

Phase 3 added real toolchain/Visual Studio detection (process probes) and capability matching.
Verified against SKILL.md §33:

- **Only known, fixed executable probes execute.** Every detector in
  `Infrastructure.Toolchains.*` calls `ToolchainProbe.RunAsync` with a literal string constant
  as the executable (`"dotnet"`, `"python"`, `"cmake"`, `"go"`, `"vswhere.exe"`'s resolved path,
  …) and a literal argument array (e.g. `["--version"]`). Grepped every detector file to confirm
  none builds an executable path or argument from a variable sourced from project/workspace data.
- **No project metadata controls executable paths.** `ProjectCapabilityMatcher` only *reads*
  `ToolchainInfo` results already produced by the fixed probes above; it has no code path that
  feeds a `ProjectInfo` field into `ToolchainProbe` or `IProcessRunner`.
- **No shell-string execution introduced.** Same `IProcessRunner`/`ProcessStartRequest` as every
  prior phase; `VisualStudioDetector` passes `vswhere.exe`'s arguments (`-all -products * -format
  json`) as a `string[]`, not a concatenated command line.
- **No package installation, compiler invocation, or build occurs.** Every probe is a read-only
  version/info query (`--version`, `-version`, `version`); none of `dotnet build`, `npm install`,
  `cmake --build`, `cargo build`, etc. ever appears in any detector.
- **No environment secrets logged.** The only environment variable read is `JAVA_HOME`
  (`JavaToolchainDetector`, via `Environment.GetEnvironmentVariable`, never executed or logged
  in bulk); `EnvironmentSnapshot` deliberately excludes full environment dumps and any
  password/token-shaped data.
- **Toolchain paths are treated as data.** `ToolchainInfo.ExecutablePath`/`InstallationRoot` and
  `VisualStudioInstance.InstallationPath`/`MsBuildPath`/`MsvcToolsetPath` are plain strings
  displayed in the Toolchains panel; nothing re-interpolates them into a new command.
- **Malformed version output cannot crash DevStudio.** Every detector's parsing is defensive
  (regex `Match.Success` checks, `JsonDocument.Parse` wrapped in try/catch for `vswhere` output)
  and falls back to `PartiallyDetected`/`DetectionFailed`, never an unhandled exception — verified
  by `VisualStudioDetectorTests.Malformed_vswhere_output_yields_an_empty_list_instead_of_throwing`
  and the registry-level `ToolchainRegistryTests.A_detector_that_throws_does_not_stop_the_others`.

## Phase 4 Review

Phase 4 added the first component that actually executes project-defined behavior:
`DotNetBuildAdapter`. Verified against SKILL.md §41:

- **No shell-string command construction.** `DotNetBuildAdapter.BuildArguments` returns a
  `string[]` (`{"build", targetPath, "-c", configuration}`, etc.); the target path and
  configuration are separate array elements passed through unchanged from `BuildTarget`/
  `BuildConfiguration.Name`, never concatenated into a single string. Verified directly by
  `DotNetBuildAdapterTests`, which asserts the exact argument array sent to `IProcessRunner` for
  every operation.
- **No user/project data becomes an executable path without validation.** The executable is
  always `IToolchainRegistry.Get("dotnet").ExecutablePath` (resolved from a real, previously
  detected toolchain) or the literal fallback `"dotnet"` — never a path derived from project
  content.
- **Working directory is explicit** — `BuildTarget.WorkingDirectory`, derived from the
  project's/solution's own detected root path, never the DevStudio process's current directory
  or the user's shell directory.
- **No automatic build or restore on workspace opening.** `OpenWorkspaceAsync` (Phase 2) still
  only calls `ProjectDetectionService`; nothing added in Phase 4 hooks a build into it. Every
  build/rebuild/clean/restore starts from an explicit `[RelayCommand]` the user invoked.
- **No package installation** beyond what `dotnet restore`/`dotnet build` themselves do as part
  of their own normal, user-invoked operation — DevStudio adds no separate package-manager call.
- **No secrets written to logs.** Build output goes to the Output panel and `BuildResult.Output`
  verbatim from the child process; DevStudio adds no environment-variable dump of its own to
  that stream.
- **Build cancellation does not leave unmanaged child processes.** Reuses Phase 1's
  `IRunningProcess.Kill(entireProcessTree: true)` unchanged; verified with a *real* `dotnet
  build` cancellation test (`Cancelling_a_real_build_terminates_the_dotnet_process_and_reports_Cancelled`),
  not only a fake-based one.
- **An untrusted workspace does not silently execute build commands.** See the Workspace Trust
  section above — `MainWindowViewModel` prompts and requires explicit confirmation first.

## Phase 5 Review

Phase 5 added the second component that actually executes project-defined behavior — this time
the project's own *application*, not just its build script: `DotNetRunAdapter`. Verified against
SKILL.md §41:

- **No shell-string command construction.** `DotNetRunAdapter` builds a `List<string>` of
  arguments (`{"run", "--project", targetPath, "-c", configuration, "--no-build"}`, plus `"--"`
  and each user-supplied argument as its own array element); nothing is concatenated into a
  single command string. Verified directly by `DotNetRunAdapterTests`, which asserts the exact
  argument array sent to `IProcessRunner` — including that a multi-word argument (`"hello
  world"`) survives as one array element, not two.
- **No user/project data becomes an executable path without validation.** The executable is
  always `IToolchainRegistry.Get("dotnet").ExecutablePath` (a real, previously detected
  toolchain) or the literal fallback `"dotnet"` — never a path derived from project content or
  from a guessed output binary name.
- **Working directory is explicit** — `RunConfiguration.WorkingDirectoryOverride ??
  Target.WorkingDirectory`, derived from the project's own detected root path, never the
  DevStudio process's current directory.
- **No automatic run on workspace opening or build.** `DiscoverRunConfigurations` only populates
  the Run configuration list (data, not execution); every actual run starts from an explicit
  `[RelayCommand]` (`Run`/`RunWithoutBuild`/`Restart`) the user invoked.
- **Environment variables are passed through, never logged.** `RunConfiguration.EnvironmentVariables`
  flows straight to `ProcessStartRequest.Environment`; Run does not log environment variable
  values (or names) anywhere, and per-run environment variables are not persisted to disk this
  phase (see Known Limitations) — so there is no on-disk plaintext-secret surface introduced by
  this feature.
- **Run does not leave unmanaged child processes.** `Stop()`/`RestartAsync` reuse Phase 1/4's
  `IRunningProcess.Kill(entireProcessTree: true)` unchanged; because `dotnet run` launches the
  real application as a child of the `dotnet` process, killing the entire tree (not just the
  wrapper) is what actually stops it. Verified with a *real* process that records its own PID and
  is confirmed dead after `Stop()`
  (`Stop_kills_the_real_process_tree_so_the_child_process_no_longer_exists`) — not only a
  fake-based test.
- **Restart never runs two instances at once.** Verified with a real single-instance-lock console
  application (`Restart_never_runs_two_instances_at_once`) that would report a conflict if
  `RestartAsync` ever started a new process before the previous one had truly exited.
- **An untrusted workspace does not silently execute application code.** `ExecuteRunAsync` checks
  `WorkspaceModel.IsTrusted` before the `BuildBeforeRun` branch, so the same prompt/confirmation
  gate from Phase 4 applies to Run even when the build step is skipped.
- **Run-without-build never silently builds.** `DotNetRunAdapter` checks a real, name-agnostic
  "has anything been built" signal before launching and fails with a clear message instead of
  invoking a build the user explicitly chose to skip.

## Phase 6 Review

Phase 6 added the third component that executes project-defined behavior — real debugger code
plus the real application it debugs — through a real DAP debugger, `netcoredbg`. Verified against
SKILL.md §41 and §45–§46:

- **No shell-string command construction anywhere in the DAP layer.** `NetCoreDebuggerAdapter`
  launches `netcoredbg` via the ordinary `IProcessRunner`/`ProcessStartRequest` (executable +
  fixed argument array `{"--interpreter=vscode"}`, never a project-defined command). Every DAP
  request/response/event is generated structurally as a `JsonObject`/`DapProtocolMessage` and
  serialized by `DapMessageSerializer` — DevStudio never hand-builds or string-concatenates DAP
  JSON, and user-supplied run arguments/environment variables are placed into the `launch`
  request's `args`/`env` fields as data, never interpolated into `netcoredbg`'s own command line.
- **No unvalidated debugger executable.** The executable is always
  `NetCoreDebuggerResolver.Resolve().ExecutablePath` — a real, `File.Exists`-verified path (an
  explicit override env var, PATH, or the real winget install location) — never a path derived
  from project content, and never assumed present without checking.
- **`vsdbg`'s license-enforcement handshake was not bypassed.** A real investigation reached the
  point of a proprietary "handshake" check failing for a non-Microsoft-IDE client; DevStudio
  stopped there rather than attempting to spoof client identity to unlock unauthorized use. This
  is treated as a genuine environment/licensing limitation (see ADR-007), not an engineering
  problem to route around.
- **No user/project data becomes the debug target's path without validation.**
  `NetCoreDebuggerAdapter`'s program-path resolution scans the real, `File.Exists`-verified
  `bin/<Configuration>/*/<ProjectName>.dll` output and requires exactly one match — never a
  project-supplied path, never a guess among ambiguous candidates (reported as an error instead).
- **Environment variables are passed through, never logged.** Debug launch arguments flow from
  `DebugConfiguration.RunConfiguration.EnvironmentVariables` straight into the DAP `launch`
  request's `env` object; nothing in the Debug system logs environment variable names or values.
- **DAP input is treated as external protocol data, not trusted structure.**
  `DapMessageSerializer.Deserialize` validates JSON structure, message `type`, and required
  fields, raising `DapProtocolException` — never an unhandled exception type — for anything
  malformed; `DapFrameReader` does the same for the wire framing (missing/negative/non-numeric
  `Content-Length`, a stream ending mid-header or mid-payload). Verified by
  `DapMessageSerializerTests`/`DapFrameReaderTests` against genuinely malformed and adversarially
  chunked input, not just well-formed examples.
- **Debug does not leave unmanaged child processes.** `Stop`/`Disconnect` reuse Phase 1/4/5's
  `IRunningProcess.Kill(entireProcessTree: true)` unchanged; verified with a real debug session
  (entry-stop, then `Stop`) confirming a clean return to `Terminated` with no lingering process,
  and with real exit-code/step/continue-to-completion tests that all clean up correctly.
- **An untrusted workspace does not silently execute application or debugger code.** See the
  Workspace Trust section above — `StartDebuggingAsync` prompts and requires explicit
  confirmation before Build or the debugger ever launches.
- **Debugging without building first fails honestly instead of silently building** — the same
  "not built yet" signal Run uses, checked before ever invoking the debugger.

## Phase 7 Review

Phase 7 added the first component that provides language intelligence by executing a real,
separate process: the Roslyn language server, driven over LSP. Verified against SKILL.md §41,
§57–§58:

- **No shell-string command construction.** `CSharpLanguageAdapter` launches the language server
  via the ordinary `IProcessRunner`/`ProcessStartRequest` (executable + fixed argument array
  `{"--stdio", "--logLevel", "Information", "--autoLoadProjects"}`, never a project-defined
  command). Every LSP request/notification is generated structurally as a `JsonObject`/
  `JsonRpcMessage` and serialized by `JsonRpcMessageSerializer` — never hand-built or
  string-concatenated JSON.
- **No unvalidated language server executable.** The executable is always
  `RoslynLanguageServerResolver.Resolve().ExecutablePath` — a real, `File.Exists`-verified path
  (an explicit override env var, PATH, or the real VS Code extension install location) — never a
  path derived from project content.
- **No shell command execution reaches DevStudio through the language server.** The Roslyn
  language server does not expose a "run a shell command" capability over LSP, and DevStudio
  never forwards one; completion/hover/definition/diagnostics are read-only queries against the
  server's own in-memory analysis.
- **LSP input is treated as external protocol data, not trusted structure.**
  `JsonRpcMessageSerializer.Deserialize` validates JSON structure and required fields, raising
  `JsonRpcProtocolException` — never an unhandled exception type — for anything malformed; the
  shared `ContentLengthFrameReader` does the same for the wire framing. Verified by
  `JsonRpcMessageSerializerTests`/`ContentLengthFrameReaderTests` against genuinely malformed and
  adversarially chunked input.
- **A server-initiated request is never left unanswered.** Real servers send requests DevStudio
  doesn't have a specific opinion about (`workspace/configuration`, `client/registerCapability`);
  `JsonRpcClient` answers every one with a safe default rather than letting the server stall
  waiting for a response that never comes.
- **Reading/editing source code stays allowed in an untrusted workspace; starting the language
  server does not.** See the Workspace Trust section above — `EnsureLanguageServerAndOpenDocumentAsync`
  prompts and requires explicit confirmation before the language server (and the real MSBuild
  design-time build it performs) ever starts.
- **Applying a completion never leaves the language server's document state stale.** Both the
  `TextEdit` and `InsertText` application paths mutate `DocumentViewModel.Text` through the same
  property the debounced `didChange` handler already observes, so the server always sees the
  edit it just helped produce.
- **The language server is terminated on workspace close/switch and on Stop/Restart**, reusing
  Phase 1/4/5/6's process-management pattern (graceful `shutdown`/`exit`, falling back to
  `IRunningProcess.Kill(entireProcessTree: true)` only if that doesn't complete promptly) — never
  left running against a workspace that's no longer open.

## Phase 8 Review

Phase 8 added test discovery/execution by executing a real, separate process: `dotnet test`.
Verified against SKILL.md §41, §57–§58:

- **No shell-string command construction.** `DotNetTestAdapter` launches `dotnet test` via the
  ordinary `IProcessRunner`/`ProcessStartRequest` (executable + a fixed, structurally-built
  argument list — `test`, the project file, `-c`, the configuration name, `--list-tests`/
  `--logger`/`--results-directory`/`--no-build`/`--filter` each appended as its own array element
  — never a concatenated string). `TestFilter.ToVsTestFilterExpression()` builds VSTest's
  `--filter` expression from structured data (fully-qualified names or a trait name/value pair),
  escaping VSTest's own special characters, rather than accepting or forwarding an arbitrary raw
  filter string from the UI.
- **No unvalidated test-runner executable.** The executable is always
  `IToolchainRegistry.Get(WellKnownToolchainIds.DotNet)`'s real, already-detected `dotnet` path —
  the same resolution `DotNetBuildAdapter`/`DotNetRunAdapter` use — never a path derived from
  project content.
- **Discovery and results come only from real tool output, never fabricated.** Discovered tests
  come only from parsing real `--list-tests` output; results come only from parsing the real TRX
  file `dotnet test` itself wrote. A source location is only ever attached when a real TRX stack
  trace actually contains one (`ExtractLocation`'s regex match) — never guessed from a test's name
  or its project's source layout.
- **Test discovery is gated by Workspace Trust exactly like execution.** See the Workspace Trust
  section above — discovery is not treated as a safe, read-only inspection merely because it
  "looks like" listing rather than running, since `--list-tests` performs a real build.
- **Cancellation reliably kills the real process tree, with no orphan.** `TestService.Cancel()` →
  the linked `CancellationTokenSource` → `IProcessRunner`'s existing process-tree-kill machinery
  (unchanged since Phase 4) terminates the real `dotnet test` process; verified by a real
  integration test that starts a real long-running test, cancels mid-run, and confirms (via `ps`)
  that no `dotnet`/`testhost`/`VSTest` process survives.
- **The temporary TRX results directory is always cleaned up**, including on cancellation or
  adapter exception (`finally`-block best-effort delete, swallowing `IOException`/
  `UnauthorizedAccessException` for a scratch directory that may still be momentarily locked) —
  no residual result files accumulate in the OS temp directory across runs.

## Phase 9 Review

Phase 9 added repository stage/commit/checkout/branch mutation by executing a real, separate
process: `git`. Verified against SKILL.md §31–§32, §57–§58:

- **No shell-string command construction.** `GitCliAdapter` launches every Git command via the
  ordinary `IProcessRunner`/`ProcessStartRequest` (executable + a structurally-built argument
  list — file paths, branch names, and commit messages are each their own array element, always
  preceded by `--` for path-like arguments, never concatenated into one string). A commit message
  containing quotes, an ampersand, backticks, and Unicode was verified to reach Git unmodified via
  a real integration test — `git commit -m "user string"` as a shell string is never constructed
  anywhere in this codebase.
- **No unvalidated Git executable.** The executable is always
  `IToolchainRegistry.Get(WellKnownToolchainIds.Git)`'s real, already-detected path — the same
  resolution `DotNetBuildAdapter`/`DotNetTestAdapter` use — never a hard-coded path, never assumed
  present merely because Phase 3 detected it once at startup.
- **Status/log/diff/branch results come only from real, parsed Git output, never fabricated.**
  A rename's original path, a diff's added/removed lines, and a commit's metadata are only ever
  populated from Git's own real, machine-readable output — never guessed from a filename or a
  project's source layout.
- **Checkout and branch deletion are never forced.** No `--force`/`-D` flag is ever passed by
  this codebase; a real Git refusal (conflicting uncommitted changes; unmerged commits) is
  surfaced to the user with Git's own real message, verified by real integration tests that
  assert the real file content/branch existence are unchanged after a blocked operation —
  DevStudio never stashes, resets, or discards to force an operation through.
- **Destructive operations require explicit confirmation.** `SourceControlViewModel.DiscardAsync`
  and `DeleteBranchAsync` both call `IDialogService.ConfirmAsync` before ever calling into
  `GitService`, and discarding an untracked file remains entirely unimplemented (never routed
  through `git clean`).
- **No credential, SSH key, PAT, or credential-helper content is ever read, stored, or
  displayed.** Remote push/pull/fetch/clone and any authentication flow are out of scope this
  phase — no such command is ever constructed, and no remote URL is ever logged (remote
  information isn't exposed at all this phase).
- **Git hooks are never bypassed.** No `--no-verify` (or equivalent) flag is ever passed to
  `commit`/`checkout`; if a repository's own hook runs, it runs exactly as it would from a real
  terminal.
- **Test discovery/execution is gated by Workspace Trust exactly like every mutating Git
  operation** — see the Workspace Trust section above.

## Phase 10 Review

Phase 10 introduced a new trust boundary — every extension is potentially untrusted code loaded
in-process. Verified against SKILL.md §2, §14–§15, §21–§22, §28–§29, §41–§42:

1. **Extension trust model.** Discovery, manifest validation, loading, and activation are kept as
   four distinct steps (SKILL.md §2) — none of "Installed = Trusted", "Signed = Safe", or
   "Declared a capability = Granted that capability" is ever assumed anywhere in
   `ExtensionManager`/`ExtensionManifestParser`. No signature verification exists this phase (not
   required, not claimed).
2. **In-process vs. out-of-process loading.** In-process, via a collectible
   `AssemblyLoadContext`, was the chosen option — evaluated against a custom collectible ALC
   (same thing) and an out-of-process host (rejected for this phase's scope; see ADR-011). This
   is **explicitly documented as not a security sandbox**, here and in the loader's own doc
   comments — an extension's code has the same OS-level privileges as DevStudio itself.
3. **`AssemblyLoadContext` limitations.** `Unload()` is advisory (GC-driven), never a guaranteed-
   immediate release; documented in `IExtensionLoader`'s doc comment and ADR-011, not glossed
   over as a real unload guarantee.
4. **Manifest validation.** Strict, deterministic, pure-logic parsing (SKILL.md §7) rejects
   malformed JSON, missing/invalid fields, unknown capabilities, and duplicate command-
   contribution ids as structured diagnostics — never a silent repair, never an exception
   escaping to the discovery caller.
5. **Path validation.** `entryPoint` is never trusted as given: absolute paths, UNC paths, and
   any resolved path that lands outside the extension's own root (however many `../` segments it
   takes to get there) are rejected before the manifest is even considered `Valid` — verified by
   real path-traversal and absolute-path integration tests.
6. **Capability model.** Declaring a capability string is recognized as valid JSON content; it
   is never treated as the host granting that behavior. Only `Command` contributions do anything
   at all this phase.
7. **Workspace Trust interaction.** See the Workspace Trust section above — structural absence of
   any execution-capable service reference in `IExtensionContext`, not a runtime permission check.
8. **Process execution restrictions.** No extension-facing equivalent of `RunCommand`/`Execute`/
   `Shell`/`Process.Start` exists anywhere in `Core.Extensions` — verified by inspection of the
   entire `IExtensionContext`/`IExtensionCommandRegistrar` surface (two members total:
   `Commands`/`Log`, plus `TryRegisterCommand`).
9. **UI access restrictions.** No `Window`/`Control`/`Application`/`MainWindow` reference is
   reachable from anything an extension receives; the only UI-adjacent effect available is
   registering a command handler that runs when a human explicitly invokes it later.
10. **Failure isolation.** A real activation exception and a real command-invocation exception
    were each verified, with real compiled fixture extensions, to be caught, recorded, and
    isolated — never propagating to crash the host or affect an unrelated extension.
11. **Credential restrictions.** Nothing in the extension system reads, stores, transmits, or
    logs credentials — there is no network or credential API surface for an extension to reach
    from `IExtensionContext` at all.
12. **Future permission model.** SKILL.md §41's "introduce process execution later as an
    explicit, permissioned capability" is the documented extension point for anything beyond
    Command contributions — not something this phase's absent API quietly leaves a gap for.

## Phase 11 Review

Phase 11 audited every prior phase's platform-sensitive code rather than adding new
execution-capable surface. Verified against SKILL.md §29 (this phase's security section):

1. **No arbitrary shell execution was added.** No `cmd.exe /c`/`bash -c`-style command-string
   construction exists anywhere in the codebase, before or after this phase; `ShellLocator`
   resolves an interactive shell *executable path*, never a command string, and
   `IProcessRunner` remains executable-plus-argument-array only.
2. **No generic unrestricted process API was exposed** — `Core.Platform.PathComparer`, the one
   new type this phase introduced, does no I/O and executes nothing.
3. **No command injection or path traversal was introduced.** The one behavioral change
   (`PathComparer`) only affects *comparison* of already-resolved paths; it never changes how a
   path is resolved, combined, or passed to a process, and the Phase 10 extension entry-point
   traversal check is strictly more correct on a case-sensitive filesystem after this fix, not
   less.
4. **No Workspace Trust bypass was introduced.** No trust-gated call site's gating logic changed.
5. **No secret logging was introduced** — no new logging surface was added this phase.
6. **No unsafe environment inheritance was introduced** — `IProcessRunner`'s explicit
   environment-variable handling is unchanged.
7. **No unsafe temporary files were introduced** — no new temp-file usage this phase.
8. **No platform-specific credential leakage** — no credential-handling code exists anywhere in
   this codebase to begin with (Git/extensions both explicitly avoid it — see the Phase 9/10
   Reviews above).
9. **No extension privilege escalation** — the Phase 10 `IExtensionContext` surface is unchanged;
   `PathComparer` is not reachable from it.
10. **No shell-string construction for IDE-controlled operations** — confirmed by inspection
    across every adapter (Build/Run/Debug/Test/Git/LSP/Extensions); all remain executable +
    structured argument array via `IProcessRunner`.

## Phase 12 Review

Phase 12 added a real localization layer (`ILocalizationService`/`LocalizationService`, `.resx`
resources) and a Settings UI, and touched dozens of dialog/ViewModel call sites to consume it.
Verified against SKILL.md §7's localization-security requirements:

1. **No localized/formatted string is ever evaluated as code, a shell command, a process
   argument, or a path.** Every `_localizationService.Format(...)` call substitutes plain display
   text (a file path, a project name, a branch name) into a `{0}`-style template purely for
   *presentation* via `string.Format` — the resulting string is only ever passed to
   `DialogWindow.ShowAsync`/`ShowInputAsync`'s `title`/`message` parameters (rendered as
   `TextBlock.Text`) or bound directly to XAML `Text`/`Content`. It is never passed to
   `IProcessRunner`, never used to build a path, and never re-parsed as a key.
   `LocFormatConverter` (the XAML-side equivalent) is documented in its own doc comment as
   existing only for display text, never executable input.
2. **No new process execution, file I/O, or network surface was introduced.** Loading `.resx`
   resources goes through the standard, already-trusted `System.Resources.ResourceManager` /
   compiled satellite-assembly mechanism — no new file reads, no new sockets.
3. **No secret can flow through localization.** Every resource value is a static, hand-written
   UI string checked into source control; the only runtime-supplied data ever substituted into a
   template is display metadata already visible elsewhere in the UI (file paths, project/branch
   names, exception messages already shown via `ShowErrorAsync` before this phase) — nothing new
   is exposed.
4. **No Workspace Trust bypass was introduced.** Every trust-gated call site's *gating logic* is
   unchanged; only the strings shown at those call sites were replaced with localized
   equivalents carrying the same information.
5. **The Settings window only ever writes to the existing, already-reviewed
   `AppSettings`/`IUserSettingsStore` JSON file** (no secrets, per the existing Secrets section
   above) — `Language` is a plain BCP-47 culture-name string, validated against exactly two known
   supported cultures (`LocalizationService.Normalize`, defaulting to en-US for anything else),
   never used to construct a file path, assembly name, or process argument beyond the
   already-trusted `ResourceManager` culture-lookup API.
6. **No new logging surface was introduced** that could leak a secret through a localized message
   — `Output.Log(...)` call sites are unchanged by this phase (deliberately left English-only,
   see ADR-013's Known Limitations); only dialog titles/messages were localized.

## Phase 13 Review (P0: NuGet, pip, npm)

Phase 13 added a generic package-management layer (`Core.Packages`/`Infrastructure.Packages`/
`PackageManagerViewModel`) capable of real Add/Remove/Update/Restore/Search against a real
project. Reviewed against SKILL.md's package-management security requirements:

1. **No generic `ExecuteCommand(string)` was introduced.** Every adapter (`NuGetPackageAdapter`/
   `PythonPackageAdapter`/`NpmPackageAdapter`) builds a `ProcessStartRequest` with a resolved
   executable path and a structured `IReadOnlyList<string>` argument array — package/version
   identifiers the user typed are always separate array elements, never concatenated into a
   command-line string, exactly like every existing adapter.
2. **Installing or restoring a package is treated as execution-capable, not a benign file
   operation.** `npm install` can run a package's lifecycle scripts, `pip install` can run a
   package's real build backend (`setup.py`/PEP 517 hooks), and `dotnet restore` can pull in and
   evaluate MSBuild targets a NuGet package contributes — all three are gated by Workspace Trust
   before `PackageService` is ever reached, the same class of risk Build/Run/Debug/Test/Git's
   existing gates cover. Read-only inspection is never gated.
3. **No automatic install/restore on workspace open.** Nothing in `MainWindowViewModel.
   OpenWorkspaceAsync` (or anywhere else) calls a package-mutation method; `PackageManagerViewModel`
   only loads read-only state (`RefreshAsync`) when a project/manager is selected, and only after
   the panel is actually shown.
4. **No credential/token of any kind is read, stored, or logged.** `PackageSource`/
   `PackageSearchResult` carry only names/locations/metadata a package registry's own public API
   already returns; no adapter reads an API key, `.npmrc`/`NuGet.Config`/`pip.conf` credential
   entry, or authentication header. Private/authenticated feeds are explicitly out of scope this
   phase (see ADR-014's Known Limitations) rather than half-implemented insecurely.
5. **No full environment variable dump is ever logged.** `PackageOperationResult`/`ProcessResult`
   only ever surface a command's own stdout/stderr text (the same as every other adapter);
   `ProcessStartRequest.Environment` is left `null` (inherit) by every Phase 13 adapter — no new
   environment-variable surface was introduced.
6. **`PythonPackageAdapter`'s project-local venv resolution is itself a security-relevant fix, not
   just a correctness one**: without it, a pip mutation would install into whatever Python the
   `IToolchainRegistry` happens to have detected globally — on a real multi-project development
   machine, this can affect packages available to unrelated projects/processes outside DevStudio
   entirely. Resolving `.venv`/`venv` first (see ADR-014) keeps a package mutation's blast radius
   scoped to the project the user is actually working in.
7. **The two `NodePackageManagerToolchainDetector`/`ExecutableLocator` fixes are not themselves
   security-sensitive** (they only affect which real, already-installed npm binary gets resolved,
   never which command runs) but are reviewed here because they were found via this phase's own
   process-execution work — see ADR-014 for the full explanation.
8. **No new logging surface was introduced.** `Output.Log(...)` is not yet wired to package
   operations this phase (consistent with ADR-013's existing English-only `Output.Log` scope) —
   failures surface through `PackageOperationResult.FailureReason` and a dialog, not a new log
   channel.

## Phase 14 Review (P1: Maven, Gradle, Cargo, Go Modules, vcpkg, Conan)

Phase 14 added six more adapters on Phase 13's unchanged security model (same
`PackageService`/Workspace Trust gate, same `IProcessRunner`/`ProcessStartRequest` structured
invocation). Reviewed against the same requirements:

1. **No generic `ExecuteCommand(string)` was introduced.** All six new adapters
   (`MavenPackageAdapter`/`GradlePackageAdapter`/`CargoPackageAdapter`/`GoModulePackageAdapter`/
   `VcpkgPackageAdapter`/`ConanPackageAdapter`) build a `ProcessStartRequest` with a resolved
   executable path and a structured `IReadOnlyList<string>` argument array, exactly like Phase
   13's three adapters — package/version identifiers are always separate array elements.
2. **Manifest/build-file mutation never falls back to unsafe string manipulation of executable
   content.** `MavenPackageAdapter` edits `pom.xml` via `XDocument` (structured XML, never
   regex-on-XML). `VcpkgPackageAdapter` edits `vcpkg.json` via `JsonNode` (structured JSON, never
   regex-on-JSON). `ConanPackageAdapter` edits `conanfile.txt`'s static `[requires]` section via
   line-based parsing of a known, simple ini-like format — and, critically, **never** attempts to
   edit or execute `conanfile.py` (a real Python script) or `build.gradle`/`build.gradle.kts` (a
   real Groovy/Kotlin script): `GradlePackageAdapter` exposes `Add`/`Remove`/`Update` as `false`
   unconditionally, and `ConanPackageAdapter` exposes them as `false` specifically for
   `conanfile.py` projects. This is the phase's central security-relevant design decision — an
   adapter that cannot safely mutate a script-shaped manifest simply does not claim the
   capability, rather than attempting a best-effort rewrite of executable code.
3. **Installing/restoring a package continues to be treated as execution-capable, not a benign
   file operation**, now additionally covering Maven/Gradle plugin code, Cargo/Go build scripts
   (`build.rs`, `go generate`), vcpkg port builds, and Conan recipe code (`conanfile.py`'s
   `build()`/`package()` methods) — all gated by Workspace Trust before `PackageService` is ever
   reached, identically to Phase 13's three ecosystems.
4. **No automatic install/restore on workspace open** — unchanged from Phase 13; no new code path
   added by this phase calls a mutation method outside the existing Package Manager panel's
   user-initiated actions.
5. **No credential/token of any kind is read, stored, or logged.**
   `MavenPackageAdapter.GetSourcesAsync` reads only the project's own `pom.xml` `<repositories>`
   element, never `~/.m2/settings.xml` (where Maven mirror/server credentials live).
   `VcpkgPackageAdapter.GetSourcesAsync` reads only `vcpkg-configuration.json`'s `registries`
   array (a git repository URL and baseline commit — vcpkg registry authentication, where
   configured, lives outside this file). `ConanPackageAdapter.GetSourcesAsync` parses `conan
   remote list --format=json`'s documented output, which Conan itself never includes credentials
   in (remote auth is stored separately by Conan). `CargoPackageAdapter` deliberately does NOT
   implement `IPackageSourceManager` at all — `.cargo/config.toml`, where a registry token could
   be configured, is never read, precisely to avoid the risk of exposing one.
6. **No full environment variable dump is ever logged** — `ProcessStartRequest.Environment` is
   left `null` (inherit) by every Phase 14 adapter, identical to Phase 13.
7. **A real bug this phase's own review found and fixed was a correctness bug, not a security
   one**: `ConanGraphInfoParser` initially derived direct-vs-transitive classification from the
   wrong graph edges (a node's own outgoing edges rather than the consumer's edges to it) — caught
   by its own unit test before landing; it never affected what command was executed, only how a
   read-only dependency list was displayed.
8. **No new logging surface was introduced** — consistent with Phase 13's `Output.Log` scope;
   failures surface through `PackageOperationResult.FailureReason` and a dialog.

## Status

Security model as implemented through Phase 14 (P1). This document will be revisited at the end
of every phase per the Security Review requirement in SKILL.md §42 Rule 4.
