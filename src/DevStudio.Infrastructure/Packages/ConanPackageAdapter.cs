using System.Text.Json;
using System.Text.RegularExpressions;
using DevStudio.Core.Packages;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Packages;

/// <summary>
/// Drives real Conan (Phase 14 P1-C, SKILL.md-style phase discipline). Targets Conan 2.x's
/// documented CLI (<c>conan graph info --format=json</c>, <c>conan install</c>, <c>conan
/// search</c>, <c>conan remote list</c>) — Conan 1.x uses different commands (<c>conan
/// info</c>) and is NOT supported by this adapter. Neither <c>conan</c> nor <c>cmake</c> is
/// installed on the development machine this adapter was written on — every command-construction/
/// output-parsing path here is unit-tested against realistic fixture data mirroring Conan 2.x's
/// own documented output/JSON shapes, never against a real invocation. This adapter must always be
/// described as "implemented but not real-environment validated," never "real-tested" — see
/// ADR-015.
///
/// <c>conanfile.py</c> and <c>conanfile.txt</c> are NOT treated identically (per the phase spec's
/// explicit instruction): a <c>conanfile.py</c> is an executable Python recipe — DevStudio never
/// attempts to statically edit or exec it, so <see cref="PackageManagerCapabilities.Add"/>/
/// <see cref="PackageManagerCapabilities.Remove"/>/<see cref="PackageManagerCapabilities.Update"/>
/// are always <c>false</c> for it (Inspector/Searcher/Restore/Sources only — see
/// <see cref="DetectProject"/>). A <c>conanfile.txt</c> is a static, ini-like format whose
/// <c>[requires]</c> section this adapter can safely add/remove/update a line in via structured
/// text editing (never regex-editing a conanfile.py's Python body).
/// </summary>
public sealed class ConanPackageAdapter : IPackageManagerAdapter, IPackageInspector, IPackageSearcher, IPackageInstaller, IPackageRemover, IPackageUpdater, IPackageSourceManager
{
    public string Id => WellKnownPackageManagerIds.Conan;
    public string DisplayName => "Conan";

    private readonly IProcessRunner _processRunner;
    private readonly IToolchainRegistry _toolchainRegistry;

    public ConanPackageAdapter(IProcessRunner processRunner, IToolchainRegistry toolchainRegistry)
    {
        _processRunner = processRunner;
        _toolchainRegistry = toolchainRegistry;
    }

    public PackageProject? DetectProject(ProjectInfo project)
    {
        if (project.ProjectType != ProjectType.CMake) return null;

        var txtPath = Path.Combine(project.RootPath, "conanfile.txt");
        var pyPath = Path.Combine(project.RootPath, "conanfile.py");
        // conanfile.txt takes precedence when both exist — it is the more common "simple project"
        // form and the one this adapter can safely mutate; a project author who also ships a
        // conanfile.py alongside it is presumed to intend the .py recipe to drive the actual
        // build (Conan itself prefers conanfile.py when both are present in the same directory).
        var manifestPath = File.Exists(pyPath) ? pyPath : (File.Exists(txtPath) ? txtPath : null);
        if (manifestPath is null) return null;

        var isPython = string.Equals(Path.GetExtension(manifestPath), ".py", StringComparison.OrdinalIgnoreCase);

        var conan = _toolchainRegistry.Get(WellKnownToolchainIds.Conan);
        if (conan is null || !conan.IsUsable)
        {
            return new PackageProject(manifestPath, project.ProjectType, Id, DisplayName, PackageManagerCapabilities.None,
                "Conan is not installed or has not been detected.");
        }

        var capabilities = new PackageManagerCapabilities(
            ListInstalled: true,
            ListDependencies: true,
            Search: true,
            Add: !isPython,
            Remove: !isPython,
            Update: !isPython,
            Restore: true,
            ListOutdated: false, // No stable, parseable "outdated" concept for Conan's recipe-resolved graph.
            ManageSources: true, // Read-only: configured remotes via `conan remote list`.
            LockfileSupport: true, // conan.lock.
            TransitiveDependencySupport: true,
            PrereleaseSupport: false);

        return new PackageProject(manifestPath, project.ProjectType, Id, DisplayName, capabilities);
    }

