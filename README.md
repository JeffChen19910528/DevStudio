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

## Using DevStudio

1. **Open a workspace** — File → Open Folder, and pick a folder containing a .NET project or
   solution.
2. **Browse and edit** — use the Explorer panel on the left to open files; edited files show a
   modified indicator and can be saved with File → Save (or Ctrl+S).
3. **Build** — pick a configuration (Debug/Release) from the toolbar and click Build, or use the
   Build menu. Errors and warnings appear in the Problems panel; click one to jump to its source
   line.
4. **Run** — pick a run target from the toolbar dropdown and click Run. Output appears in the
   Output panel; use Stop/Restart to control the running process.
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
