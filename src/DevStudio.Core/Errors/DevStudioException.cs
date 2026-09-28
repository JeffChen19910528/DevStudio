namespace DevStudio.Core.Errors;

/// <summary>
/// Base exception carrying a classified error kind plus enough structured context to render
/// an actionable message (SKILL.md §31) instead of a bare "Operation failed."
/// </summary>
public class DevStudioException : Exception
{
    public DevStudioErrorKind Kind { get; }
    public IReadOnlyDictionary<string, string> Context { get; }

    public DevStudioException(
        DevStudioErrorKind kind,
        string message,
        IReadOnlyDictionary<string, string>? context = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Context = context ?? new Dictionary<string, string>();
    }
}
