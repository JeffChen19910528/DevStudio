using System.Diagnostics;
using DevStudio.Core.Build;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Run;
using DevStudio.Core.Workspace;
using DevStudio.Infrastructure.Build;
using DevStudio.Infrastructure.Processes;
using DevStudio.Infrastructure.Run;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Run;

/// <summary>
/// SKILL.md §44–§47 (Phase 5): real, temporary .NET console applications launched through
/// DevStudio's actual RunService → DotNetRunAdapter → real ProcessRunner → real <c>dotnet</c>
/// pipeline. No fakes anywhere in this file — every success asserted here comes from a real
/// external process actually starting, running, and (where relevant) exiting or being killed.
/// </summary>
public class DotNetRunIntegrationTests
{
    private static string WriteMinimalExeCsproj(TempDirectory temp, string relativePath) => temp.WriteFile(relativePath, """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>
        """);

    private static (RunService RunService, BuildService BuildService) CreateRealServices()
    {
        var processRunner = new ProcessRunner();
        var registry = new ToolchainRegistry();
        registry.Register(new DotNetToolchainDetector(processRunner));
        registry.RefreshAsync().GetAwaiter().GetResult();
        var buildService = new BuildService(new IBuildAdapter[] { new DotNetBuildAdapter(processRunner, registry) });
        var runService = new RunService(new IRunAdapter[] { new DotNetRunAdapter(processRunner, registry) }, buildService);
        return (runService, buildService);
    }

    private sealed class CapturingOutputSink : IProcessOutputSink
    {
        private readonly List<string> _lines = new();
        public IReadOnlyList<string> Lines => _lines;
        public void OnStandardOutput(string line) { lock (_lines) _lines.Add(line); }
        public void OnStandardError(string line) { lock (_lines) _lines.Add(line); }
        public bool Contains(string fragment) { lock (_lines) return _lines.Any(l => l.Contains(fragment)); }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 60_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(50);
        Assert.True(condition(), "Condition was not met within the timeout.");
    }

    [Fact]
    public async Task A_real_built_application_starts_and_prints_its_arguments_and_environment_variable()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteMinimalExeCsproj(temp, "App.csproj");
        temp.WriteFile("Program.cs", """
            System.Console.WriteLine("run-integration-started");
            foreach (var a in args) System.Console.WriteLine($"arg:{a}");
            System.Console.WriteLine($"env:{System.Environment.GetEnvironmentVariable("DEVSTUDIO_TEST_VAR")}");
            System.Console.WriteLine($"cwd:{System.Environment.CurrentDirectory}");
            return 0;
            """);

        var (runService, buildService) = CreateRealServices();
        var target = new BuildTarget(BuildTargetKind.Project, "App", csprojPath, temp.Path, ProjectType.DotNet);
        var configuration = new RunConfiguration(
            "App", target, BuildConfiguration.Debug,
            Arguments: new[] { "--name", "hello world", "--count", "3" },
            EnvironmentVariables: new Dictionary<string, string> { ["DEVSTUDIO_TEST_VAR"] = "real-value" });

        var sink = new CapturingOutputSink();
        RunResult? completed = null;
        runService.Completed += (_, result) => completed = result;

        await runService.StartAsync(configuration, runOutputSink: sink);
        await WaitUntilAsync(() => completed is not null);

        Assert.Equal(RunStatus.Exited, completed!.Status);
        Assert.Equal(0, completed.ExitCode);
        Assert.True(sink.Contains("run-integration-started"));
        Assert.True(sink.Contains("arg:--name"));
        Assert.True(sink.Contains("arg:hello world"));
        Assert.True(sink.Contains("arg:--count"));
        Assert.True(sink.Contains("arg:3"));
        Assert.True(sink.Contains("env:real-value"));
        Assert.Contains(sink.Lines, l => l.Contains("cwd:") && l.Contains(temp.Path));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task A_real_application_exit_code_is_captured_exactly(int exitCode)
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteMinimalExeCsproj(temp, "App.csproj");
        temp.WriteFile("Program.cs", $"return {exitCode};");

        var (runService, _) = CreateRealServices();
        var target = new BuildTarget(BuildTargetKind.Project, "App", csprojPath, temp.Path, ProjectType.DotNet);
        var configuration = new RunConfiguration("App", target, BuildConfiguration.Debug);

        RunResult? completed = null;
        runService.Completed += (_, result) => completed = result;
        await runService.StartAsync(configuration);
        await WaitUntilAsync(() => completed is not null);

