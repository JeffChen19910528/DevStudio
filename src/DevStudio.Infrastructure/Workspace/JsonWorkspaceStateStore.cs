using System.Text.Json;
using DevStudio.Core.Workspace;

namespace DevStudio.Infrastructure.Workspace;

/// <summary>
/// Persists <see cref="WorkspaceState"/> to <c>&lt;workspaceRoot&gt;/.devstudio/workspace.json</c>
/// (SKILL.md §11). Metadata only — never source code, never secrets. A corrupted or
/// version-mismatched file is reported through <see cref="WorkspaceStateLoadResult.WasCorrupted"/>
/// rather than thrown (SKILL.md §13); there is no migration path yet since
/// <see cref="WorkspaceState.CurrentVersion"/> is still 1.
/// </summary>
public sealed class JsonWorkspaceStateStore : IWorkspaceStateStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static string GetFilePath(string workspaceRootPath) => Path.Combine(workspaceRootPath, ".devstudio", "workspace.json");

    public async Task<WorkspaceStateLoadResult> LoadAsync(string workspaceRootPath, CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePath(workspaceRootPath);

        try
        {
            if (!File.Exists(filePath)) return new WorkspaceStateLoadResult(null, WasCorrupted: false);

            using var stream = File.OpenRead(filePath);
            var state = await JsonSerializer.DeserializeAsync<WorkspaceState>(stream, Options, cancellationToken).ConfigureAwait(false);

            if (state is null || state.Version != WorkspaceState.CurrentVersion)
            {
                return new WorkspaceStateLoadResult(null, WasCorrupted: true);
            }

            return new WorkspaceStateLoadResult(state, WasCorrupted: false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new WorkspaceStateLoadResult(null, WasCorrupted: true);
        }
    }

    public async Task SaveAsync(string workspaceRootPath, WorkspaceState state, CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePath(workspaceRootPath);
        var directory = Path.GetDirectoryName(filePath);
        if (directory is not null) Directory.CreateDirectory(directory);

        using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, state, Options, cancellationToken).ConfigureAwait(false);
    }
}
