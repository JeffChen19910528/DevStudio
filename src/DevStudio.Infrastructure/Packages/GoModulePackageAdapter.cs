using System.Text.Json;
using DevStudio.Core.Packages;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Packages;

/// <summary>
/// Drives real Go Modules (Phase 14 P1-B, SKILL.md-style phase discipline). <c>go</c> is NOT
/// installed on the development machine this adapter was written on — every command-construction/
/// output-parsing path here is unit-tested against realistic fixture text mirroring <c>go list -m
/// -json all</c>'s documented, stable JSON-stream output format, never against a real invocation.
/// This adapter must always be described as "implemented but not real-environment validated,"
/// never "real-tested" — see ADR-015.
///
/// <see cref="Search"/> is deliberately <c>false</c>: unlike crates.io/npm/PyPI/Maven Central,
/// there is no official, stable Go module search CLI or API this adapter can rely on (pkg.go.dev
/// has a web search UI, not a documented stable JSON API) — per the Phase 14 spec's own explicit
/// instruction not to invent a search mechanism the tooling does not provide.
/// </summary>
public sealed class GoModulePackageAdapter : IPackageManagerAdapter, IPackageInspector, IPackageInstaller, IPackageRemover, IPackageUpdater
{
    public string Id => WellKnownPackageManagerIds.GoModules;
    public string DisplayName => "Go Modules";

    private static readonly PackageManagerCapabilities FullCapabilities = new(
        ListInstalled: true,
        ListDependencies: true,
        Search: false, // No reliable, documented, version-stable Go module search mechanism exists — see type doc comment.
        Add: true,
        Remove: true,
        Update: true,
        Restore: true,
        ListOutdated: false, // `go list -u -m all` can report available updates, but its exact text/JSON shape for "has an update" has changed across Go versions; not claimed without a real installation to verify against.
        ManageSources: false, // GOPROXY/GONOSUMCHECK are process-wide environment configuration, not a per-project source list.
        LockfileSupport: true, // go.sum
        TransitiveDependencySupport: true,
        PrereleaseSupport: true); // Go modules support pseudo-versions/pre-release suffixes.

    private readonly IProcessRunner _processRunner;
    private readonly IToolchainRegistry _toolchainRegistry;

    public GoModulePackageAdapter(IProcessRunner processRunner, IToolchainRegistry toolchainRegistry)
    {
        _processRunner = processRunner;
        _toolchainRegistry = toolchainRegistry;
    }

    public PackageProject? DetectProject(ProjectInfo project)
    {
        if (project.ProjectType != ProjectType.Go) return null;

        var goModPath = Path.Combine(project.RootPath, "go.mod");
        if (!File.Exists(goModPath)) return null;

        var go = _toolchainRegistry.Get(WellKnownToolchainIds.Go);
        if (go is null || !go.IsUsable)
        {
            return new PackageProject(goModPath, project.ProjectType, Id, DisplayName, PackageManagerCapabilities.None,
                "Go is not installed or has not been detected.");
        }

        return new PackageProject(goModPath, project.ProjectType, Id, DisplayName, FullCapabilities);
    }

