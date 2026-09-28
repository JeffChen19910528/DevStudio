using System.Text.RegularExpressions;
using DevStudio.Core.Packages;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Packages;

/// <summary>
/// Drives real Gradle (Phase 14 P1-A) — <see cref="IPackageInspector"/> ONLY. Unlike Maven's
/// pom.xml (a fixed, predictable XML schema DevStudio can safely edit structurally), a Gradle
/// build script is an executable Groovy or Kotlin program: the same dependency can be declared via
/// string notation (<c>implementation 'group:name:version'</c> /
/// <c>implementation("group:name:version")</c>), map notation
/// (<c>implementation group: 'g', name: 'n', version: 'v'</c>), a version catalog reference
/// (<c>implementation(libs.foo)</c>), or a version interpolated from a Gradle property/variable —
/// none of which a static text edit can rewrite correctly in the general case without risking
/// silently corrupting a working build (SKILL.md §10's own explicit caution: "do not perform
/// unsafe regex replacements across arbitrary Gradle scripts"). Rather than implement a fragile
/// Add/Remove/Update that only handles the simplest string-notation case and silently mishandles
/// every other real script, this adapter deliberately implements NEITHER this phase — see
/// ADR-015's Known Limitations. <c>mvn</c>/<c>gradle</c> real invocation was not possible on the
/// machine this was written on either (neither is installed) — every parser here is unit-tested
/// against realistic fixture text only; always report Gradle as "implemented but not
/// real-environment validated," never "real-tested."
/// </summary>
public sealed class GradlePackageAdapter : IPackageManagerAdapter, IPackageInspector
{
    public string Id => WellKnownPackageManagerIds.Gradle;
    public string DisplayName => "Gradle";

    private readonly IProcessRunner _processRunner;
    private readonly IToolchainRegistry _toolchainRegistry;

    public GradlePackageAdapter(IProcessRunner processRunner, IToolchainRegistry toolchainRegistry)
    {
        _processRunner = processRunner;
        _toolchainRegistry = toolchainRegistry;
    }

    public PackageProject? DetectProject(ProjectInfo project)
    {
        if (project.ProjectType != ProjectType.Java) return null;

        var buildFile = FindBuildFile(project.RootPath);
        if (buildFile is null) return null;

        var gradle = _toolchainRegistry.Get(WellKnownToolchainIds.Gradle);
        if (gradle is null || !gradle.IsUsable)
        {
            return new PackageProject(buildFile, project.ProjectType, Id, DisplayName, PackageManagerCapabilities.None,
                "Gradle is not installed or has not been detected.");
        }

        var capabilities = new PackageManagerCapabilities(
            ListInstalled: true,
            ListDependencies: true,
            Search: false,
            Add: false, // Deliberately unsupported this phase — see the type-level remarks above.
            Remove: false,
            Update: false,
            Restore: false,
            ListOutdated: false, // No safe, dependency-free equivalent of `mvn versions:display-dependency-updates` this phase (the `com.github.ben-manes.versions` plugin is third-party and not guaranteed present).
            ManageSources: false,
            LockfileSupport: false, // Only true when the project opts into Gradle's dependency-locking feature; not detected this phase.
            TransitiveDependencySupport: true,
            PrereleaseSupport: false);

        return new PackageProject(buildFile, project.ProjectType, Id, DisplayName, capabilities);
    }

    public async Task<IReadOnlyList<PackageReference>> ListInstalledAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var gradle = _toolchainRegistry.Get(WellKnownToolchainIds.Gradle)?.ExecutablePath ?? "gradle";
        var rootPath = Path.GetDirectoryName(project.ProjectPath) ?? Environment.CurrentDirectory;
        var request = new ProcessStartRequest(gradle, new[] { "dependencies", "--console=plain" }, rootPath);
        var result = await _processRunner.RunAsync(request, outputSink, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) return Array.Empty<PackageReference>();

