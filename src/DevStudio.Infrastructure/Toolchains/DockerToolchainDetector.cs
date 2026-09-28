using DevStudio.Core.Processes;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Toolchains;

public sealed class DockerToolchainDetector : IToolchainDetector
{
    private readonly IProcessRunner _processRunner;

    public DockerToolchainDetector(IProcessRunner processRunner) => _processRunner = processRunner;

    public string ToolchainId => WellKnownToolchainIds.Docker;

    public async Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        var probe = await ToolchainProbe.RunAsync(_processRunner, "docker", new[] { "--version" }, cancellationToken: cancellationToken).ConfigureAwait(false);

        return probe.Outcome switch
        {
            ToolchainProbeOutcome.NotFound => Result(ToolchainDetectionState.NotInstalled),
            ToolchainProbeOutcome.TimedOut => Result(ToolchainDetectionState.DetectionTimedOut, warning: "'docker --version' timed out."),
            ToolchainProbeOutcome.Failed => Result(ToolchainDetectionState.DetectionFailed, warning: "'docker --version' exited with a non-zero code."),
            _ => Result(ToolchainDetectionState.Detected, probe.CombinedOutput.Trim(), ExecutableLocator.FindOnPath("docker")),
        };
    }

    private ToolchainInfo Result(ToolchainDetectionState state, string? version = null, string? executablePath = null, string? warning = null) => new(
        ToolchainId,
        "Docker",
        state,
        Version: version,
        ExecutablePath: executablePath,
        Capabilities: state == ToolchainDetectionState.Detected ? new[] { ToolchainCapability.Detect } : Array.Empty<ToolchainCapability>(),
        DetectionWarning: warning);
}
