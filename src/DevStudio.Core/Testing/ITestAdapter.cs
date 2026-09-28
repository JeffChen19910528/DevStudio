using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Workspace;

namespace DevStudio.Core.Testing;

/// <summary>
/// Evolves the Phase 0 <c>Adapters.ITestAdapter</c> stub (discovery + a single run method, no
/// cancellation, no build integration) into a real contract capable of driving the actual .NET
/// test infrastructure (SKILL.md §7 [Phase 8]): <c>TestService → ITestAdapter →
/// DotNetTestAdapter → IProcessRunner → dotnet test</c>. The Phase 0 stub was unreferenced
/// anywhere and is replaced outright, per the same precedent ADR-005/ADR-007/ADR-008 used for
/// the equally-unreferenced Phase 0 <c>IBuildAdapter</c>/<c>IDebuggerAdapter</c>/
/// <c>ILanguageAdapter</c> stubs.
/// </summary>
public interface ITestAdapter
{
    bool SupportsProjectType(ProjectType projectType);

    /// <summary>Discovers real tests via the real test infrastructure — never by parsing
    /// source and guessing test methods (SKILL.md §9).</summary>
    Task<IReadOnlyList<TestCase>> DiscoverTestsAsync(ProjectInfo project, BuildConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary><paramref name="skipBuild"/> is true when <see cref="TestService"/> already
    /// built the project via <see cref="Build.BuildService"/> — the adapter then passes
    /// whatever "don't build again" flag the real runner supports, mirroring Phase 5's
    /// <c>dotnet run --no-build</c> decision, rather than paying for a second, redundant
    /// build.</summary>
    Task<IReadOnlyList<TestResult>> RunTestsAsync(
        ProjectInfo project,
        BuildConfiguration configuration,
        TestFilter? filter,
        bool skipBuild,
        IProcessOutputSink? outputSink = null,
        CancellationToken cancellationToken = default);
}
