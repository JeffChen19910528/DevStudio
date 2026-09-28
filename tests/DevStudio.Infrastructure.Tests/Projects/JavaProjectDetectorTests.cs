using DevStudio.Core.Projects;
using DevStudio.Infrastructure.Projects;
using DevStudio.Infrastructure.Tests.TestSupport;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Projects;

public class JavaProjectDetectorTests
{
    [Fact]
    public async Task Reads_the_artifactId_out_of_a_maven_pom()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("pom.xml", "<project><artifactId>my-service</artifactId></project>");

        var result = await new JavaProjectDetector().DetectAsync(temp.Path, new[] { "pom.xml" });

        Assert.Equal(ProjectType.Java, result!.Project!.ProjectType);
        Assert.Equal("my-service", result.Project.Name);
    }

    [Fact]
    public async Task Detects_a_gradle_project_and_flags_Kotlin_for_kts_build_files()
    {
        var result = await new JavaProjectDetector().DetectAsync("/repo", new[] { "build.gradle.kts" });

        Assert.Equal(ProjectType.Java, result!.Project!.ProjectType);
        Assert.Contains("Kotlin", result.Project.Languages);
        Assert.Contains("Java", result.Project.Languages);
    }
}
