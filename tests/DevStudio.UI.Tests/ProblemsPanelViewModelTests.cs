using DevStudio.Core.Diagnostics;
using DevStudio.UI.ViewModels;
using Xunit;

namespace DevStudio.UI.Tests;

public class ProblemsPanelViewModelTests
{
    [Fact]
    public void Starts_empty_with_no_problems()
    {
        var viewModel = new ProblemsPanelViewModel();

        Assert.True(viewModel.HasNoProblems);
        Assert.False(viewModel.HasProblems);
        Assert.Equal(0, viewModel.ErrorCount);
    }

    [Fact]
    public void ReplaceBuildDiagnostics_populates_diagnostics_and_counts_by_severity()
    {
        var viewModel = new ProblemsPanelViewModel();

        viewModel.ReplaceBuildDiagnostics(new[]
        {
            new Diagnostic(DiagnosticSeverity.Error, "CS1002", "; expected", "Program.cs", 1, 1, DiagnosticSource.Compiler),
            new Diagnostic(DiagnosticSeverity.Warning, "CS8600", "possible null reference", "Program.cs", 2, 1, DiagnosticSource.Compiler),
            new Diagnostic(DiagnosticSeverity.Warning, "CS8601", "possible null reference assignment", "Program.cs", 3, 1, DiagnosticSource.Compiler),
        });

        Assert.True(viewModel.HasProblems);
        Assert.False(viewModel.HasNoProblems);
        Assert.Equal(1, viewModel.ErrorCount);
        Assert.Equal(2, viewModel.WarningCount);
        Assert.Equal(3, viewModel.Diagnostics.Count);
    }

    [Fact]
    public void Language_server_diagnostics_do_not_erase_build_diagnostics()
    {
        var viewModel = new ProblemsPanelViewModel();
        viewModel.ReplaceBuildDiagnostics(new[]
        {
            new Diagnostic(DiagnosticSeverity.Error, "CS1002", "; expected", "Program.cs", 1, 1, DiagnosticSource.Compiler),
        });

        viewModel.ReplaceLanguageDiagnostics("Other.cs", new[]
        {
            new Diagnostic(DiagnosticSeverity.Warning, "CS8600", "possible null reference", "Other.cs", 2, 1, DiagnosticSource.LanguageServer),
        });

        Assert.Equal(2, viewModel.Diagnostics.Count);
        Assert.Contains(viewModel.Diagnostics, d => d.Source == DiagnosticSource.Compiler);
        Assert.Contains(viewModel.Diagnostics, d => d.Source == DiagnosticSource.LanguageServer);
    }

    [Fact]
    public void A_files_new_language_server_diagnostics_replace_its_previous_ones_not_append()
    {
        var viewModel = new ProblemsPanelViewModel();
        viewModel.ReplaceLanguageDiagnostics("Program.cs", new[]
        {
            new Diagnostic(DiagnosticSeverity.Error, "CS1061", "old error", "Program.cs", 1, 1, DiagnosticSource.LanguageServer),
        });

        viewModel.ReplaceLanguageDiagnostics("Program.cs", new[]
        {
            new Diagnostic(DiagnosticSeverity.Warning, "IDE0002", "new hint", "Program.cs", 2, 1, DiagnosticSource.LanguageServer),
        });

        var diagnostic = Assert.Single(viewModel.Diagnostics);
        Assert.Equal("IDE0002", diagnostic.Code);
    }

    [Fact]
    public void Publishing_empty_language_server_diagnostics_for_a_file_clears_that_files_entries()
    {
        var viewModel = new ProblemsPanelViewModel();
        viewModel.ReplaceLanguageDiagnostics("Program.cs", new[]
        {
            new Diagnostic(DiagnosticSeverity.Error, "CS1061", "an error", "Program.cs", 1, 1, DiagnosticSource.LanguageServer),
        });

        viewModel.ReplaceLanguageDiagnostics("Program.cs", Array.Empty<Diagnostic>());

        Assert.True(viewModel.HasNoProblems);
    }
}
