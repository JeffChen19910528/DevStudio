namespace DevStudio.Core.Diagnostics;

public enum DiagnosticSource
{
    Compiler,
    Linter,
    LanguageServer,
    Debugger,
    TestRunner,
    BuildSystem,
    PackageManager
}
