using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DevStudio.Core.Diagnostics;
using DevStudio.Core.Platform;

namespace DevStudio.UI.ViewModels;

/// <summary>UI foundation for the Problems panel (SKILL.md §17, §22–§23 [Phase 7]). Build and
/// language-server diagnostics are tracked as separate sources and merged into <see
/// cref="Diagnostics"/> — replacing a build re-runs <see cref="ReplaceBuildDiagnostics"/> without
/// erasing whatever the language server most recently published for open files, and a file's
/// language-server diagnostics are replaced (never appended) each time new ones arrive for that
/// same file, exactly like Build's own diagnostics are replaced each build.</summary>
public partial class ProblemsPanelViewModel : ObservableObject
{
    private IReadOnlyList<Diagnostic> _buildDiagnostics = Array.Empty<Diagnostic>();
    private IReadOnlyList<Diagnostic> _testDiagnostics = Array.Empty<Diagnostic>();
    private readonly Dictionary<string, IReadOnlyList<Diagnostic>> _languageDiagnosticsByFile = new(PathComparer.Comparer);

    public ObservableCollection<Diagnostic> Diagnostics { get; } = new();

    public bool HasProblems => Diagnostics.Count > 0;
    public bool HasNoProblems => !HasProblems;

    public int ErrorCount => Diagnostics.Count(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal);
    public int WarningCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

    public void ReplaceBuildDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        _buildDiagnostics = diagnostics.ToList();
        Rebuild();
    }

    /// <summary>A file's language-server diagnostics always replace that same file's previous
    /// ones (SKILL.md §23) — never appended — while every other file's (and Build's) diagnostics
    /// are left untouched.</summary>
    public void ReplaceLanguageDiagnostics(string filePath, IReadOnlyList<Diagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            _languageDiagnosticsByFile.Remove(filePath);
        }
        else
        {
            _languageDiagnosticsByFile[filePath] = diagnostics;
        }
        Rebuild();
    }

    /// <summary>A completed test run's failures wholesale replace the previous run's — like
    /// Build's own diagnostics, a full test run is one bounded operation, not per-file
    /// incremental like the language server's. Only failures with a real, parsed source
    /// location are ever included (SKILL.md §19 — "a failed test should not be incorrectly
    /// represented as a compiler error," and a location-less failure is still fully visible in
    /// the Test Explorer itself, just not navigable from here).</summary>
    public void ReplaceTestDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        _testDiagnostics = diagnostics.ToList();
        Rebuild();
    }

    private void Rebuild()
    {
        Diagnostics.Clear();
        foreach (var diagnostic in _buildDiagnostics) Diagnostics.Add(diagnostic);
        foreach (var fileDiagnostics in _languageDiagnosticsByFile.Values)
        {
            foreach (var diagnostic in fileDiagnostics) Diagnostics.Add(diagnostic);
        }
        foreach (var diagnostic in _testDiagnostics) Diagnostics.Add(diagnostic);

        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(HasNoProblems));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(WarningCount));
    }
}
