using DevStudio.Core.Projects;
using DevStudio.Infrastructure.Projects;
using DevStudio.Infrastructure.Tests.TestSupport;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Projects;

public class CMakeProjectDetectorTests
{
    [Fact]
    public async Task Extracts_the_project_name_from_a_parsable_CMakeLists()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("CMakeLists.txt", "cmake_minimum_required(VERSION 3.20)\nproject(MyEngine)\n");

        var result = await new CMakeProjectDetector().DetectAsync(temp.Path, new[] { "CMakeLists.txt" });

        Assert.Equal(ProjectType.CMake, result!.Project!.ProjectType);
        Assert.Equal("MyEngine", result.Project.Name);
        Assert.Equal(DetectionConfidence.Full, result.Project.DetectionConfidence);
    }

    [Fact]
    public async Task Reports_partial_confidence_when_CMakeLists_has_no_recognizable_project_call()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("CMakeLists.txt", "# just comments, no project() call\n");

        var result = await new CMakeProjectDetector().DetectAsync(temp.Path, new[] { "CMakeLists.txt" });

        Assert.Equal(DetectionConfidence.Partial, result!.Project!.DetectionConfidence);
        Assert.Contains("partial CMake project", result.Project.DetectionWarning);
    }

    [Fact]
    public async Task Detects_a_bare_Makefile_without_CMakeLists()
    {
        var result = await new CMakeProjectDetector().DetectAsync("/repo", new[] { "Makefile" });

        Assert.Equal(ProjectType.CMake, result!.Project!.ProjectType);
        Assert.Equal(DetectionConfidence.Full, result.Project.DetectionConfidence);
    }
}
