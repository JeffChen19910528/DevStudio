using DevStudio.Core.Projects;

namespace DevStudio.Core.Toolchains;

/// <summary>
/// Maps a project's required toolchains onto what's actually been detected (SKILL.md §23–§24):
/// "Cargo.toml → Rust required → Rust not installed → Build unavailable." Never builds anything
/// — this only reads already-collected <see cref="ToolchainInfo"/> results.
/// </summary>
public static class ProjectCapabilityMatcher
{
    public static IReadOnlyList<ProjectCapability> Match(ProjectType projectType, IReadOnlyList<ToolchainInfo> toolchains)
    {
        var groups = ToolchainRequirements.GetRequiredToolchainGroups(projectType);
        if (groups.Count == 0) return Array.Empty<ProjectCapability>();

        var overall = CapabilityAvailability.Available;
        string? primaryToolchainId = null;
        var explanations = new List<string>();

        foreach (var group in groups)
        {
            var groupAvailability = EvaluateGroup(group, toolchains, out var satisfiedId);

            if (groupAvailability == CapabilityAvailability.Available)
            {
                primaryToolchainId ??= satisfiedId;
            }
            else
            {
                explanations.Add(DescribeMissingGroup(group, toolchains));
            }

            if (groupAvailability == CapabilityAvailability.Unavailable)
            {
                overall = CapabilityAvailability.Unavailable;
            }
            else if (groupAvailability == CapabilityAvailability.Unknown && overall != CapabilityAvailability.Unavailable)
            {
                overall = CapabilityAvailability.Unknown;
            }
        }

        var requiredIds = ToolchainRequirements.GetRequiredToolchainIds(projectType);
        var reportedToolchainId = primaryToolchainId ?? requiredIds.FirstOrDefault() ?? "unknown";

        var results = new List<ProjectCapability>
        {
            new(ToolchainCapability.Build, overall, reportedToolchainId, explanations.Count > 0 ? string.Join(" ", explanations) : null)
        };

        if (overall == CapabilityAvailability.Available && primaryToolchainId is not null)
        {
            var primary = toolchains.First(t => t.Id == primaryToolchainId);
            foreach (var capability in primary.Capabilities.Where(IsProjectFacingCapability))
            {
                results.Add(new ProjectCapability(capability, CapabilityAvailability.Available, primaryToolchainId));
            }
        }

        return results;
    }

    /// <summary>Recomputes capabilities for every project in a graph, preserving its hierarchy
    /// (SKILL.md §24) — used both right after project detection and again whenever Tools →
    /// Refresh Toolchains changes what's installed.</summary>
    public static WorkspaceProjectGraph ApplyToGraph(WorkspaceProjectGraph graph, IReadOnlyList<ToolchainInfo> toolchains)
    {
        ProjectInfo Rewrite(ProjectInfo project)
        {
            var children = project.ChildProjects.Select(Rewrite).ToList();
            return project with { Capabilities = Match(project.ProjectType, toolchains), ChildProjects = children };
        }

        var topLevel = graph.TopLevelProjects.Select(Rewrite).ToList();
        var allProjects = Flatten(topLevel).ToList();

        return new WorkspaceProjectGraph(topLevel, graph.Solutions, allProjects);
    }

    private static IEnumerable<ProjectInfo> Flatten(IEnumerable<ProjectInfo> projects) =>
        projects.SelectMany(p => new[] { p }.Concat(Flatten(p.ChildProjects)));

    private static bool IsProjectFacingCapability(ToolchainCapability capability) => capability is
        ToolchainCapability.Run or ToolchainCapability.Test or ToolchainCapability.Package or
        ToolchainCapability.Publish or ToolchainCapability.Restore or ToolchainCapability.Format;

    private static CapabilityAvailability EvaluateGroup(IReadOnlyList<string> group, IReadOnlyList<ToolchainInfo> toolchains, out string? satisfiedId)
    {
        var anyKnown = false;

        foreach (var id in group)
        {
            var info = toolchains.FirstOrDefault(t => t.Id == id);
            if (info is null) continue;

            anyKnown = true;
            if (info.IsUsable)
            {
                satisfiedId = info.Id;
                return CapabilityAvailability.Available;
            }
        }

        satisfiedId = null;
        return anyKnown ? CapabilityAvailability.Unavailable : CapabilityAvailability.Unknown;
    }

    private static string DescribeMissingGroup(IReadOnlyList<string> group, IReadOnlyList<ToolchainInfo> toolchains)
    {
        if (group.Count == 1)
        {
            return $"'{group[0]}' is not installed.";
        }

        var anyKnown = group.Any(id => toolchains.Any(t => t.Id == id));
        return anyKnown
            ? $"No usable compiler among [{string.Join(", ", group)}] was detected."
            : $"None of [{string.Join(", ", group)}] have been detected yet.";
    }
}
