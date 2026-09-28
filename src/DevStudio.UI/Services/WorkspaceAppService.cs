using DevStudio.Core.Workspace;

namespace DevStudio.UI.Services;

/// <summary>Application-service layer between ViewModels and the read-only <see
/// cref="IWorkspaceScanner"/> Core abstraction.</summary>
public sealed class WorkspaceAppService
{
    private readonly IWorkspaceScanner _scanner;

    public WorkspaceAppService(IWorkspaceScanner scanner) => _scanner = scanner;

    public WorkspaceExclusionRules ExclusionRules { get; } = WorkspaceExclusionRules.Default;

    public Task<IReadOnlyList<FileSystemNode>> GetChildrenAsync(string directoryPath, CancellationToken cancellationToken = default)
        => _scanner.GetChildrenAsync(directoryPath, ExclusionRules, cancellationToken);
}
