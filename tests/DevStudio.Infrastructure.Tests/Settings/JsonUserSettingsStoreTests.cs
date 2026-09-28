using DevStudio.Core.Settings;
using DevStudio.Infrastructure.Settings;
using DevStudio.Infrastructure.Tests.TestSupport;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Settings;

public class JsonUserSettingsStoreTests
{
    [Fact]
    public async Task Round_trips_settings_including_recent_workspaces()
    {
        using var temp = new TempDirectory();
        var store = new JsonUserSettingsStore(Path.Combine(temp.Path, "settings.json"));
        var settings = new AppSettings(
            Theme: AppTheme.Light,
            ReopenLastWorkspaceOnStartup: true,
            RecentWorkspaces: new[] { new RecentWorkspaceEntry("C:/repo", "repo", DateTimeOffset.UtcNow) });

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal(AppTheme.Light, loaded!.Theme);
        Assert.True(loaded.ReopenLastWorkspaceOnStartup);
        Assert.Equal("repo", loaded.RecentWorkspaces[0].DisplayName);
    }

    [Fact]
    public async Task Round_trips_the_selected_UI_language_across_a_real_save_and_load()
    {
        using var temp = new TempDirectory();
        var store = new JsonUserSettingsStore(Path.Combine(temp.Path, "settings.json"));
        var settings = new AppSettings(Language: "zh-TW");

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal("zh-TW", loaded!.Language);
    }

    [Fact]
    public void Language_defaults_to_en_US_when_never_set()
    {
        var settings = new AppSettings();

        Assert.Equal("en-US", settings.Language);
    }

    [Fact]
    public async Task Missing_file_returns_null_instead_of_throwing()
    {
        using var temp = new TempDirectory();
        var store = new JsonUserSettingsStore(Path.Combine(temp.Path, "does-not-exist.json"));

        var loaded = await store.LoadAsync();

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Corrupted_file_returns_null_instead_of_throwing()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("settings.json", "{ not valid json");
        var store = new JsonUserSettingsStore(path);

        var loaded = await store.LoadAsync();

        Assert.Null(loaded);
    }
}
