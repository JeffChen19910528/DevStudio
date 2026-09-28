using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DevStudio.UI.Localization;
using DevStudio.UI.Services;

namespace DevStudio.App.Services;

public sealed class FolderPickerService : IFolderPickerService
{
    private readonly Window _owner;
    private readonly ILocalizationService _loc;

    public FolderPickerService(Window owner, ILocalizationService loc)
    {
        _owner = owner;
        _loc = loc;
    }

    public async Task<string?> PickFolderAsync()
    {
        var result = await _owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = _loc.GetString("Dialog.PickFolder.Title"),
            AllowMultiple = false,
        }).ConfigureAwait(true);

        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }
}