    public async Task<IReadOnlyList<PackageReference>> ListInstalledAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var result = await RunConanAsync(project, new[] { "graph", "info", project.ProjectPath, "--format=json" }, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput)) return Array.Empty<PackageReference>();

        return ConanGraphInfoParser.ParseInstalled(result.StandardOutput, project.ProjectPath, project.PackageManagerId);
    }

    public Task<IReadOnlyList<PackageDependency>> ListDependenciesAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var isPython = string.Equals(Path.GetExtension(project.ProjectPath), ".py", StringComparison.OrdinalIgnoreCase);
        IReadOnlyList<PackageDependency> result = isPython
            ? ConanRecipeReader.ReadDeclaredDependenciesFromPy(project.ProjectPath).Select(d => new PackageDependency(d.PackageId, d.Version, PackageDependencyKind.Direct, null)).ToList()
            : ConanRecipeReader.ReadDeclaredDependenciesFromTxt(project.ProjectPath).Select(d => new PackageDependency(d.PackageId, d.Version, PackageDependencyKind.Direct, null)).ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<PackageReference>> ListOutdatedAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PackageReference>>(Array.Empty<PackageReference>());

    public async Task<IReadOnlyList<PackageSearchResult>> SearchAsync(PackageProject project, string query, bool includePrerelease, CancellationToken cancellationToken = default)
    {
        var conan = _toolchainRegistry.Get(WellKnownToolchainIds.Conan)?.ExecutablePath ?? "conan";
        var rootPath = Path.GetDirectoryName(project.ProjectPath) ?? Environment.CurrentDirectory;
        var request = new ProcessStartRequest(conan, new[] { "search", query, "--format=json" }, rootPath);
        var result = await _processRunner.RunAsync(request, null, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput)) return Array.Empty<PackageSearchResult>();

        return ConanSearchParser.Parse(result.StandardOutput);
    }

    public Task<PackageOperationResult> AddAsync(PackageProject project, string packageId, string? version, bool prerelease, bool isDevDependency, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        if (string.Equals(Path.GetExtension(project.ProjectPath), ".py", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(PackageOperationResult.Unavailable(PackageOperation.Add, project.ProjectPath,
                "conanfile.py is an executable Python recipe; DevStudio does not edit it. Add the requirement in the recipe's own requirements() method.", packageId));
        }

        try
        {
            ConanTxtEditor.AddOrReplaceRequirement(project.ProjectPath, packageId, version);
            return Task.FromResult(new PackageOperationResult(true, PackageOperation.Add, project.ProjectPath, packageId, new[] { project.ProjectPath }, Array.Empty<string>(), null, false, null));
        }
        catch (IOException ex)
        {
            return Task.FromResult(PackageOperationResult.Unavailable(PackageOperation.Add, project.ProjectPath, $"Could not update conanfile.txt: {ex.Message}", packageId));
        }
    }

    public Task<PackageOperationResult> RemoveAsync(PackageProject project, string packageId, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        if (string.Equals(Path.GetExtension(project.ProjectPath), ".py", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(PackageOperationResult.Unavailable(PackageOperation.Remove, project.ProjectPath,
                "conanfile.py is an executable Python recipe; DevStudio does not edit it.", packageId));
        }

        try
        {
            var removed = ConanTxtEditor.RemoveRequirement(project.ProjectPath, packageId);
            return Task.FromResult(removed
                ? new PackageOperationResult(true, PackageOperation.Remove, project.ProjectPath, packageId, new[] { project.ProjectPath }, Array.Empty<string>(), null, false, null)
                : PackageOperationResult.Unavailable(PackageOperation.Remove, project.ProjectPath, "No matching requirement was found in conanfile.txt.", packageId));
        }
        catch (IOException ex)
        {
            return Task.FromResult(PackageOperationResult.Unavailable(PackageOperation.Remove, project.ProjectPath, $"Could not update conanfile.txt: {ex.Message}", packageId));
        }
    }

    public Task<PackageOperationResult> UpdateAsync(PackageProject project, string packageId, string? targetVersion, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default) =>
        // A version bump is structurally the same conanfile.txt edit as Add with an explicit
        // version — Conan's [requires] section has no separate "update" syntax.
        AddAsync(project, packageId, targetVersion, prerelease: false, isDevDependency: false, outputSink, cancellationToken);

    public async Task<PackageOperationResult> RestoreAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var result = await RunConanAsync(project, new[] { "install", project.ProjectPath, "--build=missing" }, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.WasCancelled) return PackageOperationResult.Cancelled(PackageOperation.Restore, project.ProjectPath);

        return result.ExitCode == 0
            ? new PackageOperationResult(true, PackageOperation.Restore, project.ProjectPath, null, Array.Empty<string>(), Array.Empty<string>(), result.ExitCode, false, null, result.StandardOutput)
            : new PackageOperationResult(false, PackageOperation.Restore, project.ProjectPath, null, Array.Empty<string>(), Array.Empty<string>(), result.ExitCode, false,
                PackageOperationDiagnostics.FirstErrorLine(result, "The conan install command failed."), result.StandardOutput);
    }

    public async Task<IReadOnlyList<PackageSource>> GetSourcesAsync(PackageProject project, CancellationToken cancellationToken = default)
    {
        var conan = _toolchainRegistry.Get(WellKnownToolchainIds.Conan)?.ExecutablePath ?? "conan";
        var rootPath = Path.GetDirectoryName(project.ProjectPath) ?? Environment.CurrentDirectory;
        var request = new ProcessStartRequest(conan, new[] { "remote", "list", "--format=json" }, rootPath);
        var result = await _processRunner.RunAsync(request, null, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput)) return Array.Empty<PackageSource>();

        return ConanRemoteListParser.Parse(result.StandardOutput);
    }

    private Task<ProcessResult> RunConanAsync(PackageProject project, IReadOnlyList<string> arguments, IProcessOutputSink? outputSink, CancellationToken cancellationToken)
    {
        var conan = _toolchainRegistry.Get(WellKnownToolchainIds.Conan)?.ExecutablePath ?? "conan";
        var rootPath = Path.GetDirectoryName(project.ProjectPath) ?? Environment.CurrentDirectory;
        var request = new ProcessStartRequest(conan, arguments, rootPath);
        return _processRunner.RunAsync(request, outputSink, cancellationToken);
    }
}

