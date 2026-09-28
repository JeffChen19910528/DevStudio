using System.Text.Json;
using System.Text.Json.Nodes;
using DevStudio.Core.Packages;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Packages;

/// <summary>
/// Drives real vcpkg in manifest mode (Phase 14 P1-C, SKILL.md-style phase discipline). Manifest
/// mode only (<c>vcpkg.json</c>) — classic mode (a global, non-project-scoped install) is
/// intentionally out of scope, per the phase spec's own "support manifest mode first" guidance.
/// Neither <c>vcpkg</c> nor <c>cmake</c> is installed on the development machine this adapter was
/// written on — every command-construction/output-parsing path here is unit-tested against
/// realistic fixture text mirroring vcpkg's own documented output formats, never against a real
/// invocation. This adapter must always be described as "implemented but not real-environment
/// validated," never "real-tested" — see ADR-015.
///
/// <see cref="AddAsync"/>/<see cref="RemoveAsync"/> edit <c>vcpkg.json</c>'s own
/// <c>dependencies</c> array directly (structured JSON editing via <see cref="JsonNode"/>, never
/// regex) rather than shelling out to vcpkg's own manifest-mutation subcommand — that subcommand
/// (<c>vcpkg add port</c>) was only added to vcpkg relatively recently and its exact current
/// stability/output contract could not be verified without a real vcpkg install on this machine;
/// a direct, conservative array edit that preserves every other manifest field (name, version,
/// builtin-baseline, overrides, features) is the safer, more honest choice. <see cref="UpdateAsync"/>
/// is deliberately unsupported: vcpkg resolves concrete versions through its baseline/overrides
/// mechanism, not a simple per-package version pin, so a naive version-field edit risks producing
/// a manifest vcpkg's own solver would reject — see <see cref="FullCapabilities"/>.
/// </summary>
public sealed class VcpkgPackageAdapter : IPackageManagerAdapter, IPackageInspector, IPackageSearcher, IPackageInstaller, IPackageRemover, IPackageUpdater, IPackageSourceManager
{
    public string Id => WellKnownPackageManagerIds.Vcpkg;
    public string DisplayName => "vcpkg";

    private static readonly PackageManagerCapabilities FullCapabilities = new(
        ListInstalled: true,
        ListDependencies: true,
        Search: true,
        Add: true,
        Remove: true,
        Update: false, // See class doc comment: vcpkg's baseline/overrides version model has no safe naive "set version" edit.
        Restore: true,
        ListOutdated: false, // No stable, parseable "outdated" concept in vcpkg's manifest mode — versions are pinned by baseline commit, not per-package latest-vs-installed.
        ManageSources: true, // Read-only: registries declared in vcpkg-configuration.json.
        LockfileSupport: true, // builtin-baseline + overrides function as vcpkg's lockfile-equivalent version pin.
        TransitiveDependencySupport: true,
        PrereleaseSupport: false); // vcpkg ports have no first-class prerelease flag.

    private readonly IProcessRunner _processRunner;
    private readonly IToolchainRegistry _toolchainRegistry;

    public VcpkgPackageAdapter(IProcessRunner processRunner, IToolchainRegistry toolchainRegistry)
    {
        _processRunner = processRunner;
        _toolchainRegistry = toolchainRegistry;
    }

    public PackageProject? DetectProject(ProjectInfo project)
    {
        if (project.ProjectType != ProjectType.CMake) return null;

        var manifestPath = Path.Combine(project.RootPath, "vcpkg.json");
        if (!File.Exists(manifestPath)) return null;

        var vcpkg = _toolchainRegistry.Get(WellKnownToolchainIds.Vcpkg);
        if (vcpkg is null || !vcpkg.IsUsable)
        {
            return new PackageProject(manifestPath, project.ProjectType, Id, DisplayName, PackageManagerCapabilities.None,
                "vcpkg is not installed or has not been detected.");
        }

        return new PackageProject(manifestPath, project.ProjectType, Id, DisplayName, FullCapabilities);
    }