    public async Task<IReadOnlyList<PackageReference>> ListInstalledAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var result = await RunGoAsync(project, new[] { "list", "-m", "-json", "all" }, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput)) return Array.Empty<PackageReference>();

        return GoListModuleParser.Parse(result.StandardOutput, project.ProjectPath, project.PackageManagerId);
    }

    public Task<IReadOnlyList<PackageDependency>> ListDependenciesAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        // Declared `require` lines come straight from go.mod — no process needed, mirroring
        // MavenPackageAdapter/CargoPackageAdapter's own declared-vs-installed manifest reads.
        var declared = GoModReader.ReadRequiredModules(project.ProjectPath);
        IReadOnlyList<PackageDependency> result = declared
            .Select(d => new PackageDependency(d.ModulePath, d.Version, d.Indirect ? PackageDependencyKind.Transitive : PackageDependencyKind.Direct, null))
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<PackageReference>> ListOutdatedAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PackageReference>>(Array.Empty<PackageReference>());

    public async Task<PackageOperationResult> AddAsync(PackageProject project, string packageId, string? version, bool prerelease, bool isDevDependency, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        // `go get` is both "add" and "update" in Go's own model — a module path with an explicit
        // @version is how Go distinguishes them; there is no separate "add" verb.
        var spec = string.IsNullOrWhiteSpace(version) ? packageId : $"{packageId}@{version}";
        var result = await RunGoAsync(project, new[] { "get", spec }, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.WasCancelled) return PackageOperationResult.Cancelled(PackageOperation.Add, project.ProjectPath, packageId);

        return result.ExitCode == 0
            ? new PackageOperationResult(true, PackageOperation.Add, project.ProjectPath, packageId, ChangedModuleFiles(project), Array.Empty<string>(), result.ExitCode, false, null, result.StandardOutput)
            : new PackageOperationResult(false, PackageOperation.Add, project.ProjectPath, packageId, Array.Empty<string>(), Array.Empty<string>(), result.ExitCode, false,
                PackageOperationDiagnostics.FirstErrorLine(result, "The go get command failed."), result.StandardOutput);
    }

    public async Task<PackageOperationResult> RemoveAsync(PackageProject project, string packageId, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        // Go has no single "remove a dependency" command: `go mod edit -droprequire` strikes the
        // `require` line, but go.sum is only actually cleaned up by a subsequent `go mod tidy` —
        // DevStudio runs both steps for a real removal, and reports both go.mod and go.sum as
        // changed only if both commands succeed. This is more than a single CLI invocation but
        // still two fully structured, documented `go mod` subcommands — never a manual text edit
        // of go.mod/go.sum.
        var editResult = await RunGoAsync(project, new[] { "mod", "edit", "-droprequire", packageId }, outputSink, cancellationToken).ConfigureAwait(false);
        if (editResult.WasCancelled) return PackageOperationResult.Cancelled(PackageOperation.Remove, project.ProjectPath, packageId);
        if (editResult.ExitCode != 0)
        {
            return new PackageOperationResult(false, PackageOperation.Remove, project.ProjectPath, packageId, Array.Empty<string>(), Array.Empty<string>(), editResult.ExitCode, false,
                PackageOperationDiagnostics.FirstErrorLine(editResult, "The go mod edit -droprequire command failed."), editResult.StandardOutput);
        }

        var tidyResult = await RunGoAsync(project, new[] { "mod", "tidy" }, outputSink, cancellationToken).ConfigureAwait(false);
        if (tidyResult.WasCancelled) return PackageOperationResult.Cancelled(PackageOperation.Remove, project.ProjectPath, packageId);

        return tidyResult.ExitCode == 0
            ? new PackageOperationResult(true, PackageOperation.Remove, project.ProjectPath, packageId, ChangedModuleFiles(project), Array.Empty<string>(), tidyResult.ExitCode, false, null, tidyResult.StandardOutput)
            : new PackageOperationResult(false, PackageOperation.Remove, project.ProjectPath, packageId, Array.Empty<string>(), Array.Empty<string>(), tidyResult.ExitCode, false,
                PackageOperationDiagnostics.FirstErrorLine(tidyResult, "'go mod edit' succeeded but the follow-up 'go mod tidy' failed; go.sum may not be fully cleaned up."), tidyResult.StandardOutput);
    }

    public async Task<PackageOperationResult> UpdateAsync(PackageProject project, string packageId, string? targetVersion, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var spec = string.IsNullOrWhiteSpace(targetVersion) ? $"{packageId}@latest" : $"{packageId}@{targetVersion}";
        var result = await RunGoAsync(project, new[] { "get", spec }, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.WasCancelled) return PackageOperationResult.Cancelled(PackageOperation.Update, project.ProjectPath, packageId);

        return result.ExitCode == 0
            ? new PackageOperationResult(true, PackageOperation.Update, project.ProjectPath, packageId, ChangedModuleFiles(project), Array.Empty<string>(), result.ExitCode, false, null, result.StandardOutput)
            : new PackageOperationResult(false, PackageOperation.Update, project.ProjectPath, packageId, Array.Empty<string>(), Array.Empty<string>(), result.ExitCode, false,
                PackageOperationDiagnostics.FirstErrorLine(result, "The go get command failed."), result.StandardOutput);
    }

    public async Task<PackageOperationResult> RestoreAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        // `go mod download` fetches every module go.sum already records without building anything
        // — the closest Go equivalent of `dotnet restore`/`npm install`/`cargo fetch`.
        var result = await RunGoAsync(project, new[] { "mod", "download" }, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.WasCancelled) return PackageOperationResult.Cancelled(PackageOperation.Restore, project.ProjectPath);

        return result.ExitCode == 0
            ? new PackageOperationResult(true, PackageOperation.Restore, project.ProjectPath, null, Array.Empty<string>(), Array.Empty<string>(), result.ExitCode, false, null, result.StandardOutput)
            : new PackageOperationResult(false, PackageOperation.Restore, project.ProjectPath, null, Array.Empty<string>(), Array.Empty<string>(), result.ExitCode, false,
                PackageOperationDiagnostics.FirstErrorLine(result, "The go mod download command failed."), result.StandardOutput);
    }

    private static IReadOnlyList<string> ChangedModuleFiles(PackageProject project)
    {
        var directory = Path.GetDirectoryName(project.ProjectPath) ?? Environment.CurrentDirectory;
        var sumPath = Path.Combine(directory, "go.sum");
        return File.Exists(sumPath) ? new[] { project.ProjectPath, sumPath } : new[] { project.ProjectPath };
    }

    private Task<ProcessResult> RunGoAsync(PackageProject project, IReadOnlyList<string> arguments, IProcessOutputSink? outputSink, CancellationToken cancellationToken)
    {
        var go = _toolchainRegistry.Get(WellKnownToolchainIds.Go)?.ExecutablePath ?? "go";
        var rootPath = Path.GetDirectoryName(project.ProjectPath) ?? Environment.CurrentDirectory;
        var request = new ProcessStartRequest(go, arguments, rootPath);
        return _processRunner.RunAsync(request, outputSink, cancellationToken);
    }
}

