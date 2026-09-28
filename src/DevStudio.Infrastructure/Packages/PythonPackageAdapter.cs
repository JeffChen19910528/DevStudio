using System.Text.Json;
using System.Text.RegularExpressions;
using DevStudio.Core.Packages;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Packages;

/// <summary>Which Python dependency-management style a project's own files actually declare
/// (SKILL.md §9's "must NOT assume only pip" / "do not mix package-management systems
/// accidentally"). Real detection is filename-based only, never assumed from
/// <see cref="ProjectType.Python"/> alone.</summary>
public enum PythonDependencyStyle
{
    /// <summary>No recognized manifest at all — still a real, usable pip target (bare
    /// <c>pip install</c> against whatever interpreter applies), just with no declared-dependency
    /// file to keep in sync.</summary>
    PlainPip,
    RequirementsTxt,
    Poetry,
    Uv,
    Pipenv,
}

/// <summary>
/// Drives real <c>pip</c> (SKILL.md §8's P0 priority #2, §9). Resolves a project-local virtual
/// environment's own interpreter when one exists (<c>.venv</c>/<c>venv</c> under the project
/// root) rather than always falling back to whatever global Python the toolchain registry found —
/// installing into the wrong interpreter is the single most common real pip mistake this adapter
/// exists to avoid. Only <see cref="PythonDependencyStyle.RequirementsTxt"/>/<see
/// cref="PythonDependencyStyle.PlainPip"/> projects get real Add/Remove/Update/Restore
/// capabilities — a Poetry/uv/Pipenv-managed project (none of which are installed on this
/// development machine, so their own real adapters are out of scope for this phase per SKILL.md
/// §9's priority ordering) still gets real, capability-appropriate <em>inspection</em> via
/// whatever interpreter its own environment uses, but never a pip mutation that would silently
/// diverge from its own declared dependency file.
/// </summary>
public sealed class PythonPackageAdapter : IPackageManagerAdapter, IPackageInspector, IPackageInstaller, IPackageRemover, IPackageUpdater
{
    public string Id => WellKnownPackageManagerIds.Pip;
    public string DisplayName => "pip";

    private readonly IProcessRunner _processRunner;
    private readonly IToolchainRegistry _toolchainRegistry;

    public PythonPackageAdapter(IProcessRunner processRunner, IToolchainRegistry toolchainRegistry)
    {
        _processRunner = processRunner;
        _toolchainRegistry = toolchainRegistry;
    }

    public PackageProject? DetectProject(ProjectInfo project)
    {
        if (project.ProjectType != ProjectType.Python) return null;

        var style = DetectStyle(project.RootPath);
        var pythonExecutable = ResolvePythonExecutable(project.RootPath);
        // requirements.txt is the canonical "declared dependency file" for this adapter, even when
        // it does not exist yet (Add creates it on first use) — used only for read/write bookkeeping.
        var requirementsPath = Path.Combine(project.RootPath, "requirements.txt");

        if (pythonExecutable is null)
        {
            return new PackageProject(requirementsPath, project.ProjectType, Id, DisplayName, PackageManagerCapabilities.None,
                "Python is not installed or has not been detected.");
        }

        var mutable = style is PythonDependencyStyle.PlainPip or PythonDependencyStyle.RequirementsTxt;
        var capabilities = new PackageManagerCapabilities(
            ListInstalled: true,
            ListDependencies: style != PythonDependencyStyle.PlainPip,
            Search: false, // PyPI's legacy XML-RPC search API has been disabled for years; `pip search` itself errors — never faked here.
            Add: mutable,
            Remove: mutable,
            Update: mutable,
            Restore: style == PythonDependencyStyle.RequirementsTxt,
            ListOutdated: true,
            ManageSources: false,
            LockfileSupport: style is PythonDependencyStyle.Poetry or PythonDependencyStyle.Uv,
            TransitiveDependencySupport: false, // `pip list`/`pip show` do not report a reliable dependency tree without an extra tool this phase does not add.
            PrereleaseSupport: mutable);

        var unavailableReason = style switch
        {
            PythonDependencyStyle.Poetry => "This project is Poetry-managed (poetry.lock/[tool.poetry]) — adding/removing/updating via pip is disabled to avoid diverging from pyproject.toml. Poetry itself is not implemented this phase.",
            PythonDependencyStyle.Uv => "This project is uv-managed (uv.lock) — adding/removing/updating via pip is disabled to avoid diverging from pyproject.toml. uv itself is not implemented this phase.",
            PythonDependencyStyle.Pipenv => "This project is Pipenv-managed (Pipfile) — adding/removing/updating via pip is disabled to avoid diverging from the Pipfile. Pipenv itself is not implemented this phase.",
            _ => null,
        };

        return new PackageProject(requirementsPath, project.ProjectType, Id, DisplayName, capabilities, unavailableReason);
    }

