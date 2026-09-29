using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DevStudio.UI.ViewModels;

public partial class OutputPanelViewModel : ObservableObject
{
    public ObservableCollection<OutputEntry> Entries { get; } = new();

    private readonly StringBuilder _textBuilder = new();

    [ObservableProperty]
    private string _outputText = string.Empty;

    public void Log(string source, string message, OutputEntrySeverity severity = OutputEntrySeverity.Info)
    {
        var entry = new OutputEntry(DateTimeOffset.Now, source, message, severity);
        Entries.Add(entry);
        _textBuilder.Append(entry.TimestampLabel);
        _textBuilder.Append(" [");
        _textBuilder.Append(source);
        _textBuilder.Append("] ");
        _textBuilder.AppendLine(message);
        OutputText = _textBuilder.ToString();
    }
}
