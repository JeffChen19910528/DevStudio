using CommunityToolkit.Mvvm.ComponentModel;
using DevStudio.Core.Testing;

namespace DevStudio.UI.ViewModels;

/// <summary>
/// One Test Explorer row: a real, discovered <see cref="Testing.TestCase"/> plus whatever real
/// <see cref="TestResult"/> the most recent run produced for it (SKILL.md §12–§13 [Phase 8]).
/// A dedicated ViewModel (rather than binding directly to the immutable <c>TestCase</c> record,
/// as Debug/Language's panels do for their own read-only models) is needed here specifically
/// because a test's displayed status genuinely mutates over the run's lifecycle
/// (NotRun → Running → Passed/Failed/Skipped) in a way those other panels' data never does.
/// </summary>
public sealed partial class TestNodeViewModel : ObservableObject
{
    public TestCase TestCase { get; }

    [ObservableProperty]
    private TestOutcome _outcome = TestOutcome.NotRun;

    [ObservableProperty]
    private TimeSpan _duration;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _stackTrace;

    [ObservableProperty]
    private string? _sourceFile;

    [ObservableProperty]
    private int? _line;

    public TestNodeViewModel(TestCase testCase) => TestCase = testCase;

    public void SetRunning() => Outcome = TestOutcome.Running;

    public void ApplyResult(TestResult result)
    {
        Outcome = result.Outcome;
        Duration = result.Duration;
        ErrorMessage = result.ErrorMessage;
        StackTrace = result.StackTrace;
        SourceFile = result.SourceFile;
        Line = result.Line;
    }
}
