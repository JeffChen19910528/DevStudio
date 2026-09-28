using DevStudio.Core.Projects;
using DevStudio.Infrastructure.Projects;
using DevStudio.Infrastructure.Tests.TestSupport;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Projects;

public class DotNetSolutionDetectorTests
{
    private const string SlnContent = """
        Microsoft Visual Studio Solution File, Format Version 12.00
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "src\App\App.csproj", "{11111111-1111-1111-1111-111111111111}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Tests", "tests\Tests\Tests.csproj", "{22222222-2222-2222-2222-222222222222}"
        EndProject
        """;

    [Fact]
    public async Task Parses_referenced_csproj_paths_out_of_a_classic_sln_file()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("App.sln", SlnContent);

        var result = await new DotNetSolutionDetector().DetectAsync(temp.Path, new[] { "App.sln" });

        Assert.NotNull(result?.Solution);
        Assert.Equal(2, result!.Solution!.ReferencedProjectFilePaths.Count);
        Assert.Contains(result.Solution.ReferencedProjectFilePaths, p => p.Replace('\\', '/').EndsWith("src/App/App.csproj"));
        Assert.Equal(DetectionConfidence.Full, result.Solution.DetectionConfidence);
    }

    [Fact]
    public async Task Parses_referenced_csproj_paths_out_of_a_slnx_file()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("App.slnx", """
            <Solution>
              <Project Path="src/App/App.csproj" />
              <Folder Name="/tests/">
                <Project Path="tests/Tests/Tests.csproj" />
              </Folder>
            </Solution>
            """);

        var result = await new DotNetSolutionDetector().DetectAsync(temp.Path, new[] { "App.slnx" });

        Assert.Equal(2, result!.Solution!.ReferencedProjectFilePaths.Count);
    }

    [Fact]
    public async Task Reports_partial_confidence_for_an_unparsable_slnx_file()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("Broken.slnx", "<Solution><Project Path=");

        var result = await new DotNetSolutionDetector().DetectAsync(temp.Path, new[] { "Broken.slnx" });

        Assert.Equal(DetectionConfidence.Partial, result!.Solution!.DetectionConfidence);
        Assert.NotNull(result.Solution.DetectionWarning);
    }

    [Fact]
    public async Task Returns_null_when_no_solution_file_is_present()
    {
        var result = await new DotNetSolutionDetector().DetectAsync("/repo", new[] { "App.csproj" });
        Assert.Null(result);
    }
}
