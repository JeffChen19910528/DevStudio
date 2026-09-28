# Development Guide

## Building and Testing

```bash
dotnet build DevStudio.slnx
dotnet test DevStudio.slnx
```

Requires the .NET 10 SDK (`dotnet --list-sdks` should show a `10.x` entry).

## Solution Layout

- `src/DevStudio.Core` — domain models and adapter interfaces. No UI, no concrete implementation.
- `src/DevStudio.Infrastructure` — concrete, platform-touching implementations of the Core
  abstractions (real process execution, filesystem, terminal, settings).
- `src/DevStudio.UI` — ViewModels and application services (Avalonia + CommunityToolkit.Mvvm,
  no other platform dependency).
- `src/DevStudio.App` — the Avalonia executable and composition root (Views, `Program.cs`,
  `App.axaml.cs` wires concrete `Infrastructure` types to `UI`/`Core` abstractions).
- `tests/DevStudio.Core.Tests`, `tests/DevStudio.Infrastructure.Tests`,
  `tests/DevStudio.UI.Tests` — see `ARCHITECTURE.md` for what each covers. Project/solution
  detectors and the workspace/settings persistence stores have real-filesystem integration
  tests in `DevStudio.Infrastructure.Tests`, not just fakes — when adding a new detector or
  changing a persisted record's shape, add or update those before the fake-based ones.
  Toolchain detectors have both: fake-`IProcessRunner`-based unit tests (for the missing-tool
  and malformed-output paths) and real-environment tests that actually run `dotnet`/`git`/etc.
  on this machine — a toolchain becoming newly available or newly missing on the dev machine
  will fail those loudly rather than silently. `DotNetBuildAdapter` has the same split, plus
  `DotNetBuildIntegrationTests` which build (and deliberately break, then fix) real temporary
  `.csproj`/`.sln` files on disk — nothing there ever touches the DevStudio repository itself.
  `DotNetRunAdapter` follows the same split: `DotNetRunAdapterTests` (fake `IProcessRunner`,
  exact-argument-array assertions) plus `DotNetRunIntegrationTests`, which build and run real
  temporary console applications — including one that records its own OS process ID to prove
  `Stop()` really kills it, and one that checks a PID-based lock file to prove `Restart()` never
  runs two instances at once. The DAP layer has its own split: `DapClientTests`/
  `DapMessageSerializerTests` (Core, fake `IDapTransport` — correlation, out-of-order responses,
  malformed input) plus `DapFrameReaderTests` (Infrastructure, a real `Stream` handing back
  adversarially-chunked bytes — no process needed for byte-level framing correctness) plus
  `NetCoreDebugIntegrationTests`, which build real temporary console apps and debug them through
  a real `netcoredbg` process: real breakpoint hit, real locals, real call stack, real
  step-over/step-into, real continue-to-exit, real Stop with no orphan process. The LSP layer
  reuses the DAP layer's shared framing (`ContentLengthFrameReaderTests`) plus its own
  `JsonRpcClientTests`/`JsonRpcMessageSerializerTests` (Core, fake `IJsonRpcTransport`), and
  `CSharpLanguageIntegrationTests`, which build real temporary C# projects and drive them through
  a real `Microsoft.CodeAnalysis.LanguageServer --stdio --autoLoadProjects` process: real
  diagnostics (including for unsaved, in-memory-only edits), real project-aware completion, real
  hover with real type information, real Go To Definition, real shutdown, real restart.

See `ARCHITECTURE.md` for the full layout and the MVVM boundary between these projects.

New projects are added to `DevStudio.slnx` via `dotnet sln DevStudio.slnx add <path>`.

## Running the app

```bash
dotnet run --project src/DevStudio.App/DevStudio.App.csproj
```

## Phase Discipline (from SKILL.md §38, §42)

Work proceeds through the phases defined in `SKILL.md` §38, in order:

```
0 Architecture → 1 App Shell → 2 Workspace → 3 Toolchains → 4 Build → 5 Run →
6 Debug → 7 LSP → 8 Tests → 9 Git → 10 Extensions → 11 Cross-Platform → 12 Performance
```

Rules that apply to every phase:

1. Do not implement large amounts of code without validating the architecture first (an ADR,
   or at least a short design note, before a new subsystem).
