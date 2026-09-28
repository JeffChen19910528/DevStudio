namespace DevStudio.Core.Terminal;

/// <summary>Creates new terminal sessions without the UI layer depending on the concrete
/// process/shell implementation (dependency inversion, SKILL.md §42 Rule 7).</summary>
public interface ITerminalSessionFactory
{
    ITerminalSession Create();
}
