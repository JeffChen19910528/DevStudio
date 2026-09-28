using DevStudio.Core.Processes;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Toolchains;

/// <summary>Detects gcc independently of g++ (SKILL.md §15) — g++'s path is recorded in <see
/// cref="ToolchainInfo.Metadata"/> when present, but its absence doesn't change gcc's own state.</summary>
public sealed class GccToolchainDetector : IToolchainDetector
{
    private readonly IProcessRunner _processRunner;

    public GccToolchainDetector(IProcessRunner processRunner) => _processRunner = processRunner;

    public string ToolchainId => WellKnownToolchainIds.Gcc;

    public async Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        var probe = await ToolchainProbe.RunAsync(_processRunner, "gcc", new[] { "--version" }, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (probe.Outcome == ToolchainProbeOutcome.NotFound) return Result(ToolchainDetectionState.NotInstalled);
        if (probe.Outcome == ToolchainProbeOutcome.TimedOut) return Result(ToolchainDetectionState.DetectionTimedOut, warning: "'gcc --version' timed out.");
        if (probe.Outcome != ToolchainProbeOutcome.Success) return Result(ToolchainDetectionState.DetectionFailed, warning: "'gcc --version' exited with a non-zero code.");

        var gxxPath = ExecutableLocator.FindOnPath("g++");
        var metadata = gxxPath is null ? null : new Dictionary<string, string> { ["g++Path"] = gxxPath };

        return new ToolchainInfo(
            ToolchainId,
            "GCC",
            ToolchainDetectionState.Detected,
            Version: probe.CombinedOutput.Split('\n').FirstOrDefault()?.Trim(),
            ExecutablePath: ExecutableLocator.FindOnPath("gcc"),
            Capabilities: new[] { ToolchainCapability.Detect, ToolchainCapability.Build },
            Metadata: metadata);
    }

    private ToolchainInfo Result(ToolchainDetectionState state, string? warning = null) => new(
        ToolchainId, "GCC", state, Capabilities: Array.Empty<ToolchainCapability>(), DetectionWarning: warning);
}
