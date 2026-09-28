using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Testing;
using DevStudio.Core.Toolchains;
using DevStudio.Core.Workspace;

namespace DevStudio.Infrastructure.Testing;

/// <summary>
/// Drives the real .NET test infrastructure via <c>dotnet test</c> (SKILL.md §8–§10, ADR-009):
/// discovery through the real, verified <c>--list-tests</c> mechanism, execution through a real
/// run with a TRX logger, results parsed from the real TRX file VSTest actually writes — never
/// by parsing C# source or fabricating results. <c>dotnet</c>'s path is resolved from the live
/// <see cref="IToolchainRegistry"/>, exactly like <see cref="Build.DotNetBuildAdapter"/>/
/// <see cref="Run.DotNetRunAdapter"/>.
/// </summary>
public sealed class DotNetTestAdapter : ITestAdapter
{
    /// <summary>Matches a real .NET stack trace's "in &lt;file&gt;:line &lt;n&gt;" location —
    /// the only place a real TRX result ever carries a source location (SKILL.md §14 [Phase 8]:
    /// a passing test's result carries no such information, and none is fabricated for it).</summary>
    private static readonly Regex StackTraceLocationRegex = new(@"in\s+(?<file>.+):line\s+(?<line>\d+)", RegexOptions.Compiled);

    private static readonly XNamespace TrxNamespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    private readonly IProcessRunner _processRunner;
    private readonly IToolchainRegistry _toolchainRegistry;

    public DotNetTestAdapter(IProcessRunner processRunner, IToolchainRegistry toolchainRegistry)
    {
        _processRunner = processRunner;
        _toolchainRegistry = toolchainRegistry;
    }

    public bool SupportsProjectType(ProjectType projectType) => projectType == ProjectType.DotNet;

    public async Task<IReadOnlyList<TestCase>> DiscoverTestsAsync(ProjectInfo project, BuildConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var dotnet = ResolveDotnet();
        if (project.ProjectFile is null)
        {
            throw new InvalidOperationException($"Project '{project.Name}' has no project file to run tests against.");
        }

        var request = new ProcessStartRequest(
            dotnet,
            new[] { "test", project.ProjectFile, "-c", configuration.Name, "--list-tests", "--nologo" },
            project.RootPath,
            OutputEncoding: Encoding.UTF8);

        var result = await _processRunner.RunAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseListTests(result.StandardOutput, project.Id);
    }

