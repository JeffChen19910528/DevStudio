# Phase 13 Completion Report — Cross-Language Package & Dependency Management (P0)

Scope note: this phase was explicitly requested by the user and scoped, by the user's own
choice among presented options, to **"P0 only, done right"** — a real, capability-based,
cross-language package-management architecture, with exactly three real ecosystem adapters
(.NET/NuGet, Python/pip, Node/npm). The ten P1 ecosystems named in the original spec (pnpm,
Yarn, Poetry, uv, Maven, Gradle, Cargo, Go Modules, vcpkg, Conan) were **explicitly deferred**,
not implemented, not stubbed, not faked. This report is honestly scoped to that agreed P0.

## A. Executive Summary

DevStudio now has a real, generic, capability-based Package Management layer
(`DevStudio.Core.Packages` / `DevStudio.Infrastructure.Packages`) that mirrors the existing
adapter/registry pattern used by Toolchains/Build/Run/Debug/Language/Testing/Git. Three real
adapters were implemented and real-environment validated on this Windows 11 machine: NuGet
(.NET), pip (Python), and npm (Node). A new `PackageManagerViewModel` and `MainWindow.axaml` tab
(Installed / Browse / Updates / Dependencies) expose this through fully localized (en-US/zh-TW)
UI, gated by the existing Workspace Trust mechanism for all mutating operations. Debug and
Release builds both pass with 0 warnings/0 errors; the full test suite (556 tests across
Core/UI/Infrastructure) passes with 0 failures, including real, no-fake integration tests that
create temporary real projects and drive real `dotnet`/`pip`/`npm` processes against real
package registries (nuget.org, PyPI, the npm registry) over real network access confirmed
available in this environment.

## B. Existing Architecture Audit

Before writing code, the following were read and confirmed as the patterns to mirror:
`Core.Testing`/`Infrastructure.Testing.DotNetTestAdapter` (adapter + single-flight service
shape), `Core.Git`/`Infrastructure.Git.GitCliAdapter` (per-repository state, structured CLI
invocation), `Core.Toolchains`/`Infrastructure.Toolchains` (registry + capability-matching
pattern, `ExecutableLocator`), `Core.Projects`/`Infrastructure.Projects.*Detector` (read-only,
no-process-execution detection), `DevStudio.UI.Localization` (resx + `ResourceManager` fallback,
`LocFormatConverter`), and `App.axaml.cs` (single composition root). No architectural surprises
were found; the codebase's stated boundaries (Core has no I/O, Infrastructure is the only
project touching `System.IO`/`System.Diagnostics`, UI has no direct Infrastructure reference)
held exactly as documented. One real, pre-existing bug was found during this work (see §F).

## C. Package Management Architecture

```
Project Detection (existing, unmodified)
        ↓
PackageService.DetectApplicableManagers(ProjectInfo)
        ↓
PackageManagerRegistry  (holds all registered IPackageManagerAdapter instances)
        ↓
IPackageManagerAdapter.SupportsProject / DescribeProject   → PackageProject (capabilities)
        ↓
Capability interfaces the adapter actually implements:
  IPackageInspector | IPackageSearcher | IPackageInstaller | IPackageRemover |
  IPackageUpdater | IPackageSourceManager
        ↓
Existing IProcessRunner  (no new process abstraction)
        ↓
Real dotnet / pip / npm CLI
```

`PackageService` is a thin dispatcher: it asks the registry which adapters apply to a project,
exposes the resulting `PackageProject` (which carries `PackageManagerCapabilities`, never an
assumption), and routes a requested operation to the adapter only if that adapter actually
implements the corresponding capability interface — an adapter that can't search is simply not
asked to. `PackageService` holds one `SemaphoreSlim` per project path for mutating operations
(add/remove/update/restore), mirroring `GitService`'s per-repository single-flight design
(ADR-010) rather than `Build`/`Test`'s single global gate, since a workspace may have multiple
independently-manageable projects.

## D. Core Contracts

`DevStudio.Core.Packages`:
- `PackageReference` — PackageId, RequestedVersion, ResolvedVersion, IsDirectDependency,
  IsTransitiveDependency, ProjectPath, TargetFramework, Source, Metadata.
