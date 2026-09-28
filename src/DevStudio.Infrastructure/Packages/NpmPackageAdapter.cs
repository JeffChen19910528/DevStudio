using System.Text.Json;
using DevStudio.Core.Packages;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Packages;

/// <summary>
/// Drives real <c>npm</c> (SKILL.md §8's P0 priority #3). Only <c>package.json</c> +
/// <c>package-lock.json</c> projects get real mutation capabilities here — a project whose
/// lockfile shows it is actually pnpm- or Yarn-managed (<c>pnpm-lock.yaml</c>/<c>yarn.lock</c>) is
/// still detected (so the UI can show "Node package management via pnpm/Yarn is not implemented
/// this phase" instead of silently doing nothing) but reports every capability false — running
/// npm against a pnpm/Yarn-managed project would diverge from its own declared lockfile, the same
/// class of risk the spec calls out for Python/Poetry.
/// </summary>
public sealed class NpmPackageAdapter : IPackageManagerAdapter, IPackageInspector, IPackageSearcher, IPackageInstaller, IPackageRemover, IPackageUpdater
{
    public string Id => WellKnownPackageManagerIds.Npm;
    public string DisplayName => "npm";

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
        LockfileSupport: true,
        TransitiveDependencySupport: true,
        PrereleaseSupport: true);

    private readonly IProcessRunner _processRunner;
    private readonly IToolchainRegistry _toolchainRegistry;

    public NpmPackageAdapter(IProcessRunner processRunner, IToolchainRegistry toolchainRegistry)
    {
        _processRunner = processRunner;
        _toolchainRegistry = toolchainRegistry;
    }

    public PackageProject? DetectProject(ProjectInfo project)
    {
        if (project.ProjectType != ProjectType.Node) return null;
        var packageJsonPath = Path.Combine(project.RootPath, "package.json");
        if (!File.Exists(packageJsonPath)) return null;

        if (File.Exists(Path.Combine(project.RootPath, "pnpm-lock.yaml")))
        {
            return new PackageProject(packageJsonPath, project.ProjectType, Id, DisplayName, PackageManagerCapabilities.None,
                "This project's lockfile (pnpm-lock.yaml) shows it is managed by pnpm, which is deferred to a later phase — not implemented.");
        }
        if (File.Exists(Path.Combine(project.RootPath, "yarn.lock")))
        {
            return new PackageProject(packageJsonPath, project.ProjectType, Id, DisplayName, PackageManagerCapabilities.None,
                "This project's lockfile (yarn.lock) shows it is managed by Yarn, which is deferred to a later phase — not implemented.");
        }

        var npm = _toolchainRegistry.Get(WellKnownToolchainIds.Npm);
        if (npm is null || !npm.IsUsable)
        {
            return new PackageProject(packageJsonPath, project.ProjectType, Id, DisplayName, PackageManagerCapabilities.None,
                "npm is not installed or has not been detected.");
        }

        return new PackageProject(packageJsonPath, project.ProjectType, Id, DisplayName, FullCapabilities);
    }

    public async Task<IReadOnlyList<PackageReference>> ListInstalledAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var declared = ReadDeclaredDependencies(project);
        var result = await RunAsync(project, new[] { "list", "--json", "--all" }, outputSink, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(result.StandardOutput)) return Array.Empty<PackageReference>();

        var references = new List<PackageReference>();
        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            if (document.RootElement.TryGetProperty("dependencies", out var dependencies))
            {
                Walk(dependencies, PackageDependencyKind.Direct);
            }

            void Walk(JsonElement dependenciesElement, PackageDependencyKind kind)
            {
                foreach (var dep in dependenciesElement.EnumerateObject())
                {
                    var version = dep.Value.TryGetProperty("version", out var v) ? v.GetString() : null;
                    declared.TryGetValue(dep.Name, out var isDev);
                    references.Add(new PackageReference(dep.Name, version, version, kind, project.ProjectPath, project.PackageManagerId, IsDevDependency: isDev));

                    if (dep.Value.TryGetProperty("dependencies", out var nested))
                    {
                        Walk(nested, PackageDependencyKind.Transitive);
                    }
                }
            }
        }
        catch (JsonException)
        {
            return Array.Empty<PackageReference>();
        }

        return references;
    }

    public async Task<IReadOnlyList<PackageDependency>> ListDependenciesAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var installed = await ListInstalledAsync(project, outputSink, cancellationToken).ConfigureAwait(false);
        return installed.Select(p => new PackageDependency(p.PackageId, p.RequestedVersion, p.Kind, null)).ToList();
    }

    public async Task<IReadOnlyList<PackageReference>> ListOutdatedAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(project, new[] { "outdated", "--json" }, outputSink, cancellationToken).ConfigureAwait(false);
        // `npm outdated` exits 1 when there ARE outdated packages — a real, documented quirk;
        // only an empty/unparsable body means "nothing to report", never the exit code alone.
        if (string.IsNullOrWhiteSpace(result.StandardOutput)) return Array.Empty<PackageReference>();

        var declared = ReadDeclaredDependencies(project);
        var references = new List<PackageReference>();
        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                var current = entry.Value.TryGetProperty("current", out var c) ? c.GetString() : null;
                var wanted = entry.Value.TryGetProperty("wanted", out var w) ? w.GetString() : null;
                var latest = entry.Value.TryGetProperty("latest", out var l) ? l.GetString() : null;
                declared.TryGetValue(entry.Name, out var isDev);
                references.Add(new PackageReference(entry.Name, wanted, current, PackageDependencyKind.Direct, project.ProjectPath, project.PackageManagerId,
                    IsDevDependency: isDev, IsOutdated: true, LatestVersion: latest));
            }
        }
        catch (JsonException)
        {
            return Array.Empty<PackageReference>();
        }

        return references;
    }

    public async Task<IReadOnlyList<PackageSearchResult>> SearchAsync(PackageProject project, string query, bool includePrerelease, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(project, new[] { "search", query, "--json" }, null, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(result.StandardOutput)) return Array.Empty<PackageSearchResult>();

        var results = new List<PackageSearchResult>();
        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            foreach (var pkg in document.RootElement.EnumerateArray())
            {
                var name = pkg.GetProperty("name").GetString() ?? string.Empty;
                var version = pkg.TryGetProperty("version", out var v) ? v.GetString() ?? string.Empty : string.Empty;
                var description = pkg.TryGetProperty("description", out var d) ? d.GetString() : null;
                string? homepage = pkg.TryGetProperty("links", out var links) && links.TryGetProperty("homepage", out var h) ? h.GetString() : null;
                string? license = pkg.TryGetProperty("license", out var lic) ? lic.GetString() : null;

                results.Add(new PackageSearchResult(name, version, description, LatestVersion: version, Source: "npm registry", License: license, ProjectUrl: homepage));
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
        var spec = string.IsNullOrWhiteSpace(version) ? packageId : $"{packageId}@{version}";
        var arguments = new List<string> { "install", spec };
        if (isDevDependency) arguments.Add("--save-dev");

        var result = await RunAsync(project, arguments, outputSink, cancellationToken).ConfigureAwait(false);
        return ToOperationResult(result, PackageOperation.Add, project, packageId);
    }

    public async Task<PackageOperationResult> RemoveAsync(PackageProject project, string packageId, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(project, new[] { "uninstall", packageId }, outputSink, cancellationToken).ConfigureAwait(false);
        return ToOperationResult(result, PackageOperation.Remove, project, packageId);
    }

    public async Task<PackageOperationResult> UpdateAsync(PackageProject project, string packageId, string? targetVersion, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var spec = string.IsNullOrWhiteSpace(targetVersion) ? packageId : $"{packageId}@{targetVersion}";
        var result = await RunAsync(project, new[] { "install", spec }, outputSink, cancellationToken).ConfigureAwait(false);
        return ToOperationResult(result, PackageOperation.Update, project, packageId);
    }

    public async Task<PackageOperationResult> RestoreAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(project, new[] { "install" }, outputSink, cancellationToken).ConfigureAwait(false);
        return ToOperationResult(result, PackageOperation.Restore, project, null);
    }

    private static Dictionary<string, bool> ReadDeclaredDependencies(PackageProject project)
    {
        // package.json is the one ground truth for direct-vs-dev — `npm list`'s own output does
        // not reliably distinguish them, so this is a real, deliberate second read rather than
        // guessing from the install-tree shape alone.
        var declared = new Dictionary<string, bool>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(project.ProjectPath));
            AddSection("dependencies", isDev: false);
            AddSection("devDependencies", isDev: true);

            void AddSection(string propertyName, bool isDev)
            {
                if (!document.RootElement.TryGetProperty(propertyName, out var section)) return;
                foreach (var entry in section.EnumerateObject()) declared[entry.Name] = isDev;
            }
        }
        catch (JsonException) { /* malformed package.json — dev/prod distinction degrades gracefully to "prod" */ }
        catch (IOException) { }

        return declared;
    }

    private static PackageOperationResult ToOperationResult(ProcessResult result, PackageOperation operation, PackageProject project, string? packageId)
    {
        if (result.WasCancelled) return PackageOperationResult.Cancelled(operation, project.ProjectPath, packageId);

        var changedFiles = result.ExitCode == 0
            ? new[] { project.ProjectPath, Path.Combine(Path.GetDirectoryName(project.ProjectPath) ?? ".", "package-lock.json") }
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
            result.ExitCode == 0 ? null : PackageOperationDiagnostics.FirstErrorLine(result, "The npm command failed."),
            result.StandardOutput);
    }

    private async Task<ProcessResult> RunAsync(PackageProject project, IReadOnlyList<string> arguments, IProcessOutputSink? outputSink, CancellationToken cancellationToken)
    {
        var npm = _toolchainRegistry.Get(WellKnownToolchainIds.Npm);
        var executable = npm?.ExecutablePath ?? "npm";
        var workingDirectory = Path.GetDirectoryName(project.ProjectPath) ?? Environment.CurrentDirectory;
        var request = new ProcessStartRequest(executable, arguments, workingDirectory, OutputEncoding: System.Text.Encoding.UTF8);
        return await _processRunner.RunAsync(request, outputSink, cancellationToken).ConfigureAwait(false);
    }
}
