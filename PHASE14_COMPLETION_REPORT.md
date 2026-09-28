# Phase 14 Completion Report — Cross-Language Package Ecosystem Expansion (P1)

## A. Executive Summary

Phase 14 added six more package-manager adapters (Java/Maven, Java/Gradle, Rust/Cargo, Go
Modules, C/C++/vcpkg, C/C++/Conan) on top of Phase 13's unchanged `Core.Packages` architecture.
**Zero changes** were made to `IPackageManagerAdapter`, any capability interface,
`PackageManagerRegistry`, `PackageService`, or any UI/ViewModel code — this phase is adapters
(plus new toolchain/project detection where a prerequisite did not already exist) only, proving
the architecture's original claim that a new ecosystem is "one adapter class against an
already-stable contract." None of `mvn`/`gradle`/`cargo`/`rustc`/`go`/`vcpkg`/`conan`/`cmake` is
installed on the development machine (only a bare JDK is present), so every one of these six
adapters is **implemented but not real-environment validated** — this report never claims
otherwise. Debug and Release builds pass with 0 warnings/errors; the full test suite passes
642/642 (up from Phase 13's 556-test baseline: +28 Maven/Gradle, +25 Cargo/Go Modules, +33
vcpkg/Conan = +86 new tests, adjusted for the exact final count below), 0 regressions. A
post-implementation architecture review found the design absorbed all six ecosystems with zero
required refactoring. **Final Status: PASS.**

## B. Phase 13 Baseline

Verified at the start of this phase and unchanged throughout:

- Debug build: PASS. Release build: PASS.
- Full test suite: 556/556 passing (212 Core, 70 UI, 274 Infrastructure).
- `Core.Packages` (P0): `PackageReference`/`PackageVersion`/`PackageDependency`/`PackageSource`/
  `PackageProject`/`PackageManagerCapabilities`/`PackageOperation`/`PackageOperationResult`/
  `PackageSearchResult`/`WellKnownPackageManagerIds`/`IPackageManagerAdapter` + capability
  interfaces/`PackageManagerRegistry`/`PackageService` — already carried forward-declared,
  unused `WellKnownPackageManagerIds` constants for every P1 ecosystem this phase implements
  (`Maven`, `Gradle`, `Cargo`, `GoModules`, `Vcpkg`, `Conan`), so no Core model change was needed.
- Real adapters: `NuGetPackageAdapter`, `PythonPackageAdapter`, `NpmPackageAdapter` — all three
  real-environment validated (real network calls to nuget.org/PyPI/the npm registry), unaffected
  by this phase.
- Package Manager UI (`PackageManagerViewModel`, a `MainWindow.axaml` tab: Installed/Browse/
  Updates/Dependencies), fully localized en-US/zh-TW, Workspace Trust-gated mutations — unaffected
  by this phase.
- ADR-014, `PHASE13_COMPLETION_REPORT.md`, `PHASE13_ARCHITECTURE_REVIEW.md`,
  `GITIGNORE_FIX_REPORT.md` all pre-date this phase.

## C. Architecture Reuse

No architectural change. The layering is exactly Phase 13's:

```
Package Manager (UI) → PackageManagerViewModel → PackageService → PackageManagerRegistry
    → IPackageManagerAdapter (+ capability interfaces) → IPackageManagerAdapter.DetectProject
    → MavenPackageAdapter / GradlePackageAdapter / CargoPackageAdapter / GoModulePackageAdapter /
      VcpkgPackageAdapter / ConanPackageAdapter → IProcessRunner → real tool
```

`PackageManagerRegistry.DetectApplicableManagers` needed no change to let vcpkg and Conan act as
two independent adapters both potentially applicable to the same `ProjectType.CMake` project — it
already asks every registered adapter independently and collects whichever return non-null. See
`docs/adr/ADR-015-cross-language-package-ecosystems.md` for the full design rationale.

## D. Java / Maven

