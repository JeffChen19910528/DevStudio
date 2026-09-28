using DevStudio.Core.Settings;
using DevStudio.Infrastructure.Settings;
using Xunit;

namespace DevStudio.Infrastructure.Tests;

public class InMemorySettingsServiceTests
{
    [Fact]
    public void Defaults_to_dark_theme()
    {
        var service = new InMemorySettingsService();
        Assert.Equal(AppTheme.Dark, service.Current.Theme);
    }

    [Fact]
    public void Update_replaces_current_settings_and_raises_Changed()
    {
        var service = new InMemorySettingsService();
        AppSettings? raised = null;
        service.Changed += (_, settings) => raised = settings;

        service.Update(service.Current with { Theme = AppTheme.Light });

        Assert.Equal(AppTheme.Light, service.Current.Theme);
        Assert.NotNull(raised);
        Assert.Equal(AppTheme.Light, raised!.Theme);
    }
}
