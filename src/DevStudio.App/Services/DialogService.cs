using Avalonia.Controls;
using DevStudio.App.Views;
using DevStudio.UI.Localization;
using DevStudio.UI.Services;

namespace DevStudio.App.Services;

public sealed class DialogService : IDialogService
{
    private readonly Window _owner;
    private readonly ILocalizationService _loc;

    public DialogService(Window owner, ILocalizationService loc)
    {
        _owner = owner;
        _loc = loc;
    }

    public async Task<SaveChangesResult> AskSaveChangesAsync(string documentName)
    {
        var result = await DialogWindow.ShowAsync(
            _owner,
            _loc.GetString("Dialog.UnsavedChanges.Title"),
            _loc.Format("Dialog.UnsavedChanges.Message", documentName),
            (_loc.GetString("Dialog.Save"), "save"),
            (_loc.GetString("Dialog.DontSave"), "dontsave"),
            (_loc.GetString("Dialog.Cancel"), "cancel")).ConfigureAwait(true);

        return result switch
        {
            "save" => SaveChangesResult.Save,
            "dontsave" => SaveChangesResult.DontSave,
            _ => SaveChangesResult.Cancel,
        };
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var result = await DialogWindow.ShowAsync(_owner, title, message,
            (_loc.GetString("Dialog.Ok"), "ok"), (_loc.GetString("Dialog.Cancel"), "cancel")).ConfigureAwait(true);
        return result == "ok";
    }

    public Task ShowErrorAsync(string title, string message)
        => DialogWindow.ShowAsync(_owner, title, message, (_loc.GetString("Dialog.Ok"), "ok"));
}
