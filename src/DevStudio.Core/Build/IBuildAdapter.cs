using DevStudio.Core.Processes;
using DevStudio.Core.Projects;

namespace DevStudio.Core.Build;

/// <summary>
/// Drives one project ecosystem's native build tool through <see cref="IProcessRunner"/>
/// (SKILL.md §3–§4, §12): BuildService → IBuildAdapter → IProcessRunner → external tool. An
/// adapter is responsible for checking its own required toolchain is actually usable before
/// starting a process — <see cref="BuildStatus.Unavailable"/> when it isn't, never a fabricated
/// success and never an attempted run against a missing tool.
/// </summary>
public interface IBuildAdapter
{
    bool SupportsProjectType(ProjectType projectType);

    /// <summary>Streams stdout/stderr as they arrive (SKILL.md §50) in addition to returning
    /// the final structured result.</summary>
    Task<BuildResult> ExecuteAsync(BuildRequest request, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default);
}
