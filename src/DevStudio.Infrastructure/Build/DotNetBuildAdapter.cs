using System.Diagnostics;
using System.Text;
using DevStudio.Core.Build;
using DevStudio.Core.Diagnostics;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Build;

/// <summary>
/// Drives real <c>dotnet</c> invocations (SKILL.md §10–§11). Resolves the executable from the
/// current <see cref="IToolchainRegistry"/> result rather than assuming <c>dotnet</c> is on
/// PATH just because it was detected once at startup — a stale/uninstalled SDK becomes <see
/// cref="BuildStatus.Unavailable"/>, never an attempted, doomed process launch.
/// </summary>
public sealed class DotNetBuildAdapter : IBuildAdapter
{
    private readonly IProcessRunner _processRunner;
    private readonly IToolchainRegistry _toolchainRegistry;

    public DotNetBuildAdapter(IProcessRunner processRunner, IToolchainRegistry toolchainRegistry)
    {
        _processRunner = processRunner;
        _toolchainRegistry = toolchainRegistry;
    }

    public bool SupportsProjectType(ProjectType projectType) => projectType == ProjectType.DotNet;

    public async Task<BuildResult> ExecuteAsync(BuildRequest request, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;

        var dotnet = _toolchainRegistry.Get(WellKnownToolchainIds.DotNet);
        if (dotnet is null || !dotnet.IsUsable)
        {
            return Unavailable(request, startedAt, "The .NET SDK is not installed or has not been detected. Run Tools → Refresh Toolchains and try again.");
        }

        var executable = dotnet.ExecutablePath ?? "dotnet";
        var arguments = BuildArguments(request);

        var outputBuilder = new StringBuilder();
        var diagnostics = new List<Diagnostic>();
        // The MSBuild console logger prints each diagnostic once inline during the build pass,
        // then repeats every warning/error again in its end-of-build summary — a real, observed
        // behavior (SKILL.md §27's "clear old build diagnostics... do not accidentally combine
        // stale diagnostics" applies just as much to a single run's own duplicate lines).
        var seenDiagnostics = new HashSet<(string File, int Line, int Column, string Code)>();

        void Capture(string line, bool isError)
        {
            outputBuilder.AppendLine(line);
            if (isError) outputSink?.OnStandardError(line); else outputSink?.OnStandardOutput(line);

            var diagnostic = MsBuildDiagnosticParser.TryParse(line);
            if (diagnostic is not null && seenDiagnostics.Add((diagnostic.File, diagnostic.Line, diagnostic.Column, diagnostic.Code)))
            {
                diagnostics.Add(diagnostic);
            }
        }

        var sink = new DelegateProcessOutputSink(line => Capture(line, false), line => Capture(line, true));
        // Verified: the dotnet CLI emits UTF-8 regardless of the console's active codepage;
        // without forcing it, output is mis-decoded as the OS's legacy codepage (garbled
        // non-ASCII text, e.g. on a localized Windows install) — see ADR-005.
        var processRequest = new ProcessStartRequest(executable, arguments, request.Target.WorkingDirectory, OutputEncoding: Encoding.UTF8);

        var stopwatch = Stopwatch.StartNew();
        var processResult = await _processRunner.RunAsync(processRequest, sink, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        var completedAt = DateTimeOffset.UtcNow;

        if (processResult.WasCancelled)
        {
            return new BuildResult(BuildStatus.Cancelled, processResult.ExitCode, stopwatch.Elapsed, request.Target, request.Operation, diagnostics, outputBuilder.ToString(), startedAt, completedAt, "Build was cancelled.");
        }

        if (processResult.WasTimedOut)
        {
            return new BuildResult(BuildStatus.TimedOut, processResult.ExitCode, stopwatch.Elapsed, request.Target, request.Operation, diagnostics, outputBuilder.ToString(), startedAt, completedAt, "Build timed out.");
        }

        var status = processResult.ExitCode == 0 ? BuildStatus.Succeeded : BuildStatus.Failed;
        return new BuildResult(status, processResult.ExitCode, stopwatch.Elapsed, request.Target, request.Operation, diagnostics, outputBuilder.ToString(), startedAt, completedAt);
    }

    /// <summary>
    /// Restore: <c>dotnet restore &lt;target&gt;</c> (no configuration flag — restore is
    /// configuration-independent). Build/Clean: <c>-c &lt;configuration&gt;</c>. Rebuild: real
    /// <c>dotnet build ... --no-incremental</c>, a genuine, documented MSBuild flag that forces
    /// a full, non-incremental compilation — not a two-step Clean+Build, so a Rebuild failure
    /// can't be misreported as "Build succeeded" partway through a Clean (SKILL.md §11, §21).
    /// </summary>
    private static IReadOnlyList<string> BuildArguments(BuildRequest request)
    {
        var target = request.Target.FilePath;
        var configuration = request.Configuration.Name;

        return request.Operation switch
        {
            BuildOperation.Restore => new[] { "restore", target },
            BuildOperation.Clean => new[] { "clean", target, "-c", configuration },
            BuildOperation.Build => new[] { "build", target, "-c", configuration },
            BuildOperation.Rebuild => new[] { "build", target, "-c", configuration, "--no-incremental" },
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Operation, "Unsupported build operation."),
        };
    }

    private static BuildResult Unavailable(BuildRequest request, DateTimeOffset startedAt, string message) => new(
        BuildStatus.Unavailable, -1, TimeSpan.Zero, request.Target, request.Operation,
        Array.Empty<Diagnostic>(), string.Empty, startedAt, DateTimeOffset.UtcNow, message);
}
