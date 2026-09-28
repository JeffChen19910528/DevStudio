using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;
using Xunit;

namespace DevStudio.Core.Tests;

public class ProjectCapabilityMatcherTests
{
    private static ToolchainInfo Detected(string id, params ToolchainCapability[] capabilities) =>
        new(id, id, ToolchainDetectionState.Detected, Capabilities: capabilities);

    private static ToolchainInfo NotInstalled(string id) =>
        new(id, id, ToolchainDetectionState.NotInstalled, Capabilities: Array.Empty<ToolchainCapability>());

    [Fact]
    public void Unknown_project_types_have_no_capabilities()
    {
        var result = ProjectCapabilityMatcher.Match(ProjectType.Generic, Array.Empty<ToolchainInfo>());
        Assert.Empty(result);
    }

    [Fact]
    public void Available_when_the_required_toolchain_is_detected()
    {
        var toolchains = new[] { Detected(WellKnownToolchainIds.DotNet, ToolchainCapability.Build, ToolchainCapability.Run, ToolchainCapability.Test) };

        var result = ProjectCapabilityMatcher.Match(ProjectType.DotNet, toolchains);

        var build = result.Single(c => c.Capability == ToolchainCapability.Build);
        Assert.Equal(CapabilityAvailability.Available, build.Availability);
        Assert.Equal(WellKnownToolchainIds.DotNet, build.ToolchainId);
        Assert.Contains(result, c => c.Capability == ToolchainCapability.Run);
        Assert.Contains(result, c => c.Capability == ToolchainCapability.Test);
    }

    [Fact]
    public void Never_reports_Debug_as_available_even_when_the_toolchain_is_detected()
    {
        // Debug capability requires a real IDebuggerAdapter (Phase 6); a version probe alone is
        // never a justified source for it (SKILL.md §4).
        var toolchains = new[] { Detected(WellKnownToolchainIds.DotNet, ToolchainCapability.Debug, ToolchainCapability.Build) };

        var result = ProjectCapabilityMatcher.Match(ProjectType.DotNet, toolchains);

        Assert.DoesNotContain(result, c => c.Capability == ToolchainCapability.Debug);
    }

    [Fact]
    public void Unavailable_when_the_required_toolchain_is_known_to_be_missing()
    {
        var toolchains = new[] { NotInstalled(WellKnownToolchainIds.Rust) };

        var result = ProjectCapabilityMatcher.Match(ProjectType.Rust, toolchains);

        var build = result.Single(c => c.Capability == ToolchainCapability.Build);
        Assert.Equal(CapabilityAvailability.Unavailable, build.Availability);
        Assert.NotNull(build.Explanation);
    }

    [Fact]
    public void Unknown_when_no_detection_has_run_yet()
    {
        var result = ProjectCapabilityMatcher.Match(ProjectType.Node, Array.Empty<ToolchainInfo>());

        var build = result.Single(c => c.Capability == ToolchainCapability.Build);
        Assert.Equal(CapabilityAvailability.Unknown, build.Availability);
    }

    [Fact]
    public void CMake_requires_both_cmake_itself_and_a_compiler()
    {
        // All three compiler detectors ran and found nothing (positively known missing), vs.
        // cmake found alongside a usable gcc.
        var cmakeButNoCompiler = new[]
        {
            Detected(WellKnownToolchainIds.CMake, ToolchainCapability.Build),
            NotInstalled(WellKnownToolchainIds.Msvc),
            NotInstalled(WellKnownToolchainIds.Gcc),
            NotInstalled(WellKnownToolchainIds.Clang),
        };
        var cmakeAndGcc = new[]
        {
            Detected(WellKnownToolchainIds.CMake, ToolchainCapability.Build),
            Detected(WellKnownToolchainIds.Gcc, ToolchainCapability.Build),
        };

        var withoutCompiler = ProjectCapabilityMatcher.Match(ProjectType.CMake, cmakeButNoCompiler).Single(c => c.Capability == ToolchainCapability.Build);
        var withCompiler = ProjectCapabilityMatcher.Match(ProjectType.CMake, cmakeAndGcc).Single(c => c.Capability == ToolchainCapability.Build);

        Assert.Equal(CapabilityAvailability.Unavailable, withoutCompiler.Availability);
        Assert.Equal(CapabilityAvailability.Available, withCompiler.Availability);
    }

    [Fact]
    public void CMake_is_Unknown_rather_than_Unavailable_when_the_compiler_group_was_never_checked()
    {
        var onlyCMake = new[] { Detected(WellKnownToolchainIds.CMake, ToolchainCapability.Build) };

        var build = ProjectCapabilityMatcher.Match(ProjectType.CMake, onlyCMake).Single(c => c.Capability == ToolchainCapability.Build);

        Assert.Equal(CapabilityAvailability.Unknown, build.Availability);
    }

    [Fact]
    public void ApplyToGraph_updates_capabilities_through_the_full_project_hierarchy()
    {
        var child = new ProjectInfo("child", "child", "/repo/src", ProjectType.Node, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());
        var parent = new ProjectInfo("parent", "parent", "/repo", ProjectType.DotNet, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>(), ChildProjects: new[] { child });
        var graph = new WorkspaceProjectGraph(new[] { parent }, Array.Empty<SolutionInfo>(), new[] { parent, child });

        var toolchains = new[] { Detected(WellKnownToolchainIds.DotNet, ToolchainCapability.Build), Detected(WellKnownToolchainIds.Node, ToolchainCapability.Build) };
        var updated = ProjectCapabilityMatcher.ApplyToGraph(graph, toolchains);

        var updatedParent = updated.TopLevelProjects.Single();
        var updatedChild = updatedParent.ChildProjects.Single();

        Assert.Contains(updatedParent.Capabilities, c => c.Capability == ToolchainCapability.Build && c.Availability == CapabilityAvailability.Available);
        Assert.Contains(updatedChild.Capabilities, c => c.Capability == ToolchainCapability.Build && c.Availability == CapabilityAvailability.Available);
        Assert.Equal(2, updated.AllProjects.Count);
    }
}
