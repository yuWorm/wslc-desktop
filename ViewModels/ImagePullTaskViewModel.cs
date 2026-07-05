using System.Globalization;

namespace wslc_desktop.ViewModels;

public enum ImagePullTaskState
{
    Queued,
    Pulling,
    Succeeded,
    Failed
}

public sealed class ImagePullTaskViewModel : ViewModelBase
{
    private ImagePullTaskState _state;
    private string _statusText;
    private string _detailText = string.Empty;
    private double _progressValue;
    private bool _isIndeterminate = true;

    public ImagePullTaskViewModel(string reference, DateTimeOffset startedAt, string statusText)
    {
        Id = Guid.NewGuid().ToString("N");
        Reference = reference.Trim();
        StartedAt = startedAt;
        _state = ImagePullTaskState.Queued;
        _statusText = statusText;
    }

    public string Id { get; }

    public string Reference { get; }

    public DateTimeOffset StartedAt { get; }

    public string StartedText => StartedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    public ImagePullTaskState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(IsFailed));
            }
        }
    }

    public bool IsActive => State is ImagePullTaskState.Queued or ImagePullTaskState.Pulling;

    public bool IsFailed => State == ImagePullTaskState.Failed;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string DetailText
    {
        get => _detailText;
        private set => SetProperty(ref _detailText, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set => SetProperty(ref _isIndeterminate, value);
    }

    public void MarkPulling(string statusText, string detailText = "")
    {
        State = ImagePullTaskState.Pulling;
        StatusText = statusText;
        DetailText = detailText;
        IsIndeterminate = true;
    }

    public void UpdateProgress(string statusText, string detailText, ulong currentBytes, ulong totalBytes)
    {
        State = ImagePullTaskState.Pulling;
        StatusText = statusText;
        DetailText = detailText;

        if (totalBytes == 0)
        {
            IsIndeterminate = true;
            return;
        }

        IsIndeterminate = false;
        ProgressValue = Math.Clamp(currentBytes / (double)totalBytes * 100d, 0d, 100d);
    }

    public void MarkSucceeded(string statusText)
    {
        State = ImagePullTaskState.Succeeded;
        StatusText = statusText;
        DetailText = string.Empty;
        IsIndeterminate = false;
        ProgressValue = 100d;
    }

    public void MarkFailed(string detailText)
    {
        MarkFailed(detailText, detailText);
    }

    public void MarkFailed(string statusText, string detailText)
    {
        State = ImagePullTaskState.Failed;
        StatusText = statusText;
        DetailText = detailText;
        IsIndeterminate = false;
        ProgressValue = 0d;
    }
}