        return GradleDependenciesParser.Parse(result.StandardOutput, project.ProjectPath, project.PackageManagerId);
    }

    public Task<IReadOnlyList<PackageDependency>> ListDependenciesAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        // Best-effort declared-dependency read straight from the build script text — string
        // notation only (see type remarks). Never used to drive a mutation; read-only inspection
        // is explicitly allowed without Workspace Trust (SKILL.md §12).
        if (!File.Exists(project.ProjectPath)) return Task.FromResult<IReadOnlyList<PackageDependency>>(Array.Empty<PackageDependency>());

        var text = File.ReadAllText(project.ProjectPath);
        IReadOnlyList<PackageDependency> dependencies = GradleBuildScriptParser.ParseStringNotationDependencies(text)
            .Select(d => new PackageDependency($"{d.Group}:{d.Name}", d.Version, PackageDependencyKind.Direct, null))
            .ToList();

        return Task.FromResult(dependencies);
    }

    public Task<IReadOnlyList<PackageReference>> ListOutdatedAsync(PackageProject project, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PackageReference>>(Array.Empty<PackageReference>());

    private static string? FindBuildFile(string rootPath)
    {
        foreach (var name in new[] { "build.gradle.kts", "build.gradle" })
        {
            var candidate = Path.Combine(rootPath, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}

/// <summary>
/// Parses <c>gradle dependencies</c>'s documented, version-stable ASCII-tree output (one section
/// per configuration, e.g. <c>compileClasspath</c>): depth-1 entries use a bare
/// <c>"+--- "</c>/<c>"\--- "</c> marker, deeper entries are preceded by <c>"|    "</c>/<c>"     "</c>
/// indentation units (5 characters wide, unlike Maven's 3 — Gradle's own tree renderer convention).
/// </summary>
public static class GradleDependenciesParser
{
    // <prefix> is zero or more 5-character indentation units ("|    " for a continued branch,
    // "     " for a closed one) before the depth-1 marker itself ("+--- " / "\--- ", also 5
    // characters, entirely excluded from <prefix> — unlike a naive IndexOf("--- "), which would
    // wrongly count the marker's own leading '+'/'\' glyph as part of the indentation.
    private static readonly Regex LineRegex = new(@"^(?<prefix>(?:\|    |     )*)(?:\+---|\\---) (?<rest>.+)$", RegexOptions.Compiled);
    private static readonly Regex CoordinateRegex = new(@"^(?<g>[^:\s]+):(?<a>[^:\s]+):(?<v>[^:\s(]+)", RegexOptions.Compiled);

    public static IReadOnlyList<PackageReference> Parse(string gradleOutput, string projectPath, string packageManagerId)
    {
        var references = new List<PackageReference>();

        foreach (var rawLine in gradleOutput.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var lineMatch = LineRegex.Match(line);
            if (!lineMatch.Success) continue;

            var depth = (lineMatch.Groups["prefix"].Value.Length / 5) + 1;
            var coordinateText = lineMatch.Groups["rest"].Value.Trim();
            var match = CoordinateRegex.Match(coordinateText);
            if (!match.Success) continue;

            var group = match.Groups["g"].Value;
            var name = match.Groups["a"].Value;
            // "-> x.y.z" (resolution override) wins over the requested version when present.
            var resolvedMatch = Regex.Match(coordinateText, @"->\s*(\S+)");
            var version = resolvedMatch.Success ? resolvedMatch.Groups[1].Value.TrimEnd(')') : match.Groups["v"].Value;
            var kind = depth == 1 ? PackageDependencyKind.Direct : PackageDependencyKind.Transitive;

            references.Add(new PackageReference($"{group}:{name}", version, version, kind, projectPath, packageManagerId));
        }

        return references;
    }
}

/// <summary>Best-effort static parse of Gradle string-notation dependency declarations — the one
/// declaration shape that is unambiguous without executing the build script. See
/// <see cref="GradlePackageAdapter"/>'s remarks for why map notation, version catalogs, and
/// variable-interpolated versions are intentionally not handled.</summary>
public static class GradleBuildScriptParser
{
    private static readonly Regex StringNotationRegex = new(
        "(?:implementation|api|compileOnly|runtimeOnly|testImplementation|testRuntimeOnly)\\s*[(]?\\s*['\"](?<g>[^:'\"]+):(?<a>[^:'\"]+):(?<v>[^:'\"]+)['\"]",
        RegexOptions.Compiled);

    public static IReadOnlyList<(string Group, string Name, string Version)> ParseStringNotationDependencies(string buildScriptText)
    {
        var results = new List<(string, string, string)>();
        foreach (Match match in StringNotationRegex.Matches(buildScriptText))
        {
            results.Add((match.Groups["g"].Value, match.Groups["a"].Value, match.Groups["v"].Value));
        }
        return results;
    }
}
