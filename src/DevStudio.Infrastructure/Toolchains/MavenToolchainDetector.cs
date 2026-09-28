using DevStudio.Core.Processes;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Toolchains;

/// <summary>Detects Apache Maven (Phase 14 P1-A) independently of <see cref="JavaToolchainDetector"/>
/// — a JDK being present never implies <c>mvn</c> is on PATH, exactly like npm's independence from
/// Node (see <see cref="NodePackageManagerToolchainDetector"/>). On Windows, Maven ships only as
/// <c>mvn.cmd</c> (no <c>mvn.exe</c>), so this resolves through <see cref="ExecutableLocator"/>
/// first for the same reason the real Phase 13 npm detection fix did.</summary>
public sealed class MavenToolchainDetector : IToolchainDetector
{
    private readonly IProcessRunner _processRunner;

    public MavenToolchainDetector(IProcessRunner processRunner) => _processRunner = processRunner;

    public string ToolchainId => WellKnownToolchainIds.Maven;

    public async Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        var resolvedPath = ExecutableLocator.FindOnPath("mvn");
        var probe = await ToolchainProbe.RunAsync(_processRunner, resolvedPath ?? "mvn", new[] { "--version" }, cancellationToken: cancellationToken).ConfigureAwait(false);

        return probe.Outcome switch
        {
            ToolchainProbeOutcome.NotFound => Result(ToolchainDetectionState.NotInstalled),
            ToolchainProbeOutcome.TimedOut => Result(ToolchainDetectionState.DetectionTimedOut, warning: "'mvn --version' timed out."),
            ToolchainProbeOutcome.Failed => Result(ToolchainDetectionState.DetectionFailed, warning: "'mvn --version' exited with a non-zero code."),
            _ => Result(ToolchainDetectionState.Detected, ParseVersion(probe.CombinedOutput), resolvedPath),
        };
    }

    private static string? ParseVersion(string output)
    {
        // First line looks like: "Apache Maven 3.9.6 (...)"
        var firstLine = output.Split('\n').FirstOrDefault()?.Trim();
        return string.IsNullOrWhiteSpace(firstLine) ? null : firstLine;
    }

    private ToolchainInfo Result(ToolchainDetectionState state, string? version = null, string? executablePath = null, string? warning = null) => new(
        ToolchainId,
        "Maven",
        state,
        Version: version,
        ExecutablePath: executablePath,
        Capabilities: state == ToolchainDetectionState.Detected
            ? new[] { ToolchainCapability.Detect, ToolchainCapability.Package, ToolchainCapability.Restore, ToolchainCapability.Build }
            : Array.Empty<ToolchainCapability>(),
        DetectionWarning: warning);
}
