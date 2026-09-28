using Avalonia.Controls;

namespace DevStudio.App.Views;

/// <summary>Minimal reusable modal for confirmations/errors (SKILL.md §13, §28). Presentation
/// only — all decision logic (what to ask, what the answer means) lives in the ViewModel layer
/// via <c>DevStudio.UI.Services.IDialogService</c>.</summary>
public partial class DialogWindow : Window
{
    public DialogWindow()
    {
        InitializeComponent();
    }

    public static async Task<string?> ShowAsync(Window owner, string title, string message, params (string Label, string ResultTag)[] buttons)
    {
        var dialog = new DialogWindow { Title = title };
        var messageText = dialog.FindControl<TextBlock>("MessageText")!;
        var buttonsPanel = dialog.FindControl<StackPanel>("ButtonsPanel")!;

        messageText.Text = message;

        foreach (var (label, resultTag) in buttons)
        {
            var button = new Button { Content = label, Tag = resultTag, MinWidth = 80 };
            button.Click += (_, _) => dialog.Close(resultTag);
            buttonsPanel.Children.Add(button);
        }

        return await dialog.ShowDialog<string?>(owner).ConfigureAwait(true);
    }

    public static async Task<string?> ShowInputAsync(Window owner, string title, string message, string defaultValue = "", string okLabel = "OK", string cancelLabel = "Cancel")
    {
        var dialog = new DialogWindow { Title = title };
        var messageText = dialog.FindControl<TextBlock>("MessageText")!;
        var inputBox = dialog.FindControl<TextBox>("InputBox")!;
        var buttonsPanel = dialog.FindControl<StackPanel>("ButtonsPanel")!;

        messageText.Text = message;
        inputBox.IsVisible = true;
        inputBox.Text = defaultValue;

        var okButton = new Button { Content = okLabel, MinWidth = 80 };
        okButton.Click += (_, _) => dialog.Close(inputBox.Text);
        var cancelButton = new Button { Content = cancelLabel, MinWidth = 80 };
        cancelButton.Click += (_, _) => dialog.Close(null);

        buttonsPanel.Children.Add(okButton);
        buttonsPanel.Children.Add(cancelButton);

        return await dialog.ShowDialog<string?>(owner).ConfigureAwait(true);
    }
}
