using DevStudio.Core.Processes;

namespace DevStudio.Core.Packages;

/// <summary>
/// One piece of genuinely shared, pure logic every process-backed <see
/// cref="IPackageManagerAdapter"/> needs (SKILL.md §26 — never surface a raw exception, always a
/// short structured reason): picking the first non-blank line of a failed process's own
/// stderr/stdout to use as <see cref="PackageOperationResult.FailureReason"/>. Extracted after an
/// architecture review found the exact same few lines duplicated verbatim across
/// <c>NuGetPackageAdapter</c>/<c>PythonPackageAdapter</c>/<c>NpmPackageAdapter</c> — a real,
/// evidence-based shared responsibility, not a speculative "might be useful later" abstraction.
/// Deliberately narrow (one method) rather than a general "PackageHelper" dumping ground; each
/// adapter still owns its own command construction, output parsing, and
/// <see cref="PackageOperationResult"/> shaping (e.g. which files it reports as changed), which
/// remain genuinely ecosystem-specific and are not touched by this type.
/// </summary>
public static class PackageOperationDiagnostics
{
    public static string FirstErrorLine(ProcessResult result, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault(l => l.Length > 0);
        return line ?? fallback;
    }
}
