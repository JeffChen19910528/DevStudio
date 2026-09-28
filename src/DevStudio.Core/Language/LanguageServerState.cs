namespace DevStudio.Core.Language;

/// <summary>Its own state machine (SKILL.md §14 [Phase 7]) — deliberately not <see
/// cref="Debug.DebugSessionState"/> or <see cref="Run.RunStatus"/> reused. <see
/// cref="Initializing"/> is the window between the real process starting and the LSP
/// <c>initialize</c>/<c>initialized</c> handshake actually completing.</summary>
public enum LanguageServerState
{
    NotStarted,
    Starting,
    Initializing,
    Running,
    Stopping,
    Stopped,
    Failed,
}
