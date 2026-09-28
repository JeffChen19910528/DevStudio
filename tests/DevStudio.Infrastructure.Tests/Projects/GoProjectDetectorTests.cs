using DevStudio.Core.Projects;
using DevStudio.Infrastructure.Projects;
using DevStudio.Infrastructure.Tests.TestSupport;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Projects;

public class GoProjectDetectorTests
{
    [Fact]
    public async Task Derives_the_project_name_from_the_module_path()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("go.mod", "module github.com/example/my-service\n\ngo 1.22\n");

        var result = await new GoProjectDetector().DetectAsync(temp.Path, new[] { "go.mod" });

        Assert.Equal(ProjectType.Go, result!.Project!.ProjectType);
        Assert.Equal("my-service", result.Project.Name);
    }
}