- `PackageVersion` — Version, IsPrerelease, IsLatest, IsLatestStable.
- `PackageDependency` — PackageId, VersionRange, DependencyType, ParentPackage.
- `PackageSource` — Name, Location, Enabled, IsDefault, SourceType.
- `PackageProject` — ProjectPath, PackageManagerId, ProjectType, `PackageManagerCapabilities`,
  `UnavailableReason` (human-readable, e.g. "Poetry-managed project — mutation not supported
  this phase").
- `PackageManagerCapabilities` — ListInstalled, ListDependencies, Search, Add, Remove, Update,
  Restore, ListOutdated, ManageSources, LockfileSupport, TransitiveDependencySupport,
  PrereleaseSupport — booleans, no assumed defaults.
- `PackageOperation` / `PackageOperationResult` — Success, Operation, Project, Package,
  ChangedFiles, Diagnostics, ExitCode, Cancellation, FailureReason.
- `PackageSearchResult` — PackageId, Description, Version, LatestVersion, Source, License,
  ProjectUrl (each nullable independently — no ecosystem is assumed to supply all of them).
- `WellKnownPackageManagerIds` — string constants (`"NuGet"`, `"pip"`, `"npm"`) used instead of
  an enum, so a future P1 adapter never requires a Core-level enum change.
- `IPackageManagerAdapter` (identity + `SupportsProject`/`DescribeProject` only) plus the six
  small capability interfaces listed in §C. Core has zero reference to any package-manager
  package, API, or SDK.

## E. Adapter Architecture

Each adapter implements `IPackageManagerAdapter` plus only the capability interfaces it
genuinely supports — no adapter is forced to implement a capability it cannot honestly provide.
`PackageManagerRegistry` holds a flat list of adapters and asks each one to self-report
applicability via `SupportsProject`, exactly mirroring `ToolchainRegistry`'s
"detectors self-report, the registry doesn't hardcode ecosystem knowledge" shape.

## F. Implemented Package Managers

| Ecosystem | Status | Adapter |
|---|---|---|
| .NET / NuGet | **Real-tested** | `NuGetPackageAdapter` — `dotnet add/remove/list/restore package`, `dotnet package search` |
| Python / pip | **Real-tested** | `PythonPackageAdapter` — `python -m pip install/uninstall/list/show`, real `.venv`/`venv` interpreter resolution |
| Node / npm | **Real-tested** | `NpmPackageAdapter` — `npm install/uninstall/list/outdated/search`, real recursive dependency-tree parsing |
| pnpm, Yarn | **Not implemented (P1, deferred)** | — |
| Poetry, uv, Pipenv | **Not implemented for mutation (P1, deferred)** — read-only inspection via pip adapter's project-style detection reports these projects as inspectable but not mutable, with `UnavailableReason` set | — |
| Maven, Gradle | **Not implemented (P1, deferred)** | — |
| Cargo | **Not implemented (P1, deferred)** | — |
| Go Modules | **Not implemented (P1, deferred)** | — |
| vcpkg, Conan | **Not implemented (P1, deferred)** | — |

A real, pre-existing bug was found and fixed during npm adapter work:
`Toolchains/NodePackageManagerToolchainDetector` probed the bare `"npm"` executable name, which
Win32 cannot resolve to npm's real `.cmd` launcher, and `Toolchains/ExecutableLocator`'s Windows
candidate ordering tried the bare name before `.exe`/`.cmd`/`.bat` (it could match an unrelated
non-Windows shell-script binary of the same bare name if one existed on PATH). Both were fixed.
This is a real toolchain-detection correctness fix, not a package-management feature.

## G. Project Detection Integration

No existing `IProjectDetector` was modified. Package-manager applicability is determined
read-only by `PackageService`/adapters inspecting already-known `ProjectInfo` plus a bounded,
defensive read of the relevant manifest file (`.csproj`, `requirements.txt`/`pyproject.toml`,
`package.json`) — consistent with the existing "project detection never executes a process"
rule. No new `IProjectDetector` implementation was added or needed for P0.

## H. Package Search

Implemented for all three P0 adapters via `IPackageSearcher`, with cancellation and timeout
passed through to the existing `IProcessRunner`. Search is real and network-dependent
(`dotnet package search` against nuget.org, `npm search`/npm registry, pip's own index query
surface). Real network access to nuget.org, registry.npmjs.org, and pypi.org was confirmed
available in this environment and exercised by the integration test suite. No credentials are
read, stored, or logged by search.

## I. Installed / Dependency Management

All three adapters distinguish Direct vs Transitive dependencies (`PackageReference.
IsDirectDependency`/`IsTransitiveDependency`) using each ecosystem's own real listing command's
own real structure (`dotnet list package --include-transitive`'s JSON, npm's recursive
`npm list --all --json` tree, pip's `pip list`/`pip show` dependency fields) — never inferred by
DevStudio itself.

## J. Add / Remove / Update / Restore

All four are implemented for NuGet and npm. For pip, Add/Remove/Update/Restore are available
only for plain `requirements.txt`-style projects; a Poetry/uv/Pipenv-managed project reports
these capabilities as `false` with a human-readable `UnavailableReason`, per the explicit
"never diverge pip from a project's own declared dependency tool" rule in the original spec.
All four are gated by Workspace Trust (see §N).

## K. Package Sources

Read-only source inspection only, no source management this phase.
`PackageManagerCapabilities.ManageSources` is `false` for all three P0 adapters;
`IPackageSourceManager` exists as a Core contract but is not implemented by any P0 adapter — a
concrete, capability-declared limitation, not silently missing. No credentials for any
authenticated feed are read, stored, or exposed anywhere.

## L. UI

A new "Package Manager" area in `MainWindow.axaml`/`PackageManagerViewModel` with Installed /
Browse / Updates / Dependencies tabs, following the panel-ViewModel pattern already established
by SourceControl/Extensions/Toolchains rather than growing the already-oversized
`MainWindowViewModel` further. The ViewModel contains zero ecosystem-specific branching — every
decision (which tabs/actions are available) is driven off `PackageProject.Capabilities`.

## M. Localization

All new UI strings added to `Strings.resx`/`Strings.zh-TW.resx` under the existing key-parity
convention and covered by the existing localization-completeness test
(`LocalizationServiceTests`), which passes. Terminology follows the spec's guidance (套件管理員,
套件, 相依套件/相依性, 已安裝, 更新, 瀏覽, 還原套件). Package names, project names, and all
external tool output remain untranslated, per existing Phase 12 policy.

## N. Workspace Trust / Security

Add/Remove/Update/Restore all route through the same Workspace Trust gate every other mutating
operation in DevStudio uses (`SourceControlViewModel`'s existing `Func<string, Task<bool>>`
pattern, reused rather than reimplemented for Package Manager). Read-only operations (list
installed, list dependencies, search) do not require trust. No secret/credential/token or full
environment variable is ever logged; `IProcessRunner`'s existing output-sink path is used
unchanged. No package is installed or restored automatically on project/workspace open.

## O. Cross-Platform Validation

Architecturally cross-platform: no adapter hard-codes `.exe`, no Windows-only path, executable
resolution goes entirely through the existing `ToolchainRegistry`/`ExecutableLocator`. Only
Windows 11 was actually available in this environment this phase — Linux/macOS are **implemented
but not real-environment validated**, per this project's established honesty convention (ADR-012
precedent). Do not upgrade this claim without an actual Linux/macOS run.

