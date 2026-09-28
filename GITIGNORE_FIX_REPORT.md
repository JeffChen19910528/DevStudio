# DevStudio — Fix Phase 13 Git Ignore Collision — Report

## A. Previous `.gitignore` Behavior

Line 21 read:

```
**/packages/*
!**/packages/build/
```

`**/packages/*` matches any directory named `packages` at any depth, plus everything
under it (with an exception carved out for a `build/` subfolder). This is boilerplate
copied from the standard Visual Studio `.gitignore` template, where it exists to ignore
the legacy `packages.config`-style NuGet restore folder (historically found at the
solution root as `packages/<PackageId>.<Version>/...`).

This repository's `core.ignorecase` is `true` (confirmed via `git config --get
core.ignorecase`), which is the default on a Windows checkout. Gitignore glob matching
respects that setting, so `**/packages/*` matches `Packages`, `PACKAGES`, etc. at any
path depth — not only a lowercase `packages` folder.

## B. Root Cause

Two facts combined to cause the collision:

1. **The repository never actually uses `packages.config`-style restore** — a full
   search (`find . -iname "packages" ...`) found no such directory anywhere in the
   working tree. This project is 100% SDK-style/`PackageReference`, which restores into
   the NuGet global package cache, not a repo-local `packages/` folder. The rule was
   dead boilerplate even before Phase 13.
2. **Phase 13 added real source directories named `Packages`** —
   `src/DevStudio.Core/Packages/` and `src/DevStudio.Infrastructure/Packages/` — and
   the case-insensitive `**/packages/*` pattern matched them, silently gitignoring all
   of Phase 13's product code, its tests directory counterparts, and the new
   `PackageManagerViewModel.cs`.

Verified before the fix:

```
$ git check-ignore -v src/DevStudio.Core/Packages/PackageReference.cs
.gitignore:21:**/packages/*    src/DevStudio.Core/Packages/PackageReference.cs

$ git check-ignore -v src/DevStudio.Infrastructure/Packages/NuGetPackageAdapter.cs
.gitignore:21:**/packages/*    src/DevStudio.Infrastructure/Packages/NuGetPackageAdapter.cs
```

## C. Exact `.gitignore` Change

```diff
 ## NuGet
+## (legacy packages.config restore folder at the repo/solution root only —
+## intentionally NOT **/packages/*, which on a case-insensitive filesystem also
+## matches source directories named "Packages", e.g. src/*/Packages/)
 *.nupkg
-**/packages/*
-!**/packages/build/
+/packages/*
+!/packages/build/
```

The fix anchors the rule to the repository root (`/packages/*` instead of
`**/packages/*`) rather than matching `packages`/`Packages` at any depth. This
preserves the rule's actual intent (ignore a root-level legacy restore folder, should
one ever appear) while structurally excluding any nested source directory, regardless
of casing — no case-sensitivity trick or fragile exception list required.

No `!src/DevStudio.Core/Packages/**`-style force-include exceptions were added, per the
task's preference for fixing the overly broad rule itself.

## D. Intended Ignored Paths Preserved

Simulated a legacy root `packages/` restore folder and confirmed it is still ignored,
with the `build/` exception still honored:

```
$ mkdir -p packages/SomeLib.1.0.0 && touch packages/SomeLib.1.0.0/lib.dll
$ git check-ignore -v packages/SomeLib.1.0.0/lib.dll
.gitignore:23:/packages/*    packages/SomeLib.1.0.0/lib.dll

$ git check-ignore -v packages/build/foo.targets
(no match — exit 1, correctly NOT ignored, per the !/packages/build/ exception)
```

(Test artifacts removed after verification; not part of the actual working tree.)

## E. Phase 13 Source Paths Now Tracked/Visible

`git status --short` now shows, as untracked (`??`), exactly the Phase 13 additions
that were previously invisible:

```
?? src/DevStudio.Core/Packages/
?? src/DevStudio.Infrastructure/Packages/
?? src/DevStudio.UI/ViewModels/PackageManagerViewModel.cs
?? tests/DevStudio.Core.Tests/Packages/
?? tests/DevStudio.Infrastructure.Tests/Packages/
```

plus the previously-untracked Phase 13 documentation (`ARCHITECTURE.md`,
`CHANGELOG.md`, `DEVELOPMENT.md`, `SECURITY.md`,
`PHASE13_COMPLETION_REPORT.md`, `PHASE13_ARCHITECTURE_REVIEW.md`) and the modified
files (`README.md`/`README.zh-TW.md`, `App.axaml.cs`, `MainWindow.axaml`,
`ExecutableLocator.cs`, `NodePackageManagerToolchainDetector.cs`, `Strings.resx`/
`Strings.zh-TW.resx`, `MainWindowViewModel.cs`, and their test counterparts) — all of
which were already correctly tracked/visible and unaffected by this fix.

## F. `git check-ignore` Verification

```
$ git check-ignore -q src/DevStudio.Core/Packages/PackageReference.cs; echo $?
1   # not ignored

$ git check-ignore -q src/DevStudio.Infrastructure/Packages/NuGetPackageAdapter.cs; echo $?
1   # not ignored
```

Both source directories are confirmed no longer ignored.

## G. `git status` Verification

Confirmed via `git status --short` (§E above) — Phase 13 files now appear as expected
(untracked `??` for new files/directories, modified `M` for existing files Phase 13
touched). Nothing that was previously tracked/visible became newly ignored.

## H. Build/Test Verification

Per the task's guidance ("a full 556-test rerun is not required if it would only
duplicate already verified results" for a metadata-only change), performed the
minimum meaningful check:

```
$ dotnet build DevStudio.slnx
建置成功 (Build succeeded). 0 Warnings, 0 Errors.
```

No product code was touched, so no behavior change is possible; the build's success
confirms the `.gitignore` edit introduced no incidental damage (e.g., no accidental
edit to a tracked file, no build-file collision).

## I. Files Changed

- `.gitignore` — the only file modified by this task.
- `GITIGNORE_FIX_REPORT.md` (this report) — new.

No product code, tests, architecture, or package-manager logic was touched.

## J. Commit Readiness

All previously-gitignored Phase 13 source is now visible to Git and stageable. Nothing
in this task was committed — the `.gitignore` fix and this report remain uncommitted,
alongside all previously uncommitted Phase 13 work, for the user's own review and
commit.

## Final Status

**READY FOR REVIEW / COMMIT**
