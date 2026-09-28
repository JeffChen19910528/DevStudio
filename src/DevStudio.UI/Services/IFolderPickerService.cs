namespace DevStudio.UI.Services;

/// <summary>Shows a native "choose a folder" dialog. Implemented in DevStudio.App, which owns
/// the actual Window/TopLevel needed to show it — ViewModels never touch Avalonia's storage APIs
/// directly.</summary>
public interface IFolderPickerService
{
    Task<string?> PickFolderAsync();
}