## P. Real Environment Validation

Windows 11, this machine, this session:
- **dotnet 10.0.401 / NuGet — REAL-TESTED.** Real temporary `dotnet new classlib` projects,
  real `dotnet add/remove/list/restore package` against real nuget.org, real `.csproj` diffs
  verified.
- **Python 3.11.6 / pip — REAL-TESTED.** Real temporary `requirements.txt` projects, real
  `python -m pip install/uninstall/list/show` against real PyPI.
- **Node v22.20.0 / npm 11.11.0 — REAL-TESTED.** Real temporary `package.json` projects, real
  `npm install/uninstall/list/outdated/search` against the real npm registry.
- Network access to nuget.org, registry.npmjs.org, and pypi.org was independently confirmed
  reachable (HTTP 200) from this environment immediately before the test run reported below.

## Q. Unit / Integration / Regression Tests

`dotnet build DevStudio.slnx` (Debug): **succeeded, 0 warnings, 0 errors.**
`dotnet build DevStudio.slnx -c Release`: **succeeded, 0 warnings, 0 errors.**
`dotnet test DevStudio.slnx`: **556 total, 556 passed, 0 failed, 0 skipped**
(DevStudio.Core.Tests: 212 passed; DevStudio.UI.Tests: 70 passed; DevStudio.Infrastructure.Tests:
274 passed, ~34s — consistent with real process/network-backed integration tests actually
running, not being skipped). This is the entire existing suite plus the new Phase 13 tests, so it
also serves as the regression check for Workspace/Project Detection/Toolchain
Detection/Build/Run/Debug/LSP/Test Explorer/Git/Extensions/Terminal/Localization — none of it
was broken by this phase's changes.

## R. Performance

No polling loop, no continuous registry query, no unbounded cache. Search/list operations are
explicit, user-triggered, async, and cancellable through the existing `CancellationToken`
plumbing already used by Build/Run/Test. No filesystem-wide scan was introduced; adapter
applicability checks read only the specific manifest file relevant to that project.

