using DevStudio.Core.Processes;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Toolchains;

public sealed class ClangToolchainDetector : IToolchainDetector
{
    private readonly IProcessRunner _processRunner;

    public ClangToolchainDetector(IProcessRunner processRunner) => _processRunner = processRunner;

    public string ToolchainId => WellKnownToolchainIds.Clang;

    public async Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        var probe = await ToolchainProbe.RunAsync(_processRunner, "clang", new[] { "--version" }, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (probe.Outcome == ToolchainProbeOutcome.NotFound) return Result(ToolchainDetectionState.NotInstalled);
        if (probe.Outcome == ToolchainProbeOutcome.TimedOut) return Result(ToolchainDetectionState.DetectionTimedOut, warning: "'clang --version' timed out.");
        if (probe.Outcome != ToolchainProbeOutcome.Success) return Result(ToolchainDetectionState.DetectionFailed, warning: "'clang --version' exited with a non-zero code.");

        var clangxxPath = ExecutableLocator.FindOnPath("clang++");
        var metadata = clangxxPath is null ? null : new Dictionary<string, string> { ["clang++Path"] = clangxxPath };

        return new ToolchainInfo(
            ToolchainId,
            "Clang",
            ToolchainDetectionState.Detected,
            Version: probe.CombinedOutput.Split('\n').FirstOrDefault()?.Trim(),
            ExecutablePath: ExecutableLocator.FindOnPath("clang"),
            Capabilities: new[] { ToolchainCapability.Detect, ToolchainCapability.Build },
            Metadata: metadata);
    }

    private ToolchainInfo Result(ToolchainDetectionState state, string? warning = null) => new(
        ToolchainId, "Clang", state, Capabilities: Array.Empty<ToolchainCapability>(), DetectionWarning: warning);
}
