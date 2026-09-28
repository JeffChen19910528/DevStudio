using System.Text.Json;
using DevStudio.Core.Packages;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Packages;

/// <summary>
/// Drives real Cargo (Phase 14 P1-B, SKILL.md-style phase discipline). Neither <c>cargo</c> nor
/// <c>rustc</c> is installed on the development machine this adapter was written on — every
/// command-construction/output-parsing path here is unit-tested against realistic fixture text
/// mirroring Cargo's own documented, version-stable output formats (<c>cargo metadata
/// --format-version 1</c>'s JSON schema), never against a real invocation. This adapter must
/// always be described as "implemented but not real-environment validated," never "real-tested" —
/// see ADR-015.
///
/// Mutations (<see cref="AddAsync"/>/<see cref="RemoveAsync"/>) always go through <c>cargo
/// add</c>/<c>cargo rm</c> — never manual <c>Cargo.toml</c> text editing — because Cargo itself
/// already owns safe, version-aware manifest editing and re-resolves <c>Cargo.lock</c> as part of
/// the same command; duplicating that logic here would risk falling out of sync with Cargo's own
/// resolver.
/// </summary>
public sealed class CargoPackageAdapter : IPackageManagerAdapter, IPackageInspector, IPackageSearcher, IPackageInstaller, IPackageRemover, IPackageUpdater
{
    public string Id => WellKnownPackageManagerIds.Cargo;
    public string DisplayName => "Cargo";

    private static readonly PackageManagerCapabilities FullCapabilities = new(
        ListInstalled: true,
        ListDependencies: true,
        // `cargo search` requires network access and, per crates.io's own current policy, is
        // rate-limited/deprecated for some registries — the command itself is real and documented,
        // but its real-world reliability is unverified here since cargo is not installed on this
        // machine. Command construction is implemented; treat this capability's real behavior as
        // unverified, not as a guarantee (see ADR-015).
        Search: true,
        Add: true,
        Remove: true,
        Update: true,
        Restore: true,
        ListOutdated: false, // No stable, parseable "cargo outdated" ships with Cargo itself (it is a separate, optionally-installed subcommand: cargo-outdated) — not assumed present.
        ManageSources: false, // Cargo registry configuration lives in .cargo/config.toml, which is user/workspace-wide, not a per-project source list this adapter can safely enumerate without risking exposure of configured registry tokens.
        LockfileSupport: true,
        TransitiveDependencySupport: true,
        PrereleaseSupport: true);

    private readonly IProcessRunner _processRunner;
    private readonly IToolchainRegistry _toolchainRegistry;

    public CargoPackageAdapter(IProcessRunner processRunner, IToolchainRegistry toolchainRegistry)
    {
        _processRunner = processRunner;
        _toolchainRegistry = toolchainRegistry;
    }

    public PackageProject? DetectProject(ProjectInfo project)
    {
        if (project.ProjectType != ProjectType.Rust) return null;

        var cargoTomlPath = Path.Combine(project.RootPath, "Cargo.toml");
        if (!File.Exists(cargoTomlPath)) return null;

        var cargo = _toolchainRegistry.Get(WellKnownToolchainIds.Rust);
        if (cargo is null || !cargo.IsUsable)
        {
            return new PackageProject(cargoTomlPath, project.ProjectType, Id, DisplayName, PackageManagerCapabilities.None,
                "Cargo is not installed or has not been detected (the Rust toolchain reports both rustc and cargo as a single entry).");
        }

        return new PackageProject(cargoTomlPath, project.ProjectType, Id, DisplayName, FullCapabilities);
    }