    public async Task<IReadOnlyList<PackageReference>> ListInstalledAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var result = await RunVcpkgAsync(project, new[] { "list" }, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput)) return Array.Empty<PackageReference>();

        var declaredNames = new HashSet<string>(VcpkgManifestReader.ReadDeclaredDependencies(project.ProjectPath).Select(d => d.PortName), StringComparer.OrdinalIgnoreCase);
        return VcpkgListParser.Parse(result.StandardOutput, project.ProjectPath, project.PackageManagerId, declaredNames);
    }

    public Task<IReadOnlyList<PackageDependency>> ListDependenciesAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        // Declared dependencies come straight from vcpkg.json's own "dependencies" array — no
        // process needed, mirroring every other adapter's declared-vs-installed distinction.
        IReadOnlyList<PackageDependency> result = VcpkgManifestReader.ReadDeclaredDependencies(project.ProjectPath)
            .Select(d => new PackageDependency(d.PortName, d.VersionConstraint, PackageDependencyKind.Direct, null))
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<PackageReference>> ListOutdatedAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PackageReference>>(Array.Empty<PackageReference>());

    public async Task<IReadOnlyList<PackageSearchResult>> SearchAsync(PackageProject project, string query, bool includePrerelease, CancellationToken cancellationToken = default)
    {
        var vcpkg = _toolchainRegistry.Get(WellKnownToolchainIds.Vcpkg)?.ExecutablePath ?? "vcpkg";
        var rootPath = Path.GetDirectoryName(project.ProjectPath) ?? Environment.CurrentDirectory;
        var request = new ProcessStartRequest(vcpkg, new[] { "search", query }, rootPath);
        var result = await _processRunner.RunAsync(request, null, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput)) return Array.Empty<PackageSearchResult>();

        return VcpkgSearchParser.Parse(result.StandardOutput);
    }

    public Task<PackageOperationResult> AddAsync(PackageProject project, string packageId, string? version, bool prerelease, bool isDevDependency, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        try
        {
            VcpkgManifestEditor.AddOrReplaceDependency(project.ProjectPath, packageId, version);
            return Task.FromResult(new PackageOperationResult(true, PackageOperation.Add, project.ProjectPath, packageId, new[] { project.ProjectPath }, Array.Empty<string>(), null, false, null));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Task.FromResult(PackageOperationResult.Unavailable(PackageOperation.Add, project.ProjectPath, $"Could not update vcpkg.json: {ex.Message}", packageId));
        }
    }

    public Task<PackageOperationResult> RemoveAsync(PackageProject project, string packageId, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var removed = VcpkgManifestEditor.RemoveDependency(project.ProjectPath, packageId);
            return Task.FromResult(removed
                ? new PackageOperationResult(true, PackageOperation.Remove, project.ProjectPath, packageId, new[] { project.ProjectPath }, Array.Empty<string>(), null, false, null)
                : PackageOperationResult.Unavailable(PackageOperation.Remove, project.ProjectPath, "No matching dependency was found in vcpkg.json.", packageId));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Task.FromResult(PackageOperationResult.Unavailable(PackageOperation.Remove, project.ProjectPath, $"Could not update vcpkg.json: {ex.Message}", packageId));
        }
    }

    public Task<PackageOperationResult> UpdateAsync(PackageProject project, string packageId, string? targetVersion, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(PackageOperationResult.Unavailable(PackageOperation.Update, project.ProjectPath,
            "vcpkg resolves versions through its baseline/overrides mechanism; DevStudio does not perform a direct per-package version edit (see ADR-015).", packageId));

    public async Task<PackageOperationResult> RestoreAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        // Running `vcpkg install` from the manifest's own directory installs every dependency
        // vcpkg.json declares — the manifest-mode equivalent of `dotnet restore`/`npm install`.
        var result = await RunVcpkgAsync(project, new[] { "install" }, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.WasCancelled) return PackageOperationResult.Cancelled(PackageOperation.Restore, project.ProjectPath);

        return result.ExitCode == 0
            ? new PackageOperationResult(true, PackageOperation.Restore, project.ProjectPath, null, Array.Empty<string>(), Array.Empty<string>(), result.ExitCode, false, null, result.StandardOutput)
            : new PackageOperationResult(false, PackageOperation.Restore, project.ProjectPath, null, Array.Empty<string>(), Array.Empty<string>(), result.ExitCode, false,
                PackageOperationDiagnostics.FirstErrorLine(result, "The vcpkg install command failed."), result.StandardOutput);
    }

    public Task<IReadOnlyList<PackageSource>> GetSourcesAsync(PackageProject project, CancellationToken cancellationToken = default)
    {
        // Reads only vcpkg-configuration.json's own "registries" array — a git repository URL and
        // baseline commit, never a credential (registry auth, where configured, lives outside this
        // file, e.g. in git credential helpers).
        var configPath = Path.Combine(Path.GetDirectoryName(project.ProjectPath) ?? Environment.CurrentDirectory, "vcpkg-configuration.json");
        var sources = new List<PackageSource>();
        if (File.Exists(configPath))
        {
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(configPath));
                if (node?["registries"] is JsonArray registries)
                {
                    foreach (var registry in registries.OfType<JsonObject>())
                    {
                        var kind = registry["kind"]?.GetValue<string>() ?? "registry";
                        var repository = registry["repository"]?.GetValue<string>() ?? registry["path"]?.GetValue<string>() ?? "(unknown)";
                        sources.Add(new PackageSource(kind, repository, Enabled: true, IsDefault: false, PackageSourceType.Other));
                    }
                }
            }
            catch (JsonException)
            {
                // Malformed vcpkg-configuration.json — report no additional sources rather than throwing.
            }
        }

        return Task.FromResult<IReadOnlyList<PackageSource>>(sources);
    }

    private Task<ProcessResult> RunVcpkgAsync(PackageProject project, IReadOnlyList<string> arguments, IProcessOutputSink? outputSink, CancellationToken cancellationToken)
    {
        var vcpkg = _toolchainRegistry.Get(WellKnownToolchainIds.Vcpkg)?.ExecutablePath ?? "vcpkg";
        var rootPath = Path.GetDirectoryName(project.ProjectPath) ?? Environment.CurrentDirectory;
        var request = new ProcessStartRequest(vcpkg, arguments, rootPath);
        return _processRunner.RunAsync(request, outputSink, cancellationToken);
    }
}