        Assert.Equal(RunStatus.Exited, completed!.Status);
        Assert.Equal(exitCode, completed.ExitCode);
    }

    [Fact]
    public async Task Stop_kills_the_real_process_tree_so_the_child_process_no_longer_exists()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteMinimalExeCsproj(temp, "App.csproj");
        var pidFile = Path.Combine(temp.Path, "pid.txt");
        var program = """
            System.IO.File.WriteAllText(@"__PID_FILE__", System.Environment.ProcessId.ToString());
            while (true) { System.Threading.Thread.Sleep(200); }
            """.Replace("__PID_FILE__", pidFile);
        temp.WriteFile("Program.cs", program);

        var (runService, _) = CreateRealServices();
        var target = new BuildTarget(BuildTargetKind.Project, "App", csprojPath, temp.Path, ProjectType.DotNet);
        var configuration = new RunConfiguration("App", target, BuildConfiguration.Debug);

        RunResult? completed = null;
        runService.Completed += (_, result) => completed = result;
        await runService.StartAsync(configuration);
        await WaitUntilAsync(() => File.Exists(pidFile));

        var childPid = int.Parse(await File.ReadAllTextAsync(pidFile));
        Assert.True(IsProcessAlive(childPid)); // sanity: child really started before we try to stop it

        runService.Stop();
        await WaitUntilAsync(() => completed is not null);

        Assert.Equal(RunStatus.Terminated, completed!.Status);
        await WaitUntilAsync(() => !IsProcessAlive(childPid));
    }

    [Fact]
    public async Task Restart_never_runs_two_instances_at_once()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteMinimalExeCsproj(temp, "App.csproj");
        var lockFile = Path.Combine(temp.Path, "lock.txt");
        var program = """
            var lockFile = @"__LOCK_FILE__";
            var myPid = System.Environment.ProcessId;
            if (System.IO.File.Exists(lockFile))
            {
                var existingPidText = System.IO.File.ReadAllText(lockFile).Trim();
                if (int.TryParse(existingPidText, out var existingPid))
                {
                    try
                    {
                        var existingProcess = System.Diagnostics.Process.GetProcessById(existingPid);
                        if (!existingProcess.HasExited)
                        {
                            System.Console.WriteLine("CONFLICT:" + existingPid);
                            return 99;
                        }
                    }
                    catch (ArgumentException) { }
                }
            }
            System.IO.File.WriteAllText(lockFile, myPid.ToString());
            System.Console.WriteLine("STARTED:" + myPid);
            while (true) { System.Threading.Thread.Sleep(200); }
            """.Replace("__LOCK_FILE__", lockFile);
        temp.WriteFile("Program.cs", program);

        var (runService, _) = CreateRealServices();
        var target = new BuildTarget(BuildTargetKind.Project, "App", csprojPath, temp.Path, ProjectType.DotNet);
        var configuration = new RunConfiguration("App", target, BuildConfiguration.Debug);

        var firstSink = new CapturingOutputSink();
        await runService.StartAsync(configuration, runOutputSink: firstSink);
        await WaitUntilAsync(() => firstSink.Lines.Any(l => l.StartsWith("STARTED:")));

        var secondSink = new CapturingOutputSink();
        await runService.RestartAsync(configuration, runOutputSink: secondSink);
        await WaitUntilAsync(() => secondSink.Lines.Any(l => l.StartsWith("STARTED:") || l.StartsWith("CONFLICT:")));

        Assert.False(secondSink.Contains("CONFLICT"));
        Assert.Contains(secondSink.Lines, l => l.StartsWith("STARTED:"));
        Assert.Equal(RunStatus.Running, runService.Status);

        runService.Stop();
        await WaitUntilAsync(() => runService.Status == RunStatus.Terminated);
    }

    [Fact]
    public async Task A_failed_build_blocks_the_real_run_and_the_application_never_starts()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteMinimalExeCsproj(temp, "App.csproj");
        temp.WriteFile("Program.cs", """this is not valid C# and will not compile(""");

        var (runService, _) = CreateRealServices();
        var target = new BuildTarget(BuildTargetKind.Project, "App", csprojPath, temp.Path, ProjectType.DotNet);
        var configuration = new RunConfiguration("App", target, BuildConfiguration.Debug, BuildBeforeRun: true);

        var sink = new CapturingOutputSink();
        await runService.StartAsync(configuration, runOutputSink: sink);

        Assert.Equal(RunStatus.FailedToStart, runService.Status);
        Assert.Equal("Run aborted because build failed.", runService.LastResult?.Message);
        Assert.False(sink.Contains("run-integration-started"));
    }

    [Fact]
    public async Task Running_without_building_first_fails_honestly_instead_of_silently_building()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteMinimalExeCsproj(temp, "App.csproj");
        temp.WriteFile("Program.cs", """System.Console.WriteLine("should never run"); return 0;""");

        var (runService, _) = CreateRealServices();
        var target = new BuildTarget(BuildTargetKind.Project, "App", csprojPath, temp.Path, ProjectType.DotNet);
        var configuration = new RunConfiguration("App", target, BuildConfiguration.Debug, BuildBeforeRun: false);

        await runService.StartAsync(configuration);

        Assert.Equal(RunStatus.FailedToStart, runService.Status);
        Assert.Contains("has not been built", runService.LastResult?.Message ?? string.Empty);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "bin", "Debug")));
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
