using System.Text.Json;
using DevStudio.Core.Packages;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Packages;

/// <summary>
/// Drives real <c>dotnet</c> package sub-commands (SKILL.md §8's P0 priority #1) — <c>dotnet list
/// package [--include-transitive|--outdated] --format json</c>, <c>dotnet add/remove package</c>,
/// <c>dotnet restore</c>, and <c>dotnet package search --format json</c> (all real-verified
/// against this SDK's actual JSON shape before this parser was written, the same discipline
/// ADR-010's Git parser used). Resolves the executable from the live <see
/// cref="IToolchainRegistry"/>, exactly like <see cref="Build.DotNetBuildAdapter"/>/<see
/// cref="Testing.DotNetTestAdapter"/> — never a hard-coded path.
/// </summary>
public sealed class NuGetPackageAdapter : IPackageManagerAdapter, IPackageInspector, IPackageSearcher, IPackageInstaller, IPackageRemover, IPackageUpdater
{
    public string Id => WellKnownPackageManagerIds.NuGet;
    public string DisplayName => "NuGet";

    private static readonly PackageManagerCapabilities FullCapabilities = new(
        ListInstalled: true,
        ListDependencies: true,
        Search: true,
        Add: true,
        Remove: true,
        Update: true,
        Restore: true,
        ListOutdated: true,
        ManageSources: false,
        LockfileSupport: false,
        TransitiveDependencySupport: true,
        PrereleaseSupport: true);

    private readonly IProcessRunner _processRunner;
    private readonly IToolchainRegistry _toolchainRegistry;

    public NuGetPackageAdapter(IProcessRunner processRunner, IToolchainRegistry toolchainRegistry)
    {
        _processRunner = processRunner;
        _toolchainRegistry = toolchainRegistry;
    }

    public PackageProject? DetectProject(ProjectInfo project)
    {
        if (project.ProjectType != ProjectType.DotNet || project.ProjectFile is null) return null;

        var dotnet = _toolchainRegistry.Get(WellKnownToolchainIds.DotNet);
        if (dotnet is null || !dotnet.IsUsable)
        {
            return new PackageProject(project.ProjectFile, project.ProjectType, Id, DisplayName, PackageManagerCapabilities.None,
                "The .NET SDK is not installed or has not been detected.");
        }

        return new PackageProject(project.ProjectFile, project.ProjectType, Id, DisplayName, FullCapabilities);
    }

