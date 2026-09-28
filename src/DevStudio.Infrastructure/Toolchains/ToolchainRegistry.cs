using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Toolchains;

/// <summary>
/// Holds every registered <see cref="IToolchainDetector"/> and the results of the last <see
/// cref="RefreshAsync"/> (SKILL.md §19, §26 — a reasonable in-memory cache). Never runs a build;
/// <see cref="RefreshAsync"/> only re-runs detection. Detectors run concurrently and each is
/// isolated: if one throws, only its own entry becomes <see
/// cref="ToolchainDetectionState.DetectionFailed"/> — the others still complete (SKILL.md §28).
/// </summary>
public sealed class ToolchainRegistry : IToolchainRegistry
{
    private readonly List<IToolchainDetector> _detectors = new();
    private Dictionary<string, ToolchainInfo> _results = new();

    public ToolchainRegistry(IEnumerable<IToolchainDetector>? initialDetectors = null)
    {
        foreach (var detector in initialDetectors ?? Enumerable.Empty<IToolchainDetector>())
        {
            Register(detector);
        }
    }

    public void Register(IToolchainDetector detector) => _detectors.Add(detector);

    public void Unregister(string toolchainId) => _detectors.RemoveAll(d => d.ToolchainId == toolchainId);

    public ToolchainInfo? Get(string toolchainId) => _results.GetValueOrDefault(toolchainId);

    public IReadOnlyList<ToolchainInfo> GetAll() => _results.Values.ToList();

    public IReadOnlyList<ToolchainInfo> FindByCapability(ToolchainCapability capability) =>
        _results.Values.Where(t => t.Capabilities.Contains(capability)).ToList();

    public IReadOnlyList<ToolchainInfo> FindForProjectType(ProjectType projectType)
    {
        var requiredIds = ToolchainRequirements.GetRequiredToolchainIds(projectType);
        return _results.Values.Where(t => requiredIds.Contains(t.Id)).ToList();
    }

    public async Task<IReadOnlyList<ToolchainInfo>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var detectorsSnapshot = _detectors.ToList();
        var tasks = detectorsSnapshot.Select(d => DetectSafelyAsync(d, cancellationToken));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        _results = results.ToDictionary(r => r.Id);
        return results;
    }

    private static async Task<ToolchainInfo> DetectSafelyAsync(IToolchainDetector detector, CancellationToken cancellationToken)
    {
        try
        {
            return await detector.DetectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new ToolchainInfo(
                detector.ToolchainId,
                detector.ToolchainId,
                ToolchainDetectionState.DetectionFailed,
                Capabilities: Array.Empty<ToolchainCapability>(),
                DetectionWarning: $"Detector threw an unexpected exception: {ex.Message}");
        }
    }
}
