using DevStudio.Core.Processes;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Toolchains;

public sealed class CMakeToolchainDetector : IToolchainDetector
{
    private readonly IProcessRunner _processRunner;

    public CMakeToolchainDetector(IProcessRunner processRunner) => _processRunner = processRunner;

    public string ToolchainId => WellKnownToolchainIds.CMake;

    public async Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        var probe = await ToolchainProbe.RunAsync(_processRunner, "cmake", new[] { "--version" }, cancellationToken: cancellationToken).ConfigureAwait(false);

        return probe.Outcome switch
        {
            ToolchainProbeOutcome.NotFound => Result(ToolchainDetectionState.NotInstalled),
            ToolchainProbeOutcome.TimedOut => Result(ToolchainDetectionState.DetectionTimedOut, warning: "'cmake --version' timed out."),
            ToolchainProbeOutcome.Failed => Result(ToolchainDetectionState.DetectionFailed, warning: "'cmake --version' exited with a non-zero code."),
            _ => Result(ToolchainDetectionState.Detected, ParseVersion(probe.CombinedOutput), ExecutableLocator.FindOnPath("cmake")),
        };
    }

    private static string? ParseVersion(string output) => output.Split('\n').FirstOrDefault()?.Replace("cmake version", "").Trim();

    private ToolchainInfo Result(ToolchainDetectionState state, string? version = null, string? executablePath = null, string? warning = null) => new(
        ToolchainId,
        "CMake",
        state,
        Version: version,
        ExecutablePath: executablePath,
        Capabilities: state == ToolchainDetectionState.Detected
            ? new[] { ToolchainCapability.Detect, ToolchainCapability.Build }
            : Array.Empty<ToolchainCapability>(),
        DetectionWarning: warning);
}