public readonly record struct ConanDeclaredDependency(string PackageId, string? Version);

/// <summary>Reads declared Conan dependencies without invoking Conan itself — from conanfile.txt's
/// static <c>[requires]</c> section reliably, and from conanfile.py via a deliberately conservative
/// best-effort regex over literal <c>self.requires("name/version")</c> calls only (documented, not
/// silently wrong: a recipe that builds its requirement string dynamically is not detected).</summary>
public static class ConanRecipeReader
{
    private static readonly Regex PyRequiresRegex = new(
        "self\\.requires\\(\\s*[\"'](?<spec>[^\"']+)[\"']", RegexOptions.Compiled);

    public static IReadOnlyList<ConanDeclaredDependency> ReadDeclaredDependenciesFromTxt(string conanfileTxtPath)
    {
        if (!File.Exists(conanfileTxtPath)) return Array.Empty<ConanDeclaredDependency>();

        var results = new List<ConanDeclaredDependency>();
        var inRequires = false;
        foreach (var rawLine in File.ReadAllLines(conanfileTxtPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inRequires = string.Equals(line.Trim('[', ']').Trim(), "requires", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inRequires) continue;
            var parsed = ParseSpec(line);
            if (parsed is not null) results.Add(parsed.Value);
        }

        return results;
    }

    public static IReadOnlyList<ConanDeclaredDependency> ReadDeclaredDependenciesFromPy(string conanfilePyPath)
    {
        if (!File.Exists(conanfilePyPath)) return Array.Empty<ConanDeclaredDependency>();

        var text = File.ReadAllText(conanfilePyPath);
        var results = new List<ConanDeclaredDependency>();
        foreach (Match match in PyRequiresRegex.Matches(text))
        {
            var parsed = ParseSpec(match.Groups["spec"].Value);
            if (parsed is not null) results.Add(parsed.Value);
        }

        return results;
    }

    private static ConanDeclaredDependency? ParseSpec(string spec)
    {
        // "name/version" or "name/version@user/channel" — the @user/channel suffix, if present, is not part of the version.
        var atIndex = spec.IndexOf('@');
        var core = atIndex >= 0 ? spec[..atIndex] : spec;
        var slashIndex = core.IndexOf('/');
        if (slashIndex <= 0) return null;
        return new ConanDeclaredDependency(core[..slashIndex], core[(slashIndex + 1)..]);
    }
}

/// <summary>Structured conanfile.txt <c>[requires]</c> section editing — line-based, since the
/// format is a simple ini-like list, never regex-editing conanfile.py.</summary>
public static class ConanTxtEditor
{
    public static void AddOrReplaceRequirement(string conanfileTxtPath, string packageId, string? version)
    {
        var lines = File.Exists(conanfileTxtPath) ? File.ReadAllLines(conanfileTxtPath).ToList() : new List<string>();
        var (start, end) = FindRequiresSection(lines);

        if (start < 0)
        {
            lines.Insert(0, "[requires]");
            lines.Insert(1, string.Empty);
            start = 0;
            end = 1;
        }

        var newLine = string.IsNullOrWhiteSpace(version) ? packageId : $"{packageId}/{version}";
        var existingIndex = FindRequirementLine(lines, start, end, packageId);
        if (existingIndex >= 0)
        {
            lines[existingIndex] = newLine;
        }
        else
        {
            lines.Insert(end, newLine);
        }

        File.WriteAllLines(conanfileTxtPath, lines);
    }

