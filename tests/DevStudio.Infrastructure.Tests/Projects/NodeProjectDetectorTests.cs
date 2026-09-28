using DevStudio.Core.Projects;
using DevStudio.Infrastructure.Projects;
using DevStudio.Infrastructure.Tests.TestSupport;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Projects;

public class NodeProjectDetectorTests
{
    [Fact]
    public async Task Reads_the_name_field_out_of_package_json()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("package.json", "{ \"name\": \"my-frontend\", \"version\": \"1.0.0\" }");

        var result = await new NodeProjectDetector().DetectAsync(temp.Path, new[] { "package.json" });

        Assert.Equal(ProjectType.Node, result!.Project!.ProjectType);
        Assert.Equal("my-frontend", result.Project.Name);
        Assert.Equal(new[] { "JavaScript" }, result.Project.Languages);
    }

    [Fact]
    public async Task Adds_TypeScript_when_a_tsconfig_is_present()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("package.json", "{ \"name\": \"my-app\" }");
        temp.WriteFile("tsconfig.json", "{}");

        var result = await new NodeProjectDetector().DetectAsync(temp.Path, new[] { "package.json", "tsconfig.json" });

        Assert.Equal(new[] { "TypeScript", "JavaScript" }, result!.Project!.Languages);
    }

    [Fact]
    public async Task Reports_partial_confidence_for_malformed_json()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("package.json", "{ not valid json");

        var result = await new NodeProjectDetector().DetectAsync(temp.Path, new[] { "package.json" });

        Assert.Equal(DetectionConfidence.Partial, result!.Project!.DetectionConfidence);
    }
}
