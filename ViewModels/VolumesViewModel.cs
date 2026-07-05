using System.Collections.ObjectModel;
using wslc_desktop.Models;
using wslc_desktop.Services;

namespace wslc_desktop.ViewModels;

public sealed class VolumesViewModel : ViewModelBase
{
    private readonly IWslcVolumeService _volumeService;
    private bool _isLoading;
    private bool _isBusy;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private string _message = AppServices.Strings.Format("VolumesTracked", 0, "s");
    private string _newVolumeName = "data";
    private double _newVolumeSizeMb = 1024;
    private string _searchText = string.Empty;
    private VolumeSummary? _selectedVolume;
    private readonly List<VolumeSummary> _allVolumes = [];

    public VolumesViewModel(IWslcVolumeService volumeService)
    {
        _volumeService = volumeService;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        CreateVolumeCommand = new AsyncRelayCommand(CreateVolumeAsync, () => !string.IsNullOrWhiteSpace(NewVolumeName));
        DeleteVolumeCommand = new AsyncRelayCommand(DeleteSelectedVolumeAsync, () => SelectedVolume?.IsNamed == true);
    }

    public ObservableCollection<VolumeSummary> Volumes { get; } = [];

    public ObservableCollection<VolumeSummary> VisibleVolumes { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand CreateVolumeCommand { get; }

    public AsyncRelayCommand DeleteVolumeCommand { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool IsEmpty => VisibleVolumes.Count == 0 && !IsLoading;

    public bool CanDeleteSelectedVolume => SelectedVolume?.IsNamed == true;

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

    public string NewVolumeName
    {
        get => _newVolumeName;
        set
        {
            if (SetProperty(ref _newVolumeName, value))
            {
                CreateVolumeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public double NewVolumeSizeMb
    {
        get => _newVolumeSizeMb;
        set => SetProperty(ref _newVolumeSizeMb, value);
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

    public string VolumeSummaryText => AppServices.Strings.Format("VolumesSummary", Volumes.Count);

    public VolumeSummary? SelectedVolume
    {
        get => _selectedVolume;
        set
        {
            if (SetProperty(ref _selectedVolume, value))
            {
                OnPropertyChanged(nameof(CanDeleteSelectedVolume));
                DeleteVolumeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public async Task LoadAsync()
    {
        IsLoading = true;

        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            Volumes.Clear();
            VisibleVolumes.Clear();
            _allVolumes.Clear();

            foreach (var volume in await _volumeService.ListVolumesAsync())
            {
                Volumes.Add(volume);
                _allVolumes.Add(volume);
            }

            ApplyFilters();
            Message = AppServices.Strings.Format(
                "VolumesTracked",
                Volumes.Count,
                Volumes.Count == 1 ? string.Empty : "s");
            OnPropertyChanged(nameof(VolumeSummaryText));
            OnPropertyChanged(nameof(IsEmpty));
            DeleteVolumeCommand.RaiseCanExecuteChanged();
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

    public async Task CreateVolumeAsync()
    {
        IsBusy = true;

        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            await _volumeService.CreateNamedVolumeAsync(new VolumeCreateRequest(
                NewVolumeName,
                checked((ulong)Math.Max(1, NewVolumeSizeMb) * 1024UL * 1024UL)));
            Message = AppServices.Strings.Format("NamedVolumeCreated", NewVolumeName);
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

    public async Task DeleteSelectedVolumeAsync()
    {
        if (SelectedVolume?.IsNamed != true)
        {
            return;
        }

        IsBusy = true;
        string name = SelectedVolume.Name;

        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            await _volumeService.DeleteNamedVolumeAsync(name);
            SelectedVolume = null;
            Message = AppServices.Strings.Format("NamedVolumeDeleted", name);
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
        VisibleVolumes.Clear();

        foreach (var volume in _allVolumes.Where(MatchesSearch))
        {
            VisibleVolumes.Add(volume);
        }

        if (SelectedVolume is null || !VisibleVolumes.Contains(SelectedVolume))
        {
            SelectedVolume = VisibleVolumes.FirstOrDefault();
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private bool MatchesSearch(VolumeSummary volume)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        string query = SearchText.Trim();
        return Contains(volume.Name, query)
            || Contains(volume.Kind, query)
            || Contains(volume.KindText, query)
            || Contains(volume.Size, query)
            || Contains(volume.UsedBy, query)
            || Contains(volume.Created, query);
    }

    private static bool Contains(string value, string query)
    {
        return value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }
}
