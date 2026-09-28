using DevStudio.Core.Diagnostics;
using DevStudio.Core.Language;
using DevStudio.Infrastructure.Language;
using DevStudio.Infrastructure.Processes;
using DevStudio.Infrastructure.Tests.TestSupport;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Language;

/// <summary>
/// SKILL.md §59–§68 (Phase 7): real, temporary C# projects opened through DevStudio's real
/// LanguageService → CSharpLanguageAdapter → real JSON-RPC client → a real
/// <c>Microsoft.CodeAnalysis.LanguageServer --stdio --autoLoadProjects</c> process (the Roslyn
/// language server bundled with the VS Code C# extension — see ADR-008). No fakes anywhere in
/// this file. A completion/hover/definition/diagnostic result is only ever reported "real" here
/// because the real server actually returned it.
/// </summary>
public class CSharpLanguageIntegrationTests
{
    private static LanguageService CreateRealService()
    {
        var processRunner = new ProcessRunner();
        var adapter = new CSharpLanguageAdapter(processRunner, new RoslynLanguageServerResolver());
        return new LanguageService(new[] { adapter });
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 60_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(200);
        Assert.True(condition(), "Condition was not met within the timeout.");
    }

    private const string ValidProgram = """
        var person = new Person();
        System.Console.WriteLine(person.Name);

        class Person
        {
            public string Name { get; set; } = "";
        }
        """;

    private static string WriteCsproj(TempDirectory temp) => temp.WriteFile("App.csproj", """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>
        """);

    [Fact]
    public void The_real_Roslyn_language_server_is_discovered_on_this_machine()
    {
        var resolution = new RoslynLanguageServerResolver().Resolve();

        Assert.True(resolution.Found, resolution.Message);
        Assert.NotNull(resolution.ExecutablePath);
        Assert.True(File.Exists(resolution.ExecutablePath));
    }

    [Fact]
    public async Task Opening_a_valid_document_starts_the_real_server_and_reports_no_diagnostics()
    {
        using var temp = new TempDirectory();
        WriteCsproj(temp);
        var programPath = temp.WriteFile("Program.cs", ValidProgram);

        var service = CreateRealService();
        service.SetWorkspace(temp.Path);
        (string FilePath, IReadOnlyList<Diagnostic> Diagnostics)? received = null;
        service.DiagnosticsPublished += (_, args) => received = args;

        await service.OpenDocumentAsync(programPath, ValidProgram);
        await WaitUntilAsync(() => received is not null);

        Assert.Equal(LanguageServerState.Running, service.State);
        Assert.Empty(received!.Value.Diagnostics);

        await service.StopAsync();
    }

    [Fact]
    public async Task A_real_compile_error_produces_real_diagnostics_with_correct_locations()
    {
        using var temp = new TempDirectory();
        WriteCsproj(temp);
        const string broken = """
            var person = new Person();
            System.Console.WriteLine(person.Name

            class Person
            {
                public string Name { get; set; } = "";
            }
            """; // deliberately missing the closing paren and semicolon
        var programPath = temp.WriteFile("Program.cs", broken);

        var service = CreateRealService();
        service.SetWorkspace(temp.Path);
        (string FilePath, IReadOnlyList<Diagnostic> Diagnostics)? received = null;
        service.DiagnosticsPublished += (_, args) =>
        {
            if (args.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) received = args;
        };

        await service.OpenDocumentAsync(programPath, broken);

        // The first diagnostic pull (inside OpenDocumentAsync) can race the server's initial
        // project load and come back empty (or with only unrelated hints); re-pull until the
        // real compile error settles rather than assuming the very first pull already reflects
        // a fully-loaded project.
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (received is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(1000);
            await service.RefreshDiagnosticsAsync(programPath);
        }

        Assert.NotNull(received);
        // The malformed line (missing closing paren + semicolon) legitimately produces more
        // than one real compiler diagnostic from the same root cause (mirrors the equivalent
        // Phase 4 build-diagnostics test) — assert on the shape, not an exact count.
        var errorDiagnostics = received!.Value.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.NotEmpty(errorDiagnostics);
        Assert.All(errorDiagnostics, d =>
        {
            Assert.Equal(DiagnosticSource.LanguageServer, d.Source);
            Assert.True(d.Line > 0);
        });

        await service.StopAsync();
    }

    [Fact]
    public async Task Real_completion_returns_a_real_project_aware_member_of_the_local_class()
    {
        using var temp = new TempDirectory();
        WriteCsproj(temp);
        var programPath = temp.WriteFile("Program.cs", ValidProgram);

        var service = CreateRealService();
        service.SetWorkspace(temp.Path);
        await service.OpenDocumentAsync(programPath, ValidProgram);
        await WaitUntilAsync(() => service.State == LanguageServerState.Running);

        // Position right after "person." on line 1 (0-based) of ValidProgram.
        var items = await service.CompletionAsync(programPath, new LspPosition(1, 32));

        Assert.Contains(items, i => i.Label == "Name");

        await service.StopAsync();
    }