2. Do not create fake implementations merely to make tests pass. Use explicit, clearly-named
   `Fake*` mock toolchains (SKILL.md §40) for deterministic CI tests when a real toolchain isn't
   installed — never silently stub out real logic.
3. Do not claim a compiler/debugger/toolchain is supported unless it has actually been
   installed and exercised in this environment. "Implemented but not real-environment
   validated" is the honest and acceptable phrasing when it wasn't (SKILL.md §43).
4. Every phase ends with: build, test, static analysis, security review, documentation, and a
   Phase Completion Report using the exact headers in SKILL.md §43.
5. Do not silently add a new dependency — document package, version, purpose, license, and
   security considerations.
6. Do not break existing functionality while adding a new adapter.
7. Prefer interfaces and dependency inversion — new toolchain/language/debugger support should
   be addable without modifying `DevStudio.Core`.
8. No platform-specific code in `DevStudio.Core` — isolate it in `DevStudio.Platform` (added
   when the first platform-specific need arises).
9. Never execute a project's own commands (build scripts, pre/post-build hooks, package
   installs) just because a workspace was opened — that requires Workspace Trust (SKILL.md §26).
10. Never store secrets in code, config, tests, logs, or docs.

## Environment Notes (read-only inspection, SKILL.md §44)

Updated in Phase 3 with real automated detection (`RealEnvironmentToolchainTests`), which
corrected one Phase 0 assumption below. This development machine has:

