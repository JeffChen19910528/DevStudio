# DevStudio

**Language: [English](README.md) | [繁體中文](README.zh-TW.md)**

DevStudio is a cross-platform desktop IDE for .NET development. Instead of reimplementing
compilers, debuggers, build systems, and language servers, it orchestrates the real tools you
already have installed (the .NET SDK, `git`, the Roslyn language server, `netcoredbg`) through a
single, consistent interface.

## What it does

- **Opens a folder** and automatically detects the projects/solutions inside it (.NET, plus
  read-only detection for CMake, Node, Python, Java, Rust, and Go projects).
- **Detects your installed toolchains** (SDKs, compilers, Git, Docker, etc.) and shows what each
  detected project can actually do based on what's really installed on your machine.
- **Edits and saves files**, remembers your open tabs and recent workspaces across restarts, and
  includes an integrated terminal.
- **Builds, runs, and debugs real .NET projects** — real `dotnet build`, real process launch with
  target auto-discovery, and real breakpoint debugging via the Debug Adapter Protocol.
- **Provides C# language intelligence** (completion, hover, go-to-definition, diagnostics) through
  the real Roslyn language server.
- **Runs your .NET tests** and shows results in a Test Explorer.
- **Includes a Source Control panel** backed by your real, locally installed `git`.
- **Includes a Package Manager panel** for viewing, searching, adding, removing, updating, and
  restoring dependencies — real support for NuGet (.NET), pip (Python), npm (Node), Maven,
  Gradle, Cargo, Go Modules, vcpkg, and Conan, gated by Workspace Trust for anything that mutates
  a project (some ecosystems, like Gradle, only support viewing — see `CLAUDE.md`'s Known
  Limitations for exactly which operations each one supports). pnpm, Yarn, Poetry, and uv are not
  yet supported.
- **Supports extensions** through a simple manifest + command-contribution model.
- **Supports two interface languages** — English and Traditional Chinese — switchable at any time
  from Settings, with no restart required.

Other project ecosystems (CMake, Node, Python, Java, Rust, Go) are detected but not yet
buildable/runnable/debuggable — the UI says so honestly instead of pretending they work.

## Getting started

### Option 1 — One-click launch (requires the .NET 10 SDK)

