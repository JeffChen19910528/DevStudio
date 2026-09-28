using System.Text.RegularExpressions;
using DevStudio.Core.Processes;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Toolchains;

/// <summary>Go's version flag is <c>version</c>, not <c>--version</c> (SKILL.md §17).</summary>
public sealed class GoToolchainDetector : IToolchainDetector
{
    private static readonly Regex VersionRegex = new(@"go(\d+\.\d+(\.\d+)?)", RegexOptions.Compiled);

    private readonly IProcessRunner _processRunner;

    public GoToolchainDetector(IProcessRunner processRunner) => _processRunner = processRunner;

    public string ToolchainId => WellKnownToolchainIds.Go;

    public async Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        var probe = await ToolchainProbe.RunAsync(_processRunner, "go", new[] { "version" }, cancellationToken: cancellationToken).ConfigureAwait(false);

        return probe.Outcome switch
        {
            ToolchainProbeOutcome.NotFound => Result(ToolchainDetectionState.NotInstalled),
            ToolchainProbeOutcome.TimedOut => Result(ToolchainDetectionState.DetectionTimedOut, warning: "'go version' timed out."),
            ToolchainProbeOutcome.Failed => Result(ToolchainDetectionState.DetectionFailed, warning: "'go version' exited with a non-zero code."),
            _ => Result(ToolchainDetectionState.Detected, ParseVersion(probe.CombinedOutput), ExecutableLocator.FindOnPath("go")),
        };
    }

    private static string? ParseVersion(string output)
    {
        var match = VersionRegex.Match(output);
        return match.Success ? match.Groups[1].Value : null;
    }

    private ToolchainInfo Result(ToolchainDetectionState state, string? version = null, string? executablePath = null, string? warning = null) => new(
        ToolchainId,
        "Go",
        state,
        Version: version,
        ExecutablePath: executablePath,
        Capabilities: state == ToolchainDetectionState.Detected
            ? new[] { ToolchainCapability.Detect, ToolchainCapability.Build, ToolchainCapability.Run, ToolchainCapability.Test, ToolchainCapability.Format }
            : Array.Empty<ToolchainCapability>(),
        DetectionWarning: warning);
}