`MavenPackageAdapter` — full `IPackageInspector`/`IPackageInstaller`/`IPackageRemover`/
`IPackageUpdater`/`IPackageSourceManager`. `ListInstalledAsync` parses real `mvn dependency:tree`
text (direct vs. transitive via indentation depth); declared dependencies come straight from
`pom.xml`; outdated detection parses `versions-maven-plugin:2.16.2:display-dependency-updates`'s
tabular diff. Add/Remove/Update perform structured `XDocument` edits of `<dependencies>` — never
regex-on-XML. `GetSourcesAsync` reads only the project's own `<repositories>`, never
`~/.m2/settings.xml`. `MavenToolchainDetector` (new) resolves `mvn` via `ExecutableLocator`
(Windows ships only `mvn.cmd`, no `mvn.exe`). **Status: implemented, NOT real-environment
validated** (`mvn` absent). `Search`/`LockfileSupport`/`PrereleaseSupport` are `false` (no safe
process-based search exists; Maven has no lockfile concept; no first-class prerelease flag).

## E. Java / Gradle

`GradlePackageAdapter` — **`IPackageInspector` only, deliberately.** `ListInstalledAsync` parses
real `gradle dependencies --console=plain` output; `ListDependenciesAsync` is an explicitly
documented best-effort static parse of string-notation declarations only
(`implementation 'g:a:v'` / `implementation("g:a:v")`) — map notation and version catalogs are
not detected. **Add/Remove/Update/Restore/Search/Sources are all `false`** — a Gradle build
script is executable code (Groovy or Kotlin DSL, string/map notation, version catalogs,
interpolated variables can all coexist) that no static edit can safely rewrite in the general
case; this is a scope decision, not a shortcut. `GradleToolchainDetector` (new) resolves `gradle`
the same way. **Status: implemented (Inspector-only), NOT real-environment validated** (`gradle`
absent).

## F. Rust / Cargo

`CargoPackageAdapter` — full `IPackageInspector`/`IPackageSearcher`/`IPackageInstaller`/
`IPackageRemover`/`IPackageUpdater`. `ListInstalledAsync` parses real `cargo metadata
--format-version 1` JSON (direct vs. transitive from the workspace member's own dependency
graph); declared dependencies read straight from `Cargo.toml` (conservative: inline-string/
table-with-version forms only, documented as not parsing nested `[dependencies.name]` table
headers). Add/Remove always shell out to `cargo add`/`cargo rm` — never manual TOML editing, since
Cargo already owns safe manifest+lockfile editing together. `RestoreAsync` uses `cargo fetch`.
`cargo search`'s real-world reliability (crates.io rate-limiting/deprecation) is flagged
unverified in the adapter's own doc comment. `ListOutdated`/`ManageSources` are `false` (no
stable `cargo outdated` ships with stock Cargo; `.cargo/config.toml` risks exposing registry
tokens if read). Rust project/toolchain detection (`RustProjectDetector`/`RustToolchainDetector`)
already existed from an earlier phase — no new detector needed. **Status: implemented, NOT
real-environment validated** (`cargo`/`rustc` absent).

## G. Go Modules

`GoModulePackageAdapter` — `IPackageInspector`/`IPackageInstaller`/`IPackageRemover`/
`IPackageUpdater` — **no `IPackageSearcher`**: no reliable, stable Go module search CLI/API is
documented, and the phase spec explicitly instructs not inventing one. `ListInstalledAsync`
parses `go list -m -json all`'s documented "concatenated JSON objects" stream format via a
hand-rolled brace-depth splitter (`JsonSerializer` cannot deserialize multiple root objects
directly); direct vs. indirect comes straight from the JSON's own `Indirect` field, never
guessed. `AddAsync`/`UpdateAsync` both use `go get <module>@<version>` (Go has no separate
add/update verb). `RemoveAsync` is honestly two real commands — `go mod edit -droprequire`
followed by `go mod tidy` — documented as such since `go.sum` is only actually cleaned up by the
tidy step. `RestoreAsync` uses `go mod download`. Go project/toolchain detection
(`GoProjectDetector`/`GoToolchainDetector`) already existed — no new detector needed. **Status:
implemented, NOT real-environment validated** (`go` absent).

## H. C/C++ / vcpkg

