using System.Text.RegularExpressions;
using DevStudio.Core.Processes;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Toolchains;

/// <summary>Distinguishes a JDK (has <c>javac</c>) from a JRE (only <c>java</c>) (SKILL.md §13)
/// and reads <c>JAVA_HOME</c> if set — an environment-variable read, not a command execution.</summary>
public sealed class JavaToolchainDetector : IToolchainDetector
{
    private static readonly Regex VersionRegex = new("\"([^\"]+)\"", RegexOptions.Compiled);

    private readonly IProcessRunner _processRunner;

    public JavaToolchainDetector(IProcessRunner processRunner) => _processRunner = processRunner;

    public string ToolchainId => WellKnownToolchainIds.Java;

    public async Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        var javaProbe = await ToolchainProbe.RunAsync(_processRunner, "java", new[] { "--version" }, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (javaProbe.Outcome == ToolchainProbeOutcome.NotFound)
        {
            return Result(ToolchainDetectionState.NotInstalled);
        }

        if (javaProbe.Outcome == ToolchainProbeOutcome.TimedOut)
        {
            return Result(ToolchainDetectionState.DetectionTimedOut, warning: "'java --version' timed out.");
        }

        if (javaProbe.Outcome != ToolchainProbeOutcome.Success)
        {
            return Result(ToolchainDetectionState.DetectionFailed, warning: "'java --version' exited with a non-zero code.");
        }

        var version = ParseVersion(javaProbe.CombinedOutput);
        var javacProbe = await ToolchainProbe.RunAsync(_processRunner, "javac", new[] { "-version" }, cancellationToken: cancellationToken).ConfigureAwait(false);
        var isJdk = javacProbe.Outcome == ToolchainProbeOutcome.Success;

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        var metadata = javaHome is null ? null : new Dictionary<string, string> { ["JAVA_HOME"] = javaHome };

        return new ToolchainInfo(
            ToolchainId,
            "Java",
            ToolchainDetectionState.Detected,
            Version: version,
            Vendor: isJdk ? "JDK" : "JRE",
            ExecutablePath: ExecutableLocator.FindOnPath("java"),
            InstallationRoot: javaHome,
            Capabilities: isJdk
                ? new[] { ToolchainCapability.Detect, ToolchainCapability.Build, ToolchainCapability.Run, ToolchainCapability.Test, ToolchainCapability.Package }
                : new[] { ToolchainCapability.Detect, ToolchainCapability.Run },
            Metadata: metadata);
    }

    private static string? ParseVersion(string output)
    {
        var match = VersionRegex.Match(output);
        if (match.Success) return match.Groups[1].Value;

        var firstLine = output.Split('\n').FirstOrDefault()?.Trim();
        return string.IsNullOrWhiteSpace(firstLine) ? null : firstLine;
    }

    private ToolchainInfo Result(ToolchainDetectionState state, string? warning = null) => new(
        ToolchainId, "Java", state, Capabilities: Array.Empty<ToolchainCapability>(), DetectionWarning: warning);
}
