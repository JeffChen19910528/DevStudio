using System.Text;
using System.Text.RegularExpressions;
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
/// false, <c>dotnet run --no-build</c> will itself fail with a clear error if the project has
/// not been built — no pre-flight bin-directory check is needed here (and guessing the output
/// path would be wrong for projects with a custom OutputPath).
/// <para>Exception: <c>OutputType=WinExe</c> projects (WinForms, WPF, etc.) are rejected by
/// <c>dotnet run</c>. For these, the real exe is resolved via MSBuild's
/// <c>-getProperty:TargetPath</c> and launched directly — the same <see cref="IProcessRunner"/>
/// abstraction, but targeting the built executable rather than <c>dotnet run</c>.</para>
/// </summary>
public sealed class DotNetRunAdapter : IRunAdapter
{
    private readonly IProcessRunner _processRunner;
    private readonly IToolchainRegistry _toolchainRegistry;

    private static readonly Regex OutputTypeRegex = new(
        "<OutputType>\\s*([^<]+?)\\s*</OutputType>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public DotNetRunAdapter(IProcessRunner processRunner, IToolchainRegistry toolchainRegistry)
    {
        _processRunner = processRunner;
        _toolchainRegistry = toolchainRegistry;
    }

    public bool SupportsProjectType(ProjectType projectType) => projectType == ProjectType.DotNet;

    public async Task<IRunningApplication> StartAsync(RunConfiguration configuration, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
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

        var executable = dotnet.ExecutablePath ?? "dotnet";

        // dotnet run rejects WinExe (WinForms/WPF/etc.) — resolve the real exe via MSBuild
        // TargetPath and launch it directly instead.
        if (IsWinExeProject(configuration.Target.FilePath))
        {
            return await StartGuiAppAsync(executable, configuration, workingDirectory, outputSink, cancellationToken).ConfigureAwait(false);
        }

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

        return new DotNetRunningApplication(process, configuration, startedAt, output);
    }

    private static bool IsWinExeProject(string projectFilePath)
    {
        try
        {
            var content = File.ReadAllText(projectFilePath);
            var match = OutputTypeRegex.Match(content);
            return match.Success && match.Groups[1].Value.Equals("WinExe", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private async Task<IRunningApplication> StartGuiAppAsync(
        string dotnet, RunConfiguration configuration, string workingDirectory,
        IProcessOutputSink? outputSink, CancellationToken cancellationToken)
    {
        // Ask MSBuild for the real output exe path — works even when the project has a custom
        // OutputPath that puts the exe outside the project directory.
        var captured = new StringBuilder();
        var msbuildSink = new DelegateProcessOutputSink(
            line => { lock (captured) captured.AppendLine(line); },
            _ => { });

        var msbuildRequest = new ProcessStartRequest(
            dotnet,
            new[] { "msbuild", configuration.Target.FilePath,
                    "-getProperty:TargetPath",
                    $"-p:Configuration={configuration.BuildConfiguration.Name}" },
            configuration.Target.WorkingDirectory,
            OutputEncoding: Encoding.UTF8);

        var msbuildResult = await _processRunner.RunAsync(msbuildRequest, msbuildSink, cancellationToken).ConfigureAwait(false);

        string targetPath;
        lock (captured) { targetPath = captured.ToString().Trim(); }

        if (msbuildResult.ExitCode != 0 || string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
        {
            throw new InvalidOperationException(
                $"Cannot run: the built executable was not found. " +
                $"Make sure the project has been built for the '{configuration.BuildConfiguration.Name}' configuration.");
        }

        var output = new StringBuilder();
        var sink = new DelegateProcessOutputSink(
            line => { lock (output) output.AppendLine(line); outputSink?.OnStandardOutput(line); },
            line => { lock (output) output.AppendLine(line); outputSink?.OnStandardError(line); });

        var processRequest = new ProcessStartRequest(
            targetPath,
            configuration.Arguments.ToList(),
            workingDirectory,
            Environment: configuration.EnvironmentVariables);

        var startedAt = DateTimeOffset.UtcNow;
        var process = _processRunner.Start(processRequest, sink);

        return new DotNetRunningApplication(process, configuration, startedAt, output);
    }
}
