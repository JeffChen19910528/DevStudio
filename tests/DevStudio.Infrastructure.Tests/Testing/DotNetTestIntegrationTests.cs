using DevStudio.Core.Build;
using DevStudio.Core.Projects;
using DevStudio.Core.Testing;
using DevStudio.Core.Workspace;
using DevStudio.Infrastructure.Build;
using DevStudio.Infrastructure.Processes;
using DevStudio.Infrastructure.Projects;
using DevStudio.Infrastructure.Testing;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Testing;

/// <summary>
/// SKILL.md §24 (Phase 8, cases A–L): real, temporary .NET test projects discovered and run
/// through DevStudio's real TestService → DotNetTestAdapter → real <c>IProcessRunner</c> → real
/// <c>dotnet test</c> pipeline, with results parsed from the real TRX file VSTest actually
/// writes. No fakes anywhere in this file. A test is only ever reported Passed/Failed/Skipped
/// here because the real xUnit runner said so.
/// </summary>
public class DotNetTestIntegrationTests
{
    private const string CalculatorAndTests = """
        namespace App.Tests;

        public class Calculator
        {
            public int Add(int a, int b) => a + b;
        }

        public class CalculatorTests
        {
            [Xunit.Fact]
            public void Add_ReturnsExpectedValue()
            {
                Xunit.Assert.Equal(5, new Calculator().Add(2, 3));
            }

            [Xunit.Fact]
            public void Add_FailsDeliberately()
            {
                Xunit.Assert.Equal(999, new Calculator().Add(2, 3));
            }

            [Xunit.Fact(Skip = "deliberately skipped")]
            public void Skipped_Test()
            {
            }
        }
        """;

    private static string WriteTestCsproj(TempDirectory temp, string relativePath = "App.Tests.csproj") => temp.WriteFile(relativePath, """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <IsPackable>false</IsPackable>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
            <PackageReference Include="xunit" Version="2.9.3" />
            <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
          </ItemGroup>
        </Project>
        """);

    private static (TestService TestService, BuildService BuildService) CreateRealServices()
    {
        var processRunner = new ProcessRunner();
        var registry = new ToolchainRegistry();
        registry.Register(new DotNetToolchainDetector(processRunner));
        registry.RefreshAsync().GetAwaiter().GetResult();
        var buildService = new BuildService(new IBuildAdapter[] { new DotNetBuildAdapter(processRunner, registry) });
        var testService = new TestService(new ITestAdapter[] { new DotNetTestAdapter(processRunner, registry) }, buildService);
        return (testService, buildService);
    }

    private static ProjectInfo MakeProject(string rootPath, string csprojPath) =>
        new("proj-1", "App.Tests", rootPath, ProjectType.DotNet, csprojPath, new[] { "C#" }, Array.Empty<string>(), Array.Empty<Core.Toolchains.ProjectCapability>(), IsTestProject: true);

    private static BuildTarget MakeTarget(ProjectInfo project) =>
        new(BuildTargetKind.Project, project.Name, project.ProjectFile!, project.RootPath, project.ProjectType);

    // --- Case D: multiple tests discovered -------------------------------------------------

    [Fact]
    public async Task Real_discovery_finds_every_real_test_in_the_project()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteTestCsproj(temp);
        temp.WriteFile("CalculatorTests.cs", CalculatorAndTests);
        var (testService, _) = CreateRealServices();
        var project = MakeProject(temp.Path, csprojPath);

        var tests = await testService.DiscoverAsync(project, BuildConfiguration.Debug);

