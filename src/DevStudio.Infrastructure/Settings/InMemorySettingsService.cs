using DevStudio.Core.Settings;

namespace DevStudio.Infrastructure.Settings;

public sealed class InMemorySettingsService : ISettingsService
{
    public AppSettings Current { get; private set; } = new();

    public event EventHandler<AppSettings>? Changed;

    public void Update(AppSettings settings)
    {
        Current = settings;
        Changed?.Invoke(this, settings);
    }
}