    public async Task<IReadOnlyList<PackageReference>> ListInstalledAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(project, new[] { "list", project.ProjectPath, "package", "--include-transitive", "--format", "json" }, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) return Array.Empty<PackageReference>();
        return ParseListPackage(result.StandardOutput, project, includeOutdatedOnly: false);
    }

    public async Task<IReadOnlyList<PackageReference>> ListOutdatedAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(project, new[] { "list", project.ProjectPath, "package", "--outdated", "--format", "json" }, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) return Array.Empty<PackageReference>();
        return ParseListPackage(result.StandardOutput, project, includeOutdatedOnly: true);
    }

    public async Task<IReadOnlyList<PackageDependency>> ListDependenciesAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var installed = await ListInstalledAsync(project, outputSink, cancellationToken).ConfigureAwait(false);
        return installed
            .Select(p => new PackageDependency(p.PackageId, p.RequestedVersion, p.Kind, ParentPackageId: null))
            .ToList();
    }

    private static IReadOnlyList<PackageReference> ParseListPackage(string json, PackageProject project, bool includeOutdatedOnly)
    {
        var references = new List<PackageReference>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("projects", out var projects)) return references;

            foreach (var projectElement in projects.EnumerateArray())
            {
                if (!projectElement.TryGetProperty("frameworks", out var frameworks)) continue;
                foreach (var framework in frameworks.EnumerateArray())
                {
                    var targetFramework = framework.TryGetProperty("framework", out var fw) ? fw.GetString() : null;
                    AddFrom(framework, "topLevelPackages", PackageDependencyKind.Direct);
                    AddFrom(framework, "transitivePackages", PackageDependencyKind.Transitive);

                    void AddFrom(JsonElement frameworkElement, string propertyName, PackageDependencyKind kind)
                    {
                        if (!frameworkElement.TryGetProperty(propertyName, out var list)) return;
                        foreach (var pkg in list.EnumerateArray())
                        {
                            var id = pkg.GetProperty("id").GetString() ?? string.Empty;
                            var resolved = pkg.TryGetProperty("resolvedVersion", out var rv) ? rv.GetString() : null;
                            var requested = pkg.TryGetProperty("requestedVersion", out var reqv) ? reqv.GetString() : resolved;
                            var latest = pkg.TryGetProperty("latestVersion", out var lv) ? lv.GetString() : null;
                            if (includeOutdatedOnly && latest is null) continue;

                            references.Add(new PackageReference(
                                id, requested, resolved, kind, project.ProjectPath, project.PackageManagerId,
                                targetFramework, IsOutdated: latest is not null, LatestVersion: latest));
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            // A real `dotnet list package` failure mode observed for a project with zero
            // package references is still exit code 0 with well-formed (empty) JSON, so a parse
            // failure here means the SDK's own output changed shape unexpectedly — fail closed to
            // an empty list rather than guessing at a malformed structure (SKILL.md §26).
            return Array.Empty<PackageReference>();
        }

        return references;
    }

    public async Task<IReadOnlyList<PackageSearchResult>> SearchAsync(PackageProject project, string query, bool includePrerelease, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "package", "search", query, "--format", "json" };
        if (includePrerelease) arguments.Add("--prerelease");

        var result = await RunAsync(project, arguments, null, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) return Array.Empty<PackageSearchResult>();

        var results = new List<PackageSearchResult>();
        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            if (!document.RootElement.TryGetProperty("searchResult", out var searchResults)) return results;

            foreach (var source in searchResults.EnumerateArray())
            {
                var sourceName = source.TryGetProperty("sourceName", out var sn) ? sn.GetString() : null;
                if (!source.TryGetProperty("packages", out var packages)) continue;

                foreach (var pkg in packages.EnumerateArray())
                {
                    var id = pkg.GetProperty("id").GetString() ?? string.Empty;
                    var latestVersion = pkg.TryGetProperty("latestVersion", out var lv) ? lv.GetString() ?? string.Empty : string.Empty;
                    long? downloads = pkg.TryGetProperty("totalDownloads", out var dl) && dl.TryGetInt64(out var d) ? d : null;
                    var owners = pkg.TryGetProperty("owners", out var ow) ? ow.GetString() : null;

                    results.Add(new PackageSearchResult(id, latestVersion, Description: owners, LatestVersion: latestVersion, Downloads: downloads, Source: sourceName));
                }
            }
        }
        catch (JsonException)
        {
            return Array.Empty<PackageSearchResult>();
        }

        return results;
    }

    public async Task<PackageOperationResult> AddAsync(PackageProject project, string packageId, string? version, bool prerelease, bool isDevDependency, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "add", project.ProjectPath, "package", packageId };
        if (!string.IsNullOrWhiteSpace(version)) { arguments.Add("--version"); arguments.Add(version); }
        if (prerelease) arguments.Add("--prerelease");

        var result = await RunAsync(project, arguments, outputSink, cancellationToken).ConfigureAwait(false);
        return ToOperationResult(result, PackageOperation.Add, project, packageId);
    }

    public async Task<PackageOperationResult> RemoveAsync(PackageProject project, string packageId, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(project, new[] { "remove", project.ProjectPath, "package", packageId }, outputSink, cancellationToken).ConfigureAwait(false);
        return ToOperationResult(result, PackageOperation.Remove, project, packageId);
    }

    public async Task<PackageOperationResult> UpdateAsync(PackageProject project, string packageId, string? targetVersion, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        // The `dotnet` CLI has no dedicated "update" verb for a PackageReference — the
        // documented, real way to bump a version is `add package` again with the new version,
        // which replaces the existing PackageReference's Version attribute in place rather than
        // duplicating it (verified against a real project before writing this).
        var arguments = new List<string> { "add", project.ProjectPath, "package", packageId };
        if (!string.IsNullOrWhiteSpace(targetVersion)) { arguments.Add("--version"); arguments.Add(targetVersion); }

        var result = await RunAsync(project, arguments, outputSink, cancellationToken).ConfigureAwait(false);
        return ToOperationResult(result, PackageOperation.Update, project, packageId);
    }

    public async Task<PackageOperationResult> RestoreAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(project, new[] { "restore", project.ProjectPath }, outputSink, cancellationToken).ConfigureAwait(false);
        return ToOperationResult(result, PackageOperation.Restore, project, null);
    }

    private PackageOperationResult ToOperationResult(ProcessResult result, PackageOperation operation, PackageProject project, string? packageId)
    {
        if (result.WasCancelled) return PackageOperationResult.Cancelled(operation, project.ProjectPath, packageId);

        var changedFiles = result.ExitCode == 0 && operation is PackageOperation.Add or PackageOperation.Remove or PackageOperation.Update
            ? new[] { project.ProjectPath }
            : Array.Empty<string>();

        return new PackageOperationResult(
            result.ExitCode == 0,
            operation,
            project.ProjectPath,
            packageId,
            changedFiles,
            Array.Empty<string>(),
            result.ExitCode,
            false,
            result.ExitCode == 0 ? null : PackageOperationDiagnostics.FirstErrorLine(result, "The dotnet command failed."),
            result.StandardOutput);
    }

    private async Task<ProcessResult> RunAsync(PackageProject project, IReadOnlyList<string> arguments, IProcessOutputSink? outputSink, CancellationToken cancellationToken)
    {
        var dotnet = _toolchainRegistry.Get(WellKnownToolchainIds.DotNet);
        var executable = dotnet?.ExecutablePath ?? "dotnet";
        var workingDirectory = Path.GetDirectoryName(Path.GetFullPath(project.ProjectPath)) ?? Environment.CurrentDirectory;
        var request = new ProcessStartRequest(executable, arguments, workingDirectory, OutputEncoding: System.Text.Encoding.UTF8);
        return await _processRunner.RunAsync(request, outputSink, cancellationToken).ConfigureAwait(false);
    }
}
