using System.Collections.ObjectModel;
using wslc_desktop.Models;
using wslc_desktop.Services;

namespace wslc_desktop.ViewModels;

public sealed class NetworksViewModel : ViewModelBase
{
    private readonly IWslcNetworkService _networkService;
    private bool _isLoading;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private string _message = AppServices.Strings.Format("NetworksPublishedPorts", 0, "s");
    private string _searchText = string.Empty;
    private NetworkEndpointSummary? _selectedEndpoint;
    private readonly List<NetworkEndpointSummary> _allEndpoints = [];

    public NetworksViewModel(IWslcNetworkService networkService)
    {
        _networkService = networkService;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
    }

    public ObservableCollection<NetworkEndpointSummary> Endpoints { get; } = [];

    public ObservableCollection<NetworkEndpointSummary> VisibleEndpoints { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsEmpty => VisibleEndpoints.Count == 0 && !IsLoading;

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

    public NetworkEndpointSummary? SelectedEndpoint
    {
        get => _selectedEndpoint;
        set => SetProperty(ref _selectedEndpoint, value);
    }

    public string NetworkSummaryText => AppServices.Strings.Format("NetworkEndpointsSummary", Endpoints.Count);

    public async Task LoadAsync()
    {
        IsLoading = true;

        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            Endpoints.Clear();
            VisibleEndpoints.Clear();
            _allEndpoints.Clear();

            foreach (var endpoint in await _networkService.ListPublishedPortsAsync())
            {
                Endpoints.Add(endpoint);
                _allEndpoints.Add(endpoint);
            }

            ApplyFilters();
            Message = AppServices.Strings.Format(
                "NetworksPublishedPorts",
                Endpoints.Count,
                Endpoints.Count == 1 ? string.Empty : "s");
            OnPropertyChanged(nameof(NetworkSummaryText));
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

    private void ApplyFilters()
    {
        VisibleEndpoints.Clear();

        foreach (var endpoint in _allEndpoints.Where(MatchesSearch))
        {
            VisibleEndpoints.Add(endpoint);
        }

        if (SelectedEndpoint is null || !VisibleEndpoints.Contains(SelectedEndpoint))
        {
            SelectedEndpoint = VisibleEndpoints.FirstOrDefault();
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private bool MatchesSearch(NetworkEndpointSummary endpoint)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        string query = SearchText.Trim();
        return Contains(endpoint.ContainerName, query)
            || Contains(endpoint.HostPort.ToString(System.Globalization.CultureInfo.InvariantCulture), query)
            || Contains(endpoint.ContainerPort.ToString(System.Globalization.CultureInfo.InvariantCulture), query)
            || Contains(endpoint.Protocol, query)
            || Contains(endpoint.Url, query);
    }

    private static bool Contains(string value, string query)
    {
        return value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }
}
