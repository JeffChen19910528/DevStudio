using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DevStudio.UI.ViewModels;

public partial class OutputPanelViewModel : ObservableObject
{
    public ObservableCollection<OutputEntry> Entries { get; } = new();

    public void Log(string source, string message, OutputEntrySeverity severity = OutputEntrySeverity.Info)
    {
        Entries.Add(new OutputEntry(DateTimeOffset.Now, source, message, severity));
    }
}
