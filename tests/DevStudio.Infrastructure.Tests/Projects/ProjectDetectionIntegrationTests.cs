using DevStudio.Core.Projects;
using DevStudio.Core.Workspace;
using DevStudio.Infrastructure.Projects;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Workspace;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Projects;

/// <summary>
/// End-to-end detection against a real temporary directory tree (SKILL.md §29) — the real
/// <see cref="WorkspaceScanner"/> plus every real detector, not fakes, so this exercises the
/// actual disk reads/regex/JSON parsing together. Every temp directory is created fresh and
/// deleted afterward; nothing here ever touches the DevStudio repository itself.
/// </summary>
public class ProjectDetectionIntegrationTests
{
    private static IProjectDetector[] AllDetectors() => new IProjectDetector[]
    {
        new DotNetSolutionDetector(),
        new DotNetProjectDetector(),
        new CMakeProjectDetector(),
        new NodeProjectDetector(),
        new PythonProjectDetector(),
        new JavaProjectDetector(),
        new RustProjectDetector(),
        new GoProjectDetector(),
    };

    [Fact]
    public async Task Detects_a_dotnet_solution_with_two_referenced_projects()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("App.sln", """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "src\App\App.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Tests", "tests\Tests\Tests.csproj", "{22222222-2222-2222-2222-222222222222}"
            EndProject
            """);
        temp.WriteFile("src/App/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        temp.WriteFile("tests/Tests/Tests.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var service = new ProjectDetectionService(new WorkspaceScanner(), AllDetectors());
        var graph = await service.DetectAsync(temp.Path, WorkspaceExclusionRules.Default);

        var solution = Assert.Single(graph.Solutions);
        Assert.Equal(2, solution.ProjectIds.Count);
        Assert.Equal(2, graph.AllProjects.Count(p => p.ProjectType == ProjectType.DotNet));
        Assert.All(graph.AllProjects, p => Assert.Equal(DetectionConfidence.Full, p.DetectionConfidence));
    }

    [Fact]
    public async Task Represents_a_monorepo_with_four_different_ecosystems_as_four_top_level_projects()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("frontend/package.json", "{ \"name\": \"frontend\" }");
        temp.WriteFile("backend/Api.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        temp.WriteFile("native/CMakeLists.txt", "project(NativeLib)");
        temp.WriteFile("tools/pyproject.toml", "[project]\nname = \"tools\"\n");

        var service = new ProjectDetectionService(new WorkspaceScanner(), AllDetectors());
        var graph = await service.DetectAsync(temp.Path, WorkspaceExclusionRules.Default);

        Assert.Equal(4, graph.TopLevelProjects.Count);
        Assert.Contains(graph.TopLevelProjects, p => p.ProjectType == ProjectType.Node);
        Assert.Contains(graph.TopLevelProjects, p => p.ProjectType == ProjectType.DotNet);
        Assert.Contains(graph.TopLevelProjects, p => p.ProjectType == ProjectType.CMake);
        Assert.Contains(graph.TopLevelProjects, p => p.ProjectType == ProjectType.Python);
    }

    [Fact]
    public async Task Excludes_noise_directories_from_detection_just_like_the_Explorer()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        temp.WriteFile("node_modules/some-package/package.json", "{ \"name\": \"should-not-be-detected\" }");

        var service = new ProjectDetectionService(new WorkspaceScanner(), AllDetectors());
        var graph = await service.DetectAsync(temp.Path, WorkspaceExclusionRules.Default);

        Assert.DoesNotContain(graph.AllProjects, p => p.ProjectType == ProjectType.Node);
    }

    [Fact]
    public async Task Never_modifies_the_scanned_repository()
    {
        using var temp = new TempDirectory();
        var csprojPath = temp.WriteFile("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var beforeWriteTimeUtc = File.GetLastWriteTimeUtc(csprojPath);
        var beforeContent = await File.ReadAllTextAsync(csprojPath);

        var service = new ProjectDetectionService(new WorkspaceScanner(), AllDetectors());
        await service.DetectAsync(temp.Path, WorkspaceExclusionRules.Default);

        Assert.Equal(beforeWriteTimeUtc, File.GetLastWriteTimeUtc(csprojPath));
        Assert.Equal(beforeContent, await File.ReadAllTextAsync(csprojPath));
    }
}
