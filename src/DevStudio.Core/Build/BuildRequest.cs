using DevStudio.Core.Workspace;

namespace DevStudio.Core.Build;

public sealed record BuildRequest(
    BuildTarget Target,
    BuildConfiguration Configuration,
    BuildOperation Operation);
