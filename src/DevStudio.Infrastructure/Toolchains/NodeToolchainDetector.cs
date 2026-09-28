using DevStudio.Core.Processes;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Toolchains;

public sealed class NodeToolchainDetector : IToolchainDetector
{
    private readonly IProcessRunner _processRunner;

    public NodeToolchainDetector(IProcessRunner processRunner) => _processRunner = processRunner;

    public string ToolchainId => WellKnownToolchainIds.Node;

    public async Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        var probe = await ToolchainProbe.RunAsync(_processRunner, "node", new[] { "--version" }, cancellationToken: cancellationToken).ConfigureAwait(false);

        return probe.Outcome switch
        {
            ToolchainProbeOutcome.NotFound => Result(ToolchainDetectionState.NotInstalled),
            ToolchainProbeOutcome.TimedOut => Result(ToolchainDetectionState.DetectionTimedOut, warning: "'node --version' timed out."),
            ToolchainProbeOutcome.Failed => Result(ToolchainDetectionState.DetectionFailed, warning: "'node --version' exited with a non-zero code."),
            _ => Result(ToolchainDetectionState.Detected, probe.CombinedOutput.Trim().TrimStart('v'), ExecutableLocator.FindOnPath("node")),
        };
    }

    private ToolchainInfo Result(ToolchainDetectionState state, string? version = null, string? executablePath = null, string? warning = null) => new(
        ToolchainId,
        "Node.js",
        state,
        Version: version,
        ExecutablePath: executablePath,
        Capabilities: state == ToolchainDetectionState.Detected
            ? new[] { ToolchainCapability.Detect, ToolchainCapability.Run, ToolchainCapability.Test, ToolchainCapability.Build, ToolchainCapability.Format }
            : Array.Empty<ToolchainCapability>(),
        DetectionWarning: warning);
}
