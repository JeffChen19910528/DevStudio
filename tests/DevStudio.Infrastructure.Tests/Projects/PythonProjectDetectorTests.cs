using DevStudio.Core.Projects;
using DevStudio.Infrastructure.Projects;
using DevStudio.Infrastructure.Tests.TestSupport;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Projects;

public class PythonProjectDetectorTests
{
    [Fact]
    public async Task Full_confidence_when_pyproject_toml_is_present()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("pyproject.toml", "[project]\nname = \"my-tool\"\n");

        var result = await new PythonProjectDetector().DetectAsync(temp.Path, new[] { "pyproject.toml" });

        Assert.Equal(ProjectType.Python, result!.Project!.ProjectType);
        Assert.Equal("my-tool", result.Project.Name);
        Assert.Equal(DetectionConfidence.Full, result.Project.DetectionConfidence);
    }

    [Fact]
    public async Task Partial_confidence_when_only_requirements_txt_is_present()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("requirements.txt", "requests==2.31.0\n");

        var result = await new PythonProjectDetector().DetectAsync(temp.Path, new[] { "requirements.txt" });

        Assert.Equal(DetectionConfidence.Partial, result!.Project!.DetectionConfidence);
        Assert.NotNull(result.Project.DetectionWarning);
    }

    [Fact]
    public async Task Full_confidence_when_requirements_txt_accompanies_setup_py()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("setup.py", "from setuptools import setup\nsetup()\n");
        temp.WriteFile("requirements.txt", "requests\n");

        var result = await new PythonProjectDetector().DetectAsync(temp.Path, new[] { "setup.py", "requirements.txt" });

        Assert.Equal(DetectionConfidence.Full, result!.Project!.DetectionConfidence);
    }
}
