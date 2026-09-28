namespace DevStudio.Core.Toolchains;

/// <summary>
/// A diagnostic summary suitable for a bug report (SKILL.md §25). Deliberately excludes
/// passwords/API keys/tokens and full environment-variable dumps — only OS/architecture and the
/// already-sanitized <see cref="ToolchainInfo"/>/<see cref="VisualStudioInstance"/> results.
/// </summary>
public sealed record EnvironmentSnapshot(
    string OperatingSystem,
    string Architecture,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<ToolchainInfo> Toolchains,
    IReadOnlyList<VisualStudioInstance> VisualStudioInstances);
