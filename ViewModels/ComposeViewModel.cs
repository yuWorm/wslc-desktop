using System.Collections.ObjectModel;
using wslc_desktop.Models;
using wslc_desktop.Services;

namespace wslc_desktop.ViewModels;

public sealed class ComposeViewModel : ViewModelBase
{
    private readonly IComposePlanService _composePlanService;
    private bool _isLoading;
    private bool _isBusy;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private string _message = AppServices.Strings.Get("ComposeNoProjects");
    private string _selectedComposePath = string.Empty;
    private string _searchText = string.Empty;
    private ComposeProjectSummary? _selectedProject;
    private ComposeProjectDetails? _selectedProjectDetails;

    public ComposeViewModel(IComposePlanService composePlanService)
    {
        _composePlanService = composePlanService;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        CreateAndStartCommand = new AsyncRelayCommand(CreateAndStartAsync, () => CanCreateAndStart);
        StartProjectCommand = new AsyncRelayCommand(StartSelectedProjectAsync, () => CanStartSelectedProject);
        StopProjectCommand = new AsyncRelayCommand(StopSelectedProjectAsync, () => CanStopSelectedProject);
        RestartProjectCommand = new AsyncRelayCommand(RestartSelectedProjectAsync, () => CanRestartSelectedProject);
    }

    public ObservableCollection<ComposeProjectSummary> Projects { get; } = [];

    public ObservableCollection<ComposeServicePlan> ServicePlans { get; } = [];

    public ObservableCollection<ComposeProjectSummary> VisibleProjects { get; } = [];

    public ObservableCollection<ComposeServicePlan> VisibleServicePlans { get; } = [];

    public ObservableCollection<ComposeServiceRuntimeSummary> RuntimeServices { get; } = [];

