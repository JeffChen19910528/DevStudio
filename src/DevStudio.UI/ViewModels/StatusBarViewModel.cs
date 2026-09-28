using CommunityToolkit.Mvvm.ComponentModel;

namespace DevStudio.UI.ViewModels;

/// <summary>SKILL.md §18: workspace, current file, line/column, encoding, line ending, modified state.</summary>
public partial class StatusBarViewModel : ObservableObject
{
    [ObservableProperty]
    private string _workspaceName = "(no workspace)";

    [ObservableProperty]
    private string _currentFileName = string.Empty;

    [ObservableProperty]
    private string _projectContext = string.Empty;

    [ObservableProperty]
    private int _line = 1;

    [ObservableProperty]
    private int _column = 1;

    [ObservableProperty]
    private string _encodingLabel = string.Empty;

    [ObservableProperty]
    private string _lineEndingLabel = string.Empty;

    [ObservableProperty]
    private bool _isModified;
}
