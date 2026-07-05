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

    public ComposeViewModel(IComposePlanService composePlanService)
    {
        _composePlanService = composePlanService;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        CreateAndStartCommand = new AsyncRelayCommand(CreateAndStartAsync, () => CanCreateAndStart);
    }

    public ObservableCollection<ComposeProjectSummary> Projects { get; } = [];

    public ObservableCollection<ComposeServicePlan> ServicePlans { get; } = [];

    public ObservableCollection<ComposeProjectSummary> VisibleProjects { get; } = [];

    public ObservableCollection<ComposeServicePlan> VisibleServicePlans { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand CreateAndStartCommand { get; }

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
                OnPropertyChanged(nameof(CanCreateAndStart));
                CreateAndStartCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanCreateAndStart => !IsBusy
        && ServicePlans.Count > 0
        && !string.IsNullOrWhiteSpace(SelectedComposePath);

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
            Message = AppServices.Strings.Format(
                "ComposeContainersCreated",
                containers.Count,
                containers.Count == 1 ? string.Empty : "s");
            await LoadAsync();
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

    private static bool Contains(string value, string query)
    {
        return value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }
}