    public static PythonDependencyStyle DetectStyle(string rootPath)
    {
        if (File.Exists(Path.Combine(rootPath, "poetry.lock"))) return PythonDependencyStyle.Poetry;
        if (File.Exists(Path.Combine(rootPath, "uv.lock"))) return PythonDependencyStyle.Uv;
        if (File.Exists(Path.Combine(rootPath, "Pipfile"))) return PythonDependencyStyle.Pipenv;

        var pyprojectPath = Path.Combine(rootPath, "pyproject.toml");
        if (File.Exists(pyprojectPath))
        {
            try
            {
                var content = File.ReadAllText(pyprojectPath);
                if (content.Contains("[tool.poetry]", StringComparison.Ordinal)) return PythonDependencyStyle.Poetry;
            }
            catch (IOException) { }
        }

        return File.Exists(Path.Combine(rootPath, "requirements.txt"))
            ? PythonDependencyStyle.RequirementsTxt
            : PythonDependencyStyle.PlainPip;
    }

    private string? ResolvePythonExecutable(string rootPath)
    {
        foreach (var venvName in new[] { ".venv", "venv" })
        {
            var candidate = OperatingSystem.IsWindows()
                ? Path.Combine(rootPath, venvName, "Scripts", "python.exe")
                : Path.Combine(rootPath, venvName, "bin", "python");
            if (File.Exists(candidate)) return candidate;
        }

        return _toolchainRegistry.Get(WellKnownToolchainIds.Python)?.ExecutablePath;
    }

