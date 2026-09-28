using DevStudio.Core.Workspace;
using DevStudio.Infrastructure.Tests.TestSupport;
using DevStudio.Infrastructure.Workspace;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Workspace;

public class JsonWorkspaceStateStoreTests
{
    [Fact]
    public async Task Round_trips_a_saved_state()
    {
        using var temp = new TempDirectory();
        var store = new JsonWorkspaceStateStore();
        var state = new WorkspaceState(
            WorkspaceState.CurrentVersion,
            temp.Path,
            new[] { "a.cs", "b.cs" },
            "a.cs",
            "project-1");

        await store.SaveAsync(temp.Path, state);
        var result = await store.LoadAsync(temp.Path);

        Assert.False(result.WasCorrupted);
        Assert.NotNull(result.State);
        Assert.Equal(state.OpenDocumentPaths, result.State!.OpenDocumentPaths);
        Assert.Equal(state.ActiveDocumentPath, result.State.ActiveDocumentPath);
        Assert.Equal(state.ActiveProjectId, result.State.ActiveProjectId);
    }

    [Fact]
    public async Task Missing_state_file_is_reported_as_absent_not_corrupted()
    {
        using var temp = new TempDirectory();
        var store = new JsonWorkspaceStateStore();

        var result = await store.LoadAsync(temp.Path);

        Assert.False(result.WasCorrupted);
        Assert.Null(result.State);
    }

    [Fact]
    public async Task Corrupted_json_never_throws_and_is_reported_as_corrupted()
    {
        using var temp = new TempDirectory();
        temp.WriteFile(".devstudio/workspace.json", "{ this is not valid json ");

        var store = new JsonWorkspaceStateStore();
        var result = await store.LoadAsync(temp.Path);

        Assert.True(result.WasCorrupted);
        Assert.Null(result.State);
    }

    [Fact]
    public async Task Unknown_version_is_treated_as_corrupted_rather_than_silently_used()
    {
        using var temp = new TempDirectory();
        temp.WriteFile(".devstudio/workspace.json", "{ \"Version\": 999, \"RootPath\": \"x\", \"OpenDocumentPaths\": [], \"ActiveDocumentPath\": null, \"ActiveProjectId\": null }");

        var store = new JsonWorkspaceStateStore();
        var result = await store.LoadAsync(temp.Path);

        Assert.True(result.WasCorrupted);
    }
}
