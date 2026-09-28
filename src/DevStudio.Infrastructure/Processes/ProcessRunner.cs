using System.Diagnostics;
using DevStudio.Core.Processes;

namespace DevStudio.Infrastructure.Processes;

/// <summary>
/// Concrete <see cref="IProcessRunner"/> backed by <see cref="System.Diagnostics.Process"/>.
/// Always launches "executable + argument array" — never builds or passes a shell command
/// string (SKILL.md §12). This is the single process abstraction used by everything above it
/// (builds, terminal sessions, future toolchain adapters).
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public IRunningProcess Start(ProcessStartRequest request, IProcessOutputSink? outputSink = null)
        => new RunningProcess(request, outputSink);

    public async Task<ProcessResult> RunAsync(ProcessStartRequest request, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        await using var running = Start(request, outputSink);
        return await running.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class RunningProcess : IRunningProcess
{
    private readonly Process _process;
    private readonly System.Text.StringBuilder _stdout = new();
    private readonly System.Text.StringBuilder _stderr = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly CancellationTokenSource? _timeoutCts;
    private bool _timedOut;

    public RunningProcess(ProcessStartRequest request, IProcessOutputSink? outputSink)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = request.RedirectInput || request.RawStdio,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Not every tool agrees on an output encoding: modern CLIs like `dotnet` emit UTF-8
        // regardless of the console's active codepage, but some native Win32 tools (observed:
        // vswhere.exe) emit text in the OS's legacy codepage — forcing UTF-8 on those instead
        // breaks their output. Callers that have verified a specific tool's real encoding (see
        // DotNetBuildAdapter) opt in via ProcessStartRequest.OutputEncoding; everyone else keeps
        // the .NET default (the OS's codepage), matching prior behavior.
        if (request.OutputEncoding is not null)
        {
            startInfo.StandardOutputEncoding = request.OutputEncoding;
            startInfo.StandardErrorEncoding = request.OutputEncoding;
        }

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.Environment is not null)
        {
            foreach (var (key, value) in request.Environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var rawStdio = request.RawStdio;

        if (!rawStdio)
        {
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                _stdout.AppendLine(e.Data);
                outputSink?.OnStandardOutput(e.Data);
            };
        }
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            _stderr.AppendLine(e.Data);
            outputSink?.OnStandardError(e.Data);
        };

        _stopwatch.Start();
        _process.Start();
        // A framed binary protocol (DAP) reads StandardOutput.BaseStream directly, which is
        // mutually exclusive with the line-buffered BeginOutputReadLine() API on the same
        // stream — so RawStdio skips it entirely for stdout. Stderr stays line-buffered either
        // way; nothing about it conflicts with raw stdout/stdin access.
        if (!rawStdio)
        {
            _process.BeginOutputReadLine();
        }
        _process.BeginErrorReadLine();

        StandardInput = rawStdio ? _process.StandardInput.BaseStream : null;
        StandardOutput = rawStdio ? _process.StandardOutput.BaseStream : null;

        if (request.Timeout is { } timeout)
        {
            _timeoutCts = new CancellationTokenSource(timeout);
            _timeoutCts.Token.Register(() =>
            {
                if (!HasExited)
                {
                    _timedOut = true;
                    Kill();
                }
            });
        }
    }

    public int ProcessId => _process.Id;

    public Stream? StandardInput { get; }
    public Stream? StandardOutput { get; }

    public bool HasExited
    {
        get
        {
            try { return _process.HasExited; }
            catch (InvalidOperationException) { return true; }
        }
    }

    public async Task<ProcessResult> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill();
            _stopwatch.Stop();
            return new ProcessResult(-1, _stdout.ToString(), _stderr.ToString(), _stopwatch.Elapsed, WasCancelled: true, WasTimedOut: false);
        }

        _stopwatch.Stop();
        var exitCode = _timedOut ? -1 : _process.ExitCode;
        return new ProcessResult(exitCode, _stdout.ToString(), _stderr.ToString(), _stopwatch.Elapsed, WasCancelled: false, _timedOut);
    }

    public async Task WriteInputAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!_process.StartInfo.RedirectStandardInput)
        {
            throw new InvalidOperationException("This process was not started with RedirectInput = true.");
        }

        await _process.StandardInput.WriteLineAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Kill()
    {
        try
        {
            if (!HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited between the HasExited check and Kill().
        }
    }

    public ValueTask DisposeAsync()
    {
        _timeoutCts?.Dispose();
        _process.Dispose();
        return ValueTask.CompletedTask;
    }
}
