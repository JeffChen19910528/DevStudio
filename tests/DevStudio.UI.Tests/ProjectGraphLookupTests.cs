using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;
using DevStudio.UI.ViewModels;
using Xunit;

namespace DevStudio.UI.Tests;

public class ProjectGraphLookupTests
{
    private static ProjectInfo MakeProject(string id, string rootPath, ProjectType type = ProjectType.DotNet) =>
        new(id, id, rootPath, type, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());

    [Fact]
    public void FindOwningProject_matches_a_file_directly_inside_a_projects_root()
    {
        var api = MakeProject("api", "/repo/src/Api");
        var graph = new WorkspaceProjectGraph(new[] { api }, Array.Empty<SolutionInfo>(), new[] { api });
        var lookup = ProjectGraphLookup.FromGraph(graph);

        var owner = lookup.FindOwningProject("/repo/src/Api/Program.cs");

        Assert.Equal("api", owner?.Id);
    }

    [Fact]
    public void FindOwningProject_picks_the_deepest_matching_project_for_nested_projects()
    {
        var outer = MakeProject("outer", "/repo");
        var inner = MakeProject("inner", "/repo/src/Api");
        var graph = new WorkspaceProjectGraph(new[] { outer, inner }, Array.Empty<SolutionInfo>(), new[] { outer, inner });
        var lookup = ProjectGraphLookup.FromGraph(graph);

        var owner = lookup.FindOwningProject("/repo/src/Api/Program.cs");

        Assert.Equal("inner", owner?.Id);
    }

    [Fact]
    public void FindOwningProject_returns_null_for_a_file_outside_every_detected_project()
    {
        var api = MakeProject("api", "/repo/src/Api");
        var graph = new WorkspaceProjectGraph(new[] { api }, Array.Empty<SolutionInfo>(), new[] { api });
        var lookup = ProjectGraphLookup.FromGraph(graph);

        var owner = lookup.FindOwningProject("/repo/docs/readme.md");

        Assert.Null(owner);
    }

    [Fact]
    public void Empty_lookup_never_resolves_a_project_or_solution()
    {
        var lookup = ProjectGraphLookup.Empty;

        Assert.Null(lookup.FindOwningProject("/anything/file.cs"));
        Assert.Null(lookup.FindProject("/anything"));
        Assert.Null(lookup.FindSolution("/anything"));
    }
}