/// <summary>One `require` line read directly from go.mod — never a process invocation for a
/// purely-declarative read. <see cref="Indirect"/> mirrors Go's own convention of appending a
/// <c>// indirect</c> line comment to a require entry the module doesn't import directly.</summary>
public readonly record struct GoRequiredModule(string ModulePath, string? Version, bool Indirect);

/// <summary>
/// Minimal go.mod <c>require</c> block reader — handles both the single-line form
/// (<c>require example.com/mod v1.2.3</c>) and the parenthesized block form
/// (<c>require (\n\texample.com/mod v1.2.3\n)</c>), which are go.mod's only two documented,
/// stable syntaxes for declaring a dependency.
/// </summary>
public static class GoModReader
{
    public static IReadOnlyList<GoRequiredModule> ReadRequiredModules(string goModPath)
    {
        if (!File.Exists(goModPath)) return Array.Empty<GoRequiredModule>();

        string[] lines;
        try
        {
            lines = File.ReadAllLines(goModPath);
        }
        catch (IOException)
        {
            return Array.Empty<GoRequiredModule>();
        }

        var results = new List<GoRequiredModule>();
        var inRequireBlock = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (!inRequireBlock && line.StartsWith("require (", StringComparison.Ordinal))
            {
                inRequireBlock = true;
                continue;
            }

            if (inRequireBlock)
            {
                if (line == ")") { inRequireBlock = false; continue; }
                var parsed = TryParseRequireEntry(line);
                if (parsed is not null) results.Add(parsed.Value);
                continue;
            }

            if (line.StartsWith("require ", StringComparison.Ordinal))
            {
                var parsed = TryParseRequireEntry(line["require ".Length..].Trim());
                if (parsed is not null) results.Add(parsed.Value);
            }
        }

        return results;
    }

    private static GoRequiredModule? TryParseRequireEntry(string entry)
    {
        var indirect = entry.Contains("// indirect", StringComparison.Ordinal);
        var codePart = entry.Split("//")[0].Trim();
        var parts = codePart.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1) return null;

        var modulePath = parts[0];
        var version = parts.Length >= 2 ? parts[1] : null;
        return new GoRequiredModule(modulePath, version, indirect);
    }
}

/// <summary>
/// Parses <c>go list -m -json all</c>'s output — a concatenated stream of JSON objects (NOT a
/// JSON array; this is Go's own documented "JSON stream" format), one per module. The first
/// object (<c>Main: true</c>) is the project's own module and is excluded from the result.
/// </summary>
public static class GoListModuleParser
{
    public static IReadOnlyList<PackageReference> Parse(string jsonStream, string projectPath, string packageManagerId)
    {
        var references = new List<PackageReference>();

        foreach (var moduleJson in SplitJsonObjects(jsonStream))
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(moduleJson);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                var isMain = root.TryGetProperty("Main", out var mainProp) && mainProp.ValueKind == JsonValueKind.True;
                if (isMain) continue;

                var path = root.TryGetProperty("Path", out var pathProp) ? pathProp.GetString() : null;
                if (path is null) continue;

                var version = root.TryGetProperty("Version", out var versionProp) ? versionProp.GetString() : null;
                var indirect = root.TryGetProperty("Indirect", out var indirectProp) && indirectProp.ValueKind == JsonValueKind.True;

                references.Add(new PackageReference(
                    path, version, version, indirect ? PackageDependencyKind.Transitive : PackageDependencyKind.Direct,
                    projectPath, packageManagerId));
            }
        }

        return references;
    }

    /// <summary>Splits a concatenated top-level-JSON-object stream into individual object texts by
    /// tracking brace depth outside of string literals — the documented shape `go list -m -json`
    /// emits (no enclosing array, no separators between objects).</summary>
    private static IEnumerable<string> SplitJsonObjects(string jsonStream)
    {
        var depth = 0;
        var start = -1;
        var inString = false;
        var escaped = false;

        for (var i = 0; i < jsonStream.Length; i++)
        {
            var c = jsonStream[i];

            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    if (depth == 0) start = i;
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        yield return jsonStream[start..(i + 1)];
                        start = -1;
                    }
                    break;
            }
        }
    }
}
