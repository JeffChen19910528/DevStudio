namespace DevStudio.UI.ViewModels;

public enum OutputEntrySeverity
{
    Info,
    Warning,
    Error
}

/// <summary>One line of the Output panel (SKILL.md §16): timestamp, source, message, severity.</summary>
public sealed record OutputEntry(DateTimeOffset Timestamp, string Source, string Message, OutputEntrySeverity Severity)
{
    public string TimestampLabel => Timestamp.ToString("HH:mm:ss");
}
