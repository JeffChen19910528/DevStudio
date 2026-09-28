# Phase 14 Progress Notes

Scratch handoff file between Phase 14 forks. Not the final completion report — that is
`PHASE14_COMPLETION_REPORT.md`, written once all P1 ecosystems (or their honest
"not implemented, tool unavailable" status) are done.

## P1-A: Java / Maven / Gradle — DONE (this fork)

### Environment facts (verified on this machine, do not re-check)

- `java` — installed, 25.0.2 (already had a `JavaToolchainDetector`/`JavaProjectDetector` from
  an earlier phase).
- `mvn` — **NOT installed**.
- `gradle` — **NOT installed**.

### What was implemented

- `WellKnownToolchainIds.Maven`/`.Gradle` added (`src/DevStudio.Core/Toolchains/WellKnownToolchainIds.cs`).
  `WellKnownPackageManagerIds.Maven`/`.Gradle` already existed from Phase 13 as forward-declared,
  unused placeholders — now backed by real adapters.
- `src/DevStudio.Infrastructure/Toolchains/MavenToolchainDetector.cs`,
  `GradleToolchainDetector.cs` — new, mirror `NodePackageManagerToolchainDetector`'s
  `ExecutableLocator`-first resolution pattern (Windows `.cmd` shim safety).
- `src/DevStudio.Infrastructure/Packages/MavenPackageAdapter.cs` — full
  `IPackageInspector`/`IPackageInstaller`/`IPackageRemover`/`IPackageUpdater`/
  `IPackageSourceManager`. `ListInstalledAsync` parses real `mvn dependency:tree` output (direct
  vs. transitive via indentation depth); `ListDependenciesAsync` reads declared deps straight from
  pom.xml (no process); `ListOutdatedAsync` parses `versions-maven-plugin:display-dependency-updates`
  output; `AddAsync`/`RemoveAsync`/`UpdateAsync` do structured `XDocument` edits of `<dependencies>`
  (never regex-on-XML); `RestoreAsync` runs `dependency:resolve`; `GetSourcesAsync` reads only
  `<repositories>` from the project's own pom.xml (never `~/.m2/settings.xml`, where credentials
  would live).
