using wslc_desktop.Models;

namespace wslc_desktop.ViewModels;

public enum ShellStatusState
{
    Checking,
    Ready,
    Warning,
    Offline
}

public sealed record ShellStatusLabels(
    string Checking,
    string DaemonOk,
    string DaemonOffline,
    string DaemonWarning,
    string RuntimeOk,
    string RuntimeIssue,
    string BackendUnknown)
{
    public static ShellStatusLabels English { get; } = new(
        "Checking",
        "Daemon OK",
        "Daemon offline",
        "Daemon issue",
        "Runtime OK",
        "Runtime issue",
        "Backend unknown");
}

public sealed record ShellStatusPresentation(
    ShellStatusState State,
    string DaemonText,
    string BackendText,
    string RuntimeText,
    string Message,
    string PipeHealth,
    string CompatibilityMatrix,
    IReadOnlyList<string> LogFiles)
{
    public bool IsReady => State == ShellStatusState.Ready;

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public bool HasLogFiles => LogFiles.Count > 0;

    public string SummaryText => $"{DaemonText} · {BackendText}";

    public string LogFilesText => HasLogFiles ? string.Join(Environment.NewLine, LogFiles) : "-";
}

public sealed class ShellStatusViewModel : ViewModelBase
{
    private readonly Func<CancellationToken, Task<DaemonDiagnosticsSnapshot>> _captureAsync;
    private readonly ShellStatusLabels _labels;
    private int _refreshInProgress;
    private ShellStatusPresentation _presentation;
    private bool _isBusy;
    private bool _canStartDaemon;
    private bool _canRestartDaemon;
    private bool _canStopDaemon;

    public ShellStatusViewModel(
        Func<CancellationToken, Task<DaemonDiagnosticsSnapshot>> captureAsync,
        ShellStatusLabels? labels = null)
    {
        _captureAsync = captureAsync;
        _labels = labels ?? ShellStatusLabels.English;
        _presentation = CreateCheckingPresentation(_labels);
        RefreshDaemonActionAvailability();
    }

    public ShellStatusPresentation Presentation
    {
        get => _presentation;
        private set
        {
            if (SetProperty(ref _presentation, value))
            {
                OnPropertyChanged(nameof(State));
                OnPropertyChanged(nameof(SummaryText));
                OnPropertyChanged(nameof(DaemonText));
                OnPropertyChanged(nameof(BackendText));
                OnPropertyChanged(nameof(RuntimeText));
                OnPropertyChanged(nameof(Message));
                OnPropertyChanged(nameof(PipeHealth));
                OnPropertyChanged(nameof(CompatibilityMatrix));
                OnPropertyChanged(nameof(LogFilesText));
                OnPropertyChanged(nameof(IsReady));
                RefreshDaemonActionAvailability();
            }
        }
    }

    public ShellStatusState State => Presentation.State;

    public string SummaryText => Presentation.SummaryText;

    public string DaemonText => Presentation.DaemonText;

    public string BackendText => Presentation.BackendText;

    public string RuntimeText => Presentation.RuntimeText;

    public string Message => Presentation.Message;

    public string PipeHealth => Presentation.PipeHealth;

    public string CompatibilityMatrix => Presentation.CompatibilityMatrix;

    public string LogFilesText => Presentation.LogFilesText;

    public bool IsReady => Presentation.IsReady;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshDaemonActionAvailability();
            }
        }
    }

    public bool CanStartDaemon
    {
        get => _canStartDaemon;
        private set => SetProperty(ref _canStartDaemon, value);
    }

    public bool CanRestartDaemon
    {
        get => _canRestartDaemon;
        private set => SetProperty(ref _canRestartDaemon, value);
    }

    public bool CanStopDaemon
    {
        get => _canStopDaemon;
        private set => SetProperty(ref _canStopDaemon, value);
    }

    public void SetBusy(bool isBusy)
    {
        IsBusy = isBusy;
    }

    public void ShowOfflineError(string message)
    {
        Presentation = Present(new DaemonDiagnosticsSnapshot(
            "native pipe unavailable",
            "-",
            string.Empty,
            [],
            message), _labels);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _refreshInProgress, 1) == 1)
        {
            return;
        }

        try
        {
            IsBusy = true;
            if (Presentation.State == ShellStatusState.Checking)
            {
                Presentation = CreateCheckingPresentation(_labels);
            }

            DaemonDiagnosticsSnapshot snapshot = await _captureAsync(cancellationToken);
            Presentation = Present(snapshot, _labels);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or HttpRequestException or IOException)
        {
            Presentation = Present(new DaemonDiagnosticsSnapshot(
                "native pipe unavailable",
                "-",
                string.Empty,
                [],
                ex.Message), _labels);
        }
        finally
        {
            IsBusy = false;
            Interlocked.Exchange(ref _refreshInProgress, 0);
        }
    }

    public static ShellStatusPresentation Present(DaemonDiagnosticsSnapshot snapshot)
    {
        return Present(snapshot, ShellStatusLabels.English);
    }

    public static ShellStatusPresentation Present(DaemonDiagnosticsSnapshot snapshot, ShellStatusLabels labels)
    {
        string provider = string.IsNullOrWhiteSpace(snapshot.RuntimeProvider) || snapshot.RuntimeProvider == "-"
            ? labels.BackendUnknown
            : snapshot.RuntimeProvider.Trim();

        bool isReachable = snapshot.PipeHealth.Contains("reachable", StringComparison.OrdinalIgnoreCase)
            || snapshot.PipeHealth.Contains("healthy", StringComparison.OrdinalIgnoreCase)
            || snapshot.PipeHealth.Contains("ok", StringComparison.OrdinalIgnoreCase);

        ShellStatusState state = isReachable
            ? provider == labels.BackendUnknown ? ShellStatusState.Warning : ShellStatusState.Ready
            : ShellStatusState.Offline;

        string daemonText = state switch
        {
            ShellStatusState.Ready => labels.DaemonOk,
            ShellStatusState.Warning => labels.DaemonWarning,
            ShellStatusState.Offline => labels.DaemonOffline,
            _ => labels.Checking
        };

        string runtimeText = state == ShellStatusState.Ready ? labels.RuntimeOk : labels.RuntimeIssue;

        return new ShellStatusPresentation(
            state,
            daemonText,
            provider,
            runtimeText,
            snapshot.Message,
            snapshot.PipeHealth,
            snapshot.CompatibilityMatrix,
            snapshot.LogFiles);
    }

    private static ShellStatusPresentation CreateCheckingPresentation(ShellStatusLabels labels)
    {
        return new ShellStatusPresentation(
            ShellStatusState.Checking,
            labels.Checking,
            labels.BackendUnknown,
            labels.RuntimeIssue,
            string.Empty,
            "-",
            string.Empty,
            []);
    }

    private void RefreshDaemonActionAvailability()
    {
        DaemonActionAvailability availability = DaemonActionAvailability.FromStatus(Presentation.State, IsBusy);
        CanStartDaemon = availability.CanStart;
        CanRestartDaemon = availability.CanRestart;
        CanStopDaemon = availability.CanStop;
    }
}
