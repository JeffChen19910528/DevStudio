using CommunityToolkit.Mvvm.ComponentModel;
using DevStudio.Core.Editor;

namespace DevStudio.UI.ViewModels;

/// <summary>One open editor tab. Tracks modified state against the last-saved (or
/// last-loaded) text so the UI never has to guess whether a save is needed (SKILL.md §10, §13).</summary>
public partial class DocumentViewModel : ObservableObject
{
    private string _savedText;

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    private bool _isModified;

    [ObservableProperty]
    private TextEncodingKind _encoding;

    [ObservableProperty]
    private LineEndingKind _lineEnding;

    [ObservableProperty]
    private int _line = 1;

    [ObservableProperty]
    private int _column = 1;

    [ObservableProperty]
    private int _caretIndex;

    /// <summary>Set when the file-change watcher reports an external edit; the shell prompts
    /// the user rather than silently reloading or discarding the prompt (SKILL.md §12).</summary>
    [ObservableProperty]
    private bool _hasExternalChangePending;

    /// <summary>Name of the project that owns this file (SKILL.md §22), or "Unknown" when no
    /// detected project's root contains it. Set by the shell after project detection, not
    /// computed here — this ViewModel has no access to the workspace's project graph.</summary>
    [ObservableProperty]
    private string _projectContext = "Unknown";

    public DocumentViewModel(string? filePath, string displayName, string text, TextEncodingKind encoding, LineEndingKind lineEnding)
    {
        _filePath = filePath;
        _displayName = displayName;
        _text = text;
        _savedText = text;
        _encoding = encoding;
        _lineEnding = lineEnding;
    }

    partial void OnTextChanged(string value)
    {
        IsModified = value != _savedText;
    }

    partial void OnCaretIndexChanged(int value)
    {
        var index = Math.Clamp(value, 0, Text.Length);
        var line = 1;
        var lineStart = 0;

        for (var i = 0; i < index; i++)
        {
            if (Text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        Line = line;
        Column = index - lineStart + 1;
    }

    public void MarkSaved()
    {
        _savedText = Text;
        IsModified = false;
        HasExternalChangePending = false;
    }

    public void ReloadFromDisk(string diskText)
    {
        Text = diskText;
        _savedText = diskText;
        IsModified = false;
        HasExternalChangePending = false;
    }
}