| Tool | Available |
|---|---|
| .NET SDK | Yes (10.0.401) |
| Visual Studio 2026 Enterprise + 2019 Community (Roslyn, MSBuild) | Yes — both discoverable via `vswhere.exe`; MSBuild present for both |
| **MSVC (C++ toolset)** | **No** — corrected in Phase 3. Neither VS instance has a `VC\Tools\MSVC` directory; the C++ workload was never installed. Phase 0's note that VS 2026 Enterprise "includes MSVC" was never actually verified and was wrong |
| Node.js / npm | Yes |
| Python | Yes (3.11.6, as `python3`) |
| Java | Yes (25 LTS) |
| Git | Yes (2.50.1) |
| Docker | Yes (29.8.0) |
| CMake, Rust (rustc/cargo), Go, standalone GCC/Clang | Not installed |
| **`netcoredbg`** (real .NET DAP debugger DevStudio actually uses — Phase 6) | Yes — installed via `winget install Samsung.NetCoreDbg`; resolved at `%LOCALAPPDATA%\Microsoft\WinGet\Packages\Samsung.NetCoreDbg_*\netcoredbg\netcoredbg.exe` |
| `vsdbg` (present, but not usable by DevStudio — Phase 6) | Present via the VS Code C# extension's private install, **not** via either installed Visual Studio instance. Its own license enforces a client-identity handshake that refuses non-Visual-Studio-Code/Visual-Studio clients — see ADR-007. DevStudio does not attempt to bypass this, so `NetCoreDebuggerResolver` does not resolve it |
| **`Microsoft.CodeAnalysis.LanguageServer`** (real C# LSP server DevStudio actually uses — Phase 7) | Yes — bundled with the VS Code C# extension (already present for Phase 6's investigation); resolved at `~/.vscode/extensions/ms-dotnettools.csharp-*/.roslyn/Microsoft.CodeAnalysis.LanguageServer.exe`. MIT-licensed, no client-identity restriction — see ADR-008 |
| **`dotnet test` / VSTest (real .NET test infrastructure DevStudio actually uses — Phase 8)** | Yes — part of the .NET SDK already listed above; `dotnet new xunit` on this SDK produces a classic VSTest-based xUnit 2.9.3 project (not the newer Microsoft.Testing.Platform) — see ADR-009. Only xUnit was actually created and run here; NUnit/MSTest were never installed/exercised in this environment |
| **`git` (real Git executable DevStudio actually uses — Phase 9)** | Yes, 2.50.1 (already listed above from Phase 3's detection) — `--porcelain=v2 -z`/`--format`-based log/status/branch output all verified against this exact installed version before writing `GitCliAdapter`'s parsers; see ADR-010 |
| **Extension loading (Phase 10)** | Uses the .NET 10 SDK already listed above — `System.Runtime.Loader.AssemblyLoadContext` (collectible) is part of the runtime, no separate install; verified against a real, separately-compiled extension assembly (`extensions/DevStudio.SampleExtension`), not merely assumed to work from documentation — see ADR-011 |
| **WSL2 Ubuntu 24.04 (real Linux validation environment — Phase 11)** | Yes — a separate .NET 10.0.401 SDK was installed inside WSL2 specifically for this phase (`dotnet-install.sh --channel 10.0`), matching the Windows machine's version exactly; real `git` 2.43.0 already present. **This is WSL2, not a native Linux desktop** — see ADR-012 and the Known Limitations below for exactly what was and wasn't validated there. |
| **macOS** | **Not available in this environment** — no real macOS validation was performed or claimed this phase. See ADR-012. |
| **Avalonia GUI launch on real Linux (WSLg)** | **Blocked, not verified** — WSLg provides a real `DISPLAY`/`WAYLAND_DISPLAY`, but the real Avalonia X11 backend failed to initialize with a real `DllNotFoundException` for `libICE.so.6` (a missing native X11 "Inter-Client Exchange" library, not installed by default on this Ubuntu WSL image). Installing it (`sudo apt-get install libice6`) requires root; no `sudo` password was available in this session, so this remains an environment gap, not a resolved finding — see ADR-012. |

Any adapter for a tool not listed as available must be built against that tool's documented
behavior and marked "not real-environment validated" until it is actually installed and run
here, per SKILL.md §43. This table itself is now backed by an automated test suite
(`DevStudio.Infrastructure.Tests/Toolchains/RealEnvironmentToolchainTests.cs`) rather than a
one-time manual note — run it after any environment change to see what actually changed.

## Two Phase 4 Gotchas Worth Remembering

- **A minimal hand-written `.sln` is not enough for a real build.** Phase 2's
  `DotNetSolutionDetector` only needs the `Project(...)`/`EndProject` lines to find referenced
  projects, and tolerates a `.sln` with no `Global`/`GlobalSection` footer. A real `dotnet
  build MySolution.sln` does not — without the `SolutionConfigurationPlatforms`/
  `ProjectConfigurationPlatforms` sections mapping each project GUID to a configuration, MSBuild
  silently builds zero projects ("找不到可還原的專案!" / "No restorable projects found!") and
  reports success. Verified by hand; see `DotNetBuildIntegrationTests`'s solution fixture for a
  minimal-but-complete example.
- **Not every tool's output is UTF-8.** `dotnet` is; `vswhere.exe` is not (on this machine, at
  least) — see ADR-005. `ProcessStartRequest.OutputEncoding` defaults to `null` (OS codepage) for
  exactly this reason; only set it when you've verified that specific tool's real behavior.

## Two Phase 5 Gotchas Worth Remembering

- **Proving "no orphan process" needs the child's real PID, not the wrapper's.** `dotnet run`
  launches the actual application as a *child* of the `dotnet` process `IProcessRunner.Start`
  returns a handle to. Asserting the wrapper's `IRunningProcess.HasExited` after `Stop()` isn't
  proof the real application died — you have to have the real application write its own
  `Environment.ProcessId` somewhere observable (a file, in these tests) and check *that* PID is
  gone via `Process.GetProcessById` throwing `ArgumentException`. `Kill(entireProcessTree: true)`
  (reused unchanged from Phase 1/4) is what makes this actually true; see
  `DotNetRunIntegrationTests.Stop_kills_the_real_process_tree_so_the_child_process_no_longer_exists`.
- **A killed process's `finally` blocks don't run**, so an app-side cleanup step (e.g. "delete my
  own lock file on exit") cannot be trusted after a `Stop()`/`Kill()`. The real "never two
  instances" integration test instead has the test console app check whether the PID recorded in
  a lock file is still alive via `Process.GetProcessById(...).HasExited` *before* deciding
  whether to proceed — a design that is correct regardless of whether the previous instance's own
  cleanup ever ran.

## Two Phase 6 Gotchas Worth Remembering

- **A debugger's real license can refuse a real, correctly-implemented client.** `vsdbg` is
  genuinely installed on this machine and a real DAP `initialize` exchange with it succeeds —
  but it fails at `configurationDone` with its own internal "handshake" error immediately after
  printing a banner restricting its use to Visual Studio Code/Visual Studio/VS4Mac. This is not
  a protocol bug to debug around; it is intentional license enforcement. `netcoredbg` (MIT,
  Samsung) was installed specifically because it has no such restriction and speaks the identical
  DAP-over-stdio contract — see ADR-007 for the full investigation before assuming any specific
  `vsdbg`-shaped fix would help.
- **Cleaning up inside a DAP event handler can deadlock the very client dispatching it.**
  `DapClient`'s read loop calls `EventReceived` synchronously from inside the task that is also
  what `DapClient.DisposeAsync()` awaits. Reacting to a `terminated`/`exited` event by disposing
  that same client inline is the read loop awaiting itself; `NetCoreDebugSession` escapes to
  `Task.Run(...)` for adapter-initiated termination specifically to avoid this — a real
  orphan-file-handle bug (a just-debugged binary staying locked) surfaced this during
  development, not a hypothetical concern.

## Two Phase 7 Gotchas Worth Remembering

- **`--autoLoadProjects` is a CLI argument to the server process, not something you negotiate
  over LSP.** Without it, `Microsoft.CodeAnalysis.LanguageServer` never discovers or loads any
  `.csproj` at all, no matter what `rootUri`/`workspaceFolders` you send in `initialize` — every
  opened file stays classified as a standalone "miscellaneous file," and
  `textDocument/completion` only ever returns generic language keywords, never real members of
  your own types. Watch the server's own `window/logMessage` notifications during development
  (`--logLevel Trace`) — it logs `"Discovered N projects to auto load"` when this actually works.
- **This server uses pull diagnostics (`textDocument/diagnostic`), not push
  (`publishDiagnostics`) — and the first pull can race the project load.** Sending `didOpen` and
  waiting for a `publishDiagnostics` notification will wait forever; you have to ask
  (`textDocument/diagnostic`) yourself, and the very first ask can legitimately come back empty
  because the server's own project load hasn't finished yet. `RoslynLanguageServerSession`
  re-pulls after every `didOpen`/`didChange`, and exposes `RefreshDiagnosticsAsync` for a caller
  (or a test) to explicitly re-ask later.

## Two Phase 8 Gotchas Worth Remembering

- **`IProcessRunner.RunAsync` reports cancellation as data, not an exception — code that needs to
  distinguish Cancelled from Failed has to check for it explicitly.** Unlike most .NET
  cancellation APIs, `RunAsync` returns a normal `ProcessResult` with `WasCancelled: true` when
  the token fires, rather than throwing `OperationCanceledException`. `DotNetTestAdapter` initially
  didn't check this after calling `RunAsync`, so a cancelled run's missing-TRX-file fallback error
  got mapped to `TestRunState.Failed` instead of `Cancelled` — caught by the mandatory real
  cancellation integration test failing with `Expected: Cancelled, Actual: Failed`, fixed by
  checking `result.WasCancelled` and re-throwing before the TRX-existence check. Any new adapter
  built on `IProcessRunner` that needs this distinction must do the same check itself.
- **VSTest's real console output is real console output — it must be parsed by structural shape,
  not by string-matching localized text.** `dotnet test --list-tests`' header/progress lines are
  localized and cannot be matched reliably, but every real discovered test name is indented with
  exactly four spaces regardless of locale — verified against a real throwaway probe project
  before writing `DotNetTestAdapter.ParseListTests`. The equivalent real-sample-based test fixture
  in `DotNetTestAdapterTests` captures an actual observed `--list-tests` output string (including
  a Theory-expanded name) rather than a hand-constructed approximation, so the parser is checked
  against what the real tool actually produces.

## Two Phase 9 Gotchas Worth Remembering

- **A real `git status --porcelain=v2 -z` rename/copy record has one extra field versus an
  ordinary entry, and its original path is a separate NUL-delimited record, not embedded in the
  same one.** A `"2"`-type line is `2 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <X><score> <path>`
  (note the extra `<X><score>` field, e.g. `"R100"`, that an ordinary `"1"`-type line doesn't
  have) — `GitCliAdapter`'s first version split on 9 fields (an ordinary entry's count) instead
  of 10, leaving `"R100 renamed.txt"` un-split as one field. Caught immediately by a real `git mv`
  integration test asserting the exact path string; fixed by splitting into 10 fields. The
  original path (`orig.txt`) is Git's own *next* NUL-delimited record in the same `-z` stream, not
  a suffix of this one — verified with a real rename before writing the parser (see ADR-010).
- **C#'s `\xHHHH` hex escape greedily consumes up to 4 following hex digits — writing a
  control-character-separated test fixture with `\x1f`/`\x1e` immediately before hex-digit
  characters silently produces the wrong string.** `"\x1fac20278"` doesn't parse as `0x1F`
  followed by `"ac20278"` — the digits `1`, `f`, `a`, `c` are all valid hex, so C# consumes all
  four as one escape (`\x1fac` → a single character), silently corrupting the intended separator
  test data with no compiler warning. This is not a `GitCliAdapter` bug — it's a test-authoring
  trap discovered while writing `GitCliAdapterTests`' real-sample-based log parsing test. Use
  fixed-width `\u001f`/`\u001e` escapes (always exactly 4 hex digits, never greedy) for any future
  test fixture built around control-character separators.

## Two Phase 10 Gotchas Worth Remembering

- **A private, duplicate copy of `DevStudio.Core.dll` sitting next to an extension's own
  assembly breaks `IDevStudioExtension` type identity across the `AssemblyLoadContext`
  boundary.** If an extension project's `ProjectReference` to `DevStudio.Core` copies
  `DevStudio.Core.dll` into the extension's own output directory (the default), and the
  extension's dedicated `AssemblyLoadContext` resolves its own local copy instead of falling back
  to the host's already-loaded one, `typeof(IDevStudioExtension)` in the extension's context and
  in the host's context become two genuinely different `System.Type` objects — so
  `instance is IDevStudioExtension` is always `false`, even for a syntactically correct real
  extension, with no obvious error message pointing at the real cause. Fixed by (1) marking the
  extension's `ProjectReference` to `DevStudio.Core` with `Private="false"
  ExcludeAssets="runtime"` (never copy a private duplicate), and (2) having
  `ExtensionAssemblyLoadContext.Load` always return `null`, which makes the runtime fall back to
  the default context for exactly this kind of shared/host assembly. Verified working only by the
  real `Real_full_lifecycle_...` integration test actually succeeding at the cast — see ADR-011.
- **A `"2 <XY> ..."`-shaped test fixture aside, plan the exact field-split count from a real
  sample before writing a fixed-field parser — the same class of bug from Phase 9 recurs anywhere
  a manifest/record format has an optional or extra field between two required ones.** No new
  instance of this occurred in Phase 10's own manifest parser (it uses named JSON properties, not
  positional fields), but the general lesson from ADR-010's rename-record bug directly informed
  writing `ExtensionManifestParser` as property-lookup-based rather than positional, specifically
  to avoid reintroducing that class of bug in a new format.

## Two Phase 11 Gotchas Worth Remembering

- **A hard-coded `StringComparer.OrdinalIgnoreCase` on a real filesystem path is a real,
  silent bug on Linux — and unit tests written and only ever run on Windows will never catch
  it.** Every one of the dozen sites this phase fixed (file watching, the open-document table,
  breakpoints-by-source, project/solution path lookup, Git's per-repository mutation tracking,
  the extension entry-point traversal check) had been passing its entire existing test suite for
  ten prior phases, on Windows, without a single failure — because Windows' own case-insensitive
  filesystem makes the bug invisible there. It was only findable by actually running on a
  case-sensitive filesystem (real Linux, via WSL2 in this case), confirming SKILL.md's repeated
  "real environment, not assumption" principle applies just as much to *which platform* a test
  suite happens to run on as to which tool it happens to invoke. Use `Core.Platform.PathComparer`
  for any *new* real-filesystem-path comparison/dictionary key — never a bare
  `StringComparer.OrdinalIgnoreCase` again — see ADR-012.
- **A test that hard-codes an OS-specific expected value, or a bare executable name that happens
  to exist on the author's own machine, is itself a real portability bug — not a product bug —
  and will only surface by actually running the suite elsewhere.** Two real examples found this
  phase: `GitCliAdapterTests` asserted a literal `@"C:\repo"` for output the real adapter
  correctly normalizes to *this OS's own* separator (so it produces `"C:/repo"` on Linux, which is
  correct, not a bug); and a `ProcessRunnerTests` stdin test hard-coded bare `python`, absent on a
  real, current Ubuntu (24.04) image that ships only `python3`. Fixed by comparing against
  `Path.DirectorySeparatorChar`-normalized output and by resolving `python`/`python3` via the
  existing `ExecutableLocator` respectively — mirroring the exact fallback
  `PythonToolchainDetector` itself already used. Neither fix touched any product code; both were
  test-only portability bugs this phase's real Linux run is what actually caught.

## Two Phase 12 Gotchas Worth Remembering

- **`ResourceManager.CreateFileBasedResourceManager`'s culture-file convention differs from a
  compiled satellite assembly's.** A satellite assembly places a culture's resources in a
  `<culture>/` subfolder next to the main assembly (e.g. `zh-TW/DevStudio.UI.resources.dll`) — but
  a *file-based* `ResourceManager` (used in `LocalizationServiceTests` to prove the real fallback
  chain against dynamically-written `.resources` files) expects the culture file directly in the
  resource root directory, named `<baseName>.<culture>.resources`, with **no** subdirectory. A
  test that mirrors the satellite-assembly layout for a file-based `ResourceManager` will silently
  resolve to the *neutral* resource for every culture instead of exercising real fallback — caught
  only by asserting the actual culture-specific value, not just "did it return something."
- **Avalonia's `Binding.StringFormat` requires a static XAML literal — it cannot consume a
  runtime-resolved format string.** `{Binding SomeValue, StringFormat='{}{0} things'}` only works
  when the format string itself is written in the XAML; it cannot be `{Binding Loc[Some.Key]}`
  because `StringFormat` is evaluated once at binding-compile time, not re-resolved per value
  change. Any localized *parameterized* message (anything with a `{0}`/`{1}` in its `.resx` value)
  needs a real converter — `DevStudio.UI.Localization.LocFormatConverter` — bound through a
  `MultiBinding` (first value = the resolved template, rest = its arguments) instead. Don't try to
  route a dynamic template through `StringFormat` again; it silently does nothing useful.

## Two Phase 13 Gotchas Worth Remembering

- **On Windows, probing a bare executable name is not safe for every real toolchain — only for
  ones that actually ship a `.exe`.** Win32's `CreateProcess` auto-appends `.exe` (never `.cmd`)
  to an extension-less module name, so `IProcessRunner.RunAsync(new ProcessStartRequest("npm",
  ...))` throws `Win32Exception` on a real machine where npm is only `npm.cmd` — this was a real,
  pre-existing bug in `NodePackageManagerToolchainDetector` this phase found and fixed (see
  ADR-014). Any *new* toolchain/package-manager detector must resolve through
  `ExecutableLocator.FindOnPath` before probing, never assume a bare name works cross-platform.
- **`ExecutableLocator.FindOnPath`'s Windows candidate order matters, and "bare name first" is
  wrong.** This machine's real Node.js install places an extension-less POSIX shell-script twin of
  `npm` directly alongside `npm.cmd` (for WSL/git-bash users) — `File.Exists` happily finds it,
  but `CreateProcess` cannot run it ("not a valid application for this OS platform"). The fix was
  to check `.exe`/`.cmd`/`.bat` before the bare name on Windows, not after. If you add a new
  toolchain whose real-machine detection suddenly starts failing with that exact error message,
  check whether a same-named non-Windows file exists earlier in `PATH` before assuming the tool
  itself is broken.

## One Phase 14 Gotcha Worth Remembering

- **A JSON graph/tree's "direct" (or similarly-named) boolean flag is very often an *edge*
  property, not a *node* property — read it off the right edge, not just any edge you can find.**
  `ConanGraphInfoParser`'s first version checked whether a package's own outgoing dependency edges
  included any `"direct": true` entry, and marked that package Direct if so — but Conan's
  documented graph JSON schema marks an edge `direct` from the perspective of the node it
  *originates* from, not the node it points to. A dependency's own internal dependencies can be
  marked direct-relative-to-that-dependency while still being transitive relative to the actual
  project. The fix was to find the consumer (root) node specifically and read *its own* outgoing
  edges' `direct` flags — never any other node's. If you write a new adapter whose ecosystem
  reports dependency-graph JSON with a similar per-edge flag (Cargo's `cargo metadata` avoids this
  entirely by giving you a flat `dependencies` array per package instead — see
  `CargoMetadataParser` for the simpler, less error-prone shape when a tool offers it), don't
  assume the flag means "this package is a direct dependency of the project" without checking
  whose edge you are actually reading it from. This bug was caught by
  `ConanPackageAdapterTests.ListInstalledAsync_parses_conan_graph_info_and_classifies_direct_vs_transitive`
  before it ever shipped — see ADR-015.
