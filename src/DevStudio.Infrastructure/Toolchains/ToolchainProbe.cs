using System.ComponentModel;
using DevStudio.Core.Processes;

namespace DevStudio.Infrastructure.Toolchains;

public enum ToolchainProbeOutcome
{
    Success,
    NotFound,
    TimedOut,
    Failed
}

public sealed record ToolchainProbeResult(ToolchainProbeOutcome Outcome, string CombinedOutput, int ExitCode)
{
    public static ToolchainProbeResult NotFound() => new(ToolchainProbeOutcome.NotFound, string.Empty, -1);
    public static ToolchainProbeResult TimedOut() => new(ToolchainProbeOutcome.TimedOut, string.Empty, -1);
    public static ToolchainProbeResult Failed(string output, int exitCode) => new(ToolchainProbeOutcome.Failed, output, exitCode);
    public static ToolchainProbeResult Success(string output, int exitCode) => new(ToolchainProbeOutcome.Success, output, exitCode);
}

/// <summary>
/// Runs a known, fixed version-probe command (e.g. <c>dotnet --version</c>) through <see
/// cref="IProcessRunner"/> with a timeout, and turns every failure mode — missing executable,
/// timeout, non-zero exit, permission error — into a typed result instead of an exception
/// (SKILL.md §6, §27, §28). This is the one place every toolchain detector shares so that
/// "how do we safely probe an executable" is implemented once.
/// </summary>
public static class ToolchainProbe
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public static async Task<ToolchainProbeResult> RunAsync(
        IProcessRunner processRunner,
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new ProcessStartRequest(
                executable,
                arguments,
                Directory.GetCurrentDirectory(),
                Timeout: timeout ?? DefaultTimeout);

            var result = await processRunner.RunAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result.WasTimedOut) return ToolchainProbeResult.TimedOut();

            var combined = result.StandardOutput + result.StandardError;

            return result.ExitCode == 0
                ? ToolchainProbeResult.Success(combined, result.ExitCode)
                : ToolchainProbeResult.Failed(combined, result.ExitCode);
        }
        catch (Win32Exception)
        {
            // Thrown by the underlying Process.Start when the executable cannot be found —
            // cross-platform equivalent of "not on PATH", not a crash-worthy condition here.
            return ToolchainProbeResult.NotFound();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return ToolchainProbeResult.NotFound();
        }
    }
}
