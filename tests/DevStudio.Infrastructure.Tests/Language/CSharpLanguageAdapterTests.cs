using DevStudio.Infrastructure.Language;
using DevStudio.Infrastructure.Tests.TestSupport;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Language;

/// <summary>Unit tests for the parts of <see cref="CSharpLanguageAdapter"/> that fail before
/// ever touching a real language server process, mirroring the equivalent split for
/// <c>NetCoreDebuggerAdapterTests</c>. The real handshake/completion/hover/definition pipeline
/// is covered by <c>CSharpLanguageIntegrationTests</c>.</summary>
public class CSharpLanguageAdapterTests
{
    [Fact]
    public void SupportsFile_is_true_only_for_cs_files()
    {
        var adapter = new CSharpLanguageAdapter(new FakeProcessRunner(), new RoslynLanguageServerResolver());

        Assert.True(adapter.SupportsFile(@"C:\repo\Program.cs"));
        Assert.False(adapter.SupportsFile(@"C:\repo\readme.md"));
        Assert.False(adapter.SupportsFile(@"C:\repo\App.csproj"));
    }

    [Fact]
    public async Task StartAsync_throws_when_the_workspace_directory_does_not_exist()
    {
        var adapter = new CSharpLanguageAdapter(new FakeProcessRunner(), new RoslynLanguageServerResolver());

        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.StartAsync(@"C:\this\does\not\exist"));
    }

    [Fact]
    public void ResolveLanguageServer_delegates_to_the_resolver()
    {
        var adapter = new CSharpLanguageAdapter(new FakeProcessRunner(), new RoslynLanguageServerResolver());

        var resolution = adapter.ResolveLanguageServer();

        Assert.True(resolution.Found, resolution.Message);
        Assert.True(File.Exists(resolution.ExecutablePath));
    }
}
