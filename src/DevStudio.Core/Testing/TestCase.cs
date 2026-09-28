namespace DevStudio.Core.Testing;

/// <summary>
/// One real, discovered test (SKILL.md §5 [Phase 8]) — produced entirely by the real .NET test
/// infrastructure (<c>dotnet test --list-tests</c>), never by parsing C# source and guessing
/// test methods. <see cref="SourceFile"/>/<see cref="Line"/> are left null here: real .NET test
/// discovery (VSTest's <c>--list-tests</c>) does not report a source location for a
/// not-yet-run test — only a real failure's stack trace does (see <see cref="TestResult"/>).
/// Unknown values stay null/empty rather than fabricated.
/// </summary>
public sealed record TestCase(
    string Id,
    string DisplayName,
    string FullyQualifiedName,
    string ProjectId,
    string? SourceFile,
    int? Line,
    IReadOnlyList<string> Traits,
    string? Framework);
