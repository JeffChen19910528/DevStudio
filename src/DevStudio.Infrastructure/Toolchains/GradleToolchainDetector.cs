using DevStudio.Core.Processes;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Toolchains;

/// <summary>Detects a globally-installed Gradle (Phase 14 P1-A). Deliberately does not attempt to
/// resolve a project's own Gradle Wrapper (<c>gradlew</c>/<c>gradlew.bat</c>) here — wrapper
/// resolution is project-relative, not a machine-wide toolchain fact, and belongs in
/// <see cref="Infrastructure.Packages.GradlePackageAdapter"/> if/when it is added, not this
/// detector (mirrors <see cref="MavenToolchainDetector"/>'s scope).</summary>
public sealed class GradleToolchainDetector : IToolchainDetector
{
    private readonly IProcessRunner _processRunner;

    public GradleToolchainDetector(IProcessRunner processRunner) => _processRunner = processRunner;

    public string ToolchainId => WellKnownToolchainIds.Gradle;

    public async Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        var resolvedPath = ExecutableLocator.FindOnPath("gradle");
        var probe = await ToolchainProbe.RunAsync(_processRunner, resolvedPath ?? "gradle", new[] { "--version" }, cancellationToken: cancellationToken).ConfigureAwait(false);

        return probe.Outcome switch
        {
            ToolchainProbeOutcome.NotFound => Result(ToolchainDetectionState.NotInstalled),
            ToolchainProbeOutcome.TimedOut => Result(ToolchainDetectionState.DetectionTimedOut, warning: "'gradle --version' timed out."),
            ToolchainProbeOutcome.Failed => Result(ToolchainDetectionState.DetectionFailed, warning: "'gradle --version' exited with a non-zero code."),
            _ => Result(ToolchainDetectionState.Detected, ParseVersion(probe.CombinedOutput), resolvedPath),
        };
    }

    private static string? ParseVersion(string output)
    {
        // Output contains a line like: "Gradle 8.10.2"
        var line = output.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("Gradle ", StringComparison.Ordinal));
        return line?.Trim();
    }

    private ToolchainInfo Result(ToolchainDetectionState state, string? version = null, string? executablePath = null, string? warning = null) => new(
        ToolchainId,
        "Gradle",
        state,
        Version: version,
        ExecutablePath: executablePath,
        Capabilities: state == ToolchainDetectionState.Detected
            ? new[] { ToolchainCapability.Detect, ToolchainCapability.Package, ToolchainCapability.Restore, ToolchainCapability.Build }
            : Array.Empty<ToolchainCapability>(),
        DetectionWarning: warning);
}
