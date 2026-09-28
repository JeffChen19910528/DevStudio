using DevStudio.Core.Processes;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Toolchains;

/// <summary>Shared shape for npm/pnpm/yarn (SKILL.md §12) — each is captured independently since
/// a Node project may use any of them, and none of their presence implies the others.</summary>
public sealed class NodePackageManagerToolchainDetector : IToolchainDetector
{
    private readonly IProcessRunner _processRunner;
    private readonly string _executableName;

    public NodePackageManagerToolchainDetector(IProcessRunner processRunner, string toolchainId, string executableName)
    {
        _processRunner = processRunner;
        ToolchainId = toolchainId;
        _executableName = executableName;
    }

    public string ToolchainId { get; }

    public async Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        // Real, Phase 13 finding: on Windows, npm/pnpm/yarn ship only as ".cmd" shims (no
        // ".exe"), and Win32's CreateProcess auto-appends ".exe" — never ".cmd" — to an
        // extension-less module name. Probing the bare executable name therefore throws
        // Win32Exception and misreports a genuinely-installed npm as not installed. Resolving
        // through ExecutableLocator first (same resolution ProjectAdapter code already uses)
        // fixes this without changing behavior on Linux/macOS, where the bare name always worked.
        var resolvedPath = ExecutableLocator.FindOnPath(_executableName);
        var probe = await ToolchainProbe.RunAsync(_processRunner, resolvedPath ?? _executableName, new[] { "--version" }, cancellationToken: cancellationToken).ConfigureAwait(false);

        return probe.Outcome switch
        {
            ToolchainProbeOutcome.NotFound => Result(ToolchainDetectionState.NotInstalled),
            ToolchainProbeOutcome.TimedOut => Result(ToolchainDetectionState.DetectionTimedOut, warning: $"'{_executableName} --version' timed out."),
            ToolchainProbeOutcome.Failed => Result(ToolchainDetectionState.DetectionFailed, warning: $"'{_executableName} --version' exited with a non-zero code."),
            _ => Result(ToolchainDetectionState.Detected, probe.CombinedOutput.Trim(), resolvedPath),
        };
    }

    private ToolchainInfo Result(ToolchainDetectionState state, string? version = null, string? executablePath = null, string? warning = null) => new(
        ToolchainId,
        _executableName,
        state,
        Version: version,
        ExecutablePath: executablePath,
        Capabilities: state == ToolchainDetectionState.Detected
            ? new[] { ToolchainCapability.Detect, ToolchainCapability.Package, ToolchainCapability.Restore }
            : Array.Empty<ToolchainCapability>(),
        DetectionWarning: warning);
}
