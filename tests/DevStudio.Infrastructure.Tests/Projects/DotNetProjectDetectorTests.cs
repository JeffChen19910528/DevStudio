using DevStudio.Core.Projects;
using DevStudio.Infrastructure.Projects;
using DevStudio.Infrastructure.Tests.TestSupport;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Projects;

public class DotNetProjectDetectorTests
{
    [Fact]
    public async Task Detects_a_csproj_as_a_CSharp_DotNet_project()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var result = await new DotNetProjectDetector().DetectAsync(temp.Path, new[] { "App.csproj" });

        Assert.NotNull(result?.Project);
        Assert.Equal(ProjectType.DotNet, result!.Project!.ProjectType);
        Assert.Equal("App", result.Project.Name);
        Assert.Equal(new[] { "C#" }, result.Project.Languages);
    }

    [Fact]
    public async Task Detects_an_fsproj_as_FSharp()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("App.fsproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var result = await new DotNetProjectDetector().DetectAsync(temp.Path, new[] { "App.fsproj" });

        Assert.Equal(new[] { "F#" }, result!.Project!.Languages);
    }

    [Fact]
    public async Task Returns_null_when_no_project_file_is_present()
    {
        var result = await new DotNetProjectDetector().DetectAsync("/repo", new[] { "README.md" });
        Assert.Null(result);
    }

    [Fact]
    public async Task A_project_with_OutputType_Exe_is_reported_as_executable()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>");

        var result = await new DotNetProjectDetector().DetectAsync(temp.Path, new[] { "App.csproj" });

        Assert.True(result!.Project!.IsExecutable);
    }

    [Fact]
    public async Task A_project_with_OutputType_WinExe_is_reported_as_executable()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>WinExe</OutputType></PropertyGroup></Project>");

        var result = await new DotNetProjectDetector().DetectAsync(temp.Path, new[] { "App.csproj" });

        Assert.True(result!.Project!.IsExecutable);
    }

    [Fact]
    public async Task A_class_library_with_no_OutputType_is_never_reported_as_executable()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var result = await new DotNetProjectDetector().DetectAsync(temp.Path, new[] { "Lib.csproj" });

        Assert.False(result!.Project!.IsExecutable);
    }

    [Fact]
    public async Task A_project_with_OutputType_Library_is_never_reported_as_executable()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Library</OutputType></PropertyGroup></Project>");

        var result = await new DotNetProjectDetector().DetectAsync(temp.Path, new[] { "Lib.csproj" });

        Assert.False(result!.Project!.IsExecutable);
    }

    [Fact]
    public async Task An_ASPNET_Core_web_SDK_project_with_no_explicit_OutputType_is_reported_as_executable()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("Web.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var result = await new DotNetProjectDetector().DetectAsync(temp.Path, new[] { "Web.csproj" });

        Assert.True(result!.Project!.IsExecutable);
    }

    [Fact]
    public async Task A_project_referencing_Microsoft_NET_Test_Sdk_is_reported_as_a_test_project()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("App.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
                <PackageReference Include="xunit" Version="2.9.3" />
              </ItemGroup>
            </Project>
            """);

        var result = await new DotNetProjectDetector().DetectAsync(temp.Path, new[] { "App.Tests.csproj" });

        Assert.True(result!.Project!.IsTestProject);
    }

    [Theory]
    [InlineData("NUnit3TestAdapter")]
    [InlineData("MSTest.TestAdapter")]
    [InlineData("MSTest.TestFramework")]
    public async Task A_project_referencing_a_known_test_framework_package_is_reported_as_a_test_project(string packageName)
    {
        using var temp = new TempDirectory();
        temp.WriteFile("App.Tests.csproj", $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="{packageName}" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        var result = await new DotNetProjectDetector().DetectAsync(temp.Path, new[] { "App.Tests.csproj" });

        Assert.True(result!.Project!.IsTestProject);
    }

    [Fact]
    public async Task A_normal_application_project_is_never_reported_as_a_test_project()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>");

        var result = await new DotNetProjectDetector().DetectAsync(temp.Path, new[] { "App.csproj" });

        Assert.False(result!.Project!.IsTestProject);
    }

    [Fact]
    public async Task A_class_library_with_no_test_evidence_is_never_reported_as_a_test_project()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var result = await new DotNetProjectDetector().DetectAsync(temp.Path, new[] { "Lib.csproj" });

        Assert.False(result!.Project!.IsTestProject);
    }

    [Fact]
    public async Task Project_references_are_read_as_absolute_paths()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\Business\Business.csproj" />
                <ProjectReference Include="..\DataAccess\DataAccess.csproj" />
              </ItemGroup>
            </Project>
            """);

        var result = await new DotNetProjectDetector().DetectAsync(temp.Path, new[] { "App.csproj" });

        Assert.Equal(2, result!.Project!.ProjectReferenceFilePaths.Count);
        Assert.All(result.Project.ProjectReferenceFilePaths, p => Assert.True(Path.IsPathRooted(p)));
        Assert.Contains(result.Project.ProjectReferenceFilePaths, p => p.EndsWith($"{Path.DirectorySeparatorChar}Business.csproj", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Project.ProjectReferenceFilePaths, p => p.EndsWith($"{Path.DirectorySeparatorChar}DataAccess.csproj", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_project_with_no_project_references_has_an_empty_list()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var result = await new DotNetProjectDetector().DetectAsync(temp.Path, new[] { "App.csproj" });

        Assert.Empty(result!.Project!.ProjectReferenceFilePaths);
    }
}
