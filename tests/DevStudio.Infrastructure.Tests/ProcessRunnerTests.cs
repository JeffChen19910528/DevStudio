using DevStudio.Core.Processes;
using DevStudio.Infrastructure.Processes;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests;

public class ProcessRunnerTests
{
    /// <summary>Not every real machine has a bare <c>python</c> on PATH (SKILL.md §6 [Phase 11])
    /// — this Ubuntu WSL environment, for one, ships only <c>python3</c>. Mirrors
    /// <c>PythonToolchainDetector</c>'s own real fallback rather than hard-coding one name, a
    /// real portability bug this exact test caught when first run on real Linux.</summary>
    private static string ResolveRealPythonInterpreter() =>
        ExecutableLocator.FindOnPath("python") ?? ExecutableLocator.FindOnPath("python3")
        ?? throw new InvalidOperationException("Neither 'python' nor 'python3' was found on PATH — this test requires a real Python interpreter.");

    [Fact]
    public async Task RunAsync_captures_exit_code_and_stdout_for_a_real_process()
    {
        var runner = new ProcessRunner();
        var request = new ProcessStartRequest("dotnet", new[] { "--version" }, Directory.GetCurrentDirectory());

        var result = await runner.RunAsync(request);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.WasCancelled);
        Assert.False(result.WasTimedOut);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardOutput));
    }

    [Fact]
    public async Task RunAsync_reports_a_non_zero_exit_code_for_a_failing_invocation()
    {
        var runner = new ProcessRunner();
        var request = new ProcessStartRequest("dotnet", new[] { "this-is-not-a-real-command" }, Directory.GetCurrentDirectory());

        var result = await runner.RunAsync(request);

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task WriteInputAsync_delivers_text_to_a_redirected_process_stdin()
    {
        // Uses python (verified present, SKILL.md §44 environment inspection) to read exactly
        // one line and print it, so the child process exits on its own once it has echoed the
        // input — no race against Kill() needed to observe the output.
        var runner = new ProcessRunner();
        var request = new ProcessStartRequest(
            ResolveRealPythonInterpreter(),
            new[] { "-c", "import sys; print(sys.stdin.readline().strip())" },
            Directory.GetCurrentDirectory(),
            RedirectInput: true);

        var running = runner.Start(request);
        await running.WriteInputAsync("hello from the test");

        var result = await running.WaitForExitAsync();
        await running.DisposeAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello from the test", result.StandardOutput);
    }

    [Fact]
    public async Task Kill_terminates_a_long_running_process_before_it_would_exit_naturally()
    {
        var runner = new ProcessRunner();
        var request = OperatingSystem.IsWindows()
            ? new ProcessStartRequest("cmd.exe", new[] { "/c", "timeout", "/t", "30" }, Directory.GetCurrentDirectory())
            : new ProcessStartRequest("/bin/sh", new[] { "-c", "sleep 30" }, Directory.GetCurrentDirectory());

        var running = runner.Start(request);
        Assert.False(running.HasExited);

        running.Kill();
        var result = await running.WaitForExitAsync();
        await running.DisposeAsync();

        Assert.True(running.HasExited);
        Assert.NotEqual(0, result.ExitCode);
    }
}
