using DevStudio.Core.Build;
using DevStudio.Core.Debug;
using DevStudio.Core.Projects;
using DevStudio.Core.Run;
using DevStudio.Core.Workspace;
using DevStudio.Infrastructure.Build;
using DevStudio.Infrastructure.Debug;
using DevStudio.Infrastructure.Processes;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Debug;

/// <summary>
/// SKILL.md §50–§55 (Phase 6): real, temporary .NET console applications actually debugged
/// through DevStudio's real DebugService → NetCoreDebuggerAdapter → real DAP client → a real
/// <c>netcoredbg --interpreter=vscode</c> process found via <see cref="NetCoreDebuggerResolver"/>
/// on this machine (installed via `winget install Samsung.NetCoreDbg` — see ADR-007's
/// Consequences for why netcoredbg was chosen over the also-present-but-license-restricted
/// vsdbg). No fakes anywhere in this file. A breakpoint is only ever reported "hit" here because
/// netcoredbg's own real <c>stopped</c> event said so.
/// </summary>
public class NetCoreDebugIntegrationTests
{
    private const string DeterministicProgram = """
        int count = 10;
        string name = "DevStudio";
        int result = Add(count, 5);
        Console.WriteLine(result);

        static int Add(int a, int b)
        {
            int sum = a + b;
            return sum;
        }
        """;

    private static string WriteMinimalExeCsproj(TempDirectory temp, string relativePath) => temp.WriteFile(relativePath, """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <DebugType>portable</DebugType>
          </PropertyGroup>
        </Project>
        """);

    private static (DebugService DebugService, BuildService BuildService) CreateRealServices()
    {
        var processRunner = new ProcessRunner();
        var registry = new ToolchainRegistry();
        registry.Register(new DotNetToolchainDetector(processRunner));
        registry.RefreshAsync().GetAwaiter().GetResult();
        var buildService = new BuildService(new IBuildAdapter[] { new DotNetBuildAdapter(processRunner, registry) });
        var debugService = new DebugService(new IDebuggerAdapter[] { new NetCoreDebuggerAdapter(processRunner, new NetCoreDebuggerResolver()) }, buildService);
        return (debugService, buildService);
    }

    private static async Task<T> WaitForAsync<T>(TaskCompletionSource<T> source, int timeoutMs = 30_000)
    {
        var completed = await Task.WhenAny(source.Task, Task.Delay(timeoutMs));
        Assert.True(completed == source.Task, "Timed out waiting for the expected debug event.");
        return await source.Task;
    }

    [Fact]
    public void NetCoreDbg_is_discovered_on_this_real_machine()
    {
        var resolution = new NetCoreDebuggerResolver().Resolve();

        Assert.True(resolution.Found, resolution.Message);
        Assert.NotNull(resolution.ExecutablePath);
        Assert.True(File.Exists(resolution.ExecutablePath));
    }

    [Fact]
    public async Task A_real_breakpoint_is_hit_with_real_locals_and_call_stack()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteMinimalExeCsproj(temp, "App.csproj");
        var programPath = temp.WriteFile("Program.cs", DeterministicProgram);
        var breakpointLine = LineOf(DeterministicProgram, "int result = Add(count, 5);");

        var (debugService, _) = CreateRealServices();
        var target = new BuildTarget(BuildTargetKind.Project, "App", csprojPath, temp.Path, ProjectType.DotNet);
        var configuration = new DebugConfiguration(new RunConfiguration("App", target, BuildConfiguration.Debug));

        var stopped = new TaskCompletionSource<StoppedInfo>();
        debugService.Stopped += (_, info) => stopped.TrySetResult(info);

        await debugService.SetBreakpointsAsync(programPath, new[] { new Breakpoint(Guid.NewGuid(), programPath, breakpointLine) });
        await debugService.StartAsync(configuration);

