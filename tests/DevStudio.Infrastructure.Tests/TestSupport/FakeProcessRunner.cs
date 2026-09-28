using System.ComponentModel;
using DevStudio.Core.Processes;

namespace DevStudio.Infrastructure.Tests.TestSupport;

/// <summary>
/// Simulates toolchain probes without touching the real machine (SKILL.md §31: "use fake
/// process runners to simulate Rust missing, Go missing, ..."). Register a canned <see
/// cref="ProcessResult"/> per executable name, or leave it unregistered to simulate "not on
/// PATH" (throws <see cref="Win32Exception"/>, exactly like a real missing executable would).
/// </summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Dictionary<string, ProcessResult> _resultsByExecutable = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _timeoutExecutables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<ProcessStartRequest, FakeRunningProcess>> _startFactoriesByExecutable = new(StringComparer.OrdinalIgnoreCase);
    public List<ProcessStartRequest> Requests { get; } = new();
    public List<ProcessStartRequest> StartRequests { get; } = new();
    public List<FakeRunningProcess> StartedProcesses { get; } = new();

    public void SetResult(string executable, int exitCode, string standardOutput, string standardError = "") =>
        _resultsByExecutable[executable] = new ProcessResult(exitCode, standardOutput, standardError, TimeSpan.Zero, false, false);

    public void SetTimeout(string executable) => _timeoutExecutables.Add(executable);

    /// <summary>Registers a factory invoked each time <see cref="Start"/> is called for the
    /// given executable, so a Run-style test (SKILL.md §7 [Phase 5]) can control exactly when
    /// the returned <see cref="FakeRunningProcess"/> "exits".</summary>
    public void SetStartBehavior(string executable, Func<ProcessStartRequest, FakeRunningProcess> factory) =>
        _startFactoriesByExecutable[executable] = factory;

    public IRunningProcess Start(ProcessStartRequest request, IProcessOutputSink? outputSink = null)
    {
        StartRequests.Add(request);

        if (!_startFactoriesByExecutable.TryGetValue(request.ExecutablePath, out var factory))
        {
            throw new Win32Exception("The system cannot find the file specified.");
        }

        var process = factory(request);
        StartedProcesses.Add(process);
        return process;
    }

    public Task<ProcessResult> RunAsync(ProcessStartRequest request, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);

        if (_timeoutExecutables.Contains(request.ExecutablePath))
        {
            return Task.FromResult(new ProcessResult(-1, string.Empty, string.Empty, TimeSpan.Zero, false, true));
        }

        if (_resultsByExecutable.TryGetValue(request.ExecutablePath, out var result))
        {
            // Mirrors the real ProcessRunner: stream each line to the sink as it "arrives"
            // (here, all at once) so callers that parse output via the sink — like
            // DotNetBuildAdapter's diagnostic parsing — behave the same against the fake as
            // against a real process.
            if (outputSink is not null)
            {
                foreach (var line in SplitLines(result.StandardOutput)) outputSink.OnStandardOutput(line);
                foreach (var line in SplitLines(result.StandardError)) outputSink.OnStandardError(line);
            }

            return Task.FromResult(result);
        }

        throw new Win32Exception("The system cannot find the file specified.");
    }

    private static IEnumerable<string> SplitLines(string text) =>
        string.IsNullOrEmpty(text) ? Array.Empty<string>() : text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0);
}
