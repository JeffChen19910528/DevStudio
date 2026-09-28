namespace DevStudio.Core.Workspace;

public sealed record BuildConfiguration(
    string Name,
    string? TargetArchitecture = null,
    bool IncludeDebugSymbols = true,
    bool Optimize = false,
    IReadOnlyDictionary<string, string>? Defines = null,
    IReadOnlyDictionary<string, string>? Environment = null)
{
    public static BuildConfiguration Debug { get; } = new("Debug", IncludeDebugSymbols: true, Optimize: false);
    public static BuildConfiguration Release { get; } = new("Release", IncludeDebugSymbols: false, Optimize: true);
}
