using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using DevStudio.Core.Diagnostics;
using DevStudio.Core.Extensions;
using DevStudio.Core.Git;
using DevStudio.Core.Language;
using DevStudio.Core.Settings;
using DevStudio.UI.Services;
using DevStudio.UI.ViewModels;

namespace DevStudio.App.Views;

/// <summary>
/// The application shell window. Contains only presentation glue: routing menu clicks to
/// ViewModel commands, delegating native text-editing gestures (undo/redo/cut/copy/paste) to
/// whichever TextBox has focus, and bridging the one thing a plain data binding can't do —
/// moving the editor's caret/selection for Find/Replace/Go To Line (SKILL.md §6 [MVVM]: no
/// file IO, process execution, or workspace logic lives here).
/// </summary>
public partial class MainWindow : Window
{
    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (ViewModel is { } vm)
            {
                vm.CaretMoveRequested += OnCaretMoveRequested;
                SubscribeOutputAutoScroll(vm);
            }
        };
        Closing += OnClosing;
    }

    private void SubscribeOutputAutoScroll(MainWindowViewModel vm)
    {
        vm.Output.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(OutputPanelViewModel.OutputText)) return;
            var textBox = this.FindControl<TextBox>("OutputTextBox");
            if (textBox is not null)
                textBox.CaretIndex = textBox.Text?.Length ?? 0;
        };
    }

    private bool _closeConfirmed;
    private Func<Task>? _saveWorkspaceStateHook;

    /// <summary>Lets the composition root (DevStudio.App) supply the "persist workspace state"
    /// action without this Window needing to know about <c>IWorkspaceStateStore</c> itself.</summary>
    public void SetWorkspaceStateSaveHook(Func<Task> hook) => _saveWorkspaceStateHook = hook;

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || ViewModel is not { } vm) return;

        e.Cancel = true;

        var dialogService = new Services.DialogService(this, vm.Loc);

        foreach (var document in vm.Documents.ToList())
        {
            if (!document.IsModified) continue;

            var result = await dialogService.AskSaveChangesAsync(document.DisplayName);
            switch (result)
            {
                case SaveChangesResult.Cancel:
                    return;
                case SaveChangesResult.Save:
                    var saved = await vm.TrySaveDocumentAsync(document);
                    if (!saved) return; // save failed or "Save As" was dismissed; abort the close.
                    break;
                case SaveChangesResult.DontSave:
                    break;
            }
        }

        if (_saveWorkspaceStateHook is not null)
        {
            await _saveWorkspaceStateHook().ConfigureAwait(true);
        }

        _closeConfirmed = true;
        Close();
    }

    private void OnRecentWorkspacesSubmenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || ViewModel is not { } vm) return;

        menuItem.Items.Clear();

        foreach (var entry in vm.RecentWorkspaces)
        {
            var missing = !Directory.Exists(entry.Path);
            var item = new MenuItem { Header = missing ? vm.Loc.Format("Menu.File.RecentWorkspaces.MissingSuffix", entry.DisplayName) : entry.DisplayName };
            item.Click += async (_, _) => await vm.OpenRecentWorkspaceCommand.ExecuteAsync(entry.Path);
            menuItem.Items.Add(item);
        }

        if (menuItem.Items.Count == 0)
        {
            menuItem.Items.Add(new MenuItem { Header = vm.Loc.GetString("Menu.File.RecentWorkspaces.None"), IsEnabled = false });
        }
    }

    private void OnExitClicked(object? sender, RoutedEventArgs e) => Close();

    private void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow { DataContext = DataContext };
        settingsWindow.Show(this);
    }

    private void OnUndoClicked(object? sender, RoutedEventArgs e) => (GetFocusedTextBox())?.Undo();
    private void OnRedoClicked(object? sender, RoutedEventArgs e) => (GetFocusedTextBox())?.Redo();
    private void OnCutClicked(object? sender, RoutedEventArgs e) => GetFocusedTextBox()?.Cut();
    private void OnCopyClicked(object? sender, RoutedEventArgs e) => GetFocusedTextBox()?.Copy();
    private void OnPasteClicked(object? sender, RoutedEventArgs e) => GetFocusedTextBox()?.Paste();

    private TextBox? GetFocusedTextBox() => FocusManager?.GetFocusedElement() as TextBox;

    private void OnShowFindClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.IsFindPanelVisible = !vm.IsFindPanelVisible;
        }
    }

    private async void OnGoToLineClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        var input = await DialogWindow.ShowInputAsync(this, vm.Loc.GetString("Dialog.GoToLine.Title"), vm.Loc.GetString("Dialog.GoToLine.Message"),
            okLabel: vm.Loc.GetString("Dialog.Ok"), cancelLabel: vm.Loc.GetString("Dialog.Cancel"));
        if (int.TryParse(input, out var line))
        {
            vm.GoToLineCommand.Execute(line);
        }
    }

    private async void OnToggleBreakpointClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        var input = await DialogWindow.ShowInputAsync(this, vm.Loc.GetString("Dialog.ToggleBreakpoint.Title"), vm.Loc.GetString("Dialog.ToggleBreakpoint.Message"),
            okLabel: vm.Loc.GetString("Dialog.Ok"), cancelLabel: vm.Loc.GetString("Dialog.Cancel"));
        if (int.TryParse(input, out var line))
        {
            await vm.ToggleBreakpointCommand.ExecuteAsync(line);
        }
    }

    private void OnCompletionItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: CompletionItem item } && ViewModel is { } vm)
        {
            vm.ApplyCompletionCommand.Execute(item);
        }
    }

    private void OnExplorerDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is TreeView { SelectedItem: FileTreeNodeViewModel node } && ViewModel is { } vm)
        {
            vm.Explorer.ActivateFile(node);
        }
    }

    private void OnExplorerContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (ViewModel is not { } vm) return;

        if (e.Source is not Control { DataContext: FileTreeNodeViewModel node } sourceControl
            || !node.IsDirectory)
            return;

        var item = new MenuItem { Header = vm.Loc["Explorer.SetAsStartupProject"] };
        item.Click += (_, _) => vm.SetStartupProject(node);

        var menu = new ContextMenu();
        menu.Items.Add(item);
        menu.Open(sourceControl);
        e.Handled = true;
    }

    private void OnDocumentTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // TabControl selection is already two-way bound to ActiveDocument; nothing else to do
        // here, but the handler is kept as the natural place to add editor-focus behavior later.
    }

    private async void OnReloadFromDiskClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DocumentViewModel document } && ViewModel is { } vm)
        {
            await vm.ReloadFromDiskCommand.ExecuteAsync(document);
        }
    }

    private async void OnDiagnosticClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Diagnostic diagnostic } && ViewModel is { } vm)
        {
            await vm.NavigateToDiagnosticCommand.ExecuteAsync(diagnostic);
        }
    }

    private async void OnTestNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: TestNodeViewModel node } && ViewModel is { } vm)
        {
            await vm.NavigateToTestNodeCommand.ExecuteAsync(node);
        }
    }

    private async void OnStageClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: GitFileStatus file } && ViewModel is { } vm)
        {
            await vm.SourceControl.StageCommand.ExecuteAsync(file);
        }
    }

    private async void OnUnstageClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: GitFileStatus file } && ViewModel is { } vm)
        {
            await vm.SourceControl.UnstageCommand.ExecuteAsync(file);
        }
    }

    private async void OnDiscardClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: GitFileStatus file } && ViewModel is { } vm)
        {
            await vm.SourceControl.DiscardCommand.ExecuteAsync(file);
        }
    }

    private async void OnEnableExtensionClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ExtensionDescriptor descriptor } && ViewModel is { } vm)
        {
            await vm.Extensions.EnableCommand.ExecuteAsync(descriptor);
        }
    }

    private async void OnDisableExtensionClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ExtensionDescriptor descriptor } && ViewModel is { } vm)
        {
            await vm.Extensions.DisableCommand.ExecuteAsync(descriptor);
        }
    }

    private async void OnCheckoutBranchClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: GitBranch branch } && ViewModel is { } vm)
        {
            await vm.SourceControl.CheckoutBranchCommand.ExecuteAsync(branch);
        }
    }

    private async void OnDeleteBranchClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: GitBranch branch } && ViewModel is { } vm)
        {
            await vm.SourceControl.DeleteBranchCommand.ExecuteAsync(branch);
        }
    }

    /// <summary>Double-clicking a changed file opens it through the existing editor/navigation
    /// system (SKILL.md §14) — this is not a second file-opening mechanism, only a second entry
    /// point into <see cref="MainWindowViewModel.OpenFileAsync(string)"/>.</summary>
    private async void OnGitFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: GitFileStatus file } && ViewModel is { } vm && vm.SourceControl.SelectedRepositoryRoot is { } root)
        {
            await vm.OpenFileAsync(System.IO.Path.Combine(root, file.Path));
        }
    }

    private void OnTerminalInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox { DataContext: TerminalViewModel terminal })
        {
            terminal.SendCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Applies a theme to the whole application (SKILL.md §23). Only the Avalonia
    /// <c>Application</c> object can do this, so it lives here rather than in the ViewModel.</summary>
    public void ApplyTheme(AppTheme theme)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = theme == AppTheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }

    private void OnCaretMoveRequested(object? sender, (int Start, int Length) e)
    {
        if (ViewModel?.ActiveDocument is not { } document) return;

        var textBox = this.GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault(t => t.Name == "EditorTextBox" && ReferenceEquals(t.DataContext, document));

        if (textBox is null) return;

        textBox.Focus();
        textBox.SelectionStart = e.Start;
        textBox.SelectionEnd = e.Start + e.Length;
        textBox.CaretIndex = e.Start;
    }
}
