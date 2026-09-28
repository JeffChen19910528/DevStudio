using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DevStudio.App.Views;

/// <summary>The minimal Settings surface this phase adds (SKILL.md §7 [Phase 12]) — General >
/// Language only, per the phase's explicit scope. Presentation only: the actual language change
/// and its persistence both happen in <c>MainWindowViewModel.SelectedLanguageOption</c>, exactly
/// like every other setting in this codebase.</summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