`VcpkgPackageAdapter` — manifest mode only (`vcpkg.json`), per the phase spec's own "support
manifest mode first" instruction; classic (global) mode is out of scope. Full
`IPackageInspector`/`IPackageSearcher`/`IPackageInstaller`/`IPackageRemover`/`IPackageUpdater`/
`IPackageSourceManager` — but **`Update` is `false`**: vcpkg resolves concrete versions through
its `builtin-baseline`/`overrides` mechanism, not a simple per-package version pin, so a naive
version-field edit risks producing a manifest vcpkg's own solver would reject. Add/Remove edit
`vcpkg.json`'s own `dependencies` array directly via structured `JsonNode` editing — deliberately
not via vcpkg's own `vcpkg add port` subcommand, whose exact current stability/output contract
could not be verified without a real install; a conservative, format-preserving array edit
(preserving `name`/`version`/`builtin-baseline`/`overrides`/`features`) was judged the safer,
more honest choice. `ListInstalledAsync` parses `vcpkg list`'s plain-text output, classifying
direct vs. transitive against the manifest's own declared set. `GetSourcesAsync` reads only
`vcpkg-configuration.json`'s `registries` array (a git URL and baseline commit, never a
credential). `VcpkgToolchainDetector` and vcpkg.json/vcpkg-configuration.json project-detection
recognition are both new this phase — no prior phase recognized either. **Status: implemented,
NOT real-environment validated** (`vcpkg` absent).

## I. C/C++ / Conan

`ConanPackageAdapter` — targets Conan 2.x's documented CLI (`conan graph info --format=json`,
`conan install`, `conan search --format=json`, `conan remote list --format=json`); Conan 1.x uses
different commands (`conan info`) and is explicitly not supported. `conanfile.py` and
`conanfile.txt` are treated as genuinely different ecosystems: a `conanfile.py` is executable
Python, so `Add`/`Remove`/`Update` are always `false` for it (Inspector/Searcher/Restore/Sources
only) — DevStudio never statically edits or execs a Python recipe;
`ListDependenciesAsync` for `.py` is an explicitly best-effort regex over literal
`self.requires("name/version")` calls only. A `conanfile.txt` is static and ini-like — its
`[requires]` section gets real Add/Remove/Update via structured line-based editing
(`ConanTxtEditor`), never regex over the whole file. When both manifests exist in the same
project, `conanfile.py` takes precedence (matching Conan's own real preference), so that project
is treated as Inspector-only. `ConanToolchainDetector` and conanfile.py/conanfile.txt
project-detection recognition are both new this phase. **A real bug was found and fixed during
this phase's own testing**: `ConanGraphInfoParser` initially derived direct-vs-transitive
classification from the wrong graph edges (a node's own outgoing edges rather than the consumer
node's edges to it) — Conan's `"direct"` JSON flag is a per-edge property read from the
*originating* node, not a per-node property; caught by
`ConanPackageAdapterTests.ListInstalledAsync_parses_conan_graph_info_and_classifies_direct_vs_transitive`
before landing, fixed, verified. **Status: implemented, NOT real-environment validated**
(`conan` absent).

## J. Project Detection

Rust (`RustProjectDetector`) and Go (`GoProjectDetector`) already existed. New this phase: vcpkg
and Conan recognition is performed by each adapter's own `DetectProject(ProjectInfo)` — checking
for `vcpkg.json` / `conanfile.py` / `conanfile.txt` directly against a `ProjectType.CMake`
project's root directory — rather than a new `IProjectDetector` implementation, since the existing
`ProjectType` enum has no separate "C/C++" value beyond `CMake` and no new project-level metadata
was needed beyond what `PackageProject.ProjectPath` already carries (the specific manifest file
path). This mirrors how Maven/Gradle detection (Phase 14 P1-A) and Cargo/Go detection worked:
package-manager detection reuses `ProjectInfo.RootPath`/`ProjectType` rather than duplicating
project-scanning logic, exactly as the spec's §6 requires.

## K. Toolchain Detection

Four new detectors this phase: `MavenToolchainDetector`, `GradleToolchainDetector`,
`VcpkgToolchainDetector`, `ConanToolchainDetector` — each mirrors
`NodePackageManagerToolchainDetector`'s `ExecutableLocator`-first resolution pattern (Windows
`.cmd`/shim safety for Maven/Gradle/Conan's pip-installed console-script shim; vcpkg resolves a
native `vcpkg.exe`/`vcpkg` binary either way). All four registered in `ToolchainRegistry` via
`App.axaml.cs`. Rust/Go toolchain detection (`RustToolchainDetector`/`GoToolchainDetector`)
already existed and needed no change.

## L. Package Capabilities

Each adapter made its own independent, evidence-based capability judgment rather than assuming
uniform ecosystem behavior:

| Ecosystem | ListInstalled | ListDeps | Search | Add | Remove | Update | Restore | Outdated | Sources |
|---|---|---|---|---|---|---|---|---|---|
| Maven | ✅ | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Gradle | ✅ | ✅ (best-effort) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Cargo | ✅ | ✅ | ✅ (unverified) | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| Go Modules | ✅ | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| vcpkg | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ | ❌ | ✅ |
| Conan (.txt) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Conan (.py) | ✅ | ✅ (best-effort) | ✅ | ❌ | ❌ | ❌ | ✅ | ❌ | ✅ |

## M. Dependency Classification

Direct-vs-transitive is derived from real tool output wherever the tool reliably provides it,
never guessed: Maven from `dependency:tree` indentation depth; Cargo from `cargo metadata`'s
dependency graph against workspace members; Go Modules from `go list -m -json`'s own `Indirect`
field; vcpkg from cross-referencing `vcpkg list`'s installed-port names against the manifest's own
declared set; Conan from the consumer node's own graph edges (fixed bug notwithstanding — see
§I). Gradle's `ListDependenciesAsync` only reports Direct (declared) dependencies since its static
parse cannot safely walk a full resolved graph without invoking Gradle.

