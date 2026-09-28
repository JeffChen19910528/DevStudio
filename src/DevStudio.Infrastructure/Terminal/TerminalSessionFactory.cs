using DevStudio.Core.Processes;
using DevStudio.Core.Terminal;

namespace DevStudio.Infrastructure.Terminal;

public sealed class TerminalSessionFactory : ITerminalSessionFactory
{
    private readonly IProcessRunner _processRunner;

    public TerminalSessionFactory(IProcessRunner processRunner) => _processRunner = processRunner;

    public ITerminalSession Create() => new TerminalSession(_processRunner);
}
