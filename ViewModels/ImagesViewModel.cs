using System.Collections.ObjectModel;
using wslc_desktop.Models;
using wslc_desktop.Services;

namespace wslc_desktop.ViewModels;

public sealed class ImagesViewModel : ViewModelBase
{
    private readonly IWslcImageService _imageService;
    private readonly IOperationTracker _operationTracker;
    private bool _isLoading;
    private bool _isPulling;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private string _message = AppServices.Strings.Format("ImagesAvailable", 0, "s");
    private string _imageReference = "docker.io/library/alpine:latest";
    private string _pullProgressText = string.Empty;
    private string _searchText = string.Empty;
    private ImageSummary? _selectedImage;
    private readonly List<ImageSummary> _allImages = [];
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    public ImagesViewModel(IWslcImageService imageService, IOperationTracker operationTracker)
    {
        _imageService = imageService;
        _operationTracker = operationTracker;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        PullCommand = new AsyncRelayCommand(PullAsync, () => !string.IsNullOrWhiteSpace(ImageReference));
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => SelectedImage is not null);
    }

    public ObservableCollection<ImageSummary> Images { get; } = [];

    public ObservableCollection<ImageSummary> VisibleImages { get; } = [];

    public ObservableCollection<ImagePullTaskViewModel> PullTasks { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand PullCommand { get; }

    public AsyncRelayCommand DeleteSelectedCommand { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsEmpty => VisibleImages.Count == 0 && !IsLoading;

    public bool HasPullTasks => PullTasks.Count > 0;

    public bool IsPulling
    {
        get => _isPulling;
        private set => SetProperty(ref _isPulling, value);
    }

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

    public string ImageReference
    {
        get => _imageReference;
        set
        {
            if (SetProperty(ref _imageReference, value))
            {
                PullCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string PullProgressText
    {
        get => _pullProgressText;
        private set => SetProperty(ref _pullProgressText, value);
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

    public string ImageSummaryText => AppServices.Strings.Format("ImagesSummary", Images.Count);

    public ImageSummary? SelectedImage
    {
        get => _selectedImage;
        set
        {
            if (SetProperty(ref _selectedImage, value))
            {
                DeleteSelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public async Task LoadAsync()
    {
        await _loadGate.WaitAsync();
        IsLoading = true;

        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            Images.Clear();
            VisibleImages.Clear();
            _allImages.Clear();

            foreach (var image in await _imageService.ListImagesAsync())
            {
                Images.Add(image);
                _allImages.Add(image);
            }

            ApplyFilters();
            Message = AppServices.Strings.Format(
                "ImagesAvailable",
                Images.Count,
                Images.Count == 1 ? string.Empty : "s");
            OnPropertyChanged(nameof(ImageSummaryText));
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
            DeleteSelectedCommand.RaiseCanExecuteChanged();
            _loadGate.Release();
        }
    }

    public async Task PullAsync()
    {
        string reference = ImageReference.Trim();
        if (string.IsNullOrWhiteSpace(reference))
        {
            return;
        }

        var pullTask = new ImagePullTaskViewModel(
            reference,
            DateTimeOffset.Now,
            AppServices.Strings.Get("ImagePullTaskQueued"));

        PullTasks.Insert(0, pullTask);
        OnPropertyChanged(nameof(HasPullTasks));
        UpdatePullingState();

        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            pullTask.MarkPulling(AppServices.Strings.Get("ImagePullTaskPulling"), AppServices.Strings.Get("ImagePullStart"));
            UpdatePullingState();

            await foreach (var progress in _imageService.PullImageAsync(new ImagePullRequest(reference)))
            {
                string detail = progress.HasByteProgress
                    ? AppServices.Strings.Format("ImagePulling", progress.Id, progress.CurrentBytes, progress.TotalBytes)
                    : AppServices.Strings.Format("ImagePullingStatus", progress.Id, progress.Status);
                pullTask.UpdateProgress(
                    AppServices.Strings.Get("ImagePullTaskPulling"),
                    detail,
                    progress.HasByteProgress ? progress.CurrentBytes : 0,
                    progress.HasByteProgress ? progress.TotalBytes : 0);
            }

            pullTask.MarkSucceeded(AppServices.Strings.Get("ImagePullTaskCompleted"));
            _operationTracker.Track(new OperationRecord(
                Guid.NewGuid().ToString("N"),
                "Pull image",
                OperationState.Succeeded,
                reference,
                DateTimeOffset.Now));

            await LoadAsync();
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            pullTask.MarkFailed(AppServices.Strings.Get("ImagePullTaskFailed"), ex.Message);
            _operationTracker.Track(new OperationRecord(
                Guid.NewGuid().ToString("N"),
                "Pull image",
                OperationState.Failed,
                ex.Message,
                DateTimeOffset.Now));
        }
        finally
        {
            UpdatePullingState();
        }
    }

    public async Task DeleteSelectedAsync()
    {
        if (SelectedImage is null)
        {
            return;
        }

        string reference = $"{SelectedImage.Repository}:{SelectedImage.Tag}";

        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            await _imageService.DeleteImageAsync(reference);
            _operationTracker.Track(new OperationRecord(
                Guid.NewGuid().ToString("N"),
                "Delete image",
                OperationState.Succeeded,
                reference,
                DateTimeOffset.Now));
            SelectedImage = null;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            _operationTracker.Track(new OperationRecord(
                Guid.NewGuid().ToString("N"),
                "Delete image",
                OperationState.Failed,
                ex.Message,
                DateTimeOffset.Now));
        }
    }

    private void ApplyFilters()
    {
        VisibleImages.Clear();

        foreach (var image in _allImages.Where(MatchesSearch))
        {
            VisibleImages.Add(image);
        }

        if (SelectedImage is null || !VisibleImages.Contains(SelectedImage))
        {
            SelectedImage = VisibleImages.FirstOrDefault();
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private bool MatchesSearch(ImageSummary image)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        string query = SearchText.Trim();
        return Contains(image.Repository, query)
            || Contains(image.Tag, query)
            || Contains(image.Id, query)
            || Contains(image.Size, query)
            || Contains(image.Created, query)
            || Contains(image.IsInUse ? "in use" : "unused", query);
    }

    private static bool Contains(string value, string query)
    {
        return value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdatePullingState()
    {
        int activeCount = PullTasks.Count(task => task.IsActive);
        IsPulling = activeCount > 0;
        PullProgressText = PullTasks.Count == 0
            ? string.Empty
            : AppServices.Strings.Format("ImagePullTasksSummary", activeCount, PullTasks.Count);
    }
}
