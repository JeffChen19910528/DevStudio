using System.Text.Json;
using DevStudio.Core.Processes;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Toolchains;

/// <summary>
/// Discovers Visual Studio installations through <c>vswhere.exe</c> (SKILL.md §8) — the
/// Microsoft-supported mechanism for this, rather than assuming <c>cl.exe</c>/MSBuild are on
/// PATH (they usually aren't; see ADR-001). Multiple instances are returned independently
/// (SKILL.md §9): callers decide which one to use, this detector never picks for them. Never
/// executes a build; only reads <c>vswhere</c>'s own JSON output and checks for well-known file
/// paths under each installation.
/// </summary>
public sealed class VisualStudioDetector : IVisualStudioDetector
{
    private readonly IProcessRunner _processRunner;
    private readonly string? _vswherePath;

    public VisualStudioDetector(IProcessRunner processRunner, string? vswherePathOverride = null)
    {
        _processRunner = processRunner;
        _vswherePath = vswherePathOverride ?? DefaultVswherePath();
    }

    private static string? DefaultVswherePath()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var candidate = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    public async Task<IReadOnlyList<VisualStudioInstance>> DetectAllAsync(CancellationToken cancellationToken = default)
    {
        if (_vswherePath is null) return Array.Empty<VisualStudioInstance>();

        var probe = await ToolchainProbe.RunAsync(
            _processRunner,
            _vswherePath,
            new[] { "-all", "-products", "*", "-format", "json" },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (probe.Outcome != ToolchainProbeOutcome.Success) return Array.Empty<VisualStudioInstance>();

        try
        {
            return ParseInstances(probe.CombinedOutput);
        }
        catch (JsonException)
        {
            // Malformed vswhere output (SKILL.md §28, §31): degrade to "nothing found" rather
            // than throwing — a future release changing vswhere's schema must not crash DevStudio.
            return Array.Empty<VisualStudioInstance>();
        }
    }

    private static IReadOnlyList<VisualStudioInstance> ParseInstances(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<VisualStudioInstance>();

        var instances = new List<VisualStudioInstance>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            var instanceId = GetString(element, "instanceId") ?? Guid.NewGuid().ToString("N");
            var installationPath = GetString(element, "installationPath");
            if (installationPath is null) continue;

            var version = GetString(element, "installationVersion") ?? "unknown";
            var displayName = GetString(element, "displayName") ?? "Visual Studio";
            var productId = GetString(element, "productId") ?? string.Empty;
            var edition = productId.Contains('.') ? productId[(productId.LastIndexOf('.') + 1)..] : "Unknown";

            var isComplete = GetBool(element, "isComplete") ?? true;
            var isLaunchable = GetBool(element, "isLaunchable") ?? true;
            var state = isComplete && isLaunchable ? ToolchainDetectionState.Detected : ToolchainDetectionState.PartiallyDetected;

            var msBuildPath = FindMsBuildPath(installationPath);
            var msvcToolsetPath = FindMsvcToolsetPath(installationPath);
            var windowsSdkPath = FindWindowsSdkPath();

            instances.Add(new VisualStudioInstance(
                instanceId,
                displayName,
                edition,
                version,
                installationPath,
                state,
                msBuildPath,
                msvcToolsetPath,
                windowsSdkPath,
                state == ToolchainDetectionState.PartiallyDetected ? "Installation reported as incomplete or not launchable by vswhere." : null));
        }

        return instances;
    }

    private static string? FindMsBuildPath(string installationPath)
    {
        var candidate = Path.Combine(installationPath, "MSBuild", "Current", "Bin", "MSBuild.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? FindMsvcToolsetPath(string installationPath)
    {
        var toolsRoot = Path.Combine(installationPath, "VC", "Tools", "MSVC");
        if (!Directory.Exists(toolsRoot)) return null;

        return Directory.GetDirectories(toolsRoot).OrderDescending(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    private static string? FindWindowsSdkPath()
    {
        const string conventionalPath = @"C:\Program Files (x86)\Windows Kits\10";
        return Directory.Exists(conventionalPath) ? conventionalPath : null;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool? GetBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt32(out var i) => i != 0,
            _ => null,
        };
    }
}
