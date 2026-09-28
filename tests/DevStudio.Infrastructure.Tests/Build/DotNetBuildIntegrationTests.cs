using DevStudio.Core.Build;
using DevStudio.Core.Diagnostics;
using DevStudio.Core.Projects;
using DevStudio.Core.Workspace;
using DevStudio.Infrastructure.Build;
using DevStudio.Infrastructure.Processes;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Build;

/// <summary>
/// SKILL.md §44–§47: real, temporary .NET projects, built through DevStudio's actual
/// BuildService → DotNetBuildAdapter → real ProcessRunner → real <c>dotnet</c> pipeline. No
/// fakes anywhere in this file. Every temp directory is created fresh and deleted afterward;
/// nothing here touches the DevStudio repository itself.
/// </summary>
public class DotNetBuildIntegrationTests
{
    private const string ValidProgram = """
        System.Console.WriteLine("hello from a real dotnet build");
        """;

    private const string BrokenProgram = """
        System.Console.WriteLine("this line has a deliberate compile error"
        """; // missing closing paren and semicolon

    private static BuildService CreateRealBuildService(out ToolchainRegistry registry)
    {
        var processRunner = new ProcessRunner();
        registry = new ToolchainRegistry();
        registry.Register(new DotNetToolchainDetector(processRunner));
        return new BuildService(new IBuildAdapter[] { new DotNetBuildAdapter(processRunner, registry) });
    }

    private static string WriteMinimalCsproj(TempDirectory temp, string relativePath) => temp.WriteFile(relativePath, """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>
        """);

    [Fact]
    public async Task Real_dotnet_version_probe_succeeds_on_this_machine()
    {
        var processRunner = new ProcessRunner();
        var result = await new DotNetToolchainDetector(processRunner).DetectAsync();

        Assert.Equal(Core.Toolchains.ToolchainDetectionState.Detected, result.State);
    }

    [Fact]
    public async Task A_valid_temporary_project_builds_successfully_through_the_real_pipeline()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteMinimalCsproj(temp, "TempBuild.csproj");
        temp.WriteFile("Program.cs", ValidProgram);

        var buildService = CreateRealBuildService(out var registry);
        await registry.RefreshAsync();

        var target = new BuildTarget(BuildTargetKind.Project, "TempBuild", csprojPath, temp.Path, ProjectType.DotNet);
        var result = await buildService.ExecuteAsync(target, BuildConfiguration.Debug, BuildOperation.Build);