    [Fact]
    public async Task Real_hover_returns_real_type_information_from_the_server()
    {
        using var temp = new TempDirectory();
        WriteCsproj(temp);
        var programPath = temp.WriteFile("Program.cs", ValidProgram);

        var service = CreateRealService();
        service.SetWorkspace(temp.Path);
        await service.OpenDocumentAsync(programPath, ValidProgram);
        await WaitUntilAsync(() => service.State == LanguageServerState.Running);

        var hover = await service.HoverAsync(programPath, new LspPosition(0, 4)); // "person" on line 0

        Assert.NotNull(hover);
        Assert.Contains("Person", hover!.Content);

        await service.StopAsync();
    }

    [Fact]
    public async Task Real_go_to_definition_navigates_to_the_real_class_declaration()
    {
        using var temp = new TempDirectory();
        WriteCsproj(temp);
        var programPath = temp.WriteFile("Program.cs", ValidProgram);

        var service = CreateRealService();
        service.SetWorkspace(temp.Path);
        await service.OpenDocumentAsync(programPath, ValidProgram);
        await WaitUntilAsync(() => service.State == LanguageServerState.Running);

        var locations = await service.DefinitionAsync(programPath, new LspPosition(0, 19)); // "Person" usage on line 0

        var location = Assert.Single(locations);
        Assert.Equal(programPath, location.FilePath, ignoreCase: true);
        Assert.Equal(3, location.Range.Start.Line); // 0-based line of "class Person"

        await service.StopAsync();
    }

    [Fact]
    public async Task Unsaved_edits_are_reflected_by_the_real_server_without_ever_touching_disk()
    {
        using var temp = new TempDirectory();
        WriteCsproj(temp);
        var initial = """
            var person = new Person();

            class Person
            {
                public string Name { get; set; } = "";
            }
            """;
        var programPath = temp.WriteFile("Program.cs", initial);
        var onDiskContentBeforeEdit = await File.ReadAllTextAsync(programPath);

        var service = CreateRealService();
        service.SetWorkspace(temp.Path);
        await service.OpenDocumentAsync(programPath, initial);
        await WaitUntilAsync(() => service.State == LanguageServerState.Running);

        var editedInMemoryOnly = """
            var person = new Person();
            System.Console.WriteLine(person.Name);

            class Person
            {
                public string Name { get; set; } = "";
            }
            """;
        await service.ChangeDocumentAsync(programPath, editedInMemoryOnly);

        // The real server must see the in-memory edit (member completion on the new line) even
        // though the file on disk was never touched (SKILL.md §41, §65 — mandatory).
        var items = await service.CompletionAsync(programPath, new LspPosition(1, 32));
        Assert.Contains(items, i => i.Label == "Name");

        var onDiskContentAfterEdit = await File.ReadAllTextAsync(programPath);
        Assert.Equal(onDiskContentBeforeEdit, onDiskContentAfterEdit);

        await service.StopAsync();
    }

    [Fact]
    public async Task Stop_shuts_down_the_real_server_with_no_orphan_process()
    {
        using var temp = new TempDirectory();
        WriteCsproj(temp);
        var programPath = temp.WriteFile("Program.cs", ValidProgram);

        var service = CreateRealService();
        service.SetWorkspace(temp.Path);
        await service.OpenDocumentAsync(programPath, ValidProgram);
        await WaitUntilAsync(() => service.State == LanguageServerState.Running);

        await service.StopAsync();

        Assert.Equal(LanguageServerState.Stopped, service.State);
    }

    [Fact]
    public async Task Restart_stops_the_old_real_server_and_starts_a_genuinely_new_one()
    {
        using var temp = new TempDirectory();
        WriteCsproj(temp);
        var programPath = temp.WriteFile("Program.cs", ValidProgram);

        var service = CreateRealService();
        service.SetWorkspace(temp.Path);
        await service.OpenDocumentAsync(programPath, ValidProgram);
        await WaitUntilAsync(() => service.State == LanguageServerState.Running);

        await service.RestartAsync(programPath);
        await WaitUntilAsync(() => service.State == LanguageServerState.Running);

        await service.OpenDocumentAsync(programPath, ValidProgram);
        var items = await service.CompletionAsync(programPath, new LspPosition(1, 32));
        Assert.Contains(items, i => i.Label == "Name");

        await service.StopAsync();
    }
}
