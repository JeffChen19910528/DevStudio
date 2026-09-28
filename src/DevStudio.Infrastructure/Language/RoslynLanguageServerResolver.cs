using DevStudio.Core.Language;
using DevStudio.Infrastructure.Toolchains;

namespace DevStudio.Infrastructure.Language;

/// <summary>
/// Finds a real C# language server (SKILL.md §10–§11 [Phase 7]) without ever assuming one exists
/// merely because the .NET SDK is installed. The real, verified candidate on this development
/// machine is <c>Microsoft.CodeAnalysis.LanguageServer.exe</c> ("the Roslyn language server") —
/// the same MIT-licensed, real LSP-over-stdio server the VS Code C# extension itself uses,
/// bundled in that extension's private <c>.roslyn</c> folder (confirmed with a real
/// <c>initialize</c>/<c>textDocument/completion</c>/<c>hover</c>/<c>definition</c> exchange; see
/// ADR-008). Unlike Phase 6's <c>vsdbg</c>, this server carries no client-identity restriction —
/// its license (MIT, printed in the extension's <c>LICENSE.txt</c>) permits any client. Every
/// candidate is verified with a real <see cref="File.Exists"/> check; nothing is ever assumed
/// present.
/// </summary>
public sealed class RoslynLanguageServerResolver
{
    private const string OverrideEnvironmentVariable = "DEVSTUDIO_ROSLYN_LSP_PATH";

    public LanguageServerResolution Resolve()
    {
        var overridePath = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return File.Exists(overridePath)
                ? new LanguageServerResolution(true, overridePath, null, $"{OverrideEnvironmentVariable} override", null)
                : new LanguageServerResolution(false, null, null, null, $"{OverrideEnvironmentVariable} was set to '{overridePath}', but no file exists there.");
        }

        var executableName = OperatingSystem.IsWindows() ? "Microsoft.CodeAnalysis.LanguageServer.exe" : "Microsoft.CodeAnalysis.LanguageServer";
        var onPath = ExecutableLocator.FindOnPath("Microsoft.CodeAnalysis.LanguageServer");
        if (onPath is not null)
        {
            return new LanguageServerResolution(true, onPath, null, "PATH", null);
        }

        var found = FindNewestVsCodeCSharpExtensionRoslynServer(executableName);
        if (found is not null)
        {
            return new LanguageServerResolution(true, found.Value.Path, found.Value.Version, "VS Code C# extension", null);
        }

        return new LanguageServerResolution(false, null, null, null,
            "C# language server not found: install the C# extension for VS Code, or set " +
            "DEVSTUDIO_ROSLYN_LSP_PATH to a real Microsoft.CodeAnalysis.LanguageServer executable.");
    }

    /// <summary>Deterministic selection rule (mirrors <c>NetCoreDebuggerResolver</c>'s approach
    /// for the equally version-suffixed VS Code extension folders): prefer the
    /// highest-versioned <c>ms-dotnettools.csharp-*</c> extension folder that actually contains
    /// the real executable.</summary>
    private static (string Path, string? Version)? FindNewestVsCodeCSharpExtensionRoslynServer(string executableName)
    {
        var extensionsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vscode", "extensions");
        if (!Directory.Exists(extensionsRoot)) return null;

        var candidates = Directory.EnumerateDirectories(extensionsRoot, "ms-dotnettools.csharp-*")
            .Select(dir => (Directory: dir, Version: ExtractVersion(Path.GetFileName(dir))))
            .Where(x => x.Version is not null)
            .OrderByDescending(x => x.Version);

        foreach (var (extensionDir, _) in candidates)
        {
            var candidate = Path.Combine(extensionDir, ".roslyn", executableName);
            if (!File.Exists(candidate)) continue;

            var versionFile = Path.Combine(extensionDir, ".roslyn", "RoslynVersion.txt");
            var version = File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : null;
            return (candidate, version);
        }

        return null;
    }

    private static Version? ExtractVersion(string extensionFolderName)
    {
        const string prefix = "ms-dotnettools.csharp-";
        if (!extensionFolderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var remainder = extensionFolderName[prefix.Length..];
        var versionPart = remainder.Split('-')[0];
        return System.Version.TryParse(versionPart, out var version) ? version : null;
    }
}
