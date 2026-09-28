using DevStudio.Core.Build;
using DevStudio.Core.Debug;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Workspace;
using DevStudio.Infrastructure.Dap;

namespace DevStudio.Infrastructure.Debug;

/// <summary>
/// Launches a real <c>netcoredbg --interpreter=vscode</c> process and drives it over DAP (SKILL.md
/// §2, §19, ADR-007). <c>netcoredbg</c>'s raw stdin/stdout are obtained via
/// <c>ProcessStartRequest.RawStdio</c> (the same <see cref="IProcessRunner"/> everything else
/// uses — no second process-execution path) and wrapped in a <see cref="StreamDapTransport"/>.
/// </summary>
public sealed class NetCoreDebuggerAdapter : IDebuggerAdapter
{
    private readonly IProcessRunner _processRunner;
    private readonly NetCoreDebuggerResolver _resolver;

    public NetCoreDebuggerAdapter(IProcessRunner processRunner, NetCoreDebuggerResolver resolver)
    {
        _processRunner = processRunner;
        _resolver = resolver;
    }

    public bool SupportsProjectType(ProjectType projectType) => projectType == ProjectType.DotNet;

    public async Task<IActiveDebugSession> StartAsync(DebugConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var resolution = _resolver.Resolve();
        if (!resolution.Found)
        {
            throw new InvalidOperationException(resolution.Message ?? "Debug unavailable: .NET debugger adapter (netcoredbg) not found.");
        }

        var runConfig = configuration.RunConfiguration;
        var workingDirectory = runConfig.WorkingDirectoryOverride ?? runConfig.Target.WorkingDirectory;
        if (!Directory.Exists(workingDirectory))
        {
            throw new InvalidOperationException($"Working directory does not exist: {workingDirectory}");
        }

        var programPath = ResolveProgramPath(runConfig.Target, runConfig.BuildConfiguration);

        var request = new ProcessStartRequest(resolution.ExecutablePath!, new[] { "--interpreter=vscode" }, workingDirectory, RawStdio: true);
        var process = _processRunner.Start(request);
        if (process.StandardInput is null || process.StandardOutput is null)
        {
            await process.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException("netcoredbg did not expose raw stdio (RawStdio request was not honored).");
        }

        var transport = new StreamDapTransport(process.StandardOutput, process.StandardInput);
        var client = new Core.Dap.DapClient(transport);
        var session = new NetCoreDebugSession(client, process);

        try
        {
            await session.InitializeAsync(cancellationToken).ConfigureAwait(false);
            session.BeginLaunch(configuration, programPath, workingDirectory);

            // SKILL.md §48: a launch/continue may legitimately take time, but waiting for the
            // adapter's own handshake to even begin must not hang forever — a bounded wait here
            // is what turns "an incompatible/misbehaving debugger" into a clear, timely error
            // instead of a stuck UI (this is exactly the failure mode a real, license-restricted
            // debugger produced during development — see ADR-007).
            using var initializedTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            initializedTimeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                await session.WaitForInitializedEventAsync(initializedTimeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    "The debugger did not send its 'initialized' event within 10 seconds. It may not " +
                    "be a compatible DAP debugger, or may be refusing this client.");
            }
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return session;
    }

    /// <summary>
    /// Resolves the real built managed assembly netcoredbg should launch — never a guessed
    /// executable name (SKILL.md §19, §37, mirroring Phase 5's Run adapter): scans
    /// <c>bin/&lt;Configuration&gt;/*/&lt;ProjectName&gt;.dll</c> (the one part of the .NET SDK's
    /// output layout every project follows) and requires exactly one real match. Zero matches
    /// means "not built yet"; more than one is reported as a genuine ambiguity rather than
    /// silently picking one.
    /// </summary>
    private static string ResolveProgramPath(BuildTarget target, BuildConfiguration configuration)
    {
        var projectName = Path.GetFileNameWithoutExtension(target.FilePath);
        var binRoot = Path.Combine(target.WorkingDirectory, "bin", configuration.Name);
        if (!Directory.Exists(binRoot))
        {
            throw new InvalidOperationException("Cannot start debugging because the target has not been built.");
        }

        var matches = Directory.EnumerateDirectories(binRoot)
            .Select(dir => Path.Combine(dir, projectName + ".dll"))
            .Where(File.Exists)
            .ToList();

        if (matches.Count == 0)
        {
            throw new InvalidOperationException("Cannot start debugging because the target has not been built.");
        }
        if (matches.Count > 1)
        {
            throw new InvalidOperationException($"Multiple built outputs found for '{projectName}' under '{binRoot}'; cannot determine which one to debug.");
        }

        return matches[0];
    }
}
