namespace DevStudio.UI.Services;

public enum SaveChangesResult
{
    Save,
    DontSave,
    Cancel
}

/// <summary>User-facing confirmation/error surfaces (SKILL.md §13 [unsaved changes], §28 [error
/// handling]). Implemented in DevStudio.App with a real dialog window — ViewModels only ask
/// the question, they never render it.</summary>
public interface IDialogService
{
    Task<SaveChangesResult> AskSaveChangesAsync(string documentName);

    Task<bool> ConfirmAsync(string title, string message);

    Task ShowErrorAsync(string title, string message);
}
