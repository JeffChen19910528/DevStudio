using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Toolchains;

/// <summary>
/// Adapts <see cref="IVisualStudioDetector"/> (which reports every VS instance independently,
/// SKILL.md §9) into one ordinary <see cref="ToolchainInfo"/> with id <c>"msvc"</c>, so <see
/// cref="ProjectCapabilityMatcher"/> can check for a C/C++ compiler uniformly alongside GCC/Clang
/// without special-casing Visual Studio (SKILL.md §18). The richest, per-instance detail still
/// lives in <see cref="IVisualStudioDetector"/> for the Toolchains panel; this only answers "is
/// there at least one usable MSVC toolset."
/// </summary>
public sealed class MsvcToolchainDetector : IToolchainDetector
{
    private readonly IVisualStudioDetector _visualStudioDetector;

    public MsvcToolchainDetector(IVisualStudioDetector visualStudioDetector) => _visualStudioDetector = visualStudioDetector;

    public string ToolchainId => WellKnownToolchainIds.Msvc;

    public async Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        var instances = await _visualStudioDetector.DetectAllAsync(cancellationToken).ConfigureAwait(false);
        var usable = instances.FirstOrDefault(i => i.State == ToolchainDetectionState.Detected && i.MsvcToolsetPath is not null);

        if (usable is null)
        {
            return new ToolchainInfo(ToolchainId, "MSVC", ToolchainDetectionState.NotInstalled, Capabilities: Array.Empty<ToolchainCapability>());
        }

        return new ToolchainInfo(
            ToolchainId,
            "MSVC",
            ToolchainDetectionState.Detected,
            Version: Path.GetFileName(usable.MsvcToolsetPath),
            Vendor: "Microsoft",
            InstallationRoot: usable.MsvcToolsetPath,
            Capabilities: new[] { ToolchainCapability.Detect, ToolchainCapability.Build });
    }
}