- `src/DevStudio.Infrastructure/Packages/GradlePackageAdapter.cs` — **`IPackageInspector` ONLY**,
  deliberately. Add/Remove/Update/Restore/Search/Sources are all `false` — a Gradle build script is
  an executable program (string notation / map notation / version catalogs / interpolated
  variables all coexist), and no static edit can rewrite it safely in the general case. This is a
  documented, deliberate scope decision per the Phase 14 spec's own §10 caution, not a shortcut.
  `ListInstalledAsync` parses real `gradle dependencies --console=plain` output (5-character
  indentation units, unlike Maven's 3). `ListDependenciesAsync` does a best-effort static regex
  parse of *string-notation only* declarations (`implementation 'g:a:v'` /
  `implementation("g:a:v")`) — map notation and version catalogs are NOT detected; this is
  documented in the type's own XML doc comment, not silently wrong.
- Both adapters registered in `PackageManagerRegistry` and both toolchain detectors registered in
  `ToolchainRegistry`, via `src/DevStudio.App/App.axaml.cs`.
- Zero UI changes needed — confirmed via `grep` that `PackageManagerViewModel.cs` has zero
  ecosystem-specific string literals; Maven/Gradle "just work" through the existing generic
  Installed/Browse/Updates/Dependencies tabs and capability-driven button enablement.
- Zero new localization strings needed — all existing Phase 13 `PackageManager.*` keys are already
  fully generic ("Package", "Direct", "Transitive", "Installed", etc.); verified none of the new
  code paths introduce a new user-facing string.

### Honest validation status

**Maven and Gradle are "IMPLEMENTED BUT NOT REAL-ENVIRONMENT VALIDATED."** Neither `mvn` nor
`gradle` exists on this machine, so no real invocation of either was ever made. What WAS
real-tested (via `RealEnvironmentToolchainTests.cs` and the new
`MavenGradlePackageAdapterIntegrationTests.cs`, both running against the real, unmocked
`ProcessRunner`): that both toolchain detectors and both package adapters correctly, honestly
report "not installed" / "capabilities: none" / a populated `UnavailableReason` against this real
machine — never silently claim a capability they cannot deliver. All command-construction and
output-parsing logic (dependency-tree parsing, outdated-diff parsing, pom.xml XML editing,
Gradle build-script string-notation parsing) is covered only by unit tests against realistic,
hand-constructed fixture text mirroring each tool's own documented, version-stable output format —
never a real subprocess.

### Tests added (28 new, 0 regressions)

- `tests/DevStudio.Infrastructure.Tests/Packages/MavenPackageAdapterTests.cs` (14 tests)
- `tests/DevStudio.Infrastructure.Tests/Packages/GradlePackageAdapterTests.cs` (7 tests)
- `tests/DevStudio.Infrastructure.Tests/Packages/MavenGradlePackageAdapterIntegrationTests.cs`
  (2 tests, real `ProcessRunner`/`ToolchainRegistry`, real absence)
- `tests/DevStudio.Infrastructure.Tests/Toolchains/ToolchainDetectorVersionParsingTests.cs`
  (+4 tests: Maven/Gradle NotInstalled + version-parsing)
- `tests/DevStudio.Infrastructure.Tests/Toolchains/RealEnvironmentToolchainTests.cs`
  (+2 tests: real Maven/Gradle absence on this machine)

### Final build/test numbers (this fork)

- `dotnet build DevStudio.slnx` (Debug): **PASS**, 0 warnings, 0 errors.
- `dotnet build DevStudio.slnx -c Release`: **PASS**, 0 warnings, 0 errors.
- `dotnet test DevStudio.slnx`: **584/584 PASS** (212 Core + 70 UI + 302 Infrastructure), 0
  failures, 0 skipped. (Baseline from Phase 13 was 556; +28 from this fork, 0 regressions.)

### Not done yet (left for the next fork)

- Rust/Cargo (P1-B #1)
- Go Modules (P1-B #2)
- C/C++ vcpkg (P1-C #1)
- C/C++ Conan (P1-C #2)
- `docs/adr/ADR-015-cross-language-package-ecosystems.md`
- `PHASE14_COMPLETION_REPORT.md` (sections A–AF, per the full spec's §34) — do NOT write this
  until all P1 ecosystems (or their honest non-implementation) are accounted for.
- CLAUDE.md / ARCHITECTURE.md / SECURITY.md / DEVELOPMENT.md / CHANGELOG.md / README updates for
  Phase 14 as a whole — hold until the full picture (all 6 ecosystems' real status) is known,
  rather than documenting Java alone and then re-editing the same sections five more times.

### A note on scope, for whoever picks this up

Rust/Go tests should follow the exact same honesty pattern used here: `rustc`/`cargo`/`go` are
also NOT installed on this machine (confirmed by the parent conversation before this fork started),
so those two adapters will land in the exact same "implemented, not real-environment validated"
bucket as Maven/Gradle — do not attempt to fake real testing for them either. `vcpkg`/`cmake`/
`conan` are similarly absent. If the next fork finds any of these now installed, it should
re-verify before assuming this note is still accurate.

## P1-B: Rust / Cargo, Go Modules — DONE (this fork)

### Environment facts (re-verified on this machine)

- `cargo`, `rustc`, `go` — all **NOT installed** (confirmed again, matches P1-A's note).
- Project/toolchain detection for both ecosystems **already existed from an earlier phase** —
  `RustProjectDetector`/`GoProjectDetector` (Core.Projects/Infrastructure.Projects) and
  `RustToolchainDetector`/`GoToolchainDetector` (Infrastructure.Toolchains) were already real,
  already wired into `ProjectDetectionService`/`ToolchainRegistry`, and `RustToolchainDetector`/
  `GoToolchainDetector`'s own real-absence behavior was already covered by
  `RealEnvironmentToolchainTests.cs` from a prior phase. This fork only needed to add the
  *package-management* layer on top — no new toolchain/project detector files were needed.

### What was implemented

- `src/DevStudio.Infrastructure/Packages/CargoPackageAdapter.cs` — full
  `IPackageInspector`/`IPackageSearcher`/`IPackageInstaller`/`IPackageRemover`/`IPackageUpdater`.
  `ListInstalledAsync` parses real `cargo metadata --format-version 1` JSON (direct vs. transitive
  derived from the root workspace member's own `dependencies` array against the full `packages`
  list); `ListDependenciesAsync` reads `[dependencies]`/`[dev-dependencies]`/`[build-dependencies]`
  straight from Cargo.toml (no process, via the new `CargoTomlReader`, deliberately conservative —
  only inline-string/table-with-version forms, documented as NOT parsing a nested
  `[dependencies.name]` table-header form); `AddAsync`/`RemoveAsync` always shell out to `cargo
  add`/`cargo rm` (never manual Cargo.toml editing, per the phase spec's explicit instruction —
  Cargo already owns safe manifest+lockfile editing together); `UpdateAsync` uses `cargo update
  --package <id> [--precise <version>]`; `RestoreAsync` uses `cargo fetch`; `SearchAsync` uses
  `cargo search` (command construction is real and documented, but its real-world reliability is
  explicitly flagged as unverified — crates.io has rate-limited/deprecated `cargo search` in some
  contexts and this could not be checked without a real cargo install). `ListOutdated` and
  `ManageSources` are `false` — no `cargo outdated` ships with stock Cargo, and registry config
  lives in `.cargo/config.toml`, which risks exposing configured registry tokens if read.
- `src/DevStudio.Infrastructure/Packages/GoModulePackageAdapter.cs` — full
  `IPackageInspector`/`IPackageInstaller`/`IPackageRemover`/`IPackageUpdater` (**no**
  `IPackageSearcher`). `ListInstalledAsync` parses real `go list -m -json all`'s documented
  "concatenated JSON objects, not an array" stream format (a hand-rolled brace-depth splitter,
  `GoListModuleParser`, since `JsonSerializer` cannot deserialize multiple root objects directly);
  direct vs. transitive comes from the JSON's own `Indirect` field, never guessed.
  `ListDependenciesAsync` reads `require` lines straight from go.mod (`GoModReader`, handling both
  the single-line and parenthesized-block `require` syntaxes, using go.mod's own `// indirect`
  comment convention). `AddAsync`/`UpdateAsync` both use `go get <module>@<version>` (Go's own
  model has no separate add/update verb — a version pin is the only distinction). `RemoveAsync` is
  **two** real commands, `go mod edit -droprequire <module>` followed by `go mod tidy` — documented
  explicitly in the adapter's own doc comment that go.sum is only actually cleaned up by the tidy
  step, and the operation reports failure (without running tidy) if the edit step itself fails.
  `RestoreAsync` uses `go mod download`. **`Search` capability is explicitly `false`** — per the
  phase spec's own instruction not to invent a search mechanism where none is reliably documented;
  there is no official, stable Go module search CLI/API (pkg.go.dev has a web UI, not a documented
  JSON API).
- Both adapters registered in `PackageManagerRegistry` via `src/DevStudio.App/App.axaml.cs` (no
  new toolchain detector registration needed — `RustToolchainDetector`/`GoToolchainDetector` were
  already registered from the earlier phase that added them).
- Zero UI/ViewModel changes — confirmed the same way P1-A did (grep for ecosystem-specific
  literals in `PackageManagerViewModel.cs`: none). Zero new localization strings — all existing
  Phase 13 generic keys already cover Direct/Transitive/Installed/etc.; Go's "indirect" concept
  maps onto the existing generic "Transitive Dependency" (間接相依套件) string as-is.

### Honest validation status

**Cargo and Go Modules are "IMPLEMENTED BUT NOT REAL-ENVIRONMENT VALIDATED."** Neither `cargo` nor
`go` exists on this machine, so no real invocation of either was ever made. What WAS real-tested
(via the new `CargoGoPackageAdapterIntegrationTests.cs`, running against the real, unmocked
`ProcessRunner`/`ToolchainRegistry`, plus the pre-existing `RealEnvironmentToolchainTests.cs`
Rust/Go entries from an earlier phase): that both adapters correctly, honestly report "not
installed" / "capabilities: none" / a populated `UnavailableReason` against this real machine. All
command-construction and output-parsing logic (`cargo metadata` JSON, `cargo search` text, go.mod
require-block parsing, `go list -m -json` concatenated-JSON-stream parsing) is covered only by unit
tests against realistic, hand-constructed fixture data mirroring each tool's own documented,
version-stable output format — never a real subprocess. `cargo search`'s real-world CLI behavior in
particular is flagged as unverified in the adapter's own doc comment, since its rate-limiting/
deprecation status could not be checked without a real cargo install.

### Tests added (25 new, 0 regressions)

- `tests/DevStudio.Infrastructure.Tests/Packages/CargoPackageAdapterTests.cs` (12 tests)
- `tests/DevStudio.Infrastructure.Tests/Packages/GoModulePackageAdapterTests.cs` (11 tests)
- `tests/DevStudio.Infrastructure.Tests/Packages/CargoGoPackageAdapterIntegrationTests.cs`
  (2 tests, real `ProcessRunner`/`ToolchainRegistry`, real absence)

### Final build/test numbers (this fork)

- `dotnet build DevStudio.slnx` (Debug): **PASS**, 0 warnings, 0 errors.
- `dotnet build DevStudio.slnx -c Release`: **PASS**, 0 warnings, 0 errors.
- `dotnet test DevStudio.slnx`: **609/609 PASS** (212 Core + 70 UI + 327 Infrastructure), 0
  failures, 0 skipped. (Baseline from P1-A was 584; +25 from this fork, 0 regressions.)

### Not done yet (left for the next fork)

- C/C++ vcpkg (P1-C #1)
- C/C++ Conan (P1-C #2)
- `docs/adr/ADR-015-cross-language-package-ecosystems.md`
- `PHASE14_COMPLETION_REPORT.md` (sections A–AF, per the full spec's §34) — do NOT write this
  until all P1 ecosystems (or their honest non-implementation) are accounted for.
- CLAUDE.md / ARCHITECTURE.md / SECURITY.md / DEVELOPMENT.md / CHANGELOG.md / README updates for
  Phase 14 as a whole — still holding until vcpkg/Conan are done, per P1-A's original note.

### A further note on scope, for whoever picks up vcpkg/Conan

`vcpkg` and `conan` are also NOT installed on this machine (re-verified at the start of this
fork). Unlike Rust/Go, there is **no existing project/toolchain detector for either C/C++ package
ecosystem** — `CMakeProjectDetector`/`CMakeToolchainDetector` exist but detect CMake itself, not
vcpkg.json/conanfile.py/conanfile.txt manifests. The vcpkg/Conan fork will likely need to add
project-detection recognition for `vcpkg.json`/`vcpkg-configuration.json`/`conanfile.py`/
`conanfile.txt` (check whether this belongs in a new detector or as an extension of
`CMakeProjectDetector`, since C/C++ projects using vcpkg/Conan are very often also CMake projects —
inspect `CMakeProjectDetector.cs` first before deciding) as well as new toolchain detectors for
`vcpkg`/`conan` themselves (mirroring `MavenToolchainDetector`/`RustToolchainDetector`'s shape).
