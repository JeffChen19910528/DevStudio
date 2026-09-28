using System.Text;

namespace DevStudio.Core.Processes;

/// <summary>
/// Describes a process launch as executable + argument array (SKILL.md §12).
/// There is deliberately no single-string "command line" property: constructing one
/// invites shell-injection-style bugs when arguments are concatenated by callers.
/// </summary>
public sealed record ProcessStartRequest(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string>? Environment = null,
    TimeSpan? Timeout = null,
    bool RedirectInput = false,
    /// <summary>Null (default) lets stdout/stderr decode using the OS's default codepage,
    /// matching whatever the target executable actually emits. Set only when a specific tool's
    /// real output encoding has been verified — forcing the wrong one produces mojibake or
    /// broken parsing (e.g. observed with vswhere.exe under a forced UTF-8 override).</summary>
    Encoding? OutputEncoding = null,
    /// <summary>When true, stdin/stdout are exposed as raw <see cref="System.IO.Stream"/>s
    /// (<see cref="IRunningProcess.StandardInput"/>/<see cref="IRunningProcess.StandardOutput"/>)
    /// instead of being line-buffered through <see cref="IProcessOutputSink"/>. Required for a
    /// binary, length-prefixed protocol like the Debug Adapter Protocol (SKILL.md §43 [Phase
    /// 6]), where a payload cannot be assumed to align with newline boundaries. Implies
    /// <see cref="RedirectInput"/>; the given <paramref name="outputSink"/> parameter to
    /// <see cref="IProcessRunner.Start"/> is ignored in this mode. Every existing caller leaves
    /// this false and is unaffected.</summary>
    bool RawStdio = false);
