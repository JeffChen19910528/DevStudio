using DevStudio.Core.Projects;
using DevStudio.Infrastructure.Projects;
using DevStudio.Infrastructure.Tests.TestSupport;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Projects;

public class RustProjectDetectorTests
{
    [Fact]
    public async Task Reads_the_package_name_out_of_Cargo_toml()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("Cargo.toml", "[package]\nname = \"my-crate\"\nversion = \"0.1.0\"\n");

        var result = await new RustProjectDetector().DetectAsync(temp.Path, new[] { "Cargo.toml" });

        Assert.Equal(ProjectType.Rust, result!.Project!.ProjectType);
        Assert.Equal("my-crate", result.Project.Name);
    }

    [Fact]
    public async Task Returns_null_without_Cargo_toml()
    {
        var result = await new RustProjectDetector().DetectAsync("/repo", new[] { "main.rs" });
        Assert.Null(result);
    }
}