    /// <summary>Real VSTest console output indents every discovered test's fully qualified name
    /// with exactly four spaces, after a localized (and therefore not string-matchable) header
    /// line — verified against the real <c>dotnet test --list-tests</c> output on this machine
    /// before writing this parser (see ADR-009). Any other line (restore/build progress, the
    /// header itself) is not indented this way and is skipped.</summary>
    private static IReadOnlyList<TestCase> ParseListTests(string output, string projectId)
    {
        var cases = new List<TestCase>();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (!line.StartsWith("    ", StringComparison.Ordinal)) continue;
            var name = line.Trim();
            if (name.Length == 0) continue;

            cases.Add(new TestCase(
                Id: name,
                DisplayName: name,
                FullyQualifiedName: name,
                ProjectId: projectId,
                SourceFile: null,
                Line: null,
                Traits: Array.Empty<string>(),
                Framework: null));
        }
        return cases;
    }

    public async Task<IReadOnlyList<TestResult>> RunTestsAsync(
        ProjectInfo project,
        BuildConfiguration configuration,
        TestFilter? filter,
        bool skipBuild,
        IProcessOutputSink? outputSink = null,
        CancellationToken cancellationToken = default)
    {
        var dotnet = ResolveDotnet();
        if (project.ProjectFile is null)
        {
            throw new InvalidOperationException($"Project '{project.Name}' has no project file to run tests against.");
        }

        var resultsDirectory = Path.Combine(Path.GetTempPath(), "DevStudioTestResults_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(resultsDirectory);
        const string trxFileName = "results.trx";

        try
        {
            var arguments = new List<string>
            {
                "test", project.ProjectFile, "-c", configuration.Name,
                "--logger", $"trx;LogFileName={trxFileName}",
                "--results-directory", resultsDirectory,
                "--nologo",
            };
            // Mirrors Phase 5's `dotnet run --no-build` decision: TestService already built the
            // project via BuildService when skipBuild is true, so a second implicit build here
            // would be redundant.
            if (skipBuild) arguments.Add("--no-build");

            var filterExpression = filter?.ToVsTestFilterExpression();
            if (filterExpression is not null)
            {
                arguments.Add("--filter");
                arguments.Add(filterExpression);
            }

            var request = new ProcessStartRequest(dotnet, arguments, project.RootPath, OutputEncoding: Encoding.UTF8);
            var result = await _processRunner.RunAsync(request, outputSink, cancellationToken).ConfigureAwait(false);

            // IProcessRunner.RunAsync reports cancellation as a real ProcessResult
            // (WasCancelled: true) rather than throwing (SKILL.md §17) — TestService's own
            // Cancelled-vs-Failed distinction depends on an OperationCanceledException actually
            // propagating, so it is re-raised here rather than falling through to the generic
            // "no results file" error below.
            if (result.WasCancelled)
            {
                throw new OperationCanceledException("The test run was cancelled.", cancellationToken);
            }

            var trxPath = Path.Combine(resultsDirectory, trxFileName);
            if (!File.Exists(trxPath))
            {
                throw new InvalidOperationException(
                    $"dotnet test did not produce a results file (exit code {result.ExitCode}). " +
                    $"{Truncate(result.StandardError, 500)}");
            }

            return ParseTrx(trxPath);
        }
        finally
        {
            try { Directory.Delete(resultsDirectory, recursive: true); }
            catch (IOException) { /* best-effort cleanup of a scratch directory */ }
            catch (UnauthorizedAccessException) { /* best-effort cleanup of a scratch directory */ }
        }
    }

    /// <summary>Parses the real TRX file <c>dotnet test</c> wrote (SKILL.md §10, §18) — the
    /// authoritative structured result, verified against a real run on this machine (see
    /// ADR-009) rather than assumed from documentation alone.</summary>
    private static IReadOnlyList<TestResult> ParseTrx(string path)
    {
        var document = XDocument.Load(path);
        var results = new List<TestResult>();

        foreach (var element in document.Descendants(TrxNamespace + "UnitTestResult"))
        {
            var testName = (string?)element.Attribute("testName") ?? string.Empty;
            var outcome = MapOutcome((string?)element.Attribute("outcome"));
            var duration = ParseDuration((string?)element.Attribute("duration"));

            var errorInfo = element.Element(TrxNamespace + "Output")?.Element(TrxNamespace + "ErrorInfo");
            var message = errorInfo?.Element(TrxNamespace + "Message")?.Value;
            var stackTrace = errorInfo?.Element(TrxNamespace + "StackTrace")?.Value;
            var (file, line) = ExtractLocation(stackTrace);

            results.Add(new TestResult(testName, outcome, duration, message, stackTrace, file, line));
        }

        return results;
    }

    private static TestOutcome MapOutcome(string? outcome) => outcome switch
    {
        "Passed" => TestOutcome.Passed,
        "Failed" => TestOutcome.Failed,
        "NotExecuted" => TestOutcome.Skipped,
        _ => TestOutcome.Error,
    };

    private static TimeSpan ParseDuration(string? value) =>
        TimeSpan.TryParse(value, out var duration) ? duration : TimeSpan.Zero;

    private static (string? File, int? Line) ExtractLocation(string? stackTrace)
    {
        if (stackTrace is null) return (null, null);
        var match = StackTraceLocationRegex.Match(stackTrace);
        if (!match.Success) return (null, null);
        return (match.Groups["file"].Value.Trim(), int.Parse(match.Groups["line"].Value));
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length > maxLength ? value[..maxLength] + "..." : value;

    private string ResolveDotnet()
    {
        var dotnet = _toolchainRegistry.Get(WellKnownToolchainIds.DotNet);
        if (dotnet is null || !dotnet.IsUsable)
        {
            throw new InvalidOperationException("The .NET SDK is not installed or has not been detected. Run Tools → Refresh Toolchains and try again.");
        }
        return dotnet.ExecutablePath ?? "dotnet";
    }
}
