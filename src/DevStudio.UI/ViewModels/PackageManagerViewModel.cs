using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevStudio.Core.Packages;
using DevStudio.Core.Projects;
using DevStudio.UI.Localization;
using DevStudio.UI.Services;

namespace DevStudio.UI.ViewModels;

/// <summary>
/// Package Manager panel state and orchestration (Phase 13 §18/§31): the UI's only knowledge of
/// package management is the generic <see cref="PackageProject"/>/<see
/// cref="PackageManagerCapabilities"/> contracts <see cref="PackageService"/> exposes — there is
/// no "if project is C#"/"if project is Python" branch anywhere in this class or in
/// <c>MainWindow.axaml</c>. Mirrors <see cref="SourceControlViewModel"/>'s shape: owns every real
/// package operation itself (numerous, several trust-gated), injected the same
/// <c>Func&lt;string, Task&lt;bool&gt;&gt;</c> Workspace Trust gate from the composition root.
/// </summary>
public sealed partial class PackageManagerViewModel : ObservableObject
{
    private readonly PackageService _packageService;
    private readonly IDialogService _dialogService;
    private readonly Func<string, Task<bool>> _ensureWorkspaceTrustedAsync;
    private readonly ILocalizationService _localizationService;

    public ObservableCollection<ProjectInfo> Projects { get; } = new();
    public ObservableCollection<PackageProject> ApplicableManagers { get; } = new();
    public ObservableCollection<PackageReference> Installed { get; } = new();
    public ObservableCollection<PackageReference> Outdated { get; } = new();
    public ObservableCollection<PackageDependency> Dependencies { get; } = new();
    public ObservableCollection<PackageSearchResult> SearchResults { get; } = new();
    public ObservableCollection<PackageSource> Sources { get; } = new();

    [ObservableProperty] private ProjectInfo? _selectedProject;
    [ObservableProperty] private PackageProject? _selectedManager;
    [ObservableProperty] private PackageReference? _selectedInstalled;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _lastError;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _includePrerelease;
    [ObservableProperty] private string _newPackageId = string.Empty;
    [ObservableProperty] private string _newPackageVersion = string.Empty;

    public bool HasNoProject => SelectedProject is null;
    public bool HasNoManager => SelectedManager is null;
    public bool HasUnavailableReason => !string.IsNullOrEmpty(SelectedManager?.UnavailableReason);
    public bool HasNoInstalled => Installed.Count == 0;
    public bool HasNoSearchResults => SearchResults.Count == 0;
    public bool HasNoOutdated => Outdated.Count == 0;
    public bool HasNoDependencies => Dependencies.Count == 0;

