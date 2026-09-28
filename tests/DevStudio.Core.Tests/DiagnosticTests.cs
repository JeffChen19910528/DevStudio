using DevStudio.Core.Diagnostics;
using Xunit;

namespace DevStudio.Core.Tests;

public class DiagnosticTests
{
    [Fact]
    public void Value_equality_holds_for_diagnostics_with_identical_fields()
    {
        var a = new Diagnostic(DiagnosticSeverity.Error, "CS1002", "; expected", "Program.cs", 42, 5, DiagnosticSource.Compiler);
        var b = new Diagnostic(DiagnosticSeverity.Error, "CS1002", "; expected", "Program.cs", 42, 5, DiagnosticSource.Compiler);

        Assert.Equal(a, b);
    }

    [Fact]
    public void RelatedInformation_defaults_to_null_when_not_supplied()
    {
        var diagnostic = new Diagnostic(DiagnosticSeverity.Warning, "CS8600", "possible null reference", "Program.cs", 10, 1, DiagnosticSource.Compiler);

        Assert.Null(diagnostic.RelatedInformation);
    }
}
