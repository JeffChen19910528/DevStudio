namespace DevStudio.Core.Testing;

/// <summary>
/// One real test's outcome from one real run (SKILL.md §18 [Phase 8]), parsed from the actual
/// VSTest TRX result file — never fabricated. <see cref="SourceFile"/>/<see cref="Line"/> are
/// populated only when a real stack trace in the TRX contained a parseable "in &lt;file&gt;:line
/// &lt;n&gt;" location (i.e. only ever for a real failure); a passing test's TRX entry carries no
/// such information, so these stay null rather than guessed. <see cref="ErrorMessage"/>/<see
/// cref="StackTrace"/> are the real assertion message/stack trace text the runner produced — no
/// synthesized "Expected"/"Actual" values are added beyond whatever the runner's own message
/// already contains.
/// </summary>
public sealed record TestResult(
    string TestCaseId,
    TestOutcome Outcome,
    TimeSpan Duration,
    string? ErrorMessage = null,
    string? StackTrace = null,
    string? SourceFile = null,
    int? Line = null);

/// <summary>The outcome of one <see cref="TestService"/> run request — <see cref="Results"/> is
/// only ever non-empty when tests actually executed; a build failure or trust block reports zero
/// results with an explanatory <see cref="Message"/> instead (SKILL.md §22, §21).</summary>
public sealed record TestRunResult(TestRunState State, IReadOnlyList<TestResult> Results, string? Message = null);