    public async Task<IReadOnlyList<PackageReference>> ListInstalledAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var result = await RunPipAsync(project, new[] { "list", "--format", "json" }, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput)) return Array.Empty<PackageReference>();

        var declared = ReadRequirementsNames(project);
        var references = new List<PackageReference>();
        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            foreach (var pkg in document.RootElement.EnumerateArray())
            {
                var name = pkg.GetProperty("name").GetString() ?? string.Empty;
                var version = pkg.TryGetProperty("version", out var v) ? v.GetString() : null;
                var kind = declared.Contains(NormalizeName(name)) || declared.Count == 0 ? PackageDependencyKind.Direct : PackageDependencyKind.Transitive;
                references.Add(new PackageReference(name, version, version, kind, project.ProjectPath, project.PackageManagerId));
            }
        }
        catch (JsonException)
        {
            return Array.Empty<PackageReference>();
        }

        return references;
    }

    public Task<IReadOnlyList<PackageDependency>> ListDependenciesAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        // A declared-dependency read (SKILL.md §9's "Declared vs Installed vs Resolved") — parses
        // requirements.txt directly rather than running a process, since this is exactly the kind
        // of read-only, file-based inspection Project Detection already does elsewhere.
        var declared = ParseRequirementsFile(project.ProjectPath);
        IReadOnlyList<PackageDependency> dependencies = declared
            .Select(d => new PackageDependency(d.Name, d.VersionSpecifier, PackageDependencyKind.Direct, null))
            .ToList();
        return Task.FromResult(dependencies);
    }

    public async Task<IReadOnlyList<PackageReference>> ListOutdatedAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var result = await RunPipAsync(project, new[] { "list", "--outdated", "--format", "json" }, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput)) return Array.Empty<PackageReference>();

        var references = new List<PackageReference>();
        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            foreach (var pkg in document.RootElement.EnumerateArray())
            {
                var name = pkg.GetProperty("name").GetString() ?? string.Empty;
                var current = pkg.TryGetProperty("version", out var v) ? v.GetString() : null;
                var latest = pkg.TryGetProperty("latest_version", out var lv) ? lv.GetString() : null;
                references.Add(new PackageReference(name, current, current, PackageDependencyKind.Direct, project.ProjectPath, project.PackageManagerId,
                    IsOutdated: true, LatestVersion: latest));
            }
        }
        catch (JsonException)
        {
            return Array.Empty<PackageReference>();
        }

        return references;
    }

    public async Task<PackageOperationResult> AddAsync(PackageProject project, string packageId, string? version, bool prerelease, bool isDevDependency, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var spec = string.IsNullOrWhiteSpace(version) ? packageId : $"{packageId}=={version}";
        var arguments = new List<string> { "install", spec };
        if (prerelease) arguments.Add("--pre");

        var result = await RunPipAsync(project, arguments, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.WasCancelled) return PackageOperationResult.Cancelled(PackageOperation.Add, project.ProjectPath, packageId);
        if (result.ExitCode != 0) return Failure(PackageOperation.Add, project, packageId, result);

        var resolvedVersion = version ?? await GetInstalledVersionAsync(project, packageId, cancellationToken).ConfigureAwait(false);
        var changed = UpdateRequirementsFile(project, packageId, resolvedVersion, remove: false);
        return Success(PackageOperation.Add, project, packageId, changed, result);
    }

    public async Task<PackageOperationResult> RemoveAsync(PackageProject project, string packageId, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var result = await RunPipAsync(project, new[] { "uninstall", "-y", packageId }, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.WasCancelled) return PackageOperationResult.Cancelled(PackageOperation.Remove, project.ProjectPath, packageId);
        if (result.ExitCode != 0) return Failure(PackageOperation.Remove, project, packageId, result);

        var changed = UpdateRequirementsFile(project, packageId, null, remove: true);
        return Success(PackageOperation.Remove, project, packageId, changed, result);
    }

    public async Task<PackageOperationResult> UpdateAsync(PackageProject project, string packageId, string? targetVersion, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "install", "--upgrade" };
        arguments.Add(string.IsNullOrWhiteSpace(targetVersion) ? packageId : $"{packageId}=={targetVersion}");

        var result = await RunPipAsync(project, arguments, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.WasCancelled) return PackageOperationResult.Cancelled(PackageOperation.Update, project.ProjectPath, packageId);
        if (result.ExitCode != 0) return Failure(PackageOperation.Update, project, packageId, result);

        var resolvedVersion = targetVersion ?? await GetInstalledVersionAsync(project, packageId, cancellationToken).ConfigureAwait(false);
        var changed = UpdateRequirementsFile(project, packageId, resolvedVersion, remove: false);
        return Success(PackageOperation.Update, project, packageId, changed, result);
    }

    public async Task<PackageOperationResult> RestoreAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(project.ProjectPath))
        {
            return PackageOperationResult.Unavailable(PackageOperation.Restore, project.ProjectPath, "No requirements.txt file was found to restore from.");
        }

        var result = await RunPipAsync(project, new[] { "install", "-r", project.ProjectPath }, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.WasCancelled) return PackageOperationResult.Cancelled(PackageOperation.Restore, project.ProjectPath);
        return result.ExitCode == 0
            ? Success(PackageOperation.Restore, project, null, Array.Empty<string>(), result)
            : Failure(PackageOperation.Restore, project, null, result);
    }

    private async Task<string?> GetInstalledVersionAsync(PackageProject project, string packageId, CancellationToken cancellationToken)
    {
        var result = await RunPipAsync(project, new[] { "show", packageId }, null, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) return null;
        var match = Regex.Match(result.StandardOutput, @"^Version:\s*(\S+)", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static readonly Regex RequirementLineRegex = new(@"^\s*([A-Za-z0-9_.\-]+)\s*([<>=!~]{1,2}[^;#\s]+)?", RegexOptions.Compiled);

    private static IReadOnlyList<(string Name, string? VersionSpecifier)> ParseRequirementsFile(string requirementsPath)
    {
        var entries = new List<(string, string?)>();
        if (!File.Exists(requirementsPath)) return entries;

        foreach (var rawLine in File.ReadAllLines(requirementsPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('-')) continue;

            var match = RequirementLineRegex.Match(line);
            if (!match.Success || match.Groups[1].Value.Length == 0) continue;
            entries.Add((match.Groups[1].Value, match.Groups[2].Success ? match.Groups[2].Value : null));
        }

        return entries;
    }

    private static HashSet<string> ReadRequirementsNames(PackageProject project) =>
        ParseRequirementsFile(project.ProjectPath).Select(e => NormalizeName(e.Name)).ToHashSet();

    private static string NormalizeName(string name) => name.Replace('_', '-').ToLowerInvariant();

    /// <summary>Rewrites requirements.txt to add/update/remove one pinned entry, preserving every
    /// other line exactly as written (SKILL.md §16: never silently rewrite unrelated content).
    /// Returns the changed file path, or an empty list when there was no requirements.txt to
    /// begin with (e.g. a <see cref="PythonDependencyStyle.PlainPip"/> project) — the pip install
    /// itself still succeeded, this just has nothing declared to keep in sync.</summary>
    private static IReadOnlyList<string> UpdateRequirementsFile(PackageProject project, string packageId, string? version, bool remove)
    {
        if (!File.Exists(project.ProjectPath))
        {
            if (remove) return Array.Empty<string>();
            // First real dependency ever added to a PlainPip project creates its requirements.txt
            // — this is the one case where "declared dependency file" transitions into existing.
        }

        var lines = File.Exists(project.ProjectPath) ? File.ReadAllLines(project.ProjectPath).ToList() : new List<string>();
        var normalizedTarget = NormalizeName(packageId);
        lines.RemoveAll(line =>
        {
            var match = RequirementLineRegex.Match(line.Trim());
            return match.Success && NormalizeName(match.Groups[1].Value) == normalizedTarget;
        });

        if (!remove)
        {
            lines.Add(version is null ? packageId : $"{packageId}=={version}");
        }

        File.WriteAllLines(project.ProjectPath, lines);
        return new[] { project.ProjectPath };
    }

    private static PackageOperationResult Success(PackageOperation operation, PackageProject project, string? packageId, IReadOnlyList<string> changedFiles, ProcessResult result) =>
        new(true, operation, project.ProjectPath, packageId, changedFiles, Array.Empty<string>(), result.ExitCode, false, null, result.StandardOutput);

    private static PackageOperationResult Failure(PackageOperation operation, PackageProject project, string? packageId, ProcessResult result) =>
        new(false, operation, project.ProjectPath, packageId, Array.Empty<string>(), Array.Empty<string>(), result.ExitCode, false,
            PackageOperationDiagnostics.FirstErrorLine(result, "The pip command failed."), result.StandardOutput);

    private async Task<ProcessResult> RunPipAsync(PackageProject project, IReadOnlyList<string> pipArguments, IProcessOutputSink? outputSink, CancellationToken cancellationToken)
    {
        var rootPath = Path.GetDirectoryName(project.ProjectPath) ?? Environment.CurrentDirectory;
        var python = ResolvePythonExecutable(rootPath) ?? "python";
        var arguments = new List<string> { "-m", "pip" };
        arguments.AddRange(pipArguments);

        var request = new ProcessStartRequest(python, arguments, rootPath);
        return await _processRunner.RunAsync(request, outputSink, cancellationToken).ConfigureAwait(false);
    }
}
