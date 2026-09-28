using DevStudio.Core.Packages;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Toolchains;
using DevStudio.Infrastructure.Packages;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Toolchains;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Packages;

/// <summary>
/// Unit tests for <see cref="ConanPackageAdapter"/>. <c>conan</c> is not installed on the machine
/// these tests were written on, so every command-construction/output-parsing/manifest-editing path
/// is exercised against realistic fixture text through <see cref="FakeProcessRunner"/> and real
/// temporary manifest files — never a real Conan invocation (see ADR-015).
/// </summary>
public class ConanPackageAdapterTests
{
    private sealed class FixedDetector : IToolchainDetector
    {
        private readonly bool _installed;
        public FixedDetector(bool installed) => _installed = installed;
        public string ToolchainId => WellKnownToolchainIds.Conan;
        public Task<ToolchainInfo> DetectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_installed
                ? new ToolchainInfo(ToolchainId, "Conan", ToolchainDetectionState.Detected, ExecutablePath: "conan")
                : new ToolchainInfo(ToolchainId, "Conan", ToolchainDetectionState.NotInstalled));
    }

    private static async Task<ToolchainRegistry> RegistryAsync(bool installed)
    {
        var registry = new ToolchainRegistry();
        registry.Register(new FixedDetector(installed));
        await registry.RefreshAsync();
        return registry;
    }

    private static ProjectInfo MakeProjectInfo(string rootPath) =>
        new("id", "fixture", rootPath, ProjectType.CMake, Path.Combine(rootPath, "CMakeLists.txt"), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());

    private const string MinimalConanfileTxt = """
        [requires]
        zlib/1.3.1
        fmt/10.2.1

        [generators]
        CMakeToolchain
        CMakeDeps
        """;

    private const string MinimalConanfilePy = """
        from conan import ConanFile

        class MyRecipe(ConanFile):
            def requirements(self):
                self.requires("zlib/1.3.1")
                self.requires("fmt/10.2.1")
        """;

    private const string SampleGraphInfoJson = """
        {
          "graph": {
            "nodes": {
              "0": { "name": "myproject", "version": "None", "recipe": "Consumer",
                "dependencies": { "1": { "direct": true }, "2": { "direct": true } } },
              "1": { "name": "zlib", "version": "1.3.1", "recipe": "Cache",
                "dependencies": {} },
              "2": { "name": "fmt", "version": "10.2.1", "recipe": "Cache",
                "dependencies": { "3": { "direct": true } } },
              "3": { "name": "fmt-support", "version": "10.2.1", "recipe": "Cache",
                "dependencies": {} }
            }
          }
        }
        """;

    [Fact]
    public void DetectProject_returns_null_when_project_type_is_not_CMake()
    {
        var adapter = new ConanPackageAdapter(new FakeProcessRunner(), new ToolchainRegistry());
        var project = new ProjectInfo("id", "n", "/x", ProjectType.DotNet, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProjectCapability>());

        Assert.Null(adapter.DetectProject(project));
    }

    [Fact]
    public void DetectProject_returns_null_when_no_conanfile_exists()
    {
        using var dir = new TempDirectory();
        var adapter = new ConanPackageAdapter(new FakeProcessRunner(), new ToolchainRegistry());

        Assert.Null(adapter.DetectProject(MakeProjectInfo(dir.Path)));
    }

    [Fact]
    public async Task DetectProject_reports_unavailable_when_conan_is_not_installed()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("conanfile.txt", MinimalConanfileTxt);
        var adapter = new ConanPackageAdapter(new FakeProcessRunner(), await RegistryAsync(installed: false));

        var project = adapter.DetectProject(MakeProjectInfo(dir.Path));

        Assert.NotNull(project);
        Assert.Equal(PackageManagerCapabilities.None, project!.Capabilities);
        Assert.NotNull(project.UnavailableReason);
    }

    [Fact]
    public async Task DetectProject_grants_mutation_capabilities_for_conanfile_txt()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("conanfile.txt", MinimalConanfileTxt);
        var adapter = new ConanPackageAdapter(new FakeProcessRunner(), await RegistryAsync(installed: true));

        var project = adapter.DetectProject(MakeProjectInfo(dir.Path));

        Assert.NotNull(project);
        Assert.EndsWith("conanfile.txt", project!.ProjectPath);
        Assert.True(project.Capabilities.Add);
        Assert.True(project.Capabilities.Remove);
        Assert.True(project.Capabilities.Update);
        Assert.True(project.Capabilities.ListInstalled);
    }

    [Fact]
    public async Task DetectProject_denies_mutation_capabilities_for_conanfile_py()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("conanfile.py", MinimalConanfilePy);
        var adapter = new ConanPackageAdapter(new FakeProcessRunner(), await RegistryAsync(installed: true));

        var project = adapter.DetectProject(MakeProjectInfo(dir.Path));

        Assert.NotNull(project);
        Assert.EndsWith("conanfile.py", project!.ProjectPath);
        Assert.False(project.Capabilities.Add);
        Assert.False(project.Capabilities.Remove);
        Assert.False(project.Capabilities.Update);
        Assert.True(project.Capabilities.ListInstalled); // Inspector-only, per ADR-015.
    }

    [Fact]
    public async Task DetectProject_prefers_conanfile_py_when_both_manifests_exist()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("conanfile.txt", MinimalConanfileTxt);
        dir.WriteFile("conanfile.py", MinimalConanfilePy);
        var adapter = new ConanPackageAdapter(new FakeProcessRunner(), await RegistryAsync(installed: true));

        var project = adapter.DetectProject(MakeProjectInfo(dir.Path));

        Assert.NotNull(project);
        Assert.EndsWith("conanfile.py", project!.ProjectPath);
    }

    [Fact]
    public void ReadDeclaredDependenciesFromTxt_parses_the_requires_section_only()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("conanfile.txt", MinimalConanfileTxt);

        var declared = ConanRecipeReader.ReadDeclaredDependenciesFromTxt(Path.Combine(dir.Path, "conanfile.txt"));

        Assert.Equal(2, declared.Count);
        Assert.Contains(declared, d => d.PackageId == "zlib" && d.Version == "1.3.1");
        Assert.Contains(declared, d => d.PackageId == "fmt" && d.Version == "10.2.1");
    }

    [Fact]
    public void ReadDeclaredDependenciesFromPy_finds_literal_self_requires_calls()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("conanfile.py", MinimalConanfilePy);

        var declared = ConanRecipeReader.ReadDeclaredDependenciesFromPy(Path.Combine(dir.Path, "conanfile.py"));

        Assert.Equal(2, declared.Count);
        Assert.Contains(declared, d => d.PackageId == "zlib" && d.Version == "1.3.1");
    }

    [Fact]
    public async Task ListInstalledAsync_parses_conan_graph_info_and_classifies_direct_vs_transitive()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("conanfile.txt", MinimalConanfileTxt);
        var runner = new FakeProcessRunner();
        runner.SetResult("conan", 0, SampleGraphInfoJson);
        var adapter = new ConanPackageAdapter(runner, await RegistryAsync(installed: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var installed = await adapter.ListInstalledAsync(project);

        Assert.Equal(3, installed.Count); // consumer node (myproject) excluded
        Assert.Equal(PackageDependencyKind.Direct, installed.Single(p => p.PackageId == "zlib").Kind);
        Assert.Equal(PackageDependencyKind.Direct, installed.Single(p => p.PackageId == "fmt").Kind);
        Assert.Equal(PackageDependencyKind.Transitive, installed.Single(p => p.PackageId == "fmt-support").Kind);
        Assert.Contains(runner.Requests, r => r.Arguments.Contains("info"));
    }

    [Fact]
    public async Task AddAsync_appends_to_the_requires_section_of_conanfile_txt()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("conanfile.txt", MinimalConanfileTxt);
        var adapter = new ConanPackageAdapter(new FakeProcessRunner(), await RegistryAsync(installed: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var result = await adapter.AddAsync(project, "boost", "1.83.0", false, false);

        Assert.True(result.Success);
        var declared = ConanRecipeReader.ReadDeclaredDependenciesFromTxt(project.ProjectPath);
        Assert.Contains(declared, d => d.PackageId == "boost" && d.Version == "1.83.0");
        Assert.Contains("[generators]", File.ReadAllText(project.ProjectPath));
    }

    [Fact]
    public async Task AddAsync_is_unavailable_for_conanfile_py()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("conanfile.py", MinimalConanfilePy);
        var adapter = new ConanPackageAdapter(new FakeProcessRunner(), await RegistryAsync(installed: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var result = await adapter.AddAsync(project, "boost", "1.83.0", false, false);

        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task RemoveAsync_removes_a_requirement_line_from_conanfile_txt()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("conanfile.txt", MinimalConanfileTxt);
        var adapter = new ConanPackageAdapter(new FakeProcessRunner(), await RegistryAsync(installed: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var result = await adapter.RemoveAsync(project, "zlib");

        Assert.True(result.Success);
        var declared = ConanRecipeReader.ReadDeclaredDependenciesFromTxt(project.ProjectPath);
        Assert.DoesNotContain(declared, d => d.PackageId == "zlib");
        Assert.Contains(declared, d => d.PackageId == "fmt");
    }

    [Fact]
    public async Task RestoreAsync_invokes_conan_install()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("conanfile.txt", MinimalConanfileTxt);
        var runner = new FakeProcessRunner();
        runner.SetResult("conan", 0, "");
        var adapter = new ConanPackageAdapter(runner, await RegistryAsync(installed: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var result = await adapter.RestoreAsync(project);

        Assert.True(result.Success);
        Assert.Contains("install", runner.Requests.Single(r => r.ExecutablePath == "conan").Arguments);
    }

    [Fact]
    public async Task GetSourcesAsync_parses_conan_remote_list_json_without_credentials()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("conanfile.txt", MinimalConanfileTxt);
        var runner = new FakeProcessRunner();
        runner.SetResult("conan", 0, """[ { "name": "conancenter", "url": "https://center.conan.io", "verify_ssl": true, "enabled": true } ]""");
        var adapter = new ConanPackageAdapter(runner, await RegistryAsync(installed: true));
        var project = adapter.DetectProject(MakeProjectInfo(dir.Path))!;

        var sources = await adapter.GetSourcesAsync(project);

        Assert.Single(sources);
        Assert.Equal("conancenter", sources[0].Name);
        Assert.Equal("https://center.conan.io", sources[0].Location);
    }

    [Fact]
    public void SearchParser_parses_the_remote_to_reference_array_map()
    {
        var parsed = ConanSearchParser.Parse("""{ "conancenter": [ "zlib/1.3.1", "zlib/1.2.13" ] }""");
        Assert.Equal(2, parsed.Count);
        Assert.Equal("zlib", parsed[0].PackageId);
    }

    [Fact]
    public void GraphInfoParser_returns_empty_on_malformed_json()
    {
        var parsed = ConanGraphInfoParser.ParseInstalled("not json", "/x/conanfile.txt", "conan");
        Assert.Empty(parsed);
    }
}
