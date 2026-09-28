# Changelog

## Phase 14 (P1) — Cross-Language Package Ecosystem Expansion: Maven, Gradle, Cargo, Go Modules, vcpkg, Conan (2026-09-29)

- Added six more real adapters on top of Phase 13's unchanged `Core.Packages` architecture —
  zero changes to `IPackageManagerAdapter`, any capability interface, `PackageManagerRegistry`,
  `PackageService`, or any UI/ViewModel code (see `docs/adr/ADR-015-cross-language-package-ecosystems.md`):
  `MavenPackageAdapter` (real `mvn dependency:tree`/`dependency:resolve`, structured `XDocument`
  pom.xml edits for Add/Remove/Update, `versions-maven-plugin` outdated parsing),
  `GradlePackageAdapter` (`Inspector` only, deliberately — real `gradle dependencies` parsing;
  Add/Remove/Update/Restore/Search/Sources are all `false` since a Gradle build script is
  executable code no static edit can safely rewrite), `CargoPackageAdapter` (real `cargo metadata
  --format-version 1`/`cargo add`/`cargo rm`/`cargo update`/`cargo fetch`), `GoModulePackageAdapter`
  (real `go list -m -json all`'s concatenated-JSON-stream format, `go get`/`go mod edit
  -droprequire` + `go mod tidy`/`go mod download`; no `Searcher` — no reliable Go module search API
  exists), `VcpkgPackageAdapter` (manifest mode only, real `vcpkg list`/`search`/`install`;
  Add/Remove via structured `JsonNode` edits of `vcpkg.json`'s own array rather than vcpkg's own
  not-yet-verified manifest-mutation subcommand; `Update` is `false`), and `ConanPackageAdapter`
  (targets Conan 2.x's `conan graph info`/`install`/`search`/`remote list --format=json`;
  `conanfile.py` is Inspector-only since it's executable Python, `conanfile.txt`'s `[requires]`
  section gets real Add/Remove/Update via structured line-based editing).
- Added four new toolchain detectors (`MavenToolchainDetector`, `GradleToolchainDetector`,
  `VcpkgToolchainDetector`, `ConanToolchainDetector`), each mirroring
  `NodePackageManagerToolchainDetector`'s `ExecutableLocator`-first resolution pattern. Rust/Go
  toolchain and project detection already existed from an earlier phase — only their
  package-management layer was added this phase.
- Confirmed `PackageManagerRegistry.DetectApplicableManagers` needed no change to support vcpkg and
  Conan as two independent adapters both applicable to the same `ProjectType.CMake` project — the
  registry already asks every registered adapter independently, exactly as Phase 13 designed it.
- Zero new UI/localization changes needed for any of the six ecosystems — confirmed via source
  grep that no ecosystem name appears in `DevStudio.UI`/`DevStudio.App/Views` outside compiled
  artifacts; all existing Phase 13 `PackageManager.*` localization keys are already
  ecosystem-neutral.
- **Environment fact governing this entire phase**: none of `mvn`/`gradle`/`cargo`/`rustc`/`go`/
  `vcpkg`/`conan`/`cmake` is installed on the development machine (only a bare JDK is present).
  Every one of the six adapters is therefore implemented but not real-environment validated —
  every command-construction/output-parsing/manifest-editing path is unit-tested only against
  realistic fixture data, never a real subprocess. What IS real-environment-tested for all six: a
  real project with a real manifest file correctly, honestly reports "detected ecosystem, tool
  unavailable" (`RealEnvironmentToolchainTests`, `MavenGradlePackageAdapterIntegrationTests`,
  `CargoGoPackageAdapterIntegrationTests`, `VcpkgConanPackageAdapterIntegrationTests`) rather than
  silently claiming a capability it cannot deliver.
- Performed a post-implementation architecture review (evidence-based, no busywork refactoring):
  confirmed no ecosystem-specific logic leaked into `Core`, no branching in the UI, no duplicated
  process execution, no service locator, no global state, no circular project references, and no
  God Service growth — zero refactoring was needed or performed.
- Added 33 new tests (13 `VcpkgPackageAdapterTests`, 16 `ConanPackageAdapterTests`, 2 real
  `VcpkgConanPackageAdapterIntegrationTests`, 2 real-environment vcpkg/Conan-absence entries in
  `RealEnvironmentToolchainTests`) plus Maven/Gradle/Cargo/Go Modules tests already landed earlier
  in this phase. Full suite: 642/642 passing (212 Core, 70 UI, 360 Infrastructure), 0 regressions
  from Phase 13's 556-test baseline (556 → 584 after Maven/Gradle → 609 after Cargo/Go Modules →
  642 after vcpkg/Conan).
- Fixed one real bug found during this phase's own review: `ConanGraphInfoParser` initially
  derived a package's direct-vs-transitive classification from that package's *own* outgoing
  dependency edges rather than from the root consumer node's edges to it — caught by its own unit
  test, fixed before landing (Conan's graph JSON `"direct"` flag is a per-edge property, not a
  per-node one).

## Phase 13 (P0) — Cross-Language Package Management: NuGet, pip, npm (2026-09-28)

- Added a generic, capability-based Package Management layer per `docs/adr/ADR-014-package-management.md`:
  `Core.Packages` (`PackageReference`, `PackageVersion`, `PackageDependency`, `PackageSource`,
  `PackageProject`, `PackageManagerCapabilities`, `PackageOperation`, `PackageOperationResult`,
  `PackageSearchResult`, `IPackageManagerAdapter` + capability interfaces `IPackageInspector`/
  `IPackageSearcher`/`IPackageInstaller`/`IPackageRemover`/`IPackageUpdater`/
  `IPackageSourceManager`, `PackageManagerRegistry`, `PackageService`). Core references no
  package-manager API of any kind.
- Added three real adapters (P0 priority order): `NuGetPackageAdapter` (`dotnet add/remove/list/
  restore package`, `dotnet package search`, all JSON shapes captured from a real SDK 10.0.401
  invocation), `PythonPackageAdapter` (real pip, with real project-local `.venv`/`venv`
  interpreter resolution and per-project-style capability gating — Poetry/uv/Pipenv-managed
  projects get inspection only, never mutation), `NpmPackageAdapter` (real npm, with real
  direct/transitive dependency-tree parsing and package.json-sourced dev-dependency
  classification). pnpm/Yarn/Maven/Gradle/Cargo/Go Modules/vcpkg/Conan are explicitly deferred —
  not implemented, not faked.
- Added a Package Manager panel (`PackageManagerViewModel` + a new `MainWindow.axaml` tab:
  Installed/Browse/Updates/Dependencies) mirroring `SourceControlViewModel`'s shape — no
  per-language branch anywhere in the UI layer, only the generic capability contracts.
  Add/Remove/Update/Restore are gated by the same Workspace Trust mechanism Build/Run/Debug/Test/
  Git already use (`MainWindowViewModel.EnsurePackageWorkspaceTrustedAsync`); read-only
  inspection is never gated.
- Added full en-US/zh-TW localization for every new string (`PackageManager.*`/`Trust.Action.*
  {Installing,Removing,Updating,Restoring}Package(s)`/`Dialog.*Package*`/
  `Dialog.WorkspaceNotTrusted.PackageManager`), verified by the existing key-parity test plus new
  entries in the representative-cross-section test.
- **Found and fixed a real, pre-existing Phase 3 bug** while real-environment-testing the npm
  adapter: `NodePackageManagerToolchainDetector` probed the bare `"npm"` executable name, which
  Win32's `CreateProcess` cannot resolve to npm's real `.cmd` launcher (`.exe` is auto-appended,
  not `.cmd`) — this silently misreported a genuinely-installed npm as not installed on Windows. A
  second, compounding bug in `ExecutableLocator.FindOnPath` tried the bare name before `.exe`/
  `.cmd`/`.bat` on Windows, which on this real machine matched a non-Windows POSIX shell-script
  twin of `npm` that Node's own installer places alongside `npm.cmd`. Both fixed; see
  ADR-014. Verified via the existing 44 real toolchain-detector tests (unaffected) plus new real
  npm integration tests (previously failing with `Win32Exception`, now passing).
- Added 49 new tests: 14 Core unit tests (models/registry/service dispatch/single-flight-per-
  project), 21 Infrastructure unit tests (command construction + parsing of real, captured JSON
  samples per adapter, no process execution), and 14 real integration tests (temporary real .NET/
  Node/Python projects, real `dotnet`/`npm`/`pip` processes, real network calls to nuget.org/the
  npm registry — no fakes). Full suite: 556/556 passing (212 Core, 70 UI, 274 Infrastructure).
- Real-environment validation is Windows-only this phase (see ADR-014's Known Limitations) — the
  architecture is platform-neutral but Linux/macOS package-manager operations are implemented but
  not real-environment validated.

## Post-Phase-12 — Final Audit, Localization Verification & Coupling Analysis (2026-09-28)

- Performed a full audit pass (not a new feature phase): repository-wide coupling analysis,
  re-verification of Phase 12 localization, real Windows + real Linux/WSL2 regression, and a
  security-regression check — before considering any refactoring, per the audit's own required
  ordering.
- **Coupling analysis found the project-dependency graph, service ownership, and localization/
  settings/trust boundaries already clean** — zero circular project references; zero UI→
  Infrastructure references (structurally impossible, no project reference exists); zero Core→
  platform-specific code outside `Platform/PathComparer`; zero service-locator usage anywhere;
  zero duplicated `IProcessRunner`/`ILocalizationService`/`IUserSettingsStore` implementations;
  the 7 Workspace Trust check sites are intentional defense at each real execution boundary
  (Build/Run/Debug/Test/Git/LanguageServer), not duplicated logic. Only one real coupling hotspot
  was found and measured: `MainWindowViewModel` (1568 lines, a 20-parameter constructor — the only
  class in the entire `src/` tree with 8 or more constructor parameters; the next-largest file is
  453 lines). **Deliberately not split this pass** — seven other panels (Explorer/Output/Problems/
  Toolchains/SourceControl/Extensions) already follow the "extract a dedicated panel ViewModel"
  pattern; further splitting the remaining Build/Run/Debug/Test/LSP orchestration would require
  rewiring the ~700-line `MainWindow.axaml`'s bindings and the existing `MainWindowViewModelTests`
  test double, for a class with zero open defects and zero test failures — exactly the "giant
  rewrite with no concrete defect motivating it" this audit was explicitly told to avoid. Recorded
  as a known, factual, unresolved hotspot rather than silently left undocumented.
- **The localization unused-key audit found 3 genuinely dead resource keys**:
  `SourceControl.ContributedCommandsSeparator` (never wired to anything) and
  `Settings.Language.English`/`Settings.Language.TraditionalChinese` (superseded by the literal
  native-name strings already set directly on `MainWindowViewModel.LanguageOption`, per
  ADR-013). Removed from both `Strings.resx` and `Strings.zh-TW.resx` (255 keys each, still in
  full parity) and from the one test (`LocalizationServiceTests`) that still referenced one of
  them — a real, if minor, defect this audit caught and fixed, with the full suite re-verified
  green before and after (507/507).
- **Re-verified Phase 12's localization claims against real Linux (WSL2 Ubuntu 24.04)**, not just
  Windows: `DevStudio.Core.Tests` (198/198) and `DevStudio.UI.Tests` (70/70, including all 14
  `LocalizationServiceTests` and both language-switching tests) pass **identically** to Windows —
  real proof the `.resx`/satellite-assembly/`ResourceManager` mechanism is genuinely
  cross-platform, not Windows-only. `DevStudio.Infrastructure.Tests` reproduces the exact same
  environment-blocked failure categories Phase 11 already documented (`netcoredbg` not installed
  on this WSL2 image; the Roslyn language server integration tests time out on this
  resource-constrained VM) — 216/239 passing, 23 failed, none a Phase 12 regression.
- **Security regression check**: no new generic command-execution API, no secret-handling code,
  no `IExtensionContext` surface widening, no Workspace Trust bypass, no new
  `StringComparer.OrdinalIgnoreCase` usage against a real filesystem path (every remaining
  instance was individually re-inspected and confirmed to compare something other than path
  identity — file extensions, a VS Code extension folder-name prefix, a DAP stream-category
  string, and a display-name sort key).
- No architectural refactor beyond the dead-resource-key removal above was performed — the audit
  concluded the existing architecture was already appropriately decoupled, per this task's own
  explicit permission to report that outcome rather than manufacture a refactor.

## Phase 12 — Localization, UX Consistency & Release Readiness (2026-09-28)

- Added real, resource-based localization for exactly two UI languages, `en-US` (default) and
  `zh-TW` (Traditional Chinese) — no other language added, per explicit phase scope.
  `src/DevStudio.UI/Localization/Strings.resx` and `Strings.zh-TW.resx` (258 keys each,
  mechanically kept in parity) are ordinary MSBuild `EmbeddedResource` items; the SDK compiles
  the culture-suffixed file into a real satellite assembly with zero extra MSBuild configuration.
- Added `ILocalizationService`/`LocalizationService` (`DevStudio.UI.Localization`) —
  `CurrentCulture`/`SupportedCultures`/indexer/`GetString`/`Format`/`SetCulture`/`MissingKeys`,
  backed by a real `System.Resources.ResourceManager` so the missing-key-falls-back-to-en-US
  behavior is the actual .NET fallback chain, never hand-rolled. A genuinely unknown key returns
  `"[[key]]"` and is recorded in `MissingKeys` rather than ever returning null/empty silently.
  14 real unit tests, including one that dynamically writes real `.resources` files via
  `ResourceWriter` to prove the fallback chain against a real file-based `ResourceManager`, and
  one that enumerates each culture's own `ResourceSet` directly to prove real 258-key parity.
- Added `LocFormatConverter`, a small `IMultiValueConverter`, so parameterized localized messages
  (e.g. `"Ln {0}, Col {1}"`, `"Building '{0}' will execute ... (dotnet {1})..."`) can be bound
  through a `MultiBinding` rather than requiring a computed ViewModel property per message or an
  imperative `string.Format` call in a constructor.
- Added runtime language switching with no restart: `LocalizationService.SetCulture` raises the
  real WPF/Avalonia indexer-change notification (`PropertyChangedEventArgs("Item[]")`), which
  every `{Binding Loc[Key]}` binding in `MainWindow.axaml`/`SettingsWindow.axaml` picks up
  immediately.
- Localized the entire `MainWindow.axaml` UI surface: the full menu bar, toolbar, Explorer/
  Editor/Properties panel, all 13 bottom-panel tabs (Problems, Output, Terminal, Toolchains, Call
  Stack, Threads, Locals, Breakpoints, Completion, Hover, Tests, Source Control [Changes/
  Branches/History], Extensions), and the status bar. Natural Taiwan Traditional Chinese
  terminology throughout (建置/執行/偵錯/中斷點/呼叫堆疊/工作區/方案/專案/原始碼控制/提交/暫存/
  取消暫存/捨棄/分支/擴充功能/設定/問題/警告/錯誤/測試總管), not mechanical word-by-word
  translation.
- Localized every IDE-authored dialog/confirmation string: `DialogService.AskSaveChangesAsync`,
  every `_dialogService.ConfirmAsync`/`ShowErrorAsync` call site in `MainWindowViewModel` and
  `SourceControlViewModel` (~34 sites), all 7 Workspace-Trust confirmation prompts (Build/Run/
  Debug/LanguageServer/Test/Git), the Go To Line/Toggle Breakpoint input dialogs, and the native
  OS folder/file picker dialog titles (`FolderPickerService`/`FilePickerService`).
- Added a Settings window (`Views/SettingsWindow.axaml`, opened from Tools → Settings) with a
  language picker showing native self-names — "English"/"繁體中文", never the ambiguous
  "Chinese" — persisted through the existing `AppSettings.Language`/`IUserSettingsStore`
  mechanism (no second persistence system).
- Explicitly left untranslated, by design: every external tool's own stdout/stderr
  (`dotnet`/MSBuild/`git`/Roslyn/DAP/LSP/compiler/test-framework output) — translating this would
  make it actively less useful. Documented, not silently done: enum `ToString()` displays
  (severity/status/state enums) and `OutputPanelViewModel.Log(...)` message bodies remain
  English-only this phase — see `docs/adr/ADR-013-localization.md`'s Known Limitations.
- Added `docs/adr/ADR-013-localization.md`. Test count: 489 → 507 (18 new: 14
  `LocalizationServiceTests`, 2 `JsonUserSettingsStoreTests` (Language persistence), 2
  `MainWindowViewModelTests` (language switching/available languages)); 0 failed, 0 skipped.

## Phase 11 — Cross-Platform Support (2026-09-28)

- Audited the entire repository (`.exe`, `C:\`, `%APPDATA%`, `cmd.exe`/`powershell.exe`,
  `OperatingSystem.Is*`, `Environment.SpecialFolder`, `FileSystemWatcher`, `Kill`, and every
  `StringComparer`/`StringComparison` usage) for Windows-specific assumptions before changing any
  code. Found the codebase already largely cross-platform-correct from Phase 0 onward:
  `ExecutableLocator`/`ShellLocator` (Phase 1/3), `PythonToolchainDetector`'s `python`/`python3`
  fallback, every settings/extension store's use of `Environment.SpecialFolder`,
  `RoslynLanguageServerResolver`'s `.vscode/extensions` convention, `DotNetSolutionDetector`'s
  `.sln` backslash normalization, and `MsBuildDiagnosticParser`'s OS-agnostic path regex were all
  already written correctly; `VisualStudioDetector`'s Windows-only gating is correct as-is since
  Visual Studio itself is Windows-only.
- **The one real bug found**: roughly a dozen sites (file-change watching, the open-document
  table, breakpoints-by-source, project/solution path lookup, Git's per-repository mutation
  tracking, the extension entry-point traversal check, project ancestor/containment checks)
  hard-coded a case-*insensitive* `StringComparer.OrdinalIgnoreCase`/`StringComparison
  .OrdinalIgnoreCase` for real filesystem paths — correct on Windows/macOS, silently wrong on
  Linux's case-sensitive ext4, where `Foo.cs`/`foo.cs` are two different real files. Fixed by
  adding `Core.Platform.PathComparer` (`IsCaseInsensitive`/`Comparer`/`Comparison`/`Equals`,
  computed once from `OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()`) and updating
  every genuine path-identity site to consume it. Sites comparing something other than a real
  path (file extensions, known project-marker filenames, JSON capability names, Git's own stderr
  text) were deliberately left case-insensitive.
- **Real Linux validation, performed under WSL2 Ubuntu 24.04** (explicitly documented as WSL2,
  never claimed as native Linux desktop validation): a separate, real .NET 10.0.401 SDK was
  installed inside WSL2 (matching the Windows machine's SDK version exactly) via
  `dotnet-install.sh`; the whole solution — including the Avalonia `DevStudio.App` — builds with
  zero source changes. 465 of 473 runnable tests pass unmodified, excluding two long
  environment-blocked Debug/LSP integration suites (no `netcoredbg`/Roslyn language server
  installed in this Linux environment — a real, documented environment gap, not a code defect).
  The real Phase 10 Extension System lifecycle (discover → load → activate → invoke →
  deactivate, plus real failure isolation) passes unchanged on Linux with the exact same compiled
  sample extension assembly used on Windows.
- **A real GUI launch attempt on Linux, via WSLg** (a genuine `DISPLAY`/`WAYLAND_DISPLAY`):
  Avalonia's real X11 backend failed to initialize with a real `DllNotFoundException` for
  `libICE.so.6` — a missing native system library, not a DevStudio defect. Installing it requires
  `sudo`, unavailable this session; real Linux GUI rendering remains unverified as a result,
  reported honestly rather than worked around or claimed complete.
- **Two real, pre-existing test-portability bugs (not product bugs) were found and fixed only by
  actually running the suite on Linux**: `GitCliAdapterTests` hard-coded a Windows-style expected
  path (`@"C:\repo"`) for an adapter that correctly normalizes to *this OS's own* separator (fixed
  to expect `Path.DirectorySeparatorChar`-normalized output); a `ProcessRunnerTests` stdin test
  hard-coded bare `python` (absent on this real Ubuntu 24.04 image, which ships only `python3`;
  fixed to resolve either name via the existing `ExecutableLocator`, mirroring
  `PythonToolchainDetector`'s own real fallback).
- Added `docs/adr/ADR-012-cross-platform-support.md`.
- **macOS was not available and is not claimed as tested.**
- Added 4 tests: `PathComparerTests` (Core — asserts `PathComparer` against the real running
  platform's own `OperatingSystem.IsWindows`/`IsMacOS`, meaningful on every platform it runs on).
- Full solution rebuild (Debug + Release, 0 warnings/0 errors) and full suite rerun on Windows:
  Core 198 + UI 54 + Infrastructure 237 = 489 tests, 0 failures (0 regressions from Phase 10's
  485). App launch smoke test confirmed clean startup and clean shutdown with no orphan process
  on Windows; a real Linux console/build/test smoke test confirmed the same for the non-GUI path.

## Phase 10 — Extension System, Controlled Plugin Architecture (2026-09-28)

- Added `Core.Extensions`: `ExtensionId` (validated `publisher.name[.more]` identity, lowercase,
  max 128 chars), `ExtensionVersion`/`ExtensionVersionRange` (strict `Major.Minor.Patch` plus a
  minimal ANDed comparator range grammar) and `ExtensionHostInfo.HostVersion` (DevStudio's own
  extension-API version, `1.0.0`), `ExtensionManifest`/`ExtensionManifestParser` (pure,
  string-in/structured-diagnostics-out JSON manifest parsing — never throws for malformed input),
  `ExtensionCapability`/`ExtensionContributions`/`ExtensionCommandContribution`,
  `ExtensionState` (11-value lifecycle)/`ExtensionDescriptor`,
  `IDevStudioExtension`/`IExtensionContext`/`IExtensionCommandRegistrar` (the entire, deliberately
  minimal host API surface an extension ever receives), `ICommandRegistry`/`CommandRegistry` (the
  real global command table, first-registration-wins, invocation-failure-isolated),
  `IExtensionDiscovery`/`IExtensionLoader`/`ILoadedExtension`, and `ExtensionManager` (the
  orchestrator, mirroring `BuildService`/`TestService`/`GitService`'s shape).
- Added `Infrastructure.Extensions.FileSystemExtensionDiscovery` (real, bounded — one directory
  level per root, never recursive, never the whole filesystem — manifest scan) and
  `AssemblyLoadContextExtensionLoader` (real collectible `AssemblyLoadContext` loading).
- **Entry-point path safety is real, not assumed**: absolute paths, UNC paths, and any relative
  path whose resolved location lands outside the extension's own root are rejected before a
  manifest is ever considered valid — verified with real path-traversal and absolute-path
  integration tests.
- **Explicitly not a security sandbox**: an extension's code runs with DevStudio's own OS-level
  privileges. The real, structural safety boundary is that `IExtensionContext` exposes only
  command registration (scoped to the extension's own declared contributions) and logging — no
  reference to `IProcessRunner`, arbitrary UI, or any of Build/Run/Debug/Test/Git's services is
  ever reachable from anything an extension receives, so it cannot bypass Workspace Trust or
  execute a process merely by being installed.
- Added a real, separately-compiled sample extension project
  (`extensions/DevStudio.SampleExtension`): a harmless `SampleExtension` contributing one command
  (`sample.hello`), plus `ActivationFailingExtension`/`CommandFailingExtension` — two more real
  compiled fixtures used only by real integration tests to prove failure isolation.
- **A real cross-`AssemblyLoadContext` type-identity pitfall was designed around up front**: the
  sample extension's `ProjectReference` to `DevStudio.Core` is marked `Private="false"
  ExcludeAssets="runtime"` (never copy a duplicate `DevStudio.Core.dll` into the extension's own
  output), and `ExtensionAssemblyLoadContext.Load` always returns `null` so the runtime falls back
  to the host's already-loaded copy — verified working by the real full-lifecycle integration
  test successfully casting a genuinely separately-loaded instance to `IDevStudioExtension`.
- Added `AppSettings.DisabledExtensionIds` (default empty) — the only extension state ever
  persisted, through the existing JSON settings pipeline; never an extension instance, delegate,
  or other runtime object.
- Added `UI.ViewModels.ExtensionsPanelViewModel` and an Extensions tab/menu in `MainWindow.axaml`
  (discovered extensions with real state/validation-errors/failure-reason, Enable/Disable/Refresh,
  contributed-commands list with a real Invoke action).
- Added `docs/adr/ADR-011-extension-system.md`.
- Added 88 tests: `ExtensionIdTests`/`ExtensionVersionTests`/`ExtensionVersionRangeTests` (Core —
  identity/version/range parsing and comparison edge cases); `ExtensionManifestParserTests` (Core
  — every real validation branch: malformed JSON, missing fields, invalid id/version/range,
  unknown capability, duplicate command contribution id, command-without-capability, path
  traversal, absolute/UNC/non-`.dll` entry points); `CommandRegistryTests` (Core — registration,
  no-overwrite, per-owner unregistration, invocation, invocation-failure isolation);
  `ExtensionManagerTests` (Core, fake discovery/loader — duplicate-id rejection,
  disabled-extensions-never-loaded, activation-failure isolation, deactivation, refresh-never-
  regresses-Active); `ExtensionManagerIntegrationTests` (Infrastructure) — **all real**: full
  real lifecycle (discover → load → activate → invoke → deactivate) against the real compiled
  sample assembly, real activation-failure isolation (a genuinely broken compiled extension next
  to a genuinely working one), real command-invocation-failure isolation, real malformed/
  incomplete manifest rejection, real incompatible/malformed host-version-range rejection, real
  duplicate-extension-id rejection, real path-traversal/absolute-entry-point rejection, real
  enable/disable round-tripping, and refresh never disturbing an already-active extension;
  `ExtensionsPanelViewModelTests` (UI, fake discovery + working fake loader) — refresh/enable/
  disable/invalid-manifest-display/invoke-command.
- Full solution rebuild (Debug + Release, 0 warnings/0 errors) and full suite rerun: Core 194 +
  UI 54 + Infrastructure 237 = 485 tests, 0 failures. App launch smoke test confirmed clean
  startup (zero extensions discovered on this machine, no crash) and clean shutdown with no
  orphan process.

## Phase 9 — Git Integration, Real Git Workflow / CLI First (2026-09-28)

- Added `Core.Git`: `GitChangeType` (10-value, preserving porcelain v2's real index/worktree
  distinction)/`GitFileStatus`/`GitRepositoryStatus`/`GitBranch`/`GitCommit`/`GitDiffLineKind`/
  `GitDiffLine`/`GitDiffHunk`/`GitDiff`/`GitErrorKind`/`GitOperationResult`/`IGitAdapter`/
  `GitService`. `GitService` deviates from `BuildService`/`TestService`'s single global
  `CancellationTokenSource`: it tracks one per repository root (a `ConcurrentDictionary`), since a
  workspace may contain multiple independent repositories whose mutating operations must not block
  each other (SKILL.md §9, §34) — read-only operations are never gated by this lock at all.
- Added `Infrastructure.Git.GitCliAdapter`: resolves `git` from the existing `IToolchainRegistry`
  (Phase 3's `GitToolchainDetector`, already implemented, needed no changes); real repository
  detection via `git rev-parse --show-toplevel`; real status via NUL-delimited
  `git status --porcelain=v2 -z --branch` (verified empirically to survive the existing
  line-oriented `IProcessRunner` unmodified — no `RawStdio` mode needed); real log via a real
  unit/record-separator custom `--format`; real unified-diff parsing with real binary-file
  detection ("Binary files ... differ"); real stage/unstage/discard/commit/checkout/create-branch/
  delete-branch, every argument (paths, branch names, commit messages) passed as its own
  structured array element, never shell-concatenated.
- **A real bug found and fixed by a real `git mv` integration test**: the rename/copy status
  record parser initially split on 9 space-separated fields (an ordinary entry's count), but a
  real `"2"`-type (rename/copy) record has one extra field (`<X><score>`, e.g. `"R100"`) before
  the path — undercounting left `"R100 renamed.txt"` un-split as one field. Fixed by splitting
  into 10 fields; the original path is confirmed to be Git's own *separate*, next NUL-delimited
  record, not embedded in the same one.
- Checkout and branch deletion never force their way through a real Git refusal — a real
  conflicting-uncommitted-changes checkout refusal is mapped to `GitErrorKind.CheckoutBlocked`,
  and a real unmerged-commits deletion refusal to `GitErrorKind.BranchDeletionBlocked`, both with
  Git's own real message; verified by real integration tests asserting the real file
  content/branch existence are unchanged afterward. `DeleteBranchAsync` always uses `-d`
  (never `-D`), and the current branch can never be targeted for deletion.
- Added the fifth real Workspace Trust gate, covering every mutating Git operation (Stage/
  Unstage/Discard/Commit/CheckoutBranch/CreateBranch/DeleteBranch) since commit/checkout can
  trigger repository-defined hooks; repository detection/status/log/diff/branch listing are never
  gated, exactly like every prior phase's read-only operations.
- Added `UI.ViewModels.SourceControlViewModel` — unlike `ToolchainsPanelViewModel`/
  `ProblemsPanelViewModel`, it owns its own trust-gating and destructive-confirmation logic
  directly via delegates injected from `MainWindowViewModel`, since Git has substantially more
  mutating operations than any prior panel. Discovers the workspace root's own repository plus
  any repository one level of immediate nesting below it (SKILL.md §9's example); deeper nesting
  is not discovered — a documented limitation, not full multi-repository support.
- Added a Source Control tab (repository selector, commit message + Commit, Changes/Staged
  Changes with per-file Stage/Unstage/Discard and Stage All/Unstage All, a diff viewer, a
  Branches sub-tab with Create/Checkout/Delete, and a History sub-tab with commit metadata),
  replacing nothing (there was no Phase 0–8 Source Control placeholder). Double-clicking a
  changed file opens it through the existing editor/navigation system, not a second file-opening
  mechanism.
- Added `docs/adr/ADR-010-git-integration.md`.
- Added 37 tests: `GitServiceTests` (Core, fake adapter — dispatch, per-repository mutation
  gating, cross-repository non-blocking, cancellation → `OperationCancelled`, adapter-exception
  propagation, read-only operations never gated); `GitCliAdapterTests` (Infrastructure, fake
  process runner — command-construction assertions for every operation, real-captured-sample
  parsing tests for status/rename/log/diff/binary-diff, `GitNotInstalled` fast-fail,
  `NothingToCommit`/`CheckoutBlocked` real-message mapping, never passing `-D`);
  `GitCliIntegrationTests` — **all real**: repository detection (positive and negative), a real
  untracked file, real stage/commit producing a real commit and clean working tree, real
  modify/diff/stage-modification/unstage, real rename (asserting whatever Git itself actually
  reports), real delete, real branch create + checkout changing the real current branch, real
  checkout-safety (blocked by a real conflicting uncommitted change, file content proven
  unchanged), a real commit message with quotes/ampersand/backticks/Unicode read back unmodified,
  a real Unicode filename in status and diff, two real separate repositories reporting distinct
  roots, and a real unmerged-commits branch-deletion refusal.
- Full solution rebuild (Debug + Release, 0 warnings/0 errors) and full suite rerun: Core 125 +
  UI 49 + Infrastructure 223 = 397 tests, 0 failures. App launch smoke test confirmed clean
  startup and clean shutdown with no orphan process.

## Phase 8 — Test Explorer / Test Runner Integration, .NET First (2026-09-28)

- Added `Core.Testing`: `TestOutcome` (9-value)/`TestRunState` (8-value)/`TestCase`/`TestResult`/
  `TestRunResult`/`TestFilter` (structured, translates to VSTest's real `--filter` mini-language,
  never a raw shell string)/`ITestAdapter`/`TestService` (single-flight dispatcher, mirrors
  `BuildService`'s shape rather than `RunService`/`DebugService`'s long-lived-session shape, since
  a test run is one bounded operation; holds a `BuildService` reference for build-before-test).
- Removed the Phase 0 `Adapters/ITestAdapter.cs` stub — never implemented or referenced anywhere
  (same precedent as ADR-005/ADR-007/ADR-008's removal of the equally-unreferenced Phase 0
  `IBuildAdapter`/`IDebuggerAdapter`/`ILanguageAdapter` stubs) — superseded by the real
  `Core.Testing.ITestAdapter`.
- `ProjectInfo` gained a trailing optional `IsTestProject` (default `false`, mirroring
  `IsExecutable`'s precedent); `DotNetProjectDetector` sets it only from a real `PackageReference`
  to a known test package (`Microsoft.NET.Test.Sdk`/`xunit`/`NUnit`/`MSTest.*`), never from
  project naming conventions.
- Added `Infrastructure.Testing.DotNetTestAdapter`: discovers tests via a real
  `dotnet test --list-tests` invocation (parsed by a locale-independent four-space-indentation
  rule, verified against a real throwaway probe project) and runs them via a real TRX-logged
  `dotnet test` run, parsing the actual TRX XML VSTest writes for
  Passed/Failed/Skipped/Error outcomes and — only for a real failure whose stack trace actually
  contains one — a real source location.
- **A real bug found and fixed by the mandatory real cancellation integration test**:
  `IProcessRunner.RunAsync` reports cancellation via `ProcessResult.WasCancelled` rather than
  throwing; `DotNetTestAdapter` didn't check this, so a cancelled run's "no TRX produced" fallback
  error was mapped by `TestService` to `Failed` instead of `Cancelled`. Fixed by checking
  `WasCancelled` and re-throwing `OperationCanceledException` before the TRX-existence check;
  verified no orphan `dotnet`/`testhost`/`VSTest` process remains after cancellation via `ps -W`.
- Added the fourth real Workspace Trust gate, covering both test discovery *and* execution:
  `dotnet test --list-tests` performs a real build as a side effect, so discovery is gated
  identically to execution rather than treated as safe/read-only.
- Extended `ProblemsPanelViewModel` with a third, separately-tracked diagnostic source
  (`ReplaceTestDiagnostics`) — a completed run's failures wholesale-replace the previous run's,
  like Build's own diagnostics, and only failures with a real TRX-parsed source location ever
  become a `Diagnostic`.
- Added `UI.ViewModels.TestNodeViewModel` — a dedicated mutable per-row model (unlike Debug/
  Language's read-only panels, a test's displayed status genuinely mutates across
  NotRun → Running → Passed/Failed/Skipped).
- Added a Test Explorer tab (Refresh Tests/Run All/Run Selected/Stop, a live run-state label, and
  a results list) replacing the Phase 0–7 placeholder; double-clicking a row navigates to its real
  source location when one exists, reusing the existing `CaretMoveRequested` bridge.
- Added `docs/adr/ADR-009-test-explorer-and-runner.md`.
- **Only xUnit was created and exercised against real `dotnet test`/TRX output in this
  environment** — `DotNetTestAdapter` is framework-agnostic, so NUnit/MSTest are expected to work
  identically once detected, but this is reported honestly as Implemented, Not Real Tested rather
  than Real Tested, per the Anti-Fake Rule.
- Added 37 tests: `TestFilterTests` (Core — no-criteria/single/multiple FQN, trait filter,
  special-character escaping); `TestServiceTests` (Core, fake adapter — discovery raises
  `TestsDiscovered`, build-before-test success/failure/skip, no-adapter-for-project-type, adapter
  exception → Failed not thrown, overlap-protection, Cancel → Cancelled not Failed,
  `HasAdapterFor`); `DotNetProjectDetectorTests` additions (5 — real test-package detection for
  each known framework, negative cases for normal apps/libraries); `DotNetTestAdapterTests`
  (Infrastructure, fake process runner — argument-array assertions, a real-sample-based
  `--list-tests` parse test using an actual captured output string including a Theory-expanded
  name, `--no-build` presence/absence, structured filter translation, dotnet-not-detected
  fast-fail); `DotNetTestIntegrationTests` — **all real**: real discovery finding every real test,
  real Passed/Failed/Skipped outcomes with a real extracted failure source location, running a
  single/selected/filtered subset, a filter matching nothing running zero tests, real cancellation
  with no orphan process, a real compile error blocking the run before any test executes, and
  correct test-project isolation in a real multi-project workspace.
- Full solution rebuild (Debug + Release, 0 warnings/0 errors) and full suite rerun: Core 119 +
  UI 49 + Infrastructure 194 = 362 tests, 0 failures. App launch smoke test confirmed clean
  startup and clean shutdown with no orphan process.

## Phase 7 — LSP / Language Intelligence, C# First (2026-09-28)

- Added `Core.Rpc.RpcFramingException` and extracted `Infrastructure.Rpc.ContentLengthFrameReader`/
  `ContentLengthFrameWriter` (protocol-agnostic `Content-Length` framing, shared by DAP and LSP)
  out of Phase 6's `Infrastructure.Dap.DapFrameReader`/`DapFrameWriter`, which now delegate to
  them — their own public behavior and test suite are unchanged; a new
  `ContentLengthFrameReaderTests` exercises the shared class directly, including a large-payload
  case relevant to LSP's bigger completion-list responses.
- Added `Core.Lsp`: `JsonRpcMessage`/`JsonRpcRequest`/`JsonRpcResponse`/`JsonRpcNotification`/
  `JsonRpcError` (pure data)/`JsonRpcMessageSerializer`/`IJsonRpcTransport`/`JsonRpcClient`
  (assigns each request an id, correlates the eventual response regardless of arrival order,
  dispatches notifications, and — new relative to DAP — answers server-initiated requests like
  `workspace/configuration`/`client/registerCapability` with a safe default so a real server
  never stalls waiting for a response DevStudio has no opinion about). Kept as its own type from
  `Core.Dap.DapClient` rather than unified into one generic RPC client, since JSON-RPC's
  id-only-on-request/response shape genuinely differs from DAP's global `seq` — see ADR-008.
- Added `Infrastructure.Lsp.StreamJsonRpcTransport` (built on the shared `Rpc/` framing).
- Added `Core.Language`: `LanguageServerState` (7-value, its own state machine)/`LspPosition`/
  `LspRange`/`LspLocation`/`CompletionItem`/`LspTextEdit`/`HoverResult`/
  `LanguageServerResolution`/`ILanguageAdapter`/`ILanguageServerSession`/`LanguageService`
  (single-flight dispatcher, structurally mirrors `RunService`/`DebugService`, no `BuildService`
  dependency — opening/editing source never triggers a build).
- Removed the Phase 0 `Adapters/ILanguageAdapter.cs` stub — never implemented or referenced
  anywhere (same precedent as ADR-005/ADR-007's removal of the equally-unreferenced Phase 0
  `IBuildAdapter`/`IDebuggerAdapter` stubs) — superseded by the real `Core.Language.ILanguageAdapter`.
- Added `Infrastructure.Language.RoslynLanguageServerResolver`/`CSharpLanguageAdapter`/
  `RoslynLanguageServerSession`: launches a real `Microsoft.CodeAnalysis.LanguageServer --stdio
  --autoLoadProjects` process (the Roslyn language server bundled with the VS Code C# extension;
  MIT-licensed, no client-identity restriction unlike Phase 6's `vsdbg`) and drives it over LSP.
- **Two real, load-bearing findings from empirical investigation** (mirroring ADR-007's vsdbg/
  netcoredbg investigation): (1) `--autoLoadProjects` is a *process argument*, not an LSP
  parameter — without it the server never loads any `.csproj`, and every file stays a standalone
  "miscellaneous file" with only generic keyword completions; a real completion request for
  `person.` only returned the real member `Name` once this flag was added. (2) This server uses
  the LSP 3.17 *pull* diagnostics model (`textDocument/diagnostic`) exclusively — it never sends
  `publishDiagnostics`, no matter how long you wait. `RoslynLanguageServerSession` pulls
  diagnostics itself after every `didOpen`/`didChange` and exposes `RefreshDiagnosticsAsync` for
  when the very first pull races the server's own project-load completion.
- Full-document synchronization (`didChange` sends one content-change entry with no `range`) —
  a valid degenerate case of incremental sync per the LSP spec, accepted per SKILL.md's explicit
  permission for a reliable first implementation.
- Added a Language menu/toolbar (Trigger Completion/Show Hover/Go To Definition/Restart Language
  Server) and Completion/Hover panels; completion prefers a real `TextEdit` and falls back to
  `InsertText` only when the server didn't supply one; Go To Definition reuses the existing
  `CaretMoveRequested` editor-navigation bridge. Document changes are debounced (400ms) into one
  real `didChange` per pause rather than one per keystroke.
- Extended `ProblemsPanelViewModel` to track Build and per-file language-server diagnostics as
  separate sources merged into one displayed collection (`ReplaceBuildDiagnostics`/
  `ReplaceLanguageDiagnostics`) — rebuilding never erases a file's language-server diagnostics,
  and a file's new language-server diagnostics always replace (never append to) its previous
  ones, mirroring how Build's own diagnostics are replaced wholesale on every build.
- Added the third real Workspace Trust gate: starting the C# language server (which runs a real
  MSBuild design-time build) prompts once per workspace before ever launching; reading/editing
  source code as plain text remains allowed regardless of trust, unchanged from every prior phase.
- Added `docs/adr/ADR-008-lsp-language-intelligence.md`.
- **Explicitly deferred, not faked**: Find References, Document Symbols, Workspace Symbols,
  Signature Help.
- Added 54 tests: `JsonRpcClientTests`/`JsonRpcMessageSerializerTests` (Core, fake transport —
  id correlation, out-of-order responses, error responses, notification dispatch, server-request
  auto-answering, transport-failure fault-out); `LanguageServiceTests` (Core, fake adapter —
  lazy server start on first supported file, no-workspace guard, adapter exception → Failed not
  thrown, diagnostics forwarding, completion/hover/definition pass-through with empty-list/null
  defaults before any server starts, Stop/Restart); `ContentLengthFrameReaderTests`
  (Infrastructure, real adversarially-chunked `Stream` — including a large payload);
  `CSharpLanguageAdapterTests` (file-extension/workspace-missing fast-fail paths);
  `CSharpLanguageIntegrationTests` — **all real**: real server discovery, a real document open
  with real (empty) diagnostics, a real compile error producing real diagnostics at the correct
  location, real project-aware completion (`person.` → `Name`), real hover with real type
  information, real Go To Definition landing on the real class declaration, a real unsaved edit
  reflected by the server without ever touching disk, real shutdown, and real restart with a
  genuinely new server session. Plus new `MainWindowViewModelTests`/`ProblemsPanelViewModelTests`
  coverage for the trust gate, non-`.cs`-file skip, completion plumbing, diagnostic-source
  separation, and the Restart command.

## Phase 6 — Debug System, DAP-Based .NET Debugging (2026-09-28)

- Added `Core.Dap`: `DapProtocolMessage`/`DapRequest`/`DapResponse`/`DapEvent` (pure data,
  `JsonNode` bodies)/`DapMessageSerializer`/`IDapTransport`/`DapClient` (assigns each request a
  unique `seq`, correlates the eventual response by `request_seq` regardless of arrival order,
  dispatches unsolicited events) — pure protocol-orchestration logic, unit-testable with a fake
  transport and no real process.
- Added `Core.Debug`: `DebugSessionState` (7-value, its own state machine — `Paused` is a state a
  plain run never has)/`Breakpoint`/`BreakpointVerification`/`ThreadInfo`/`StackFrameInfo`/
  `Scope`/`Variable`/`StoppedInfo`/`DebugConfiguration` (wraps `Run.RunConfiguration`)/
  `DebugResult`/`IDebuggerAdapter`/`IActiveDebugSession`/`DebugService` (single-flight dispatcher,
  structurally mirrors `RunService`, holds a `BuildService` reference for build-before-debug).
- Removed the Phase 0 `Adapters/IDebuggerAdapter.cs` and `Adapters/Breakpoint.cs` stubs — never
  implemented or referenced anywhere (same precedent as ADR-005's removal of the Phase 0
  `IBuildAdapter` stub) — superseded by the real `Core.Debug` versions.
- Added `Infrastructure.Dap`: `DapFrameReader`/`DapFrameWriter` implement real
  `Content-Length: <n>\r\n\r\n<n bytes>` framing directly over a `Stream` (never
  `StreamReader.ReadLine()`), correctly handling a header/payload split across reads and multiple
  messages already sitting in one read; `StreamDapTransport` composes them into `IDapTransport`.
- Extended `Core.Processes.ProcessStartRequest` with `RawStdio` (default `false`) and
  `IRunningProcess` with `StandardInput`/`StandardOutput` (`Stream?`, null unless `RawStdio` was
  set) — DAP's binary framing cannot go through the existing line-buffered stdout handling, so
  `IProcessRunner` was extended rather than building a second process-execution abstraction.
  Every existing caller is unaffected.
- Added `Infrastructure.Debug.NetCoreDebuggerResolver`/`NetCoreDebuggerAdapter`/
  `NetCoreDebugSession`: launches a real `netcoredbg --interpreter=vscode` process and drives it
  over DAP, resolving the real built assembly to launch by scanning
  `bin/<Configuration>/*/<ProjectName>.dll` (never a guessed executable name — mirrors Phase 5's
  Run adapter).
- **Real, load-bearing finding**: `vsdbg` is genuinely present on this development machine
  (installed by the VS Code C# extension, not by either installed Visual Studio instance), and a
  real DAP `initialize` exchange with it succeeds — but its own license enforces a
  client-identity handshake that refuses a non-Visual-Studio-Code/Visual-Studio client, failing
  at `configurationDone` with its own internal "handshake" error right after printing a banner
  restricting its use to Microsoft's own products. DevStudio does not attempt to bypass this.
  **New dependency, user-approved**: `netcoredbg` (Samsung, MIT license, installed via
  `winget install Samsung.NetCoreDbg`) was added instead — same DAP-over-stdio contract, no
  client-identity restriction — specifically to obtain genuine real-debugger validation this
  phase. See `docs/adr/ADR-007-debug-system.md`.
- Verified the real DAP handshake ordering against netcoredbg (not assumed): `initialize` (await)
  → `launch` (send, don't await yet) → wait for the real `initialized` event (bounded to 10
  seconds — a robustness fix the vsdbg investigation's hang demonstrated the necessity of) →
  `setBreakpoints` → `configurationDone` (await) → confirm the deferred `launch` response.
- **Fixed a real exit-code-capture bug caught by a real integration test**: DAP's `exited`
  (carries the exit code) and `terminated` (session-end signal) are separate events, and
  netcoredbg sends `terminated` first — the exit code is now captured from `exited` into a field
  and consumed whenever the session actually finalizes on `terminated`, rather than being lost to
  whichever event happened to fire first.
- **Fixed a real deadlock/orphan-file-handle bug caught by a real integration test**: an
  adapter-initiated termination (the debuggee exits on its own) now escapes to a background
  `Task.Run` for cleanup instead of disposing the `DapClient` inline from inside that same
  client's own event-dispatch callback, which was the read loop awaiting itself.
- Added Debug menu/toolbar (Start Debugging/Continue/Pause/Step Over/Step Into/Step Out/Stop) and
  Call Stack/Threads/Locals/Breakpoints panels; stack-frame selection navigates the editor to the
  real source/line via the existing `CaretMoveRequested` bridge (ADR-002). Breakpoints are
  toggled via a line-number prompt (reusing "Go To Line"'s exact input-dialog pattern) since the
  plain `TextBox` editor has no gutter — a documented UI limitation, not an oversight.
- Added the second real Workspace Trust gate for Debug, checked before the build-before-debug
  branch so it applies even if a future UI exposes `BuildBeforeDebug = false`.
- Added `docs/adr/ADR-007-debug-system.md`.
- Added 49 tests: `DapClientTests`/`DapMessageSerializerTests` (Core, fake transport —
  correlation, out-of-order responses, event dispatch, malformed input, transport-failure
  fault-out); `DebugServiceTests` (Core, fake adapter — build-before-debug, build failure blocks
  debug, no-adapter path, adapter exception → Failed not thrown, overlap protection, breakpoints
  sent on start, Stopped → Paused with current thread, Continue/StepOver forward the current
  thread, Stop → Terminated); `DapFrameReaderTests` (Infrastructure, a real `Stream` handing back
  adversarially-chunked bytes — single/multiple/split-header/split-payload messages, malformed
  Content-Length, clean vs. mid-header vs. mid-payload stream endings); `NetCoreDebuggerAdapterTests`
  (not-built/missing-working-directory/ambiguous-output-directory error paths, project-type
  support); `NetCoreDebugIntegrationTests` — **all real**: a real breakpoint hit with real
  locals and call stack, a real Step Over then Continue to a real exit with the correct exit
  code, a real Step Into landing inside the real helper method, a real Stop terminating both
  netcoredbg and the debuggee with no orphan process, a real deliberate compile error blocking
  debug before the debugger ever launches, and a real "never built" failure when debugging
  without building first.

## Phase 5 — Run System, .NET First (2026-09-28)

- Added `Core.Run`: `RunStatus` (8-value, its own state machine — never `BuildStatus`
  reused)/`RunConfiguration`/`RunResult`/`IRunAdapter`/`IRunningApplication`/`RunService`
  (single-flight dispatcher, structurally mirrors `BuildService`, holds a `BuildService`
  reference for build-before-run rather than duplicating build logic or calling `dotnet build`
  directly).
- Added `ProjectInfo.IsExecutable` (new field, defaults to `false` so all seven pre-existing
  detectors compile and behave unchanged by omission). `DotNetProjectDetector` (now `async`)
  computes it from the real `<OutputType>` (`Exe`/`WinExe`) or an `Sdk="...Web"` project file —
  never assumed from `ProjectType.DotNet` alone. A class library is never offered as a run
  target.
- Added `Infrastructure.Run.DotNetRunAdapter`/`DotNetRunningApplication`: launches real
  `dotnet run --project <target> -c <configuration> --no-build [-- <args>]` rather than
  resolving a built artifact's path directly (avoids guessing the output binary's name across
  platforms); checks a real, name-agnostic "has anything been built" signal before launching and
  fails with a clear message instead of a confusing raw `dotnet run` error; distinguishes
  `RunStatus.Exited` (process ended on its own) from `RunStatus.Terminated` (a deliberate
  `Stop()`) via a `_stopRequested` flag.
- Added Run/Run Without Building/Stop/Restart to a new Run menu and toolbar (with a run
  configuration selector auto-populated from each workspace's runnable projects), and run status
  in the status bar.
- Added the second real Workspace Trust gate: running an untrusted workspace's application
  prompts for explicit confirmation, checked *before* the build-before-run branch so it applies
  even when `BuildBeforeRun` is false.
- Stop/Restart reuse Phase 1/4's `IRunningProcess.Kill(entireProcessTree: true)` unchanged — no
  new process-killing code was needed. Verified with real processes: one that records its own OS
  process ID to prove `Stop()` actually kills the real child application (not just the `dotnet
  run` wrapper), and one that checks a PID-based lock file to prove `RestartAsync` never starts a
  second instance before the first has truly exited.
- Added `docs/adr/ADR-006-run-system.md`.
- **Known limitation, documented rather than silently worked around**: run configurations
  (including any environment variables entered for a run) are not persisted this phase — SKILL.md
  explicitly permits deferring persistent secret storage; a future phase must revisit this before
  adding any on-disk persistence for run environment variables.
- Added 28 tests: `RunServiceTests` (Core, fake adapter — build-before-run, build failure blocks
  run, `BuildBeforeRun=false` skip, no-adapter/no-build-invoked, adapter exception →
  `FailedToStart` not thrown, overlap protection, Stop → `Terminated`, Restart → never two
  instances, `HasAdapterFor`); `DotNetRunAdapterTests` (fake process runner — exact argument
  arrays including a preserved multi-word argument, environment/working-directory/toolchain/
  not-built error paths); `DotNetProjectDetectorTests` additions for `IsExecutable` (`Exe`,
  `WinExe`, plain library, explicit `Library`, ASP.NET Core Web SDK); `DotNetRunIntegrationTests`
  — **all real**: a real built application starting and printing its own arguments/environment
  variable/working directory, real exit codes 0 and 7, a real `Stop()` verified by checking the
  real child application's OS process ID is actually gone, a real `Restart()` verified by a
  PID-lock-checking console app that never reports a conflict, a real deliberate compile error
  blocking Run before the application ever starts, and a real "never built" failure when running
  without building first.

## Phase 4 — Build System, .NET First (2026-09-28)

- Added `Core.Build`: `BuildOperation`/`BuildStatus` (7-value, never a bool)/`BuildTarget`
  (Project vs Solution)/`BuildRequest`/`BuildResult`/`IBuildAdapter`/`BuildService`
  (single-flight dispatcher with per-adapter routing and minimal in-memory history) and
  `MsBuildDiagnosticParser` (pure logic — real MSBuild/Roslyn line parsing, including
  location-less project-level diagnostics like `MSB4025`).
- Replaced the never-implemented, never-referenced Phase 0 `Adapters/IBuildAdapter` stub outright
  (per SKILL.md §4's "inspect references before evolving" — there were none to preserve).
- Added `Infrastructure.Build.DotNetBuildAdapter`: real `dotnet restore`/`build`/`clean`
  invocations, plus Rebuild as a single `dotnet build --no-incremental` call (not a two-step
  Clean+Build); resolves the `dotnet` executable from the live `IToolchainRegistry`, returning
  `BuildStatus.Unavailable` rather than attempting a doomed launch if it's not detected.
  Deduplicates diagnostics (MSBuild's console logger genuinely repeats each one in its
  end-of-build summary — caught by a real integration test).
- Added Build/Rebuild/Clean/Restore/Cancel to the Build menu and toolbar, a Debug/Release
  configuration selector, and build status in the status bar. Problems panel diagnostics are now
  clickable and navigate to the real file/line/column via the existing caret-move bridge.
- Added the first real Workspace Trust gate: building an untrusted workspace prompts for
  explicit confirmation before any project-defined command runs.
- **Fixed two real bugs caught by real (not fake) integration tests**: (1) process output was
  mis-decoded under the OS's legacy codepage — `dotnet`'s UTF-8 output came back as mojibake;
  fixed by adding an opt-in `ProcessStartRequest.OutputEncoding` (default `null`), set only by
  `DotNetBuildAdapter`, after a blanket global fix was tried and broke `vswhere.exe`'s real
  detection (it doesn't emit UTF-8). (2) MSBuild repeats every diagnostic in its summary —
  deduplicated in `DotNetBuildAdapter`.
- Added `docs/adr/ADR-005-build-system.md`.
- **Environment finding**: a hand-written `.sln` missing the `Global`/`GlobalSection` footer is
  sufficient for Phase 2's lightweight detector but makes a real `dotnet build` silently build
  zero projects ("No restorable projects found!"). Documented in `DEVELOPMENT.md`.
- Added 37 tests: `MsBuildDiagnosticParserTests`/`BuildServiceTests` (Core, fake adapters);
  `DotNetBuildAdapterTests` (fake process runner — exact argument arrays for every operation,
  toolchain-unavailable behavior); `DotNetBuildIntegrationTests` — **all real**: a real temporary
  project building successfully, a deliberate compile error producing a structured diagnostic
  with real file/line, fixing it and building again, a real two-project solution build, real
  Restore/Clean, and a real cancellation test that verifies the `dotnet` process actually
  terminates.

## Phase 3 — Toolchain Detection & Registry (2026-09-27)

- Added `Core.Toolchains`: `IToolchainDetector`/`IToolchainRegistry`/`IVisualStudioDetector`,
  `ToolchainInfo`/`ToolchainDetectionState`/`ToolchainCapability`, `VisualStudioInstance`,
  `WellKnownToolchainIds`, `ToolchainRequirements`, `CapabilityAvailability`/`ProjectCapability`/
  `ProjectCapabilityMatcher`, `EnvironmentSnapshot`.
- Added 15 real toolchain detectors in `Infrastructure.Toolchains` (.NET, MSVC via Visual
  Studio, Python, Node.js + npm/pnpm/yarn, Java, CMake, GCC, Clang, Rust, Go, Git, Docker) plus
  `ToolchainProbe` (safe, timeout-bounded, typed-failure version probing on top of the existing
  `IProcessRunner` — never a new execution path), `ExecutableLocator` (PATH resolution without
  assuming `.exe`), `ToolchainRegistry` (concurrent detection, per-detector failure isolation),
  and `VisualStudioDetector` (`vswhere.exe`-based, multiple-instance-aware).
- Changed `ProjectInfo.Capabilities` from always-empty `IReadOnlyList<string>` to
  `IReadOnlyList<ProjectCapability>`, now populated by `ProjectCapabilityMatcher` against
  actually-detected toolchains. `Debug` is never claimed as a capability by anything (see
  ADR-004) — a version probe succeeding is not a justified source for it.
- Added a Toolchains panel (bottom tab) and Tools → Refresh Toolchains; project capabilities in
  the Explorer/Properties panel re-apply automatically after a refresh, without re-scanning the
  filesystem.
- Added `docs/adr/ADR-004-toolchain-detection.md`.
- **Corrected a wrong Phase 0 assumption**: real detection found neither Visual Studio instance
  on this machine has the MSVC C++ toolset installed, despite Phase 0's ADR-001 assuming VS
  2026 Enterprise "includes MSVC." Both `docs/adr/ADR-001-ui-framework.md` and
  `DEVELOPMENT.md`'s environment table were corrected with this finding.
- Added 52 tests: `ProjectCapabilityMatcherTests` (Core); `ToolchainProbeTests`,
  `MissingToolchainTests`, `ToolchainDetectorVersionParsingTests`, `ToolchainRegistryTests`,
  `VisualStudioDetectorTests` (all fake-`IProcessRunner`-based), and
  `RealEnvironmentToolchainTests` (actually runs every detector against this real machine,
  including a real `vswhere.exe` call — 13 tests, all passing, confirming the corrected
  MSVC finding above).

## Phase 2 — Workspace & Project System (2026-09-27)

- Added adapter-based project detection: `IProjectDetector` (Core) plus eight real detectors
  (Infrastructure) for .NET (project + solution/.sln/.slnx), CMake/Make/Meson, Node, Python,
  Java/Maven/Gradle, Rust, Go — metadata discovery only, never invoking the ecosystem's own
  tooling.
- Added `ProjectDetectionService`: bounded, exclusion-aware recursive scan; builds a
  parent/child project hierarchy by directory containment; resolves solution → project
  references (including stubbing out a reference that can't be found, instead of dropping it);
  falls back to a single Generic Folder Project when nothing is detected anywhere.
- Redesigned `ProjectInfo` (Id, ProjectType, Languages, ConfigurationFiles, ChildProjects,
  DetectionConfidence, DetectionWarning) and added `SolutionInfo`; extended `WorkspaceModel`
  (Id, Name, Solutions, ActiveProjectId).
- Explorer now shows `[Solution]`/`[ProjectType]` markers on directories a detector recognized;
  a basic Properties panel shows the selected project/solution's metadata; the status bar and
  each open document show which project owns the file (`ProjectGraphLookup.FindOwningProject`).
- Added workspace state persistence (`WorkspaceState`/`IWorkspaceStateStore`, JSON at
  `<root>/.devstudio/workspace.json`, versioned) — reopening a workspace restores its open tabs
  and active document/project. Added user-settings persistence (`IUserSettingsStore`, JSON under
  the user's application-data directory) — theme, Recent Workspaces, and "reopen last workspace
  on startup" now survive a restart. Both treat a missing/corrupted file as "start fresh,"
  never as a crash.
- Added File → Recent Workspaces, File → Reopen Last Workspace on Startup, and Project →
  Trust/Untrust Workspace to the shell.
- Added `docs/adr/ADR-003-workspace-persistence.md` (JSON via `System.Text.Json` — no new
  dependency; per-workspace vs. per-user file split; versioning and corruption-handling policy).
- Added 49 tests: `ProjectDetectionServiceTests` (Core, with local fake detectors — generic
  fallback, nesting, monorepo, solution reference resolution including a missing reference);
  per-detector tests plus a real-filesystem monorepo/solution integration suite
  (`DevStudio.Infrastructure.Tests`); `JsonWorkspaceStateStoreTests`/`JsonUserSettingsStoreTests`
  (round-trip, missing, corrupted, version-mismatch); `ProjectGraphLookupTests`; and
  `MainWindowViewModel` tests for the full open/detect/restore/save-state pipeline.

## Phase 1 — Application Shell (2026-09-27)

- Added `DevStudio.Infrastructure` (concrete `ProcessRunner`, `WorkspaceScanner`,
  `TextFileService`, `FileChangeWatcher`, `TerminalSession`/`TerminalSessionFactory`,
  `ShellLocator`, `InMemorySettingsService`) implementing the Core abstractions.
- Added `DevStudio.UI` (ViewModels + application services: `MainWindowViewModel`,
  `WorkspaceExplorerViewModel`, `FileTreeNodeViewModel`, `DocumentViewModel`,
  `TerminalViewModel`, `OutputPanelViewModel`, `ProblemsPanelViewModel`, `StatusBarViewModel`,
  `WorkspaceAppService`, `DocumentAppService`, `TextSearchService`).
- Added `DevStudio.App`, a real Avalonia desktop application: main window with
  File/Edit/View/Project/Build/Debug/Test/Git/Tools menu, toolbar, Explorer, tabbed editor,
  Problems/Output/Terminal/Debug Console/Tests bottom panel, status bar, Dark/Light theme
  toggle.
- Extended Core: `IWorkspaceScanner`/`FileSystemNode`/`WorkspaceExclusionRules`,
  `ITextFileService`/`TextEncodingKind`/`LineEndingKind`, `IFileChangeWatcher`,
  `ITerminalSession`/`ITerminalSessionFactory`, `ISettingsService`/`AppSettings`/`AppTheme`;
  added `IRunningProcess.WriteInputAsync` and `ProcessStartRequest.RedirectInput` for
  interactive terminal support.
- Added `docs/adr/ADR-002-phase1-ui-dependencies.md` (CommunityToolkit.Mvvm; plain `TextBox`
  editor instead of AvaloniaEdit, which pins an incompatible pre-1.0 Avalonia dependency; no
  docking library).
- Added `DevStudio.Infrastructure.Tests` (integration tests against real temp directories and
  real child processes) and `DevStudio.UI.Tests` (ViewModel/service tests against in-memory
  fakes).
- Fixed a static-initialization-order bug in `WorkspaceExclusionRules` (caught by actually
  running the app, not just by the test/build gate — see Phase 1 completion report).

## Phase 0 — Architecture (2026-09-27)

- Selected .NET + Avalonia as the UI framework (`docs/adr/ADR-001-ui-framework.md`).
- Scaffolded `DevStudio.slnx` with `src/DevStudio.Core` and `tests/DevStudio.Core.Tests`.
- Added Core domain models and adapter interfaces: `IProcessRunner`, `IToolchainDetector`,
  `IToolchainRegistry`, `IProjectAdapter`, `IBuildAdapter`, `ITestAdapter`, `IDebuggerAdapter`,
  `ILanguageAdapter`, `Diagnostic`, `DevStudioException`/`DevStudioErrorKind`, `WorkspaceModel`,
  `ProjectInfo`, `BuildConfiguration`.
- Added `README.md`, `ARCHITECTURE.md`, `DEVELOPMENT.md`, `SECURITY.md`, `CLAUDE.md`.