    public ObservableCollection<ComposeContainerRuntimeSummary> RuntimeContainers { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand CreateAndStartCommand { get; }

    public AsyncRelayCommand StartProjectCommand { get; }

    public AsyncRelayCommand StopProjectCommand { get; }

    public AsyncRelayCommand RestartProjectCommand { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsEmpty => VisibleProjects.Count == 0 && VisibleServicePlans.Count == 0 && !IsLoading;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool CanCreateAndStart => !IsBusy
        && ServicePlans.Count > 0
        && !string.IsNullOrWhiteSpace(SelectedComposePath);

    public bool CanStartSelectedProject => !IsBusy
        && SelectedProject is not null
        && RuntimeContainers.Any(container => !container.IsRunning);

    public bool CanStopSelectedProject => !IsBusy
        && SelectedProject is not null
        && RuntimeContainers.Any(container => container.IsRunning);

    public bool CanRestartSelectedProject => !IsBusy
        && SelectedProject is not null
        && RuntimeContainers.Any(container => container.IsRunning);

    public bool CanDeleteSelectedProject => !IsBusy
        && SelectedProject is not null
        && RuntimeContainers.Count > 0;

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public string SelectedComposePath
    {
        get => _selectedComposePath;
        private set
        {
            if (SetProperty(ref _selectedComposePath, value))
            {
                OnPropertyChanged(nameof(CanCreateAndStart));
                CreateAndStartCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ComposeProjectSummary? SelectedProject
    {
        get => _selectedProject;
        set => SetSelectedProject(value, loadDetails: true);
    }

    public ComposeProjectDetails? SelectedProjectDetails
    {
        get => _selectedProjectDetails;
        private set
        {
            if (SetProperty(ref _selectedProjectDetails, value))
            {
                OnPropertyChanged(nameof(HasSelectedProject));
                OnPropertyChanged(nameof(SelectedProjectName));
                OnPropertyChanged(nameof(SelectedProjectSourcePath));
                OnPropertyChanged(nameof(SelectedProjectStatusText));
            }
        }
    }

    public bool HasSelectedProject => SelectedProjectDetails is not null;

    public string SelectedProjectName => SelectedProjectDetails?.Project.Name ?? AppServices.Strings.Get("ComposeNoProjectSelected");

    public string SelectedProjectSourcePath => string.IsNullOrWhiteSpace(SelectedProjectDetails?.Project.SourcePath)
        ? "-"
        : SelectedProjectDetails.Project.SourcePath;

    public string SelectedProjectStatusText => SelectedProjectDetails?.Project.StatusText ?? "-";

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilters();
            }
        }
    }

    public string ComposeSummaryText => AppServices.Strings.Format("ComposeSummary", Projects.Count, ServicePlans.Count);

    public async Task LoadAsync()
    {
        IsLoading = true;
        string selectedName = SelectedProject?.Name ?? string.Empty;

        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            Projects.Clear();
            VisibleProjects.Clear();

            foreach (var project in await _composePlanService.ListProjectsAsync())
            {
                Projects.Add(project);
            }

            ApplyFilters();
            var nextSelection = Projects.FirstOrDefault(project => project.Name.Equals(selectedName, StringComparison.OrdinalIgnoreCase))
                ?? Projects.FirstOrDefault();
            SetSelectedProject(nextSelection, loadDetails: false);
            await LoadSelectedProjectDetailsAsync();
            Message = Projects.Count == 0
                ? AppServices.Strings.Get("ComposeNoProjects")
                : AppServices.Strings.Format(
                    "ComposeProjectsLoaded",
                    Projects.Count,
                    Projects.Count == 1 ? string.Empty : "s");
            OnPropertyChanged(nameof(ComposeSummaryText));
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            ClearSelectedProjectDetails();
            OnPropertyChanged(nameof(IsEmpty));
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public async Task OpenComposePathAsync(string composePath)
    {
        IsBusy = true;

        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            ServicePlans.Clear();
            VisibleServicePlans.Clear();
            SelectedComposePath = composePath;

            foreach (var service in await _composePlanService.PreviewAsync(composePath))
            {
                ServicePlans.Add(service);
            }

            ApplyFilters();
            Message = AppServices.Strings.Format(
                "ComposePlansGenerated",
                ServicePlans.Count,
                ServicePlans.Count == 1 ? string.Empty : "s",
                Path.GetFileName(composePath));
            OnPropertyChanged(nameof(ComposeSummaryText));
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanCreateAndStart));
        CreateAndStartCommand.RaiseCanExecuteChanged();
    }

    public Task DeleteSelectedProjectAsync()
    {
        return CanDeleteSelectedProject
            ? PerformSelectedProjectActionAsync(_composePlanService.DeleteProjectAsync, "ComposeProjectDeleted")
            : Task.CompletedTask;
    }

    private Task StartSelectedProjectAsync()
    {
        return CanStartSelectedProject
            ? PerformSelectedProjectActionAsync(_composePlanService.StartProjectAsync, "ComposeProjectStarted")
            : Task.CompletedTask;
    }

    private Task StopSelectedProjectAsync()
    {
        return CanStopSelectedProject
            ? PerformSelectedProjectActionAsync(_composePlanService.StopProjectAsync, "ComposeProjectStopped")
            : Task.CompletedTask;
    }

    private Task RestartSelectedProjectAsync()
    {
        return CanRestartSelectedProject
            ? PerformSelectedProjectActionAsync(_composePlanService.RestartProjectAsync, "ComposeProjectRestarted")
            : Task.CompletedTask;
    }

    private async Task CreateAndStartAsync()
    {
        if (!CanCreateAndStart)
        {
            return;
        }

        IsBusy = true;

        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            var containers = await _composePlanService.CreateAndStartAsync(SelectedComposePath);
            await LoadAsync();
            Message = AppServices.Strings.Format(
                "ComposeContainersCreated",
                containers.Count,
                containers.Count == 1 ? string.Empty : "s");
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PerformSelectedProjectActionAsync(
        Func<string, CancellationToken, Task> action,
        string successMessageKey)
    {
        string? projectName = SelectedProject?.Name;
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return;
        }

        IsBusy = true;

        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            await action(projectName, CancellationToken.None);
            await LoadAsync();
            Message = AppServices.Strings.Format(successMessageKey, projectName);
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadSelectedProjectDetailsAsync()
    {
        if (SelectedProject is null)
        {
            ClearSelectedProjectDetails();
            return;
        }

        try
        {
            var details = await _composePlanService.InspectProjectAsync(SelectedProject.Name);
            SelectedProjectDetails = details;
            RuntimeServices.Clear();
            RuntimeContainers.Clear();

            foreach (var service in details.Services)
            {
                RuntimeServices.Add(service);
            }

            foreach (var container in details.Containers)
            {
                RuntimeContainers.Add(container);
            }

            RaiseCommandStates();
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            ClearSelectedProjectDetails();
        }
    }

    private void SetSelectedProject(ComposeProjectSummary? project, bool loadDetails)
    {
        if (!SetProperty(ref _selectedProject, project, nameof(SelectedProject)))
        {
            if (loadDetails)
            {
                _ = LoadSelectedProjectDetailsAsync();
            }

            return;
        }

        OnPropertyChanged(nameof(HasSelectedProject));
        RaiseCommandStates();

        if (loadDetails)
        {
            _ = LoadSelectedProjectDetailsAsync();
        }
    }

    private void ClearSelectedProjectDetails()
    {
        SelectedProjectDetails = null;
        RuntimeServices.Clear();
        RuntimeContainers.Clear();
        RaiseCommandStates();
    }

    private void ApplyFilters()
    {
        VisibleProjects.Clear();
        VisibleServicePlans.Clear();

        foreach (var project in Projects.Where(MatchesSearch))
        {
            VisibleProjects.Add(project);
        }

        foreach (var service in ServicePlans.Where(MatchesSearch))
        {
            VisibleServicePlans.Add(service);
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private bool MatchesSearch(ComposeProjectSummary project)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        string query = SearchText.Trim();
        return Contains(project.Name, query)
            || Contains(project.SourcePath, query)
            || Contains(project.ServiceCount.ToString(System.Globalization.CultureInfo.InvariantCulture), query)
            || Contains(project.RunningCount.ToString(System.Globalization.CultureInfo.InvariantCulture), query);
    }

    private bool MatchesSearch(ComposeServicePlan service)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        string query = SearchText.Trim();
        return Contains(service.ProjectName, query)
            || Contains(service.ServiceName, query)
            || Contains(service.Image, query)
            || Contains(service.PortSummary, query)
            || Contains(service.MountSummary, query)
            || Contains(service.DependsOnSummary, query)
            || Contains(service.UnsupportedSummary, query);
    }

    private void RaiseCommandStates()
    {
        OnPropertyChanged(nameof(CanCreateAndStart));
        OnPropertyChanged(nameof(CanStartSelectedProject));
        OnPropertyChanged(nameof(CanStopSelectedProject));
        OnPropertyChanged(nameof(CanRestartSelectedProject));
        OnPropertyChanged(nameof(CanDeleteSelectedProject));
        CreateAndStartCommand.RaiseCanExecuteChanged();
        StartProjectCommand.RaiseCanExecuteChanged();
        StopProjectCommand.RaiseCanExecuteChanged();
        RestartProjectCommand.RaiseCanExecuteChanged();
    }

    private static bool Contains(string value, string query)
    {
        return value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }
}