    public async Task<IReadOnlyList<PackageReference>> ListInstalledAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var result = await RunCargoAsync(project, new[] { "metadata", "--format-version", "1", "--no-deps=false" }, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput)) return Array.Empty<PackageReference>();

        return CargoMetadataParser.ParseInstalled(result.StandardOutput, project.ProjectPath, project.PackageManagerId);
    }

    public Task<IReadOnlyList<PackageDependency>> ListDependenciesAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        // Declared [dependencies]/[dev-dependencies]/[build-dependencies] come straight from
        // Cargo.toml — no process needed, mirroring PythonPackageAdapter's requirements.txt read
        // and MavenPackageAdapter's declared-vs-installed pom.xml read.
        var declared = CargoTomlReader.ReadDeclaredDependencies(project.ProjectPath);
        IReadOnlyList<PackageDependency> result = declared
            .Select(d => new PackageDependency(d.PackageId, d.VersionRange, PackageDependencyKind.Direct, null))
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<PackageReference>> ListOutdatedAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PackageReference>>(Array.Empty<PackageReference>());

    public async Task<IReadOnlyList<PackageSearchResult>> SearchAsync(PackageProject project, string query, bool includePrerelease, CancellationToken cancellationToken = default)
    {
        var cargo = _toolchainRegistry.Get(WellKnownToolchainIds.Rust)?.ExecutablePath ?? "cargo";
        var rootPath = Path.GetDirectoryName(project.ProjectPath) ?? Environment.CurrentDirectory;
        var request = new ProcessStartRequest(cargo, new[] { "search", query, "--limit", "25" }, rootPath);
        var result = await _processRunner.RunAsync(request, null, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput)) return Array.Empty<PackageSearchResult>();

        return CargoSearchParser.Parse(result.StandardOutput);
    }

    public async Task<PackageOperationResult> AddAsync(PackageProject project, string packageId, string? version, bool prerelease, bool isDevDependency, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var spec = string.IsNullOrWhiteSpace(version) ? packageId : $"{packageId}@{version}";
        var arguments = new List<string> { "add", spec };
        if (isDevDependency) arguments.Add("--dev");

        var result = await RunCargoAsync(project, arguments, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.WasCancelled) return PackageOperationResult.Cancelled(PackageOperation.Add, project.ProjectPath, packageId);

        var changedFiles = result.ExitCode == 0 ? ChangedManifestFiles(project) : Array.Empty<string>();
        return result.ExitCode == 0
            ? new PackageOperationResult(true, PackageOperation.Add, project.ProjectPath, packageId, changedFiles, Array.Empty<string>(), result.ExitCode, false, null, result.StandardOutput)
            : new PackageOperationResult(false, PackageOperation.Add, project.ProjectPath, packageId, Array.Empty<string>(), Array.Empty<string>(), result.ExitCode, false,
                PackageOperationDiagnostics.FirstErrorLine(result, "The cargo add command failed."), result.StandardOutput);
    }

    public async Task<PackageOperationResult> RemoveAsync(PackageProject project, string packageId, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var result = await RunCargoAsync(project, new[] { "rm", packageId }, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.WasCancelled) return PackageOperationResult.Cancelled(PackageOperation.Remove, project.ProjectPath, packageId);

        var changedFiles = result.ExitCode == 0 ? ChangedManifestFiles(project) : Array.Empty<string>();
        return result.ExitCode == 0
            ? new PackageOperationResult(true, PackageOperation.Remove, project.ProjectPath, packageId, changedFiles, Array.Empty<string>(), result.ExitCode, false, null, result.StandardOutput)
            : new PackageOperationResult(false, PackageOperation.Remove, project.ProjectPath, packageId, Array.Empty<string>(), Array.Empty<string>(), result.ExitCode, false,
                PackageOperationDiagnostics.FirstErrorLine(result, "The cargo rm command failed."), result.StandardOutput);
    }

    public async Task<PackageOperationResult> UpdateAsync(PackageProject project, string packageId, string? targetVersion, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "update", "--package", packageId };
        if (!string.IsNullOrWhiteSpace(targetVersion)) { arguments.Add("--precise"); arguments.Add(targetVersion); }

        var result = await RunCargoAsync(project, arguments, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.WasCancelled) return PackageOperationResult.Cancelled(PackageOperation.Update, project.ProjectPath, packageId);

        return result.ExitCode == 0
            ? new PackageOperationResult(true, PackageOperation.Update, project.ProjectPath, packageId, ChangedLockfileOnly(project), Array.Empty<string>(), result.ExitCode, false, null, result.StandardOutput)
            : new PackageOperationResult(false, PackageOperation.Update, project.ProjectPath, packageId, Array.Empty<string>(), Array.Empty<string>(), result.ExitCode, false,
                PackageOperationDiagnostics.FirstErrorLine(result, "The cargo update command failed."), result.StandardOutput);
    }

    public async Task<PackageOperationResult> RestoreAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        // `cargo fetch` downloads every crate Cargo.lock already resolves without compiling
        // anything — the closest Cargo equivalent of `dotnet restore`/`npm install`/`pip install
        // -r requirements.txt`.
        var result = await RunCargoAsync(project, new[] { "fetch" }, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.WasCancelled) return PackageOperationResult.Cancelled(PackageOperation.Restore, project.ProjectPath);

        return result.ExitCode == 0
            ? new PackageOperationResult(true, PackageOperation.Restore, project.ProjectPath, null, Array.Empty<string>(), Array.Empty<string>(), result.ExitCode, false, null, result.StandardOutput)
            : new PackageOperationResult(false, PackageOperation.Restore, project.ProjectPath, null, Array.Empty<string>(), Array.Empty<string>(), result.ExitCode, false,
                PackageOperationDiagnostics.FirstErrorLine(result, "The cargo fetch command failed."), result.StandardOutput);
    }

    private static IReadOnlyList<string> ChangedManifestFiles(PackageProject project)
    {
        var directory = Path.GetDirectoryName(project.ProjectPath) ?? Environment.CurrentDirectory;
        var lockPath = Path.Combine(directory, "Cargo.lock");
        return File.Exists(lockPath) ? new[] { project.ProjectPath, lockPath } : new[] { project.ProjectPath };
    }

    private static IReadOnlyList<string> ChangedLockfileOnly(PackageProject project)
    {
        var directory = Path.GetDirectoryName(project.ProjectPath) ?? Environment.CurrentDirectory;
        var lockPath = Path.Combine(directory, "Cargo.lock");
        return File.Exists(lockPath) ? new[] { lockPath } : Array.Empty<string>();
    }

    private Task<ProcessResult> RunCargoAsync(PackageProject project, IReadOnlyList<string> arguments, IProcessOutputSink? outputSink, CancellationToken cancellationToken)
    {
        var cargo = _toolchainRegistry.Get(WellKnownToolchainIds.Rust)?.ExecutablePath ?? "cargo";
        var rootPath = Path.GetDirectoryName(project.ProjectPath) ?? Environment.CurrentDirectory;
        var request = new ProcessStartRequest(cargo, arguments, rootPath);
        return _processRunner.RunAsync(request, outputSink, cancellationToken);
    }
}

