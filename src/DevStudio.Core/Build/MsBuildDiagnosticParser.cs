using System.Text.RegularExpressions;
using DevStudio.Core.Diagnostics;

namespace DevStudio.Core.Build;

/// <summary>
/// Parses real MSBuild/Roslyn console output lines (SKILL.md §24), e.g.:
/// <c>C:\repo\Program.cs(12,34): error CS1002: ; expected [C:\repo\App.csproj]</c>
/// Tolerant of Windows and Unix-style paths, spaces in paths, and both absolute and relative
/// paths. Project-level diagnostics with no source location (e.g. <c>MSB4025</c>) are also
/// recognized, with <see cref="Diagnostic.File"/> empty and <see cref="Diagnostic.Line"/>/<see
/// cref="Diagnostic.Column"/> zero — callers must treat that as "not navigable," never crash on
/// it. A line that matches neither pattern is not a diagnostic and <see cref="TryParse"/> returns
/// null; it is never guessed at.
/// </summary>
public static class MsBuildDiagnosticParser
{
    // "<file>(<line>,<col>): <severity> <code>: <message> [<project>]"
    // The file group is lazy so it stops at the first "(digit,digit):" it finds, which in
    // practice is always the real location marker even when the path itself contains parens.
    private static readonly Regex WithLocation = new(
        @"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s*(?<severity>error|warning|info)\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<message>.*?)(?:\s\[(?<project>.+)\])?$",
        RegexOptions.Compiled);

    // "<severity> <code>: <message>" — project-level diagnostics with no source location, e.g.
    // "error MSB4025: The project file could not be loaded."
    private static readonly Regex WithoutLocation = new(
        @"^(?<severity>error|warning|info)\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<message>.*)$",
        RegexOptions.Compiled);

    public static Diagnostic? TryParse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var withLocation = WithLocation.Match(line);
        if (withLocation.Success)
        {
            return new Diagnostic(
                ParseSeverity(withLocation.Groups["severity"].Value),
                withLocation.Groups["code"].Value,
                withLocation.Groups["message"].Value.Trim(),
                withLocation.Groups["file"].Value.Trim(),
                int.Parse(withLocation.Groups["line"].Value),
                int.Parse(withLocation.Groups["col"].Value),
                ClassifySource(withLocation.Groups["code"].Value));
        }

        var withoutLocation = WithoutLocation.Match(line);
        if (withoutLocation.Success)
        {
            return new Diagnostic(
                ParseSeverity(withoutLocation.Groups["severity"].Value),
                withoutLocation.Groups["code"].Value,
                withoutLocation.Groups["message"].Value.Trim(),
                string.Empty,
                0,
                0,
                ClassifySource(withoutLocation.Groups["code"].Value));
        }

        return null;
    }

    private static DiagnosticSeverity ParseSeverity(string text) => text.ToLowerInvariant() switch
    {
        "error" => DiagnosticSeverity.Error,
        "warning" => DiagnosticSeverity.Warning,
        _ => DiagnosticSeverity.Info,
    };

    private static DiagnosticSource ClassifySource(string code) =>
        code.StartsWith("MSB", StringComparison.OrdinalIgnoreCase) ? DiagnosticSource.BuildSystem : DiagnosticSource.Compiler;
}