/// <summary>One declared vcpkg.json dependency-array entry: either a bare port-name string or an
/// object with a "name" and an optional version constraint field.</summary>
public readonly record struct VcpkgDeclaredDependency(string PortName, string? VersionConstraint);

/// <summary>Reads vcpkg.json's own "dependencies" array — a purely declarative read, never a
/// process invocation.</summary>
public static class VcpkgManifestReader
{
    public static IReadOnlyList<VcpkgDeclaredDependency> ReadDeclaredDependencies(string manifestPath)
    {
        if (!File.Exists(manifestPath)) return Array.Empty<VcpkgDeclaredDependency>();

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(File.ReadAllText(manifestPath));
        }
        catch (JsonException)
        {
            return Array.Empty<VcpkgDeclaredDependency>();
        }

        if (node?["dependencies"] is not JsonArray dependencies) return Array.Empty<VcpkgDeclaredDependency>();

        var results = new List<VcpkgDeclaredDependency>();
        foreach (var entry in dependencies)
        {
            switch (entry)
            {
                case JsonValue value when value.TryGetValue<string>(out var name):
                    results.Add(new VcpkgDeclaredDependency(name, null));
                    break;
                case JsonObject obj when obj["name"]?.GetValue<string>() is { } name:
                    // vcpkg supports several version-constraint keys ("version>=", "version", "version^", "version~") — surface whichever is present as-is, never resolved.
                    var constraint = obj["version>="]?.GetValue<string>() ?? obj["version"]?.GetValue<string>() ?? obj["version^"]?.GetValue<string>() ?? obj["version~"]?.GetValue<string>();
                    results.Add(new VcpkgDeclaredDependency(name, constraint));
                    break;
            }
        }

        return results;
    }
}

