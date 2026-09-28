namespace DevStudio.UI.Services;

public interface IFilePickerService
{
    Task<string?> PickFileToOpenAsync();

    Task<string?> PickFileToSaveAsAsync(string? suggestedFileName);
}