    public static bool RemoveRequirement(string conanfileTxtPath, string packageId)
    {
        if (!File.Exists(conanfileTxtPath)) return false;

        var lines = File.ReadAllLines(conanfileTxtPath).ToList();
        var (start, end) = FindRequiresSection(lines);
        if (start < 0) return false;

        var existingIndex = FindRequirementLine(lines, start, end, packageId);
        if (existingIndex < 0) return false;

        lines.RemoveAt(existingIndex);
        File.WriteAllLines(conanfileTxtPath, lines);
        return true;
    }

    private static (int Start, int End) FindRequiresSection(List<string> lines)
    {
        var start = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (start < 0)
            {
                if (string.Equals(trimmed, "[requires]", StringComparison.OrdinalIgnoreCase)) start = i + 1;
                continue;
            }

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) return (start, i);
        }

        return start < 0 ? (-1, -1) : (start, lines.Count);
    }

    private static int FindRequirementLine(List<string> lines, int start, int end, string packageId)
    {
        for (var i = start; i < end && i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            var slashIndex = trimmed.IndexOf('/');
            var name = slashIndex > 0 ? trimmed[..slashIndex] : trimmed;
            if (string.Equals(name, packageId, StringComparison.OrdinalIgnoreCase)) return i;
        }

        return -1;
    }
}