If you have the [.NET 10 SDK](https://dotnet.microsoft.com/download) installed:

- **Windows**: double-click `run.bat`
- **Linux / macOS**: open a terminal in this folder and run:
  ```bash
  chmod +x run.sh   # only needed the first time
  ./run.sh
  ```

Either script builds and launches DevStudio for you.

### Option 2 — Standalone executable (no .NET SDK required to *run* it)

You can build a self-contained, single-file executable for each platform. Building it still
requires the .NET SDK, but the resulting file can be copied to and run on another machine that
has no .NET installed at all.

```bash
# Windows
scripts\publish.bat

# Linux / macOS
chmod +x scripts/publish.sh   # only needed the first time
./scripts/publish.sh
```

This produces:

| Platform | Executable |
|---|---|
| Windows | `publish/win-x64/DevStudio.App.exe` |
| Linux | `publish/linux-x64/DevStudio.App` |
| macOS (Intel) | `publish/osx-x64/DevStudio.App` |
| macOS (Apple Silicon) | `publish/osx-arm64/DevStudio.App` |

Copy the executable for your platform anywhere you like and run it directly — no installation
step, no terminal required after that.

## User Interface

### Layout overview

The window is divided into four main areas:

```
┌─────────────────────────────────────────────────────────┐
│  Menu bar                                               │
├─────────────────────────────────────────────────────────┤
│  Toolbar row 1 — file / build / run controls            │
│  Toolbar row 2 — debug / language / test controls       │
├──────────────┬──────────────────────────┬───────────────┤
│              │                          │               │
│   Explorer   │        Editor            │  Properties   │
│  (file tree) │      (tabbed files)      │   (project    │
│              │                          │    details)   │
├──────────────┴──────────────────────────┴───────────────┤
│  Bottom panel (tabbed)                                  │
│  Problems · Output · Terminal · Toolchains · Call Stack │
│  Threads · Locals · Breakpoints · Completion · Hover   │
│  Test Explorer · Source Control · Extensions · Packages │
├─────────────────────────────────────────────────────────┤
│  Status bar                                             │
└─────────────────────────────────────────────────────────┘
```

### Toolbar

**Row 1 — file, build, and run:**

| Control | Description |
|---|---|
| Open Folder | Open a workspace folder |
| Save | Save the active file |
| Terminal | Open a new integrated terminal tab |
| Build | Build the selected project/solution |
| Cancel | Cancel a running build |
| Build config dropdown | Choose Debug or Release for the build target |
| Deps config dropdown | Choose Debug or Release for dependency projects |
| Run config dropdown | Choose which project/target to run |
| Run | Build (if needed) and launch the selected target |
| Stop | Stop the running process |
| Restart | Stop then immediately relaunch the process |

**Row 2 — debug and language:**

| Control | Description |
|---|---|
| Start Debugging | Launch the selected target under the debugger |
| Continue | Resume execution from a breakpoint |
| Pause | Pause a running debug session |
| Step Over | Execute the current line and stop at the next |
| Step Into | Step into a called method |
| Step Out | Run until the current method returns |
| Stop Debugging | Terminate the debug session |
| Completion | Request code completions at the cursor (C# files) |
| Hover | Show type/documentation for the symbol at the cursor |
| Go To Definition | Jump to the definition of the symbol at the cursor |
| Restart LSP | Restart the Roslyn language server |

### Panels

**Left — Explorer:** Shows the folder tree for the open workspace. Double-click a file to open it in the editor. Right-click a project node for context actions (e.g. **Set as Startup Project**).

**Centre — Editor:** A tabbed code editor. Modified files show a `*` indicator next to their name. The find/replace bar (Edit → Find) appears above the editor when active and supports case-sensitive search and Replace All.

When a file is modified on disk by an external tool while it is open, a yellow banner appears offering to reload it from disk.

**Right — Properties:** Shows metadata for the item selected in the Explorer: project type, file path, detection confidence, whether it is executable, and which capabilities are available (Build, Run, Debug, etc.) based on your installed toolchains.

**Bottom panel tabs:**

| Tab | Description |
|---|---|
| Problems | Errors and warnings from the last build or language server. Click any entry to jump to its source line. |
| Output | Live stdout/stderr from build and run operations. |
| Terminal | Integrated shell sessions. Use New / Restart / Close to manage tabs. Type commands in the input box at the bottom of each tab. |
| Toolchains | Lists every detected toolchain (SDK, compiler, Git, Docker, etc.) with its version and path. Also shows installed Visual Studio instances. Use Refresh to re-scan. |
| Call Stack | Active stack frames during a debug session. Click a frame to navigate to that source location. |
| Threads | List of threads in the debugged process. |
| Locals | Variables in scope at the current breakpoint, grouped by scope. |
| Breakpoints | All breakpoints currently set, with file, line, and enabled status. |
| Completion | Results from the last Completion request. Double-click an item to insert it. |
| Hover | Documentation or type information from the last Hover request. |
| Test Explorer | Discover and run .NET tests. Use Refresh Tests, Run All, Run Selected, or Stop. Double-click a result to navigate to the test. |
| Source Control | Full Git UI: view staged/unstaged changes and diffs, stage/unstage/discard, commit, manage branches, and browse history. |
| Extensions | Lists discovered extensions. Select one to see details, validation errors, and its contributed commands. Use Enable/Disable to toggle, and Invoke to run a contributed command manually. |
| Package Manager | Manage dependencies per-project. Select a project and its package manager, then use the Installed / Browse / Updates / Dependencies tabs. |

**Status bar:** Displays workspace name, active filename, project context, cursor line/column, file encoding, line-ending style, modified flag, last build status, run status, and language-server state.

---

### Menus

#### File

| Item | Action |
|---|---|
| New File | Create a new untitled file |
| Open File | Open a single file in the editor |
| Open Folder | Open a folder as the active workspace |
| Recent Workspaces | Submenu of recently opened folders |
| Reopen Last Workspace on Startup | Toggle: automatically restore the last workspace on next launch |
| Save | Save the active file (Ctrl+S) |
| Save As | Save the active file to a new path |
| Close | Close the active editor tab |
| Exit | Quit DevStudio |

#### Edit

| Item | Action |
|---|---|
| Undo | Undo the last edit |
| Redo | Redo the last undone edit |
| Cut | Cut the selection |
| Copy | Copy the selection |
| Paste | Paste from the clipboard |
| Find | Open the find/replace bar |
| Replace | Open the find/replace bar (replace mode) |
| Go To Line | Jump to a specific line number |

#### View

| Item | Action |
|---|---|
| Terminal | Open a new terminal tab (same as the toolbar button) |
| Toggle Theme | Switch between light and dark themes |

#### Project

| Item | Action |
|---|---|
| Trust / Untrust Workspace | Toggle Workspace Trust for the current folder. Trusted workspaces unlock Build, Run, Debug, tests, and mutating Git operations. |

#### Build

| Item | Action |
|---|---|
| Build | Build the active workspace |
| Rebuild | Clean then build |
| Clean | Delete build outputs |
| Restore | Restore NuGet packages (`dotnet restore`) |
| Cancel | Cancel the running build |

#### Run

| Item | Action |
|---|---|
| Run | Build (if needed) and launch the selected run target |
| Run Without Building | Launch the last built output directly |
| Stop | Stop the running process |
| Restart | Stop and immediately relaunch |

#### Debug

| Item | Action |
|---|---|
| Start Debugging | Launch under the debugger |
| Continue | Resume from a breakpoint |
| Pause | Pause execution |
| Step Over | Step to the next line |
| Step Into | Step into a method call |
| Step Out | Run until the current method returns |
| Stop | Terminate the debug session |
| Toggle Breakpoint at Line | Add or remove a breakpoint at the cursor line |

#### Language

| Item | Action |
|---|---|
| Completion | Request completions at the cursor |
| Hover | Request hover info at the cursor |
| Go To Definition | Jump to the symbol's definition |
| Restart Language Server | Restart the Roslyn language server |

#### Tools

| Item | Action |
|---|---|
| Refresh Toolchains | Re-scan for installed SDKs, compilers, and tools |
| Settings | Open the Settings window (theme, language, startup behaviour) |

#### Extensions

| Item | Action |
|---|---|
| Refresh | Rescan the extensions folder for new or changed extensions |

---

## Using DevStudio

1. **Open a workspace** — File → Open Folder, and pick a folder containing a .NET project or
   solution.
2. **Browse and edit** — use the Explorer panel on the left to open files; edited files show a
   modified indicator and can be saved with File → Save (or Ctrl+S).
3. **Build** — pick a configuration (Debug/Release) from the toolbar and click Build, or use the
   Build menu. Errors and warnings appear in the Problems panel; click one to jump to its source
   line.
4. **Run** — pick a run target from the toolbar dropdown and click Run. Output appears in the
   Output panel; use Stop/Restart to control the running process. To set a project as the active
   startup target (including projects not auto-detected as runnable, such as legacy ASP.NET web
   projects), right-click the project in the Explorer and choose **Set as Startup Project**.
5. **Debug** — set breakpoints with Debug → Toggle Breakpoint at Line, then click Start Debugging.
   Use the Call Stack, Threads, Locals, and Breakpoints panels while stopped at a breakpoint.
6. **Get code help** — as you type in a `.cs` file, use the Completion/Hover buttons or Go To
   Definition to query the language server; diagnostics appear automatically.
7. **Run tests** — open the Tests panel and use Refresh Tests / Run All / Run Selected.
8. **Use source control** — open the Source Control panel to see changes, stage/unstage/commit,
   and manage branches for a real Git repository.
9. **Manage extensions** — open the Extensions panel to see discovered extensions and
   enable/disable them.
10. **Change language** — go to Tools → Settings, pick English or 繁體中文 from the Language
    dropdown. The interface updates immediately.

Some actions (Build, Run, Debug, starting the language server, running tests, and Git operations
that change repository state) ask you to confirm trusting the workspace the first time, since they
execute real code from the project you opened.

## Requirements

- .NET 10 SDK — only needed to build/run from source, or to build the standalone executables.
  A standalone executable built via `scripts/publish.*` does **not** require .NET to be installed
  on the machine that runs it.
- `git` — required for the Source Control panel.
- `netcoredbg` — required for debugging (install via `winget install Samsung.NetCoreDbg` on
  Windows, or your platform's package manager elsewhere).
