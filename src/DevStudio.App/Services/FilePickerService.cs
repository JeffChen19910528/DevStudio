using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DevStudio.UI.Localization;
using DevStudio.UI.Services;

namespace DevStudio.App.Services;

public sealed class FilePickerService : IFilePickerService
{
    private readonly Window _owner;
    private readonly ILocalizationService _loc;

    public FilePickerService(Window owner, ILocalizationService loc)
    {
        _owner = owner;
        _loc = loc;
    }

    public async Task<string?> PickFileToOpenAsync()
    {
        var result = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = _loc.GetString("Dialog.PickFileToOpen.Title"),
            AllowMultiple = false,
        }).ConfigureAwait(true);

        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFileToSaveAsAsync(string? suggestedFileName)
    {
        var result = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = _loc.GetString("Dialog.PickFileToSaveAs.Title"),
            SuggestedFileName = suggestedFileName,
        }).ConfigureAwait(true);

        return result?.TryGetLocalPath();
    }
}
