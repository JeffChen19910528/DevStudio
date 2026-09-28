using DevStudio.Core.Build;
using DevStudio.Core.Diagnostics;
using Xunit;

namespace DevStudio.Core.Tests;

public class MsBuildDiagnosticParserTests
{
    [Fact]
    public void Parses_an_error_with_an_absolute_Windows_path()
    {
        var diagnostic = MsBuildDiagnosticParser.TryParse(
            @"C:\repo\Program.cs(12,34): error CS1002: ; expected [C:\repo\App.csproj]");

        Assert.NotNull(diagnostic);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic!.Severity);
        Assert.Equal("CS1002", diagnostic.Code);
        Assert.Equal("; expected", diagnostic.Message);
        Assert.Equal(@"C:\repo\Program.cs", diagnostic.File);
        Assert.Equal(12, diagnostic.Line);
        Assert.Equal(34, diagnostic.Column);
        Assert.Equal(DiagnosticSource.Compiler, diagnostic.Source);
    }

    [Fact]
    public void Parses_a_warning_with_an_absolute_Unix_style_path()
    {
        var diagnostic = MsBuildDiagnosticParser.TryParse(
            "/home/user/repo/Program.cs(10,5): warning CS8600: Converting null literal or possible null value to non-nullable type. [/home/user/repo/App.csproj]");

        Assert.NotNull(diagnostic);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic!.Severity);
        Assert.Equal("CS8600", diagnostic.Code);
        Assert.Equal("/home/user/repo/Program.cs", diagnostic.File);
        Assert.Equal(10, diagnostic.Line);
        Assert.Equal(5, diagnostic.Column);
    }

    [Fact]
    public void Parses_a_relative_path()
    {
        var diagnostic = MsBuildDiagnosticParser.TryParse(@"src\Program.cs(1,1): error CS0103: The name 'x' does not exist in the current context");

        Assert.NotNull(diagnostic);
        Assert.Equal(@"src\Program.cs", diagnostic!.File);
    }

    [Fact]
    public void Parses_a_path_containing_spaces()
    {
        var diagnostic = MsBuildDiagnosticParser.TryParse(
            @"C:\My Repo\Program.cs(5,1): error CS1001: Identifier expected [C:\My Repo\App.csproj]");

        Assert.NotNull(diagnostic);
        Assert.Equal(@"C:\My Repo\Program.cs", diagnostic!.File);
        Assert.Equal(5, diagnostic.Line);
    }

    [Fact]
    public void Parses_a_project_level_diagnostic_with_no_source_location()
    {
        var diagnostic = MsBuildDiagnosticParser.TryParse("error MSB4025: The project file could not be loaded.");

        Assert.NotNull(diagnostic);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic!.Severity);
        Assert.Equal("MSB4025", diagnostic.Code);
        Assert.Equal(string.Empty, diagnostic.File);
        Assert.Equal(0, diagnostic.Line);
        Assert.Equal(DiagnosticSource.BuildSystem, diagnostic.Source);
    }

    [Fact]
    public void Classifies_MSB_codes_as_BuildSystem_and_other_codes_as_Compiler()
    {
        var buildSystem = MsBuildDiagnosticParser.TryParse(@"C:\repo\App.csproj(1,1): warning MSB3277: Found conflicts.");
        var compiler = MsBuildDiagnosticParser.TryParse(@"C:\repo\Program.cs(1,1): error CS1002: ; expected");

        Assert.Equal(DiagnosticSource.BuildSystem, buildSystem!.Source);
        Assert.Equal(DiagnosticSource.Compiler, compiler!.Source);
    }

    [Fact]
    public void Parses_multiple_diagnostics_independently_line_by_line()
    {
        var lines = new[]
        {
            @"C:\repo\A.cs(1,1): error CS1002: ; expected [C:\repo\App.csproj]",
            @"C:\repo\B.cs(2,3): warning CS8600: possible null value [C:\repo\App.csproj]",
            "Build succeeded.",
            @"C:\repo\C.cs(4,5): error CS0103: The name 'y' does not exist in the current context [C:\repo\App.csproj]",
        };

        var diagnostics = lines.Select(MsBuildDiagnosticParser.TryParse).Where(d => d is not null).ToList();

        Assert.Equal(3, diagnostics.Count);
        Assert.Equal(2, diagnostics.Count(d => d!.Severity == DiagnosticSeverity.Error));
        Assert.Equal(1, diagnostics.Count(d => d!.Severity == DiagnosticSeverity.Warning));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Build succeeded.")]
    [InlineData("0 Warning(s)")]
    [InlineData("Restore complete (1.2s)")]
    [InlineData("this is just some unrelated log text with (parentheses) in it")]
    public void Returns_null_for_malformed_or_unrelated_lines_instead_of_guessing(string line)
    {
        Assert.Null(MsBuildDiagnosticParser.TryParse(line));
    }

    [Fact]
    public void Trims_the_project_suffix_out_of_the_message()
    {
        var diagnostic = MsBuildDiagnosticParser.TryParse(@"C:\repo\Program.cs(1,1): error CS1002: ; expected [C:\repo\App.csproj]");

        Assert.Equal("; expected", diagnostic!.Message);
        Assert.DoesNotContain("[", diagnostic.Message);
    }
}
