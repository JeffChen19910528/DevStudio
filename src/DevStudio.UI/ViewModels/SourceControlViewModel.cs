using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevStudio.Core.Git;
using DevStudio.Core.Platform;
using DevStudio.UI.Localization;
using DevStudio.UI.Services;

namespace DevStudio.UI.ViewModels;

/// <summary>
/// Source Control panel state and orchestration (SKILL.md §12, §37 [Phase 9]). Owns every real
/// Git operation itself — unlike <see cref="ToolchainsPanelViewModel"/>/<see
/// cref="ProblemsPanelViewModel"/>, this panel's operations are numerous and several are
/// destructive/trust-gated, so centralizing them here (rather than as another dozen commands on
/// <see cref="MainWindowViewModel"/>) keeps that composition root from growing further while
/// still following its exact same trust-gate/confirm/refresh conventions, injected in via
/// delegates from the composition root. <see cref="RefreshAsync"/> is the single place that
/// re-queries Git — no individual command or UI control independently invokes a Git command of
/// its own (SKILL.md §37).
/// </summary>
public sealed partial class SourceControlViewModel : ObservableObject
{
    private readonly GitService _gitService;
    private readonly IDialogService _dialogService;
    private readonly Func<string, Task<bool>> _ensureWorkspaceTrustedAsync;
    private readonly ILocalizationService _localizationService;

    public ObservableCollection<string> Repositories { get; } = new();
    public ObservableCollection<GitBranch> Branches { get; } = new();
    public ObservableCollection<GitCommit> CommitHistory { get; } = new();
    public ObservableCollection<GitFileStatus> StagedChanges { get; } = new();
    public ObservableCollection<GitFileStatus> UnstagedChanges { get; } = new();

    [ObservableProperty] private string? _selectedRepositoryRoot;
    [ObservableProperty] private GitRepositoryStatus? _status;
    [ObservableProperty] private GitFileStatus? _selectedChange;
    [ObservableProperty] private GitDiff? _selectedDiff;
    [ObservableProperty] private GitCommit? _selectedCommit;
    [ObservableProperty] private string _commitMessage = string.Empty;
    [ObservableProperty] private string _newBranchName = string.Empty;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private string? _lastError;

    public bool IsRepository => SelectedRepositoryRoot is not null;
    public bool HasNoRepository => !IsRepository;

    public SourceControlViewModel(GitService gitService, IDialogService dialogService, Func<string, Task<bool>> ensureWorkspaceTrustedAsync, ILocalizationService localizationService)
    {
        _gitService = gitService;
        _dialogService = dialogService;
        _ensureWorkspaceTrustedAsync = ensureWorkspaceTrustedAsync;
        _localizationService = localizationService;
    }