/// <summary>One declared dependency read directly from Cargo.toml's own [dependencies]-style
/// tables — never a process invocation for a purely-declarative read.</summary>
public readonly record struct CargoDeclaredDependency(string PackageId, string? VersionRange);

/// <summary>
/// Minimal, deliberately conservative Cargo.toml [dependencies]/[dev-dependencies]/
/// [build-dependencies] table reader. Only handles the common inline-string-or-table-with-version
/// forms (<c>name = "1.0"</c> / <c>name = { version = "1.0", features = [...] }</c>); a dependency
/// declared via a nested <c>[dependencies.name]</c> table-header form is not parsed by this
/// deliberately simple reader — documented here rather than silently dropped without explanation
/// (mirrors GradlePackageAdapter's own documented static-parse limitation).
/// </summary>
public static class CargoTomlReader
{
    public static IReadOnlyList<CargoDeclaredDependency> ReadDeclaredDependencies(string cargoTomlPath)
    {
        if (!File.Exists(cargoTomlPath)) return Array.Empty<CargoDeclaredDependency>();

        string[] lines;
        try
        {
            lines = File.ReadAllLines(cargoTomlPath);
        }
        catch (IOException)
        {
            return Array.Empty<CargoDeclaredDependency>();
        }

        var results = new List<CargoDeclaredDependency>();
        var inDependenciesSection = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var section = line.Trim('[', ']').Trim();
                inDependenciesSection = section is "dependencies" or "dev-dependencies" or "build-dependencies";
                continue;
            }

