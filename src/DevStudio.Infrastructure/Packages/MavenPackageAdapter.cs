using System.Text.RegularExpressions;
using System.Xml.Linq;
using DevStudio.Core.Packages;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Packages;

/// <summary>
/// Drives real Apache Maven (Phase 14 P1-A, SKILL.md-style phase discipline). <c>mvn</c> is NOT
/// installed on the development machine this adapter was written on (only a bare JDK is) — every
/// command-construction/output-parsing path here is unit-tested against realistic fixture text
/// captured from Maven's own documented, version-stable output formats
/// (<c>dependency:tree</c>'s default text serializer, <c>versions:display-dependency-updates</c>'s
/// tabular diff), never against a real invocation. This adapter must always be described as
/// "implemented but not real-environment validated," never "real-tested" — see ADR-015.
///
/// Mutations (<see cref="AddAsync"/>/<see cref="RemoveAsync"/>) edit <c>pom.xml</c> through
/// <see cref="XDocument"/> structured XML editing, never regex-on-XML, so existing formatting
/// quirks aside from whitespace (comments, ordering of untouched elements, other
/// dependencyManagement/profiles/plugins sections) survive untouched (SKILL.md §9's "prefer
/// structured XML editing" guidance, applied here as it already was nowhere in Phase 13 since none
/// of NuGet/pip/npm's manifests are XML).
/// </summary>
public sealed class MavenPackageAdapter : IPackageManagerAdapter, IPackageInspector, IPackageInstaller, IPackageRemover, IPackageUpdater, IPackageSourceManager
{
    public string Id => WellKnownPackageManagerIds.Maven;
    public string DisplayName => "Maven";

    private readonly IProcessRunner _processRunner;
    private readonly IToolchainRegistry _toolchainRegistry;

    public MavenPackageAdapter(IProcessRunner processRunner, IToolchainRegistry toolchainRegistry)
    {
        _processRunner = processRunner;
        _toolchainRegistry = toolchainRegistry;
    }

    public PackageProject? DetectProject(ProjectInfo project)
    {
        if (project.ProjectType != ProjectType.Java) return null;

        var pomPath = Path.Combine(project.RootPath, "pom.xml");
        if (!File.Exists(pomPath)) return null;

        var maven = _toolchainRegistry.Get(WellKnownToolchainIds.Maven);
        if (maven is null || !maven.IsUsable)
        {
            return new PackageProject(pomPath, project.ProjectType, Id, DisplayName, PackageManagerCapabilities.None,
                "Maven (mvn) is not installed or has not been detected.");
        }

        var capabilities = new PackageManagerCapabilities(
            ListInstalled: true,
            ListDependencies: true,
            Search: false, // No safe process-based `mvn search` exists; Maven Central's search is an HTTP API, out of scope for a process-only adapter this phase.
            Add: true,
            Remove: true,
            Update: true,
            Restore: true,
            ListOutdated: true,
            ManageSources: true,
            LockfileSupport: false, // Maven has no lockfile concept — resolution is deterministic from the POM + repository metadata.
            TransitiveDependencySupport: true,
            PrereleaseSupport: false); // Maven has no first-class prerelease flag; -SNAPSHOT/qualifier handling is a version-string convention, not a capability this adapter models.

        return new PackageProject(pomPath, project.ProjectType, Id, DisplayName, capabilities);
    }

    public async Task<IReadOnlyList<PackageReference>> ListInstalledAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var result = await RunMavenAsync(project, new[] { "dependency:tree" }, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) return Array.Empty<PackageReference>();

