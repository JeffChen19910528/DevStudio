using DevStudio.Core.Projects;
using DevStudio.Core.Tests.Fakes;
using DevStudio.Core.Workspace;
using Xunit;

namespace DevStudio.Core.Tests;

public class ProjectDetectionServiceTests
{
    [Fact]
    public async Task Falls_back_to_a_single_Generic_project_when_nothing_is_detected()
    {
        var scanner = new FakeWorkspaceScanner(new()
        {
            ["/repo"] = new List<FileSystemNode> { new("README.md", "/repo/README.md", IsDirectory: false) }
        });
        var service = new ProjectDetectionService(scanner, Array.Empty<IProjectDetector>());

        var graph = await service.DetectAsync("/repo", WorkspaceExclusionRules.Default);

        Assert.Single(graph.TopLevelProjects);
        Assert.Equal(ProjectType.Generic, graph.TopLevelProjects[0].ProjectType);
        Assert.Equal("repo", graph.TopLevelProjects[0].Name);
    }

    [Fact]
    public async Task Detects_a_single_project_at_the_workspace_root()
    {
        var scanner = new FakeWorkspaceScanner(new()
        {
            ["/repo"] = new List<FileSystemNode> { new("proj.marker", "/repo/proj.marker", IsDirectory: false) }
        });
        var detector = new FakeProjectDetector("proj.marker", ProjectType.DotNet);
        var service = new ProjectDetectionService(scanner, new[] { detector });

        var graph = await service.DetectAsync("/repo", WorkspaceExclusionRules.Default);

        Assert.Single(graph.TopLevelProjects);
        Assert.Equal(ProjectType.DotNet, graph.TopLevelProjects[0].ProjectType);
        Assert.Empty(graph.TopLevelProjects[0].ChildProjects);
    }

    [Fact]
    public async Task Nests_a_project_found_inside_another_projects_directory_as_a_child()
    {
        var scanner = new FakeWorkspaceScanner(new()
        {
            ["/repo"] = new List<FileSystemNode>
            {
                new("outer.marker", "/repo/outer.marker", IsDirectory: false),
                new("nested", "/repo/nested", IsDirectory: true),
            },
            ["/repo/nested"] = new List<FileSystemNode>
            {
                new("inner.marker", "/repo/nested/inner.marker", IsDirectory: false),
            },
        });
        var detectors = new IProjectDetector[]
        {
            new FakeProjectDetector("outer.marker", ProjectType.Node),
            new FakeProjectDetector("inner.marker", ProjectType.Python),
        };
        var service = new ProjectDetectionService(scanner, detectors);

        var graph = await service.DetectAsync("/repo", WorkspaceExclusionRules.Default);

        Assert.Single(graph.TopLevelProjects);
        var outer = graph.TopLevelProjects[0];
        Assert.Equal(ProjectType.Node, outer.ProjectType);
        Assert.Single(outer.ChildProjects);
        Assert.Equal(ProjectType.Python, outer.ChildProjects[0].ProjectType);
        Assert.Equal(2, graph.AllProjects.Count);
    }

    [Fact]
    public async Task Represents_a_monorepo_as_multiple_independent_top_level_projects()
    {
        var scanner = new FakeWorkspaceScanner(new()
        {
            ["/repo"] = new List<FileSystemNode>
            {
                new("frontend", "/repo/frontend", IsDirectory: true),
                new("backend", "/repo/backend", IsDirectory: true),
            },
            ["/repo/frontend"] = new List<FileSystemNode> { new("node.marker", "/repo/frontend/node.marker", IsDirectory: false) },
            ["/repo/backend"] = new List<FileSystemNode> { new("dotnet.marker", "/repo/backend/dotnet.marker", IsDirectory: false) },
        });
        var detectors = new IProjectDetector[]
        {
            new FakeProjectDetector("node.marker", ProjectType.Node),
            new FakeProjectDetector("dotnet.marker", ProjectType.DotNet),
        };
        var service = new ProjectDetectionService(scanner, detectors);

        var graph = await service.DetectAsync("/repo", WorkspaceExclusionRules.Default);

        Assert.Equal(2, graph.TopLevelProjects.Count);
        Assert.Contains(graph.TopLevelProjects, p => p.ProjectType == ProjectType.Node);
        Assert.Contains(graph.TopLevelProjects, p => p.ProjectType == ProjectType.DotNet);
    }

    [Fact]
    public async Task Resolves_a_solutions_project_reference_to_the_matching_detected_project()
    {
        var scanner = new FakeWorkspaceScanner(new()
        {
            ["/repo"] = new List<FileSystemNode>
            {
                new("app.sln.marker", "/repo/app.sln.marker", IsDirectory: false),
                new("App", "/repo/App", IsDirectory: true),
            },
            ["/repo/App"] = new List<FileSystemNode> { new("proj.marker", "/repo/App/proj.marker", IsDirectory: false) },
        });
        var projectDetector = new FakeProjectDetector("proj.marker", ProjectType.DotNet);
        var solutionDetector = new FakeProjectDetector("app.sln.marker", dir =>
            new RawSolutionDetection("sln-id", "App", Path.Combine(dir, "app.sln.marker"), new[] { "/repo/App/proj.marker" }));
        var service = new ProjectDetectionService(scanner, new IProjectDetector[] { projectDetector, solutionDetector });

        var graph = await service.DetectAsync("/repo", WorkspaceExclusionRules.Default);

        var solution = Assert.Single(graph.Solutions);
        var referencedProject = graph.AllProjects.Single(p => p.ProjectType == ProjectType.DotNet);
        Assert.Equal(new[] { referencedProject.Id }, solution.ProjectIds);
        Assert.Equal(DetectionConfidence.Full, referencedProject.DetectionConfidence);
    }

    [Fact]
    public async Task Represents_a_solutions_missing_project_reference_as_a_partial_stub_instead_of_dropping_it()
    {
        var scanner = new FakeWorkspaceScanner(new()
        {
            ["/repo"] = new List<FileSystemNode> { new("app.sln.marker", "/repo/app.sln.marker", IsDirectory: false) },
        });
        var solutionDetector = new FakeProjectDetector("app.sln.marker", dir =>
            new RawSolutionDetection("sln-id", "App", Path.Combine(dir, "app.sln.marker"), new[] { "/repo/Missing/Missing.csproj" }));
        var service = new ProjectDetectionService(scanner, new IProjectDetector[] { solutionDetector });

        var graph = await service.DetectAsync("/repo", WorkspaceExclusionRules.Default);

        var solution = Assert.Single(graph.Solutions);
        var stub = graph.AllProjects.Single(p => solution.ProjectIds.Contains(p.Id));
        Assert.Equal(DetectionConfidence.Partial, stub.DetectionConfidence);
        Assert.NotNull(stub.DetectionWarning);
    }
}