            if (!inDependenciesSection) continue;

            var equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0) continue;

            var name = line[..equalsIndex].Trim().Trim('"');
            var valuePart = line[(equalsIndex + 1)..].Trim();

            string? version = null;
            if (valuePart.StartsWith('"') && valuePart.EndsWith('"') && valuePart.Length >= 2)
            {
                version = valuePart[1..^1];
            }
            else if (valuePart.StartsWith('{'))
            {
                var versionMatch = System.Text.RegularExpressions.Regex.Match(valuePart, "version\\s*=\\s*\"([^\"]+)\"");
                if (versionMatch.Success) version = versionMatch.Groups[1].Value;
            }

            if (name.Length > 0) results.Add(new CargoDeclaredDependency(name, version));
        }

        return results;
    }
}

/// <summary>
/// Parses <c>cargo metadata --format-version 1</c>'s documented, stable JSON schema. Direct vs.
/// transitive is derived from the root package's own <c>dependencies</c> array (by package name)
/// against the full <c>packages</c> list — any resolved package not directly named by the
/// workspace's root package(s) is transitive.
/// </summary>
public static class CargoMetadataParser
{
    public static IReadOnlyList<PackageReference> ParseInstalled(string metadataJson, string projectPath, string packageManagerId)
    {
        using var document = JsonDocument.Parse(metadataJson);
        var root = document.RootElement;

        if (!root.TryGetProperty("packages", out var packages) || packages.ValueKind != JsonValueKind.Array)
            return Array.Empty<PackageReference>();

        var workspaceMembers = new HashSet<string>();
        if (root.TryGetProperty("workspace_members", out var members) && members.ValueKind == JsonValueKind.Array)
        {
            foreach (var member in members.EnumerateArray())
            {
                var value = member.GetString();
                if (value is not null) workspaceMembers.Add(value);
            }
        }

        var directNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in packages.EnumerateArray())
        {
            var id = package.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            if (id is null || !workspaceMembers.Contains(id)) continue;

            if (package.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array)
            {
                foreach (var dep in deps.EnumerateArray())
                {
                    var depName = dep.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                    if (depName is not null) directNames.Add(depName);
                }
            }
        }

        var references = new List<PackageReference>();
        foreach (var package in packages.EnumerateArray())
        {
            var name = package.TryGetProperty("name", out var n) ? n.GetString() : null;
            var version = package.TryGetProperty("version", out var v) ? v.GetString() : null;
            var id = package.TryGetProperty("id", out var idProp2) ? idProp2.GetString() : null;
            if (name is null || version is null) continue;

            // A workspace member itself is not a "dependency" of the project — skip it.
            if (id is not null && workspaceMembers.Contains(id)) continue;

            var kind = directNames.Contains(name) ? PackageDependencyKind.Direct : PackageDependencyKind.Transitive;
            references.Add(new PackageReference(name, version, version, kind, projectPath, packageManagerId));
        }

        return references;
    }
}

/// <summary>Parses <c>cargo search</c>'s documented plain-text output format: each line is
/// <c>name = "version"    # description</c>.</summary>
public static class CargoSearchParser
{
    private static readonly System.Text.RegularExpressions.Regex LineRegex = new(
        "^(?<name>[A-Za-z0-9_-]+)\\s*=\\s*\"(?<version>[^\"]+)\"\\s*(?:#\\s*(?<desc>.*))?$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    public static IReadOnlyList<PackageSearchResult> Parse(string cargoSearchOutput)
    {
        var results = new List<PackageSearchResult>();
        foreach (var rawLine in cargoSearchOutput.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("...", StringComparison.Ordinal)) continue; // trailing "... and N crates more" footer

            var match = LineRegex.Match(line);
            if (!match.Success) continue;

            var name = match.Groups["name"].Value;
            var version = match.Groups["version"].Value;
            var description = match.Groups["desc"].Success && match.Groups["desc"].Value.Length > 0 ? match.Groups["desc"].Value : null;

            results.Add(new PackageSearchResult(name, version, description, LatestVersion: version, Source: "crates.io"));
        }

        return results;
    }
}