/// <summary>Structured, format-preserving vcpkg.json "dependencies" array editing via
/// <see cref="JsonNode"/> — never regex-on-JSON. Every other manifest field (name, version,
/// builtin-baseline, overrides, features) is left untouched.</summary>
public static class VcpkgManifestEditor
{
    public static void AddOrReplaceDependency(string manifestPath, string portName, string? versionConstraint)
    {
        var node = JsonNode.Parse(File.ReadAllText(manifestPath)) ?? throw new JsonException("vcpkg.json could not be parsed.");
        var root = node.AsObject();

        if (root["dependencies"] is not JsonArray dependencies)
        {
            dependencies = new JsonArray();
            root["dependencies"] = dependencies;
        }

        RemoveMatching(dependencies, portName);

        JsonNode entry = string.IsNullOrWhiteSpace(versionConstraint)
            ? JsonValue.Create(portName)!
            : new JsonObject { ["name"] = portName, ["version>="] = versionConstraint };
        dependencies.Add(entry);

        File.WriteAllText(manifestPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public static bool RemoveDependency(string manifestPath, string portName)
    {
        var node = JsonNode.Parse(File.ReadAllText(manifestPath)) ?? throw new JsonException("vcpkg.json could not be parsed.");
        var root = node.AsObject();
        if (root["dependencies"] is not JsonArray dependencies) return false;

        var removed = RemoveMatching(dependencies, portName);
        if (removed) File.WriteAllText(manifestPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return removed;
    }

    private static bool RemoveMatching(JsonArray dependencies, string portName)
    {
        for (var i = dependencies.Count - 1; i >= 0; i--)
        {
            var entryName = dependencies[i] switch
            {
                JsonValue v when v.TryGetValue<string>(out var s) => s,
                JsonObject o => o["name"]?.GetValue<string>(),
                _ => null,
            };
            if (string.Equals(entryName, portName, StringComparison.OrdinalIgnoreCase))
            {
                dependencies.RemoveAt(i);
                return true;
            }
        }

        return false;
    }
}

/// <summary>Parses <c>vcpkg list</c>'s documented plain-text output: one installed port per line,
/// <c>portname:triplet   version   description</c>.</summary>
public static class VcpkgListParser
{
    private static readonly System.Text.RegularExpressions.Regex LineRegex = new(
        @"^(?<name>[A-Za-z0-9_-]+):(?<triplet>\S+)\s+(?<version>\S+)\s*(?<desc>.*)$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    public static IReadOnlyList<PackageReference> Parse(string vcpkgListOutput, string projectPath, string packageManagerId, IReadOnlySet<string> declaredNames)
    {
        var results = new List<PackageReference>();
        foreach (var rawLine in vcpkgListOutput.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;

            var match = LineRegex.Match(line);
            if (!match.Success) continue;

            var name = match.Groups["name"].Value;
            var version = match.Groups["version"].Value;
            var kind = declaredNames.Contains(name) ? PackageDependencyKind.Direct : PackageDependencyKind.Transitive;
            results.Add(new PackageReference(name, version, version, kind, projectPath, packageManagerId));
        }

        return results;
    }
}

/// <summary>Parses <c>vcpkg search</c>'s documented plain-text output:
/// <c>portname   version   description</c> per line.</summary>
public static class VcpkgSearchParser
{
    private static readonly System.Text.RegularExpressions.Regex LineRegex = new(
        @"^(?<name>[A-Za-z0-9_-]+)\s+(?<version>\S+)\s+(?<desc>.*)$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    public static IReadOnlyList<PackageSearchResult> Parse(string vcpkgSearchOutput)
    {
        var results = new List<PackageSearchResult>();
        foreach (var rawLine in vcpkgSearchOutput.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith("The result", StringComparison.OrdinalIgnoreCase)) continue;

            var match = LineRegex.Match(line);
            if (!match.Success) continue;

            var name = match.Groups["name"].Value;
            var version = match.Groups["version"].Value;
            var description = match.Groups["desc"].Value.Length > 0 ? match.Groups["desc"].Value : null;
            results.Add(new PackageSearchResult(name, version, description, LatestVersion: version, Source: "vcpkg"));
        }

        return results;
    }
}