    /// <summary>Discovers the workspace root's own repository plus any real, separate
    /// repositories one level of nesting below it (SKILL.md §9's multi-repository example). This
    /// is a deliberately shallow scan — a repository nested two or more levels deep is not
    /// discovered; see ADR-010's Known Limitations.</summary>
    public async Task SetWorkspaceRootAsync(string workspaceRootPath, CancellationToken cancellationToken = default)
    {
        Repositories.Clear();
        Status = null;
        Branches.Clear();
        CommitHistory.Clear();
        StagedChanges.Clear();
        UnstagedChanges.Clear();

        var found = new List<string>();
        var primary = await _gitService.FindRepositoryRootAsync(workspaceRootPath, cancellationToken).ConfigureAwait(true);
        if (primary is not null) found.Add(primary);

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(workspaceRootPath))
            {
                var root = await _gitService.FindRepositoryRootAsync(directory, cancellationToken).ConfigureAwait(true);
                if (root is not null && !found.Contains(root, PathComparer.Comparer))
                {
                    found.Add(root);
                }
            }
        }
        catch (IOException) { /* unreadable subdirectory — skip it, not fatal to workspace open */ }
        catch (UnauthorizedAccessException) { /* same */ }

        foreach (var root in found) Repositories.Add(root);
        SelectedRepositoryRoot = Repositories.FirstOrDefault();
    }

    partial void OnSelectedRepositoryRootChanged(string? value)
    {
        OnPropertyChanged(nameof(IsRepository));
        OnPropertyChanged(nameof(HasNoRepository));
        _ = RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        SelectedChange = null;
        SelectedDiff = null;

        if (SelectedRepositoryRoot is not { } root)
        {
            Status = null;
            Branches.Clear();
            CommitHistory.Clear();
            StagedChanges.Clear();
            UnstagedChanges.Clear();
            return;
        }

        IsRefreshing = true;
        try
        {
            var statusTask = _gitService.GetStatusAsync(root);
            var branchesTask = _gitService.GetBranchesAsync(root);
            var logTask = _gitService.GetLogAsync(root);
            await Task.WhenAll(statusTask, branchesTask, logTask).ConfigureAwait(true);

            Status = statusTask.Result;

            StagedChanges.Clear();
            UnstagedChanges.Clear();
            foreach (var file in statusTask.Result.Files)
            {
                if (file.IsStaged) StagedChanges.Add(file);
                if (file.WorktreeStatus != GitChangeType.Unmodified) UnstagedChanges.Add(file);
            }

            Branches.Clear();
            foreach (var branch in branchesTask.Result) Branches.Add(branch);

            CommitHistory.Clear();
            foreach (var commit in logTask.Result) CommitHistory.Add(commit);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    partial void OnSelectedChangeChanged(GitFileStatus? value) => _ = LoadDiffAsync(value);

    /// <summary>An untracked file is never diffed — real <c>git diff</c> has no output for one
    /// (SKILL.md §15's "do not invent diff results"); its content is only visible by opening it
    /// in the editor.</summary>
    private async Task LoadDiffAsync(GitFileStatus? file)
    {
        SelectedDiff = null;
        if (file is null || file.IsUntracked || SelectedRepositoryRoot is not { } root) return;
        SelectedDiff = await _gitService.GetDiffAsync(root, file.Path, staged: file.IsStaged).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task StageAsync(GitFileStatus? file)
    {
        if (file is null || SelectedRepositoryRoot is not { } root) return;
        if (!await _ensureWorkspaceTrustedAsync(_localizationService.GetString("Trust.Action.StagingFile")).ConfigureAwait(true)) return;
        await RunMutationAsync(() => _gitService.StageAsync(root, new[] { file.Path })).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task StageAllAsync()
    {
        if (SelectedRepositoryRoot is not { } root || UnstagedChanges.Count == 0) return;
        if (!await _ensureWorkspaceTrustedAsync(_localizationService.GetString("Trust.Action.StagingAllChanges")).ConfigureAwait(true)) return;
        var paths = UnstagedChanges.Select(f => f.Path).ToList();
        await RunMutationAsync(() => _gitService.StageAsync(root, paths)).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task UnstageAsync(GitFileStatus? file)
    {
        if (file is null || SelectedRepositoryRoot is not { } root) return;
        if (!await _ensureWorkspaceTrustedAsync(_localizationService.GetString("Trust.Action.UnstagingFile")).ConfigureAwait(true)) return;
        await RunMutationAsync(() => _gitService.UnstageAsync(root, new[] { file.Path })).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task UnstageAllAsync()
    {
        if (SelectedRepositoryRoot is not { } root || StagedChanges.Count == 0) return;
        if (!await _ensureWorkspaceTrustedAsync(_localizationService.GetString("Trust.Action.UnstagingAllChanges")).ConfigureAwait(true)) return;
        var paths = StagedChanges.Select(f => f.Path).ToList();
        await RunMutationAsync(() => _gitService.UnstageAsync(root, paths)).ConfigureAwait(true);
    }

    /// <summary>Discards real working-tree changes for one tracked file — always behind an
    /// explicit confirmation (SKILL.md §18, §40), and never for an untracked file (deleting an
    /// untracked file is deliberately unsupported/deferred this phase).</summary>
    [RelayCommand]
    public async Task DiscardAsync(GitFileStatus? file)
    {
        if (file is null || file.IsUntracked || SelectedRepositoryRoot is not { } root) return;
        if (!await _ensureWorkspaceTrustedAsync(_localizationService.GetString("Trust.Action.DiscardingChanges")).ConfigureAwait(true)) return;

        var confirmed = await _dialogService.ConfirmAsync(
            _localizationService.GetString("Dialog.DiscardChanges.Title"),
            _localizationService.Format("Dialog.DiscardChanges.Message", file.Path)).ConfigureAwait(true);
        if (!confirmed) return;

        await RunMutationAsync(() => _gitService.DiscardChangesAsync(root, new[] { file.Path })).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task CommitAsync()
    {
        if (SelectedRepositoryRoot is not { } root) return;
        if (StagedChanges.Count == 0)
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.NothingToCommit.Title"), _localizationService.GetString("Dialog.NothingToCommit.Message")).ConfigureAwait(true);
            return;
        }
        if (string.IsNullOrWhiteSpace(CommitMessage))
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.CommitMessageRequired.Title"), _localizationService.GetString("Dialog.CommitMessageRequired.Message")).ConfigureAwait(true);
            return;
        }
        if (!await _ensureWorkspaceTrustedAsync(_localizationService.GetString("Trust.Action.Committing")).ConfigureAwait(true)) return;

        var message = CommitMessage;
        var result = await RunMutationAsync(() => _gitService.CommitAsync(root, message)).ConfigureAwait(true);
        if (result?.Succeeded == true) CommitMessage = string.Empty;
    }

    [RelayCommand]
    public async Task CreateBranchAsync()
    {
        if (SelectedRepositoryRoot is not { } root || string.IsNullOrWhiteSpace(NewBranchName)) return;
        if (!await _ensureWorkspaceTrustedAsync(_localizationService.GetString("Trust.Action.CreatingBranch")).ConfigureAwait(true)) return;

        var name = NewBranchName;
        var result = await RunMutationAsync(() => _gitService.CreateBranchAsync(root, name)).ConfigureAwait(true);
        if (result?.Succeeded == true) NewBranchName = string.Empty;
    }

    [RelayCommand]
    public async Task CheckoutBranchAsync(GitBranch? branch)
    {
        if (branch is null || branch.IsRemote || SelectedRepositoryRoot is not { } root) return;
        if (!await _ensureWorkspaceTrustedAsync(_localizationService.GetString("Trust.Action.CheckingOutBranch")).ConfigureAwait(true)) return;

        var result = await RunMutationAsync(() => _gitService.CheckoutBranchAsync(root, branch.Name)).ConfigureAwait(true);
        if (result is { Succeeded: false })
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.CheckoutBlocked.Title"), result.Message ?? _localizationService.GetString("Dialog.CheckoutBlocked.Fallback")).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    public async Task DeleteBranchAsync(GitBranch? branch)
    {
        if (branch is null || branch.IsRemote || SelectedRepositoryRoot is not { } root) return;
        if (branch.IsCurrent)
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.CannotDeleteBranch.Title"), _localizationService.GetString("Dialog.CannotDeleteBranch.CurrentBranch")).ConfigureAwait(true);
            return;
        }
        if (!await _ensureWorkspaceTrustedAsync(_localizationService.GetString("Trust.Action.DeletingBranch")).ConfigureAwait(true)) return;

        var confirmed = await _dialogService.ConfirmAsync(
            _localizationService.GetString("Dialog.DeleteBranch.Title"),
            _localizationService.Format("Dialog.DeleteBranch.Message", branch.Name)).ConfigureAwait(true);
        if (!confirmed) return;

        var result = await RunMutationAsync(() => _gitService.DeleteBranchAsync(root, branch.Name)).ConfigureAwait(true);
        if (result is { Succeeded: false })
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.DeleteBranchBlocked.Title"), result.Message ?? _localizationService.GetString("Dialog.DeleteBranchBlocked.Fallback")).ConfigureAwait(true);
        }
    }

    private async Task<GitOperationResult?> RunMutationAsync(Func<Task<GitOperationResult>> operation)
    {
        try
        {
            var result = await operation().ConfigureAwait(true);
            LastError = result.Succeeded ? null : result.Message;
            await RefreshAsync().ConfigureAwait(true);
            return result;
        }
        catch (InvalidOperationException ex)
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.GitOperationAlreadyRunning.Title"), ex.Message).ConfigureAwait(true);
            return null;
        }
    }
}