/// <summary>
/// Parses <c>conan graph info --format=json</c>'s documented Conan 2.x graph JSON shape: a
/// "graph" object with a "nodes" map keyed by node id, each node carrying "name"/"version" and a
/// "dependencies" map whose entries carry a boolean "direct" flag. This shape is based on Conan
/// 2.x's published documentation and has not been verified against a real invocation — see the
/// class-level doc comment on <see cref="ConanPackageAdapter"/>.
/// </summary>
public static class ConanGraphInfoParser
{
    public static IReadOnlyList<PackageReference> ParseInstalled(string graphInfoJson, string projectPath, string packageManagerId)
    {
        try
        {
            using var document = JsonDocument.Parse(graphInfoJson);
            if (!document.RootElement.TryGetProperty("graph", out var graph) || !graph.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Object)
                return Array.Empty<PackageReference>();

            // "direct" is an edge property (is THIS specific edge a direct requirement of the
            // node it originates from), not a node property — so project-level direct-vs-
            // transitive must be read off the consumer (root) node's own outgoing edges only,
            // never off some other package's internal dependency edges (a dependency's own
            // "direct" requirement is still transitive from the project's point of view).
            string? consumerId = null;
            foreach (var node in nodes.EnumerateObject())
            {
                var recipe = node.Value.TryGetProperty("recipe", out var r) ? r.GetString() : null;
                if (string.Equals(recipe, "Consumer", StringComparison.OrdinalIgnoreCase))
                {
                    consumerId = node.Name;
                    break;
                }
            }

            var directIds = new HashSet<string>(StringComparer.Ordinal);
            if (consumerId is not null && nodes.TryGetProperty(consumerId, out var consumerNode) &&
                consumerNode.TryGetProperty("dependencies", out var consumerDeps) && consumerDeps.ValueKind == JsonValueKind.Object)
            {
                foreach (var dep in consumerDeps.EnumerateObject())
                {
                    if (dep.Value.TryGetProperty("direct", out var directFlag) && directFlag.ValueKind == JsonValueKind.True)
                    {
                        directIds.Add(dep.Name);
                    }
                }
            }

            var results = new List<PackageReference>();
            foreach (var node in nodes.EnumerateObject())
            {
                if (string.Equals(node.Name, consumerId, StringComparison.Ordinal)) continue; // The consumer (root project) is never a dependency of itself.

                var value = node.Value;
                var name = value.TryGetProperty("name", out var n) ? n.GetString() : null;
                var version = value.TryGetProperty("version", out var v) ? v.GetString() : null;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(version) || string.Equals(version, "None", StringComparison.Ordinal)) continue;

                var kind = directIds.Contains(node.Name) ? PackageDependencyKind.Direct : PackageDependencyKind.Transitive;
                results.Add(new PackageReference(name, version, version, kind, projectPath, packageManagerId));
            }

            return results;
        }
        catch (JsonException)
        {
            return Array.Empty<PackageReference>();
        }
    }
}

/// <summary>Parses <c>conan search --format=json</c>'s documented Conan 2.x JSON shape: a map of
/// remote name to an array of recipe references (<c>"name/version"</c> strings).</summary>
public static class ConanSearchParser
{
    public static IReadOnlyList<PackageSearchResult> Parse(string searchJson)
    {
        try
        {
            using var document = JsonDocument.Parse(searchJson);
            var results = new List<PackageSearchResult>();
            foreach (var remote in document.RootElement.EnumerateObject())
            {
                if (remote.Value.ValueKind != JsonValueKind.Array) continue;
                foreach (var reference in remote.Value.EnumerateArray())
                {
                    var spec = reference.GetString();
                    if (string.IsNullOrEmpty(spec)) continue;
                    var slashIndex = spec.IndexOf('/');
                    if (slashIndex <= 0) continue;
                    var name = spec[..slashIndex];
                    var version = spec[(slashIndex + 1)..];
                    results.Add(new PackageSearchResult(name, version, Description: null, LatestVersion: version, Source: remote.Name));
                }
            }

            return results;
        }
        catch (JsonException)
        {
            return Array.Empty<PackageSearchResult>();
        }
    }
}

/// <summary>Parses <c>conan remote list --format=json</c>'s documented Conan 2.x JSON shape: an
/// array of <c>{ "name", "url", "verify_ssl", "enabled" }</c> objects. Never surfaces
/// authentication tokens — remote credentials are stored separately by Conan and are never part of
/// this command's output.</summary>
public static class ConanRemoteListParser
{
    public static IReadOnlyList<PackageSource> Parse(string remoteListJson)
    {
        try
        {
            using var document = JsonDocument.Parse(remoteListJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<PackageSource>();

            var results = new List<PackageSource>();
            foreach (var remote in document.RootElement.EnumerateArray())
            {
                var name = remote.TryGetProperty("name", out var n) ? n.GetString() : null;
                var url = remote.TryGetProperty("url", out var u) ? u.GetString() : null;
                var enabled = !remote.TryGetProperty("enabled", out var e) || e.ValueKind != JsonValueKind.False;
                if (name is null || url is null) continue;
                results.Add(new PackageSource(name, url, enabled, IsDefault: false, PackageSourceType.Registry));
            }

            return results;
        }
        catch (JsonException)
        {
            return Array.Empty<PackageSource>();
        }
    }
}