        return MavenDependencyTreeParser.Parse(result.StandardOutput, project.ProjectPath, project.PackageManagerId);
    }

    public Task<IReadOnlyList<PackageDependency>> ListDependenciesAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        // Declared dependencies come straight from the POM itself (SKILL.md §9's declared-vs-
        // installed distinction) — no process needed, mirrors PythonPackageAdapter's
        // requirements.txt read.
        var doc = TryLoadPom(project.ProjectPath);
        if (doc?.Root is null) return Task.FromResult<IReadOnlyList<PackageDependency>>(Array.Empty<PackageDependency>());

        var ns = doc.Root.GetDefaultNamespace();
        var dependencies = doc.Root.Element(ns + "dependencies")?.Elements(ns + "dependency") ?? Enumerable.Empty<XElement>();

        IReadOnlyList<PackageDependency> result = dependencies
            .Select(dep =>
            {
                var groupId = dep.Element(ns + "groupId")?.Value;
                var artifactId = dep.Element(ns + "artifactId")?.Value;
                var version = dep.Element(ns + "version")?.Value;
                if (groupId is null || artifactId is null) return null;
                return new PackageDependency($"{groupId}:{artifactId}", version, PackageDependencyKind.Direct, null);
            })
            .Where(d => d is not null)
            .Select(d => d!)
            .ToList();

        return Task.FromResult(result);
    }

    public async Task<IReadOnlyList<PackageReference>> ListOutdatedAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        // versions-maven-plugin is invoked by fully-qualified goal so it works even when the
        // project's own POM does not declare it as a <plugin> — Maven resolves it from Maven
        // Central on first use exactly like any other plugin goal, real network access permitting.
        var result = await RunMavenAsync(project, new[] { "org.codehaus.mojo:versions-maven-plugin:2.16.2:display-dependency-updates" }, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) return Array.Empty<PackageReference>();

        return MavenOutdatedParser.Parse(result.StandardOutput, project.ProjectPath, project.PackageManagerId);
    }

    public async Task<PackageOperationResult> AddAsync(PackageProject project, string packageId, string? version, bool prerelease, bool isDevDependency, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var coordinate = MavenCoordinate.Parse(packageId, version);
        if (coordinate is null)
        {
            return PackageOperationResult.Unavailable(PackageOperation.Add, project.ProjectPath,
                "Expected a Maven coordinate in 'groupId:artifactId' form.", packageId);
        }

        try
        {
            var changed = MavenPomEditor.AddOrReplaceDependency(project.ProjectPath, coordinate.Value, scope: null);
            return new PackageOperationResult(true, PackageOperation.Add, project.ProjectPath, packageId, changed, Array.Empty<string>(), null, false, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return PackageOperationResult.Unavailable(PackageOperation.Add, project.ProjectPath, $"Could not update pom.xml: {ex.Message}", packageId);
        }
    }

    public async Task<PackageOperationResult> RemoveAsync(PackageProject project, string packageId, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var coordinate = MavenCoordinate.Parse(packageId, version: null);
        if (coordinate is null)
        {
            return PackageOperationResult.Unavailable(PackageOperation.Remove, project.ProjectPath,
                "Expected a Maven coordinate in 'groupId:artifactId' form.", packageId);
        }

        try
        {
            var changed = MavenPomEditor.RemoveDependency(project.ProjectPath, coordinate.Value.GroupId, coordinate.Value.ArtifactId);
            return changed.Count == 0
                ? PackageOperationResult.Unavailable(PackageOperation.Remove, project.ProjectPath, "No matching <dependency> was found in pom.xml.", packageId)
                : new PackageOperationResult(true, PackageOperation.Remove, project.ProjectPath, packageId, changed, Array.Empty<string>(), null, false, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return PackageOperationResult.Unavailable(PackageOperation.Remove, project.ProjectPath, $"Could not update pom.xml: {ex.Message}", packageId);
        }
        finally
        {
            await Task.CompletedTask;
        }
    }

    public async Task<PackageOperationResult> UpdateAsync(PackageProject project, string packageId, string? targetVersion, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        // An update is structurally the same pom.xml edit as Add with an explicit version — Maven
        // has no separate "update" CLI verb the way `npm update`/`pip install --upgrade` do.
        return await AddAsync(project, packageId, targetVersion, prerelease: false, isDevDependency: false, outputSink, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PackageOperationResult> RestoreAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        // `dependency:resolve` downloads/verifies every declared dependency without running the
        // project's own build lifecycle (compiling/testing) — the closest Maven equivalent of
        // `dotnet restore`/`npm install`/`pip install -r requirements.txt`.
        var result = await RunMavenAsync(project, new[] { "dependency:resolve" }, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.WasCancelled) return PackageOperationResult.Cancelled(PackageOperation.Restore, project.ProjectPath);
        return result.ExitCode == 0
            ? new PackageOperationResult(true, PackageOperation.Restore, project.ProjectPath, null, Array.Empty<string>(), Array.Empty<string>(), result.ExitCode, false, null, result.StandardOutput)
            : new PackageOperationResult(false, PackageOperation.Restore, project.ProjectPath, null, Array.Empty<string>(), Array.Empty<string>(), result.ExitCode, false,
                PackageOperationDiagnostics.FirstErrorLine(result, "The mvn command failed."), result.StandardOutput);
    }

    public Task<IReadOnlyList<PackageSource>> GetSourcesAsync(PackageProject project, CancellationToken cancellationToken = default)
    {
        // Reads only <repositories> declared inside the project's own pom.xml — never
        // ~/.m2/settings.xml, which is where Maven mirror/server credentials actually live
        // (SKILL.md §13/§16: never surface package-manager credentials).
        var sources = new List<PackageSource> { new("Maven Central", "https://repo.maven.apache.org/maven2", Enabled: true, IsDefault: true, PackageSourceType.Registry) };

        var doc = TryLoadPom(project.ProjectPath);
        if (doc?.Root is not null)
        {
            var ns = doc.Root.GetDefaultNamespace();
            foreach (var repo in doc.Root.Element(ns + "repositories")?.Elements(ns + "repository") ?? Enumerable.Empty<XElement>())
            {
                var id = repo.Element(ns + "id")?.Value;
                var url = repo.Element(ns + "url")?.Value;
                if (id is null || url is null) continue;
                sources.Add(new PackageSource(id, url, Enabled: true, IsDefault: false, PackageSourceType.Registry));
            }
        }

        return Task.FromResult<IReadOnlyList<PackageSource>>(sources);
    }

    private static XDocument? TryLoadPom(string pomPath)
    {
        try
        {
            return File.Exists(pomPath) ? XDocument.Load(pomPath, LoadOptions.PreserveWhitespace) : null;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private Task<ProcessResult> RunMavenAsync(PackageProject project, IReadOnlyList<string> arguments, IProcessOutputSink? outputSink, CancellationToken cancellationToken)
    {
        var mvn = _toolchainRegistry.Get(WellKnownToolchainIds.Maven)?.ExecutablePath ?? "mvn";
        var rootPath = Path.GetDirectoryName(project.ProjectPath) ?? Environment.CurrentDirectory;
        var request = new ProcessStartRequest(mvn, arguments, rootPath);
        return _processRunner.RunAsync(request, outputSink, cancellationToken);
    }
}

/// <summary>A Maven coordinate as accepted from the UI: "groupId:artifactId[:version]", with an
/// optional separately-supplied version taking precedence over one embedded in the id string.</summary>
public readonly record struct MavenCoordinate(string GroupId, string ArtifactId, string? Version)
{
    public static MavenCoordinate? Parse(string packageId, string? version)
    {
        var parts = packageId.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0) return null;

        var resolvedVersion = version ?? (parts.Length >= 3 ? parts[2] : null);
        return new MavenCoordinate(parts[0], parts[1], resolvedVersion);
    }
}

/// <summary>Structured, namespace-aware pom.xml editing — never regex-on-XML (SKILL.md §9).</summary>
public static class MavenPomEditor
{
    public static IReadOnlyList<string> AddOrReplaceDependency(string pomPath, MavenCoordinate coordinate, string? scope)
    {
        var doc = XDocument.Load(pomPath, LoadOptions.PreserveWhitespace);
        var root = doc.Root ?? throw new System.Xml.XmlException("pom.xml has no root element.");
        var ns = root.GetDefaultNamespace();

        var dependencies = root.Element(ns + "dependencies");
        if (dependencies is null)
        {
            dependencies = new XElement(ns + "dependencies");
            root.Add(dependencies);
        }

        var existing = dependencies.Elements(ns + "dependency").FirstOrDefault(d =>
            d.Element(ns + "groupId")?.Value == coordinate.GroupId &&
            d.Element(ns + "artifactId")?.Value == coordinate.ArtifactId);
        existing?.Remove();

        var element = new XElement(ns + "dependency",
            new XElement(ns + "groupId", coordinate.GroupId),
            new XElement(ns + "artifactId", coordinate.ArtifactId));
        if (!string.IsNullOrWhiteSpace(coordinate.Version)) element.Add(new XElement(ns + "version", coordinate.Version));
        if (!string.IsNullOrWhiteSpace(scope)) element.Add(new XElement(ns + "scope", scope));

        dependencies.Add(element);
        doc.Save(pomPath);
        return new[] { pomPath };
    }

    public static IReadOnlyList<string> RemoveDependency(string pomPath, string groupId, string artifactId)
    {
        var doc = XDocument.Load(pomPath, LoadOptions.PreserveWhitespace);
        var root = doc.Root ?? throw new System.Xml.XmlException("pom.xml has no root element.");
        var ns = root.GetDefaultNamespace();

        var dependencies = root.Element(ns + "dependencies");
        var match = dependencies?.Elements(ns + "dependency").FirstOrDefault(d =>
            d.Element(ns + "groupId")?.Value == groupId &&
            d.Element(ns + "artifactId")?.Value == artifactId);

        if (match is null) return Array.Empty<string>();

        match.Remove();
        doc.Save(pomPath);
        return new[] { pomPath };
    }
}

/// <summary>
/// Parses the Maven dependency plugin's default text tree serializer
/// (<c>mvn dependency:tree</c>'s documented, version-stable output shape — unchanged across
/// maven-dependency-plugin 2.x/3.x): each line after the root GAV is indented in 3-character units
/// (<c>"|  "</c> or <c>"   "</c>) before a <c>"+- "</c>/<c>"\- "</c> branch marker; depth-1 lines
/// (no indentation before the marker) are direct dependencies, deeper lines are transitive.
/// </summary>
public static class MavenDependencyTreeParser
{
    private static readonly Regex GavRegex = new(@"^(?<g>[^:\s]+):(?<a>[^:\s]+):(?<t>[^:\s]+):(?:(?<c>[^:\s]+):)?(?<v>[^:\s]+):(?<s>[^:\s]+)$", RegexOptions.Compiled);

    public static IReadOnlyList<PackageReference> Parse(string mavenOutput, string projectPath, string packageManagerId)
    {
        var references = new List<PackageReference>();

        foreach (var rawLine in mavenOutput.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var infoIndex = line.IndexOf("[INFO] ", StringComparison.Ordinal);
            if (infoIndex < 0) continue;
            var content = line[(infoIndex + "[INFO] ".Length)..];

            var markerIndex = content.IndexOfAny(new[] { '+', '\\' });
            if (markerIndex < 0 || !content.Contains('-')) continue; // root GAV line / lifecycle banner lines have no branch marker

            var prefix = content[..markerIndex];
            if (prefix.Length % 3 != 0) continue;
            var depth = (prefix.Length / 3) + 1;

            var gavStart = content.IndexOf("- ", markerIndex, StringComparison.Ordinal);
            if (gavStart < 0) continue;
            var gav = content[(gavStart + 2)..].Trim();

            var match = GavRegex.Match(gav);
            if (!match.Success) continue;

            var groupId = match.Groups["g"].Value;
            var artifactId = match.Groups["a"].Value;
            var version = match.Groups["v"].Value;
            var scope = match.Groups["s"].Value;
            var kind = depth == 1 ? PackageDependencyKind.Direct : PackageDependencyKind.Transitive;

            references.Add(new PackageReference(
                $"{groupId}:{artifactId}", version, version, kind, projectPath, packageManagerId,
                IsDevDependency: scope is "test",
                Metadata: new Dictionary<string, string> { ["scope"] = scope }));
        }

        return references;
    }
}

/// <summary>Parses <c>versions-maven-plugin</c>'s <c>display-dependency-updates</c> tabular diff
/// (documented, version-stable format: <c>groupId:artifactId ..... current -&gt; latest</c>).</summary>
public static class MavenOutdatedParser
{
    private static readonly Regex UpdateLineRegex = new(@"^(?<g>[^:\s]+):(?<a>[^:\s]+)\s+\.+\s*(?<cur>\S+)\s*->\s*(?<new>\S+)\s*$", RegexOptions.Compiled);

    public static IReadOnlyList<PackageReference> Parse(string mavenOutput, string projectPath, string packageManagerId)
    {
        var references = new List<PackageReference>();
        foreach (var rawLine in mavenOutput.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var infoIndex = line.IndexOf("[INFO] ", StringComparison.Ordinal);
            if (infoIndex < 0) continue;
            var content = line[(infoIndex + "[INFO] ".Length)..].Trim();

            var match = UpdateLineRegex.Match(content);
            if (!match.Success) continue;

            var groupId = match.Groups["g"].Value;
            var artifactId = match.Groups["a"].Value;
            var current = match.Groups["cur"].Value;
            var latest = match.Groups["new"].Value;

            references.Add(new PackageReference(
                $"{groupId}:{artifactId}", current, current, PackageDependencyKind.Direct, projectPath, packageManagerId,
                IsOutdated: true, LatestVersion: latest));
        }

        return references;
    }
}
