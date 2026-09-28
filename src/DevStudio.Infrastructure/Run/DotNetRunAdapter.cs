using System.Text;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Run;
using DevStudio.Core.Toolchains;

namespace DevStudio.Infrastructure.Run;

/// <summary>
/// Launches a real .NET application via <c>dotnet run --project &lt;target&gt; -c
/// &lt;configuration&gt; --no-build [-- args...]</c> (SKILL.md §7, §10 — see ADR-006 for why
/// <c>dotnet run</c> was chosen over resolving and executing a built artifact directly).
/// <c>--no-build</c> is always passed: when <see cref="RunConfiguration.BuildBeforeRun"/> is
/// true, <see cref="RunService"/> has already built through <see
/// cref="Core.Build.BuildService"/>, so a second implicit build would be redundant; when it's
/// false, <c>--no-build</c> is exactly what makes <c>dotnet run</c> refuse to silently build
/// (SKILL.md §12) and instead fail with its own real, honest error if the project was never
/// built.
/// </summary>
public sealed class DotNetRunAdapter : IRunAdapter
{
    private readonly IProcessRunner _processRunner;
    private readonly IToolchainRegistry _toolchainRegistry;

    public DotNetRunAdapter(IProcessRunner processRunner, IToolchainRegistry toolchainRegistry)
    {
        _processRunner = processRunner;
        _toolchainRegistry = toolchainRegistry;
    }

    public bool SupportsProjectType(ProjectType projectType) => projectType == ProjectType.DotNet;

    public Task<IRunningApplication> StartAsync(RunConfiguration configuration, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        var dotnet = _toolchainRegistry.Get(WellKnownToolchainIds.DotNet);
        if (dotnet is null || !dotnet.IsUsable)
        {
            throw new InvalidOperationException("The .NET SDK is not installed or has not been detected. Run Tools → Refresh Toolchains and try again.");
        }

        var workingDirectory = configuration.WorkingDirectoryOverride ?? configuration.Target.WorkingDirectory;
        if (!Directory.Exists(workingDirectory))
        {
            throw new InvalidOperationException($"Working directory does not exist: {workingDirectory}");
        }

        // A real, name-agnostic "has this configuration ever been built" check (SKILL.md §12,
        // §37: never assume/guess the output executable's name) — just whether the SDK's own
        // bin/<Configuration> convention has anything in it at all.
        var binDirectory = Path.Combine(configuration.Target.WorkingDirectory, "bin", configuration.BuildConfiguration.Name);
        if (!Directory.Exists(binDirectory) || !Directory.EnumerateFileSystemEntries(binDirectory).Any())
        {
            throw new InvalidOperationException("Cannot run because the application has not been built.");
        }

        var executable = dotnet.ExecutablePath ?? "dotnet";
        var arguments = new List<string> { "run", "--project", configuration.Target.FilePath, "-c", configuration.BuildConfiguration.Name, "--no-build" };
        if (configuration.Arguments.Count > 0)
        {
            arguments.Add("--");
            arguments.AddRange(configuration.Arguments);
        }

        var output = new StringBuilder();
        var sink = new DelegateProcessOutputSink(
            line => { lock (output) output.AppendLine(line); outputSink?.OnStandardOutput(line); },
            line => { lock (output) output.AppendLine(line); outputSink?.OnStandardError(line); });

        var request = new ProcessStartRequest(
            executable,
            arguments,
            workingDirectory,
            Environment: configuration.EnvironmentVariables,
            OutputEncoding: Encoding.UTF8);

        var startedAt = DateTimeOffset.UtcNow;
        var process = _processRunner.Start(request, sink);

        return Task.FromResult<IRunningApplication>(new DotNetRunningApplication(process, configuration, startedAt, output));
    }
}