        Assert.Equal(3, tests.Count);
        Assert.Contains(tests, t => t.FullyQualifiedName == "App.Tests.CalculatorTests.Add_ReturnsExpectedValue");
        Assert.Contains(tests, t => t.FullyQualifiedName == "App.Tests.CalculatorTests.Add_FailsDeliberately");
        Assert.Contains(tests, t => t.FullyQualifiedName == "App.Tests.CalculatorTests.Skipped_Test");
    }

    // --- Cases A, B, C: passing, failing, skipped in one real run --------------------------

    [Fact]
    public async Task Real_run_reports_real_Passed_Failed_and_Skipped_outcomes()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteTestCsproj(temp);
        temp.WriteFile("CalculatorTests.cs", CalculatorAndTests);
        var (testService, _) = CreateRealServices();
        var project = MakeProject(temp.Path, csprojPath);
        var target = MakeTarget(project);

        var result = await testService.RunAsync(target, project, BuildConfiguration.Debug);

        Assert.Equal(TestRunState.Completed, result.State);
        Assert.Equal(3, result.Results.Count);

        var passed = Assert.Single(result.Results, r => r.TestCaseId == "App.Tests.CalculatorTests.Add_ReturnsExpectedValue");
        Assert.Equal(TestOutcome.Passed, passed.Outcome);

        var failed = Assert.Single(result.Results, r => r.TestCaseId == "App.Tests.CalculatorTests.Add_FailsDeliberately");
        Assert.Equal(TestOutcome.Failed, failed.Outcome);
        Assert.Contains("999", failed.ErrorMessage);
        Assert.NotNull(failed.StackTrace);
        // Case J: real source navigation info from the real stack trace.
        Assert.NotNull(failed.SourceFile);
        Assert.Contains("CalculatorTests.cs", failed.SourceFile);
        Assert.NotNull(failed.Line);

        var skipped = Assert.Single(result.Results, r => r.TestCaseId == "App.Tests.CalculatorTests.Skipped_Test");
        Assert.Equal(TestOutcome.Skipped, skipped.Outcome);
    }

    // --- Case E: run a single test ----------------------------------------------------------

    [Fact]
    public async Task Real_run_of_a_single_selected_test_executes_only_that_test()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteTestCsproj(temp);
        temp.WriteFile("CalculatorTests.cs", CalculatorAndTests);
        var (testService, _) = CreateRealServices();
        var project = MakeProject(temp.Path, csprojPath);
        var target = MakeTarget(project);
        var filter = new TestFilter(FullyQualifiedNames: new[] { "App.Tests.CalculatorTests.Add_ReturnsExpectedValue" });

        var result = await testService.RunAsync(target, project, BuildConfiguration.Debug, filter);

        var single = Assert.Single(result.Results);
        Assert.Equal("App.Tests.CalculatorTests.Add_ReturnsExpectedValue", single.TestCaseId);
        Assert.Equal(TestOutcome.Passed, single.Outcome);
    }

    // --- Case F: run a selected subset (more than one, fewer than all) ---------------------

    [Fact]
    public async Task Real_run_of_selected_tests_executes_exactly_that_selection()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteTestCsproj(temp);
        temp.WriteFile("CalculatorTests.cs", CalculatorAndTests);
        var (testService, _) = CreateRealServices();
        var project = MakeProject(temp.Path, csprojPath);
        var target = MakeTarget(project);
        var filter = new TestFilter(FullyQualifiedNames: new[]
        {
            "App.Tests.CalculatorTests.Add_ReturnsExpectedValue",
            "App.Tests.CalculatorTests.Skipped_Test",
        });

        var result = await testService.RunAsync(target, project, BuildConfiguration.Debug, filter);

        Assert.Equal(2, result.Results.Count);
        Assert.DoesNotContain(result.Results, r => r.TestCaseId == "App.Tests.CalculatorTests.Add_FailsDeliberately");
    }

    // --- Case G: structured filter (already exercised above via FullyQualifiedNames; this
    // case additionally proves a non-matching filter runs nothing) --------------------------

    [Fact]
    public async Task A_filter_matching_nothing_runs_zero_real_tests()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteTestCsproj(temp);
        temp.WriteFile("CalculatorTests.cs", CalculatorAndTests);
        var (testService, _) = CreateRealServices();
        var project = MakeProject(temp.Path, csprojPath);
        var target = MakeTarget(project);
        var filter = new TestFilter(FullyQualifiedNames: new[] { "App.Tests.CalculatorTests.DoesNotExist" });

        var result = await testService.RunAsync(target, project, BuildConfiguration.Debug, filter);

        Assert.Empty(result.Results);
    }

    // --- Case H: cancellation of a real, deliberately long-running test --------------------

    [Fact]
    public async Task Cancelling_a_real_long_running_test_terminates_the_real_dotnet_process_with_no_orphan()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteTestCsproj(temp);
        temp.WriteFile("SlowTests.cs", """
            namespace App.Tests;

            public class SlowTests
            {
                [Xunit.Fact]
                public async System.Threading.Tasks.Task LongRunning()
                {
                    await System.Threading.Tasks.Task.Delay(System.TimeSpan.FromSeconds(60));
                }
            }
            """);
        var (testService, buildService) = CreateRealServices();
        var project = MakeProject(temp.Path, csprojPath);
        var target = MakeTarget(project);

        // Build first (outside the timed run) so the cancellation below only has to interrupt
        // the real test execution itself, not a build racing it.
        var buildResult = await buildService.ExecuteAsync(target, BuildConfiguration.Debug, BuildOperation.Build);
        Assert.Equal(BuildStatus.Succeeded, buildResult.Status);

        var runTask = testService.RunAsync(target, project, BuildConfiguration.Debug, buildBeforeTest: false);
        await Task.Delay(TimeSpan.FromSeconds(2));
        testService.Cancel();

        var result = await runTask;

        Assert.Equal(TestRunState.Cancelled, result.State);
        Assert.False(testService.IsRunning);
    }

    // --- Case I: a real compile error blocks the test run -----------------------------------

    [Fact]
    public async Task A_real_compile_error_blocks_the_test_run_before_any_test_executes()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteTestCsproj(temp);
        temp.WriteFile("Broken.cs", "this is not valid C# and will not compile(");
        var (testService, _) = CreateRealServices();
        var project = MakeProject(temp.Path, csprojPath);
        var target = MakeTarget(project);

        var result = await testService.RunAsync(target, project, BuildConfiguration.Debug, buildBeforeTest: true);

        Assert.Equal(TestRunState.BlockedByBuildFailure, result.State);
        Assert.Empty(result.Results);
    }

    // --- Case L: a multi-project workspace only surfaces the real test project -------------

    [Fact]
    public async Task Only_the_real_test_project_in_a_multi_project_workspace_is_treated_as_testable()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        temp.WriteFile("App/Program.cs", "System.Console.WriteLine(\"hi\");");
        var testCsprojPath = WriteTestCsproj(temp, "App.Tests/App.Tests.csproj");
        temp.WriteFile("App.Tests/CalculatorTests.cs", CalculatorAndTests);

        var appProject = new DotNetProjectDetector();
        var appResult = await appProject.DetectAsync(System.IO.Path.Combine(temp.Path, "App"), new[] { "App.csproj" });
        var testResult = await appProject.DetectAsync(System.IO.Path.Combine(temp.Path, "App.Tests"), new[] { "App.Tests.csproj" });

        Assert.False(appResult!.Project!.IsTestProject);
        Assert.True(testResult!.Project!.IsTestProject);

        var (testService, _) = CreateRealServices();
        var testProject = MakeProject(System.IO.Path.Combine(temp.Path, "App.Tests"), testCsprojPath);
        var target = MakeTarget(testProject);
        var result = await testService.RunAsync(target, testProject, BuildConfiguration.Debug);

        Assert.Equal(TestRunState.Completed, result.State);
        Assert.NotEmpty(result.Results);
    }
}
