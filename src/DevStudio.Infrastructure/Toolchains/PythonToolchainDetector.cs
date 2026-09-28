using System.Text.RegularExpressions;
using DevStudio.Core.Processes;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Toolchains;

/// <summary>Tries <c>python</c> then <c>python3</c> (SKILL.md §11) — older Python 2 installs
/// print <c>--version</c> to stderr, which <see cref="ToolchainProbe"/> already captures
/// alongside stdout.</summary>
public sealed class PythonToolchainDetector : IToolchainDetector
{
    private static readonly Regex VersionRegex = new(@"Python\s+(\S+)", RegexOptions.Compiled);

    private readonly IProcessRunner _processRunner;

    public PythonToolchainDetector(IProcessRunner processRunner) => _processRunner = processRunner;

    public string ToolchainId => WellKnownToolchainIds.Python;

    public async Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        foreach (var executable in new[] { "python", "python3" })
        {
            var probe = await ToolchainProbe.RunAsync(_processRunner, executable, new[] { "--version" }, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (probe.Outcome == ToolchainProbeOutcome.NotFound) continue;

            if (probe.Outcome == ToolchainProbeOutcome.TimedOut)
            {
                return Result(ToolchainDetectionState.DetectionTimedOut, warning: $"'{executable} --version' timed out.");
            }

            var match = VersionRegex.Match(probe.CombinedOutput);
            if (probe.Outcome == ToolchainProbeOutcome.Success && match.Success)
            {
                return Result(ToolchainDetectionState.Detected, match.Groups[1].Value, ExecutableLocator.FindOnPath(executable));
            }

            return Result(ToolchainDetectionState.PartiallyDetected, warning: $"'{executable} --version' ran but its output could not be parsed.");
        }

        return Result(ToolchainDetectionState.NotInstalled);
    }

    private ToolchainInfo Result(ToolchainDetectionState state, string? version = null, string? executablePath = null, string? warning = null) => new(
        ToolchainId,
        "Python",
        state,
        Version: version,
        ExecutablePath: executablePath,
        Capabilities: state == ToolchainDetectionState.Detected
            ? new[] { ToolchainCapability.Detect, ToolchainCapability.Run, ToolchainCapability.Test, ToolchainCapability.Package, ToolchainCapability.Format }
            : Array.Empty<ToolchainCapability>(),
        DetectionWarning: warning);
}
