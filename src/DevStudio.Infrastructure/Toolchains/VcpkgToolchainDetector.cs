using DevStudio.Core.Processes;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Toolchains;

/// <summary>Detects vcpkg (Phase 14 P1-C). vcpkg ships as <c>vcpkg.exe</c> on Windows and a native
/// <c>vcpkg</c> binary on Linux/macOS (both resolved by name through <see cref="ExecutableLocator"/>
/// — no OS-specific extension assumption in this class, matching every other toolchain
/// detector).</summary>
public sealed class VcpkgToolchainDetector : IToolchainDetector
{
    private readonly IProcessRunner _processRunner;

    public VcpkgToolchainDetector(IProcessRunner processRunner) => _processRunner = processRunner;

    public string ToolchainId => WellKnownToolchainIds.Vcpkg;

    public async Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        var resolvedPath = ExecutableLocator.FindOnPath("vcpkg");
        var probe = await ToolchainProbe.RunAsync(_processRunner, resolvedPath ?? "vcpkg", new[] { "version" }, cancellationToken: cancellationToken).ConfigureAwait(false);

        return probe.Outcome switch
        {
            ToolchainProbeOutcome.NotFound => Result(ToolchainDetectionState.NotInstalled),
            ToolchainProbeOutcome.TimedOut => Result(ToolchainDetectionState.DetectionTimedOut, warning: "'vcpkg version' timed out."),
            ToolchainProbeOutcome.Failed => Result(ToolchainDetectionState.DetectionFailed, warning: "'vcpkg version' exited with a non-zero code."),
            _ => Result(ToolchainDetectionState.Detected, ParseVersion(probe.CombinedOutput), resolvedPath),
        };
    }

    private static string? ParseVersion(string output)
    {
        // First line looks like: "vcpkg package management program version 2024.01.12"
        var firstLine = output.Split('\n').FirstOrDefault()?.Trim();
        return string.IsNullOrWhiteSpace(firstLine) ? null : firstLine;
    }

    private ToolchainInfo Result(ToolchainDetectionState state, string? version = null, string? executablePath = null, string? warning = null) => new(
        ToolchainId,
        "vcpkg",
        state,
        Version: version,
        ExecutablePath: executablePath,
        Capabilities: state == ToolchainDetectionState.Detected
            ? new[] { ToolchainCapability.Detect, ToolchainCapability.Package, ToolchainCapability.Restore }
            : Array.Empty<ToolchainCapability>(),
        DetectionWarning: warning);
}