    public PackageManagerViewModel(PackageService packageService, IDialogService dialogService, Func<string, Task<bool>> ensureWorkspaceTrustedAsync, ILocalizationService localizationService)
    {
        _packageService = packageService;
        _dialogService = dialogService;
        _ensureWorkspaceTrustedAsync = ensureWorkspaceTrustedAsync;
        _localizationService = localizationService;

        // These four collections back an empty-state TextBlock each in MainWindow.axaml — Avalonia
        // bindings cannot express "Count == 0" directly, so a real change-notified bool property
        // is kept in sync here instead (SKILL.md's "no invented/estimated state" applies just as
        // much to UI plumbing: this always reflects the collection's real current count).
        Installed.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoInstalled));
        SearchResults.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoSearchResults));
        Outdated.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoOutdated));
        Dependencies.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoDependencies));
    }

    /// <summary>Called by <see cref="MainWindowViewModel"/> every time workspace detection (re-)
    /// runs (SKILL.md §6's Project Detection integration) — never runs its own detection.</summary>
    public void SetProjects(IReadOnlyList<ProjectInfo> projects)
    {
        var previouslySelectedPath = SelectedProject?.RootPath;
        Projects.Clear();
        foreach (var project in projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)) Projects.Add(project);

        SelectedProject = previouslySelectedPath is null
            ? Projects.FirstOrDefault()
            : Projects.FirstOrDefault(p => string.Equals(p.RootPath, previouslySelectedPath, StringComparison.Ordinal)) ?? Projects.FirstOrDefault();
    }

    public void Clear()
    {
        Projects.Clear();
        ApplicableManagers.Clear();
        ClearManagerState();
        SelectedProject = null;
    }

    partial void OnSelectedProjectChanged(ProjectInfo? value)
    {
        OnPropertyChanged(nameof(HasNoProject));
        ApplicableManagers.Clear();
        if (value is not null)
        {
            foreach (var manager in _packageService.DetectApplicableManagers(value)) ApplicableManagers.Add(manager);
        }
        SelectedManager = ApplicableManagers.FirstOrDefault();
    }

    partial void OnSelectedManagerChanged(PackageProject? value)
    {
        OnPropertyChanged(nameof(HasNoManager));
        OnPropertyChanged(nameof(HasUnavailableReason));
        ClearManagerState();
        if (value is not null) _ = RefreshAsync();
    }

    private void ClearManagerState()
    {
        Installed.Clear();
        Outdated.Clear();
        Dependencies.Clear();
        SearchResults.Clear();
        Sources.Clear();
        SelectedInstalled = null;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (SelectedManager is not { } manager) return;

        IsBusy = true;
        try
        {
            var installedTask = _packageService.ListInstalledAsync(manager);
            var outdatedTask = manager.Capabilities.ListOutdated ? _packageService.ListOutdatedAsync(manager) : Task.FromResult<IReadOnlyList<PackageReference>>(Array.Empty<PackageReference>());
            var dependenciesTask = manager.Capabilities.ListDependencies ? _packageService.ListDependenciesAsync(manager) : Task.FromResult<IReadOnlyList<PackageDependency>>(Array.Empty<PackageDependency>());
            var sourcesTask = manager.Capabilities.ManageSources ? _packageService.GetSourcesAsync(manager) : Task.FromResult<IReadOnlyList<PackageSource>>(Array.Empty<PackageSource>());
            await Task.WhenAll(installedTask, outdatedTask, dependenciesTask, sourcesTask).ConfigureAwait(true);

            Installed.Clear();
            foreach (var reference in installedTask.Result) Installed.Add(reference);

            Outdated.Clear();
            foreach (var reference in outdatedTask.Result) Outdated.Add(reference);

            Dependencies.Clear();
            foreach (var dependency in dependenciesTask.Result) Dependencies.Add(dependency);

            Sources.Clear();
            foreach (var source in sourcesTask.Result) Sources.Add(source);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        if (SelectedManager is not { } manager || string.IsNullOrWhiteSpace(SearchQuery)) return;
        if (!manager.Capabilities.Search)
        {
            SearchResults.Clear();
            LastError = _localizationService.Format("PackageManager.SearchUnsupported", manager.PackageManagerDisplayName);
            return;
        }

        IsBusy = true;
        try
        {
            var results = await _packageService.SearchAsync(manager, SearchQuery, IncludePrerelease).ConfigureAwait(true);
            SearchResults.Clear();
            foreach (var result in results) SearchResults.Add(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task AddAsync()
    {
        if (SelectedManager is not { } manager || string.IsNullOrWhiteSpace(NewPackageId)) return;
        if (!await _ensureWorkspaceTrustedAsync(_localizationService.GetString("Trust.Action.InstallingPackage")).ConfigureAwait(true)) return;

        var packageId = NewPackageId;
        var version = string.IsNullOrWhiteSpace(NewPackageVersion) ? null : NewPackageVersion;
        var result = await RunMutationAsync(() => _packageService.AddAsync(manager, packageId, version, IncludePrerelease, isDevDependency: false)).ConfigureAwait(true);
        if (result?.Success == true)
        {
            NewPackageId = string.Empty;
            NewPackageVersion = string.Empty;
        }
    }

    [RelayCommand]
    public async Task RemoveAsync(PackageReference? reference)
    {
        if (reference is null || SelectedManager is not { } manager) return;
        if (!await _ensureWorkspaceTrustedAsync(_localizationService.GetString("Trust.Action.RemovingPackage")).ConfigureAwait(true)) return;

        var confirmed = await _dialogService.ConfirmAsync(
            _localizationService.GetString("Dialog.RemovePackage.Title"),
            _localizationService.Format("Dialog.RemovePackage.Message", reference.PackageId)).ConfigureAwait(true);
        if (!confirmed) return;

        await RunMutationAsync(() => _packageService.RemoveAsync(manager, reference.PackageId)).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task UpdateAsync(PackageReference? reference)
    {
        if (reference is null || SelectedManager is not { } manager) return;
        if (!await _ensureWorkspaceTrustedAsync(_localizationService.GetString("Trust.Action.UpdatingPackage")).ConfigureAwait(true)) return;

        await RunMutationAsync(() => _packageService.UpdateAsync(manager, reference.PackageId, reference.LatestVersion)).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task RestoreAsync()
    {
        if (SelectedManager is not { } manager) return;
        if (!await _ensureWorkspaceTrustedAsync(_localizationService.GetString("Trust.Action.RestoringPackages")).ConfigureAwait(true)) return;

        await RunMutationAsync(() => _packageService.RestoreAsync(manager)).ConfigureAwait(true);
    }

    private async Task<PackageOperationResult?> RunMutationAsync(Func<Task<PackageOperationResult>> operation)
    {
        IsBusy = true;
        try
        {
            var result = await operation().ConfigureAwait(true);
            LastError = result.Success ? null : result.FailureReason;
            if (!result.Success && !result.WasCancelled)
            {
                await _dialogService.ShowErrorAsync(
                    _localizationService.GetString("Dialog.PackageOperationFailed.Title"),
                    result.FailureReason ?? _localizationService.GetString("Dialog.PackageOperationFailed.Fallback")).ConfigureAwait(true);
            }
            await RefreshAsync().ConfigureAwait(true);
            return result;
        }
        catch (InvalidOperationException ex)
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Dialog.PackageOperationAlreadyRunning.Title"), ex.Message).ConfigureAwait(true);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
