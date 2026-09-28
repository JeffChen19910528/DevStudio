using DevStudio.Core.Processes;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Toolchains;

public sealed class DotNetToolchainDetector : IToolchainDetector
{
    private readonly IProcessRunner _processRunner;

    public DotNetToolchainDetector(IProcessRunner processRunner) => _processRunner = processRunner;

    public string ToolchainId => WellKnownToolchainIds.DotNet;

    public async Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        var probe = await ToolchainProbe.RunAsync(_processRunner, "dotnet", new[] { "--version" }, cancellationToken: cancellationToken).ConfigureAwait(false);

        return probe.Outcome switch
        {
            ToolchainProbeOutcome.NotFound => Result(ToolchainDetectionState.NotInstalled),
            ToolchainProbeOutcome.TimedOut => Result(ToolchainDetectionState.DetectionTimedOut, warning: "'dotnet --version' timed out."),
            ToolchainProbeOutcome.Failed => Result(ToolchainDetectionState.DetectionFailed, warning: $"'dotnet --version' exited with a non-zero code."),
            _ => Result(ToolchainDetectionState.Detected, probe.CombinedOutput.Trim(), executablePath: ExecutableLocator.FindOnPath("dotnet")),
        };
    }

    private ToolchainInfo Result(ToolchainDetectionState state, string? version = null, string? warning = null, string? executablePath = null) => new(
        ToolchainId,
        "dotnet",
        state,
        Version: version,
        Vendor: "Microsoft",
        ExecutablePath: executablePath,
        Capabilities: state == ToolchainDetectionState.Detected
            ? new[] { ToolchainCapability.Detect, ToolchainCapability.Restore, ToolchainCapability.Build, ToolchainCapability.Run, ToolchainCapability.Test, ToolchainCapability.Publish, ToolchainCapability.Package, ToolchainCapability.Format }
            : Array.Empty<ToolchainCapability>(),
        DetectionWarning: warning);
}
