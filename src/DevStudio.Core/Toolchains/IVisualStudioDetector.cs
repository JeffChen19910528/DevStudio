namespace DevStudio.Core.Toolchains;

/// <summary>
/// Discovers Visual Studio installations via the officially supported <c>vswhere.exe</c>
/// mechanism (SKILL.md §8) rather than assuming <c>cl.exe</c>/MSBuild are on PATH. Never
/// executes a build; never modifies the Visual Studio installation.
/// </summary>
public interface IVisualStudioDetector
{
    Task<IReadOnlyList<VisualStudioInstance>> DetectAllAsync(CancellationToken cancellationToken = default);
}