## S. Coupling Analysis

`PackageService` has 1 constructor parameter (`PackageManagerRegistry`).
`PackageManagerViewModel` follows the same shape as `SourceControlViewModel`
(a handful of Core service/trust-gate dependencies, not a growth of
`MainWindowViewModel`'s existing 20-parameter constructor). No adapter depends on another
adapter. No service locator, no new global/static state, no circular project reference
introduced. `Core` still has zero reference to `Infrastructure`; `UI` still has zero direct
reference to `Infrastructure`.

## T. Refactoring Performed

`Toolchains/ExecutableLocator` and `Toolchains/NodePackageManagerToolchainDetector` were fixed
(see §F) — a real bug fix required for the npm adapter to function correctly on Windows at all,
not a stylistic refactor.

## U. Architecture After Refactoring

Unchanged from the documented `Core → Infrastructure → UI ← App` composition shape; Packages is
a new vertical slice through all three layers following the same pattern as every prior phase's
feature (Build, Run, Debug, Testing, Git, Extensions).

## V. Files Changed

New: `src/DevStudio.Core/Packages/{PackageModels,IPackageManagerAdapter,PackageManagerRegistry,PackageService}.cs`,
`src/DevStudio.Infrastructure/Packages/{NuGetPackageAdapter,PythonPackageAdapter,NpmPackageAdapter}.cs`,
`src/DevStudio.UI/ViewModels/PackageManagerViewModel.cs`,
`tests/DevStudio.Core.Tests/Packages/*`, `tests/DevStudio.Infrastructure.Tests/Packages/*`,
`docs/adr/ADR-014-package-management.md`.
Modified: `src/DevStudio.App/App.axaml.cs`, `src/DevStudio.App/Views/MainWindow.axaml`,
`src/DevStudio.Infrastructure/Toolchains/{ExecutableLocator,NodePackageManagerToolchainDetector}.cs`,
`src/DevStudio.UI/Localization/Strings*.resx`, `src/DevStudio.UI/ViewModels/MainWindowViewModel.cs`,
`tests/DevStudio.UI.Tests/{Localization/LocalizationServiceTests,MainWindowViewModelTests}.cs`,
`README.md`, `README.zh-TW.md`.
Documentation-only (untracked/gitignored per repo convention): `CLAUDE.md`, `ARCHITECTURE.md`,
`SECURITY.md`, `DEVELOPMENT.md`, `CHANGELOG.md`.

## W. Documentation Changes

`CLAUDE.md` Current State narrative, Architecture Snapshot, and Known Limitations updated;
`ARCHITECTURE.md`, `SECURITY.md`, `DEVELOPMENT.md`, `CHANGELOG.md`, `README.md`/`README.zh-TW.md`
updated to describe the Phase 13 P0 package-management layer; `docs/adr/ADR-014-package-management.md`
added following the exact Context/Decision/Consequences/Known Limitations structure of
ADR-009/010/011/013.

## X. Known Limitations

- Only NuGet, pip, and npm are implemented; the ten P1 ecosystems are explicitly deferred, not
  faked.
- Poetry/uv/Pipenv-managed Python projects get read-only inspection only, never mutation.
- No package-source management (add/remove/enable feed) this phase for any adapter.
- No credential/authenticated-feed handling of any kind.
- Linux/macOS are implemented but not real-environment validated this phase.
- Search/list/outdated are real, network-dependent operations; in an environment with no network
  access these would fail loudly (real non-zero exit codes), not silently report false success —
  this was not an issue in this session since network access was confirmed available.

## Y. Environment Matrix

| Ecosystem | Windows | Linux/WSL2 | macOS |
|---|---|---|---|
| .NET / NuGet | REAL-TESTED | IMPLEMENTED BUT NOT REAL-TESTED | IMPLEMENTED BUT NOT REAL-TESTED |
| Python / pip | REAL-TESTED | IMPLEMENTED BUT NOT REAL-TESTED | IMPLEMENTED BUT NOT REAL-TESTED |
| npm | REAL-TESTED | IMPLEMENTED BUT NOT REAL-TESTED | IMPLEMENTED BUT NOT REAL-TESTED |
| pnpm, Yarn, Maven, Gradle, Cargo, Go, vcpkg, Conan | NOT AVAILABLE (not implemented) | NOT AVAILABLE (not implemented) | NOT AVAILABLE (not implemented) |

## Z. Final Status

**PASS**