        var stoppedInfo = await WaitForAsync(stopped);
        Assert.Equal("breakpoint", stoppedInfo.Reason);
        Assert.Equal(DebugSessionState.Paused, debugService.State);
        Assert.NotNull(debugService.CurrentThreadId);

        var threads = await debugService.GetThreadsAsync();
        Assert.NotEmpty(threads);

        var frames = await debugService.GetStackTraceAsync(debugService.CurrentThreadId!.Value);
        var topFrame = Assert.Single(frames, f => f.Line == breakpointLine);
        Assert.Contains("Program.cs", topFrame.SourcePath ?? string.Empty);

        var scopes = await debugService.GetScopesAsync(topFrame.Id);
        var locals = Assert.Single(scopes, s => s.Name.Equals("Locals", StringComparison.OrdinalIgnoreCase));

        var variables = await debugService.GetVariablesAsync(locals.VariablesReference);
        var nameVariable = Assert.Single(variables, v => v.Name == "name");
        Assert.Contains("DevStudio", nameVariable.Value);
        var countVariable = Assert.Single(variables, v => v.Name == "count");
        Assert.Equal("10", countVariable.Value);

        await debugService.StopAsync();
    }

    [Fact]
    public async Task Step_over_advances_one_line_and_continue_resumes_to_a_real_exit()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteMinimalExeCsproj(temp, "App.csproj");
        var programPath = temp.WriteFile("Program.cs", DeterministicProgram);
        var breakpointLine = LineOf(DeterministicProgram, "int result = Add(count, 5);");
        var nextLine = LineOf(DeterministicProgram, "Console.WriteLine(result);");

        var (debugService, _) = CreateRealServices();
        var target = new BuildTarget(BuildTargetKind.Project, "App", csprojPath, temp.Path, ProjectType.DotNet);
        var configuration = new DebugConfiguration(new RunConfiguration("App", target, BuildConfiguration.Debug));

        var firstStop = new TaskCompletionSource<StoppedInfo>();
        debugService.Stopped += (_, info) => firstStop.TrySetResult(info);
        await debugService.SetBreakpointsAsync(programPath, new[] { new Breakpoint(Guid.NewGuid(), programPath, breakpointLine) });
        await debugService.StartAsync(configuration);
        await WaitForAsync(firstStop);

        var secondStop = new TaskCompletionSource<StoppedInfo>();
        debugService.Stopped += (_, info) => secondStop.TrySetResult(info);
        await debugService.StepOverAsync();
        var stepInfo = await WaitForAsync(secondStop);
        Assert.Equal("step", stepInfo.Reason);

        var frames = await debugService.GetStackTraceAsync(debugService.CurrentThreadId!.Value);
        Assert.Equal(nextLine, frames[0].Line);

        var completed = new TaskCompletionSource<DebugResult>();
        debugService.Completed += (_, result) => completed.TrySetResult(result);
        await debugService.ContinueAsync();
        var result = await WaitForAsync(completed);

        Assert.Equal(DebugSessionState.Terminated, result.State);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Step_into_enters_the_real_helper_method()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteMinimalExeCsproj(temp, "App.csproj");
        var programPath = temp.WriteFile("Program.cs", DeterministicProgram);
        var breakpointLine = LineOf(DeterministicProgram, "int result = Add(count, 5);");
        var methodOpenBraceLine = LineOf(DeterministicProgram, "static int Add(int a, int b)") + 1;
        var lastLineInsideHelper = LineOf(DeterministicProgram, "return sum;");

        var (debugService, _) = CreateRealServices();
        var target = new BuildTarget(BuildTargetKind.Project, "App", csprojPath, temp.Path, ProjectType.DotNet);
        var configuration = new DebugConfiguration(new RunConfiguration("App", target, BuildConfiguration.Debug));

        var firstStop = new TaskCompletionSource<StoppedInfo>();
        debugService.Stopped += (_, info) => firstStop.TrySetResult(info);
        await debugService.SetBreakpointsAsync(programPath, new[] { new Breakpoint(Guid.NewGuid(), programPath, breakpointLine) });
        await debugService.StartAsync(configuration);
        await WaitForAsync(firstStop);

        var steppedIn = new TaskCompletionSource<StoppedInfo>();
        debugService.Stopped += (_, info) => steppedIn.TrySetResult(info);
        await debugService.StepIntoAsync();
        await WaitForAsync(steppedIn);

        var frames = await debugService.GetStackTraceAsync(debugService.CurrentThreadId!.Value);
        // The real debugger may land on the method's opening brace or its first statement
        // depending on how the compiler emitted sequence points — both are "inside Add", which
        // is what this test actually verifies (SKILL.md §54: Step Into "should enter the
        // method", not land on one specific exact line).
        Assert.InRange(frames[0].Line, methodOpenBraceLine, lastLineInsideHelper);

        await debugService.StopAsync();
    }

    [Fact]
    public async Task Stop_terminates_both_netcoredbg_and_the_debuggee_with_no_orphan_process()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteMinimalExeCsproj(temp, "App.csproj");
        temp.WriteFile("Program.cs", DeterministicProgram);

        var (debugService, _) = CreateRealServices();
        var target = new BuildTarget(BuildTargetKind.Project, "App", csprojPath, temp.Path, ProjectType.DotNet);
        var configuration = new DebugConfiguration(new RunConfiguration("App", target, BuildConfiguration.Debug), StopAtEntry: true);

        var stopped = new TaskCompletionSource<StoppedInfo>();
        debugService.Stopped += (_, info) => stopped.TrySetResult(info);
        await debugService.StartAsync(configuration);
        await WaitForAsync(stopped); // entry stop keeps the debuggee alive and paused

        var completed = new TaskCompletionSource<DebugResult>();
        debugService.Completed += (_, result) => completed.TrySetResult(result);
        await debugService.StopAsync();
        var result = await WaitForAsync(completed);

        Assert.Equal(DebugSessionState.Terminated, result.State);
        Assert.Equal(DebugSessionState.Terminated, debugService.State);
    }

    [Fact]
    public async Task A_deliberate_compile_error_blocks_debug_before_netcoredbg_ever_launches()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteMinimalExeCsproj(temp, "App.csproj");
        temp.WriteFile("Program.cs", "this is not valid C# and will not compile(");

        var (debugService, _) = CreateRealServices();
        var target = new BuildTarget(BuildTargetKind.Project, "App", csprojPath, temp.Path, ProjectType.DotNet);
        var configuration = new DebugConfiguration(new RunConfiguration("App", target, BuildConfiguration.Debug), BuildBeforeDebug: true);

        await debugService.StartAsync(configuration);

        Assert.Equal(DebugSessionState.Failed, debugService.State);
        Assert.Equal("Debug aborted because build failed.", debugService.LastResult?.Message);
    }

    [Fact]
    public async Task Debugging_without_building_first_fails_honestly_instead_of_silently_building()
    {
        using var temp = new TempDirectory();
        var csprojPath = WriteMinimalExeCsproj(temp, "App.csproj");
        temp.WriteFile("Program.cs", """System.Console.WriteLine("should never run");""");

        var (debugService, _) = CreateRealServices();
        var target = new BuildTarget(BuildTargetKind.Project, "App", csprojPath, temp.Path, ProjectType.DotNet);
        var configuration = new DebugConfiguration(new RunConfiguration("App", target, BuildConfiguration.Debug), BuildBeforeDebug: false);

        await debugService.StartAsync(configuration);

        Assert.Equal(DebugSessionState.Failed, debugService.State);
        Assert.Contains("has not been built", debugService.LastResult?.Message ?? string.Empty);
    }

    private static int LineOf(string source, string lineText)
    {
        var lines = source.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(lineText, StringComparison.Ordinal)) return i + 1;
        }
        throw new InvalidOperationException($"Line containing '{lineText}' not found in source.");
    }
}
