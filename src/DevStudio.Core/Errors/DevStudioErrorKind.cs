namespace DevStudio.Core.Errors;

/// <summary>Error classification per SKILL.md §31, used to drive actionable, specific error UI.</summary>
public enum DevStudioErrorKind
{
    UserError,
    ToolchainError,
    BuildError,
    DebuggerError,
    ProjectError,
    ConfigurationError,
    InfrastructureError,
    InternalError
}