        Assert.Equal(BuildStatus.Succeeded, result.Status);
        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Output));
    }

    [Fact]
    public async Task A_deliberate_compile_error_produces_a_Failed_result_with_a_structured_diagnostic()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteMinimalCsproj(temp, "TempBuild.csproj");
        temp.WriteFile("Program.cs", BrokenProgram);

        var buildService = CreateRealBuildService(out var registry);
        await registry.RefreshAsync();

        var target = new BuildTarget(BuildTargetKind.Project, "TempBuild", csprojPath, temp.Path, ProjectType.DotNet);
        var result = await buildService.ExecuteAsync(target, BuildConfiguration.Debug, BuildOperation.Build);

        Assert.Equal(BuildStatus.Failed, result.Status);
        Assert.NotEqual(0, result.ExitCode);

        // The deliberately malformed line (missing closing paren + semicolon) legitimately
        // produces more than one compiler diagnostic (e.g. CS1026 and CS1002) from the same
        // root cause — assert on the shape of the error(s), not an exact count.
        var errorDiagnostics = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.NotEmpty(errorDiagnostics);
        Assert.All(errorDiagnostics, d =>
        {
            Assert.False(string.IsNullOrEmpty(d.File));
            Assert.True(d.Line > 0);
            Assert.Contains("Program.cs", d.File);
        });
    }

    [Fact]
    public async Task Fixing_the_compile_error_and_building_again_succeeds()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteMinimalCsproj(temp, "TempBuild.csproj");
        var programPath = temp.WriteFile("Program.cs", BrokenProgram);

        var buildService = CreateRealBuildService(out var registry);
        await registry.RefreshAsync();
        var target = new BuildTarget(BuildTargetKind.Project, "TempBuild", csprojPath, temp.Path, ProjectType.DotNet);

        var failedResult = await buildService.ExecuteAsync(target, BuildConfiguration.Debug, BuildOperation.Build);
        Assert.Equal(BuildStatus.Failed, failedResult.Status);

        await File.WriteAllTextAsync(programPath, ValidProgram);
        var succeededResult = await buildService.ExecuteAsync(target, BuildConfiguration.Debug, BuildOperation.Build);

        Assert.Equal(BuildStatus.Succeeded, succeededResult.Status);
    }

    [Fact]
    public async Task A_real_multi_project_solution_builds_with_one_invocation()
    {
        using var temp = new TempDirectory();
        // A plain library — no OutputType, so it must not have (and does not need) a Main.
        temp.WriteFile("Core/Core.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        temp.WriteFile("Core/Class1.cs", "namespace Core; public class Class1 { }");

        var appCsprojPath = temp.WriteFile("App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Core\Core.csproj" />
              </ItemGroup>
            </Project>
            """);
        temp.WriteFile("App/Program.cs", ValidProgram);

        // A real dotnet build needs the full Global/GlobalSection footer (project configuration
        // mappings) to treat the referenced projects as buildable — MSBuild's solution parser
        // silently builds zero projects ("No restorable projects found!") without it, even
        // though Phase 2's own lightweight regex-based detector tolerates the minimal form.
        // Verified by hand against the real dotnet CLI before writing this test.
        var slnPath = temp.WriteFile("TempSolution.sln", """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Core", "Core\Core.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "App\App.csproj", "{22222222-2222-2222-2222-222222222222}"
            EndProject
            Global
                GlobalSection(SolutionConfigurationPlatforms) = preSolution
                    Debug|Any CPU = Debug|Any CPU
                    Release|Any CPU = Release|Any CPU
                EndGlobalSection
                GlobalSection(ProjectConfigurationPlatforms) = postSolution
                    {11111111-1111-1111-1111-111111111111}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                    {11111111-1111-1111-1111-111111111111}.Debug|Any CPU.Build.0 = Debug|Any CPU
                    {11111111-1111-1111-1111-111111111111}.Release|Any CPU.ActiveCfg = Release|Any CPU
                    {11111111-1111-1111-1111-111111111111}.Release|Any CPU.Build.0 = Release|Any CPU
                    {22222222-2222-2222-2222-222222222222}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                    {22222222-2222-2222-2222-222222222222}.Debug|Any CPU.Build.0 = Debug|Any CPU
                    {22222222-2222-2222-2222-222222222222}.Release|Any CPU.ActiveCfg = Release|Any CPU
                    {22222222-2222-2222-2222-222222222222}.Release|Any CPU.Build.0 = Release|Any CPU
                EndGlobalSection
            EndGlobal
            """);

        var buildService = CreateRealBuildService(out var registry);
        await registry.RefreshAsync();

        var target = new BuildTarget(BuildTargetKind.Solution, "TempSolution", slnPath, temp.Path, ProjectType.DotNet);
        var result = await buildService.ExecuteAsync(target, BuildConfiguration.Debug, BuildOperation.Build);

        Assert.Equal(BuildStatus.Succeeded, result.Status);
        Assert.Contains("Core", result.Output);
        Assert.Contains("App", result.Output);
    }

    [Fact]
    public async Task Restore_and_Clean_both_succeed_against_a_real_project()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteMinimalCsproj(temp, "TempBuild.csproj");
        temp.WriteFile("Program.cs", ValidProgram);

        var buildService = CreateRealBuildService(out var registry);
        await registry.RefreshAsync();
        var target = new BuildTarget(BuildTargetKind.Project, "TempBuild", csprojPath, temp.Path, ProjectType.DotNet);

        var restoreResult = await buildService.ExecuteAsync(target, BuildConfiguration.Debug, BuildOperation.Restore);
        Assert.Equal(BuildStatus.Succeeded, restoreResult.Status);

        var buildResult = await buildService.ExecuteAsync(target, BuildConfiguration.Debug, BuildOperation.Build);
        Assert.Equal(BuildStatus.Succeeded, buildResult.Status);

        var cleanResult = await buildService.ExecuteAsync(target, BuildConfiguration.Debug, BuildOperation.Clean);
        Assert.Equal(BuildStatus.Succeeded, cleanResult.Status);
    }

    [Fact]
    public async Task Cancelling_a_real_build_terminates_the_dotnet_process_and_reports_Cancelled()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteMinimalCsproj(temp, "TempBuild.csproj");
        temp.WriteFile("Program.cs", ValidProgram);

        var buildService = CreateRealBuildService(out var registry);
        await registry.RefreshAsync();
        var target = new BuildTarget(BuildTargetKind.Project, "TempBuild", csprojPath, temp.Path, ProjectType.DotNet);

        // Restore first so the actual Build invocation below has real MSBuild/compiler work to
        // do (a bare `dotnet build` on an already-restored, trivial project can complete inside
        // a few hundred ms, which would race the cancellation below).
        await buildService.ExecuteAsync(target, BuildConfiguration.Debug, BuildOperation.Restore);

        using var cts = new CancellationTokenSource();
        var buildTask = buildService.ExecuteAsync(target, BuildConfiguration.Debug, BuildOperation.Rebuild, cancellationToken: cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var result = await buildTask;

        Assert.Equal(BuildStatus.Cancelled, result.Status);
        Assert.False(buildService.IsRunning);
    }
}