## N. Version Handling

No universal cross-ecosystem version parser was introduced. Each adapter treats version strings as
opaque data from its own ecosystem: Maven versions/`-SNAPSHOT` qualifiers, Cargo SemVer, Go's
module version strings (respecting `// indirect` semantics), vcpkg's baseline/overrides
constraints (`version>=`, `version`, `version^`, `version~` — all surfaced as-is, never resolved
by DevStudio), and Conan's own version syntax. `PackageVersion`'s `IsPrerelease`/`IsStable` model
from Phase 13 is not used by any P1 adapter's search results this phase since none of the six
ecosystems has a first-class prerelease flag DevStudio can reliably read.

## O. Package Search

Implemented where a real, documented command exists: `cargo search` (reliability unverified),
`vcpkg search`, `conan search --format=json`. Explicitly NOT implemented for Maven (no safe
process-based search — Maven Central's search is an HTTP API, out of scope for a process-only
adapter), Gradle (no capability at all), or Go Modules (no reliable, stable module search
API/CLI exists — not invented, per the phase spec's explicit instruction).

## P. Package Sources

Implemented, read-only, credential-free: Maven (`pom.xml`'s own `<repositories>`), vcpkg
(`vcpkg-configuration.json`'s `registries`), Conan (`conan remote list --format=json`). Not
implemented for Cargo (`.cargo/config.toml` deliberately not read, to avoid any risk of exposing a
configured registry token) or Gradle/Go Modules (no capability at all / no safe read established
this phase).

## Q. UI

Zero changes. Confirmed via source grep that no ecosystem name (`Maven`, `Gradle`, `Cargo`,
`Vcpkg`, `Conan`, `GoModules`) appears anywhere in `src/DevStudio.UI` or
`src/DevStudio.App/Views` outside compiled `.dll` artifacts. The existing generic Installed/
Browse/Updates/Dependencies tabs and capability-driven button enablement handle all six new
ecosystems without modification, exactly as ADR-014 intended.

## R. Localization

Zero new strings needed. All existing Phase 13 `PackageManager.*` keys (`Direct`, `Transitive`,
`Installed`, `Add`, `Remove`, `Update`, `Restore`, `Search`, `Sources`, etc.) are already
ecosystem-neutral in both `en-US` and `zh-TW`. The existing localization-completeness test
(unchanged, still passing) continues to verify 1:1 key parity between both `.resx` files.

## S. Workspace Trust

Unchanged mechanism, extended to six more ecosystems. `PackageService`'s Add/Remove/Update/
Restore dispatch is gated by `MainWindowViewModel.EnsurePackageWorkspaceTrustedAsync` before any
of the six new adapters' mutation methods are ever called — identical to Phase 13's three
ecosystems and to Build/Run/Debug/Test/Git's pre-existing gates. Read-only inspection
(ListInstalled/ListDependencies/ListOutdated/Search) is never gated, matching the established
convention.

## T. Security

See `SECURITY.md`'s new "Phase 14 Review" section for the full write-up. Summary: no
`ExecuteCommand(string)` introduced; all manifest/build-file mutation uses structured editing
(`XDocument` for pom.xml, `JsonNode` for vcpkg.json, line-based parsing for conanfile.txt) or the
tool's own safe CLI-owned editing (Cargo, Go Modules) — never regex-on-executable-content, and
never any attempt to edit or exec a Gradle script or a `conanfile.py` recipe; no credential/token
read, stored, or logged by any of the three new `GetSourcesAsync` implementations; no full
environment variable dump logged.

## U. Cross-Platform

Source-level portability preserved: no Windows-specific path/extension assumption was added to
any new `Core`/`Infrastructure` type. All four new toolchain detectors resolve their executable
by bare name through the cross-platform `ExecutableLocator`, exactly like every prior toolchain
detector. This is a source-level claim only — no Linux/macOS execution of any kind occurred this
phase (see §V).

## V. Real Environment Validation

**None of the six P1 ecosystems' tools is installed on this Windows development machine**:
`mvn`, `gradle`, `cargo`, `rustc`, `go`, `vcpkg`, `conan`, `cmake` all report NOT AVAILABLE (only
`java` 25.0.2 is present). Every adapter is therefore IMPLEMENTED BUT NOT REAL-ENVIRONMENT
VALIDATED for any operation beyond toolchain-absence detection. What IS real (against the real,
unmocked `ProcessRunner`/`ToolchainRegistry`, no fakes): all six toolchain detectors and all six
package adapters correctly, honestly report "not installed" / "capabilities: none" / a populated
`UnavailableReason` on this real machine — never silently claiming a capability they cannot
deliver. See §Y for the full matrix.

## W. Unit Tests

New this phase (all against realistic, hand-constructed fixture data through `FakeProcessRunner`
— never a real subprocess): 14 `MavenPackageAdapterTests`, 7 `GradlePackageAdapterTests`, 12
`CargoPackageAdapterTests`, 11 `GoModulePackageAdapterTests`, 13 `VcpkgPackageAdapterTests`, 16
`ConanPackageAdapterTests`, plus 4 toolchain-detector version-parsing tests (Maven/Gradle).

## X. Integration Tests

New this phase, real (unmocked `ProcessRunner`/`ToolchainRegistry`), proving only the honestly
real-testable path (toolchain absence): 2 `MavenGradlePackageAdapterIntegrationTests`, 2
`CargoGoPackageAdapterIntegrationTests`, 2 `VcpkgConanPackageAdapterIntegrationTests`, plus 4 new
`RealEnvironmentToolchainTests` entries (Maven/Gradle/vcpkg/Conan absence). No real `mvn`/
`gradle`/`cargo`/`go`/`vcpkg`/`conan` invocation was ever made — none of these binaries exists on
this machine.

## Y. Regression Tests

The full test suite run (§ below) is itself the regression check for every prior phase
(Workspace/Project Detection/Toolchain Detection/Build/Run/Debug/LSP/Test Explorer/Git/
Extensions/Terminal/Localization/Phase 13 Package Management) — none broken. Final counts:

- Debug build: PASS, 0 warnings, 0 errors.
- Release build: PASS, 0 warnings, 0 errors.
- Full test suite: **642/642 passing** — 212 Core, 70 UI, 360 Infrastructure. 0 failures, 0
  skipped. (556 Phase-13 baseline → 584 after Maven/Gradle → 609 after Cargo/Go Modules → 642
  after vcpkg/Conan; +86 new tests total this phase, 0 regressions at every step.)

## Z. Performance

No new global cache, no new background polling, no command executed on UI render or project-open.
Every new operation is invoked explicitly by user action (selecting a project/manager in the
Package Manager panel, clicking Add/Remove/Update/Restore/Search) and is fully cancellable via the
same `CancellationToken` plumbing every existing adapter uses.

## AA. Architecture Review

Performed after implementation, per the phase spec's §29: inspected `PackageService`,
`PackageManagerRegistry`, all six new adapters, and `PackageManagerViewModel` for ecosystem-
specific logic in Core (none found — `Core.Packages` was not touched at all this phase),
ecosystem-specific branching in UI (none found — confirmed by source grep), duplicated process
execution (none — every adapter uses the same `IProcessRunner`/`ProcessStartRequest` pattern),
duplicated registry-resolution logic (none — `PackageManagerRegistry` untouched), a service
locator (none), global/static package state (none — no `static` mutable field was introduced
anywhere in the six new adapter files), circular project dependencies (none — verified via
`ProjectReference` inspection: `Infrastructure`/`UI` → `Core` only), and God Service growth (none
— `PackageService`/`PackageManagerRegistry` are both unchanged by this phase).

## AB. Coupling / Refactoring

**No refactoring was performed.** The evidence-based review above found the existing Phase 13
architecture absorbed six structurally very different ecosystems (a JVM build tool with no
lockfile, a Rust tool with a safe CLI-owned manifest editor, a Go tool with an indirect-flag
convention baked into its own JSON output, and two C/C++ tools with fundamentally different
manifest formats — one JSON, one ini-like, one executable Python) without any strain on the
contract. Per the phase's own "if no meaningful refactoring is required, do not invent
refactoring" rule (mirroring the Phase 13 architecture review's own precedent), none was
performed.

## AC. Documentation

Updated this phase: `CLAUDE.md` (Current State narrative, Architecture Snapshot's
`Core.Packages`/`Infrastructure.Packages` bullets, Known Limitations, Phase Discipline
paragraph), `ARCHITECTURE.md` (file-tree listing, toolchain list, Package Management Architecture
section, security-model bullet), `SECURITY.md` (Toolchain and Package Management section, new
"Phase 14 Review" section, Status), `DEVELOPMENT.md` (new "One Phase 14 Gotcha Worth
Remembering" section — the Conan graph-edge direct/transitive bug), `CHANGELOG.md` (new Phase 14
entry), `README.md`/`README.zh-TW.md` (Package Manager panel bullet updated to list all nine
implemented ecosystems). New: `docs/adr/ADR-015-cross-language-package-ecosystems.md`
(Context/Decision/Consequences/Known Limitations, mirroring ADR-014's structure exactly),
`PHASE14_PROGRESS_NOTES.md` (inter-fork handoff notes, not a permanent artifact but retained for
this report's accuracy), this report.

## AD. Known Limitations

See `docs/adr/ADR-015-cross-language-package-ecosystems.md`'s Known Limitations section for the
full, authoritative list. Summary:

- All six P1 ecosystems are implemented but not real-environment validated (no `mvn`/`gradle`/
  `cargo`/`rustc`/`go`/`vcpkg`/`conan`/`cmake` on this machine).
- Gradle supports read-only inspection only — by design, not a gap.
- vcpkg has no Update capability — by design.
- Conan mutation only works for `conanfile.txt`, never `conanfile.py` — by design.
- `cargo search`'s real-world reliability is unverified.
- The Conan JSON parsers target Conan 2.x's documented (not real-verified) schema.
- vcpkg's classic (non-manifest) mode is out of scope.
- No CMake package-manager adapter exists or is planned (CMake is a build system, not a package
  manager, handled elsewhere).
- pnpm, Yarn, Poetry, and uv remain entirely unimplemented.
- Real environment validation for this phase is Windows-only; Linux/macOS behavior is
  source-level-portable only, not real-tested.
- No credential/authentication handling for any P1 ecosystem.

## AE. Environment Matrix

| Ecosystem | Adapter | Tool Available | Real Tested | Status |
|---|---|---|---|---|
| NuGet | Yes | Yes | Yes | REAL-TESTED (Phase 13) |
| pip | Yes | Yes | Yes | REAL-TESTED (Phase 13) |
| npm | Yes | Yes | Yes | REAL-TESTED (Phase 13) |
| Maven | Yes | No (`mvn` absent) | Absence only | IMPLEMENTED BUT NOT REAL-ENVIRONMENT VALIDATED |
| Gradle | Yes (Inspector-only) | No (`gradle` absent) | Absence only | IMPLEMENTED BUT NOT REAL-ENVIRONMENT VALIDATED |
| Cargo | Yes | No (`cargo`/`rustc` absent) | Absence only | IMPLEMENTED BUT NOT REAL-ENVIRONMENT VALIDATED |
| Go Modules | Yes | No (`go` absent) | Absence only | IMPLEMENTED BUT NOT REAL-ENVIRONMENT VALIDATED |
| vcpkg | Yes | No (`vcpkg` absent) | Absence only | IMPLEMENTED BUT NOT REAL-ENVIRONMENT VALIDATED |
| Conan | Yes | No (`conan` absent) | Absence only | IMPLEMENTED BUT NOT REAL-ENVIRONMENT VALIDATED |
| pnpm | No | — | — | NOT IMPLEMENTED (deferred) |
| Yarn | No | — | — | NOT IMPLEMENTED (deferred) |
| Poetry | No | — | — | NOT IMPLEMENTED (deferred) |
| uv | No | — | — | NOT IMPLEMENTED (deferred) |

Windows: as above. Linux/WSL2: not exercised this phase (no environment available). macOS: never
available, not claimed.

## AF. Final Status

**PASS**
