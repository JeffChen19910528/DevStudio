using DevStudio.Core.Processes;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Toolchains;

/// <summary>Detects Conan (Phase 14 P1-C). Conan is typically installed via pip and, on Windows,
/// resolves to a <c>conan.exe</c> console-script shim (not a bare <c>conan</c> name) — resolved
/// through <see cref="ExecutableLocator"/> first for the same reason
/// <see cref="MavenToolchainDetector"/>/the real Phase 13 npm fix does.</summary>
public sealed class ConanToolchainDetector : IToolchainDetector
{
    private readonly IProcessRunner _processRunner;

    public ConanToolchainDetector(IProcessRunner processRunner) => _processRunner = processRunner;

    public string ToolchainId => WellKnownToolchainIds.Conan;

    public async Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        var resolvedPath = ExecutableLocator.FindOnPath("conan");
        var probe = await ToolchainProbe.RunAsync(_processRunner, resolvedPath ?? "conan", new[] { "--version" }, cancellationToken: cancellationToken).ConfigureAwait(false);

        return probe.Outcome switch
        {
            ToolchainProbeOutcome.NotFound => Result(ToolchainDetectionState.NotInstalled),
            ToolchainProbeOutcome.TimedOut => Result(ToolchainDetectionState.DetectionTimedOut, warning: "'conan --version' timed out."),
            ToolchainProbeOutcome.Failed => Result(ToolchainDetectionState.DetectionFailed, warning: "'conan --version' exited with a non-zero code."),
            _ => Result(ToolchainDetectionState.Detected, ParseVersion(probe.CombinedOutput), resolvedPath),
        };
    }

    private static string? ParseVersion(string output)
    {
        // First line looks like: "Conan version 2.9.2"
        var firstLine = output.Split('\n').FirstOrDefault()?.Trim();
        return string.IsNullOrWhiteSpace(firstLine) ? null : firstLine;
    }

    private ToolchainInfo Result(ToolchainDetectionState state, string? version = null, string? executablePath = null, string? warning = null) => new(
        ToolchainId,
        "Conan",
        state,
        Version: version,
        ExecutablePath: executablePath,
        Capabilities: state == ToolchainDetectionState.Detected
            ? new[] { ToolchainCapability.Detect, ToolchainCapability.Package, ToolchainCapability.Restore }
            : Array.Empty<ToolchainCapability>(),
        DetectionWarning: warning);
}
