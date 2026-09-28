using DevStudio.Core.Processes;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Toolchains;

/// <summary>Reported as one "Rust" toolchain (matching the detection matrix's single row) backed
/// by both <c>rustc</c> (compiler) and <c>cargo</c> (the actual build interface) — SKILL.md §16.
/// Both must be present for the toolchain to count as usable, since real Rust workflows use
/// cargo, not rustc directly.</summary>
public sealed class RustToolchainDetector : IToolchainDetector
{
    private readonly IProcessRunner _processRunner;

    public RustToolchainDetector(IProcessRunner processRunner) => _processRunner = processRunner;

    public string ToolchainId => WellKnownToolchainIds.Rust;

    public async Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        var rustcProbe = await ToolchainProbe.RunAsync(_processRunner, "rustc", new[] { "--version" }, cancellationToken: cancellationToken).ConfigureAwait(false);
        var cargoProbe = await ToolchainProbe.RunAsync(_processRunner, "cargo", new[] { "--version" }, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (rustcProbe.Outcome == ToolchainProbeOutcome.TimedOut || cargoProbe.Outcome == ToolchainProbeOutcome.TimedOut)
        {
            return Result(ToolchainDetectionState.DetectionTimedOut, warning: "'rustc --version'/'cargo --version' timed out.");
        }

        if (rustcProbe.Outcome != ToolchainProbeOutcome.Success && cargoProbe.Outcome != ToolchainProbeOutcome.Success)
        {
            return Result(ToolchainDetectionState.NotInstalled);
        }

        if (rustcProbe.Outcome != ToolchainProbeOutcome.Success || cargoProbe.Outcome != ToolchainProbeOutcome.Success)
        {
            return Result(ToolchainDetectionState.PartiallyDetected, warning: "Only one of rustc/cargo was found; both are expected for a usable Rust toolchain.");
        }

        var cargoPath = ExecutableLocator.FindOnPath("cargo");
        var metadata = new Dictionary<string, string>();
        var rustcPath = ExecutableLocator.FindOnPath("rustc");
        if (rustcPath is not null) metadata["rustcPath"] = rustcPath;

        return new ToolchainInfo(
            ToolchainId,
            "Rust",
            ToolchainDetectionState.Detected,
            Version: rustcProbe.CombinedOutput.Trim(),
            ExecutablePath: cargoPath,
            Capabilities: new[] { ToolchainCapability.Detect, ToolchainCapability.Build, ToolchainCapability.Run, ToolchainCapability.Test, ToolchainCapability.Package, ToolchainCapability.Format },
            Metadata: metadata.Count > 0 ? metadata : null);
    }

    private ToolchainInfo Result(ToolchainDetectionState state, string? warning = null) => new(
        ToolchainId, "Rust", state, Capabilities: Array.Empty<ToolchainCapability>(), DetectionWarning: warning);
}
