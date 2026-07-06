using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using wslc_desktop.Models;
using wslc_desktop.Services;

namespace wslc_desktop.ViewModels;

public sealed record ContainerPortLink(
    string Label,
    string Detail,
    string Url,
    int HostPort,
    int ContainerPort,
    string Protocol);

public sealed record ContainerEnvironmentEntry(
    string Key,
    string Value);

public sealed class ContainersViewModel : ViewModelBase
{
    private const string FilterAll = "All";
    private const string FilterRunning = "Running";
    private const string FilterStopped = "Stopped";

    private readonly IWslcContainerService _containerService;
    private readonly IWslcProcessService _processService;
    private readonly IWslcTerminalService _terminalService;
    private readonly IOperationTracker _operationTracker;
    private readonly List<ContainerSummary> _allContainers = [];
    private ITerminalSession? _terminalSession;

    private ContainerSummary? _selectedContainer;
    private bool _isLoading;
    private bool _isBusy;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private string _statusMessage = AppServices.Strings.Format("ContainersLoaded", 0, "s");
    private string _searchText = string.Empty;
    private string _selectedStateFilter = FilterAll;
    private string _selectedContainerLogs = string.Empty;
    private string _newContainerName = ContainerCreateDraft.Default().Name;
    private string _newContainerImage = ContainerCreateDraft.Default().Image;
    private string _newContainerCommand = ContainerCreateDraft.Default().Command;
    private string _newContainerPort = string.Empty;
    private string _newContainerMounts = string.Empty;
    private string _newContainerEnvironment = string.Empty;
    private string _terminalInputLine = string.Empty;
    private string _terminalTranscript = AppServices.Strings.Get("TerminalReadyMessage");
    private TerminalSessionState _terminalState = TerminalSessionState.Disconnected;
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    public ContainersViewModel(
        IWslcContainerService containerService,
        IWslcProcessService processService,
        IWslcTerminalService terminalService,
        IOperationTracker operationTracker)
    {
        _containerService = containerService;
        _processService = processService;
        _terminalService = terminalService;
        _operationTracker = operationTracker;
        CreateCommand = new AsyncRelayCommand(CreateAsync, () => !string.IsNullOrWhiteSpace(NewContainerImage));
        StartCommand = new AsyncRelayCommand(StartSelectedAsync, () => CanStartSelectedContainer);
        StopCommand = new AsyncRelayCommand(StopSelectedAsync, () => CanStopSelectedContainer);
        RestartCommand = new AsyncRelayCommand(RestartSelectedAsync, () => CanRestartSelectedContainer);
        DeleteCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => CanDeleteSelectedContainer);
        ConnectTerminalCommand = new AsyncRelayCommand(ConnectTerminalAsync, () => CanConnectTerminal);
        DisconnectTerminalCommand = new AsyncRelayCommand(DisconnectTerminalAsync, () => CanDisconnectTerminal);
        ExternalTerminalCommand = new AsyncRelayCommand(OpenExternalTerminalAsync, () => CanOpenTerminal);
    }

    public ObservableCollection<ContainerSummary> Containers { get; } = [];

    public ObservableCollection<ContainerSummary> VisibleContainers { get; } = [];

    public IReadOnlyList<string> StateFilters { get; } = [FilterAll, FilterRunning, FilterStopped];

    public AsyncRelayCommand CreateCommand { get; }

    public AsyncRelayCommand StartCommand { get; }

    public AsyncRelayCommand StopCommand { get; }

    public AsyncRelayCommand RestartCommand { get; }

    public AsyncRelayCommand DeleteCommand { get; }

    public AsyncRelayCommand ConnectTerminalCommand { get; }

    public AsyncRelayCommand DisconnectTerminalCommand { get; }

    public AsyncRelayCommand ExternalTerminalCommand { get; }

    public ContainerSummary? SelectedContainer
    {
        get => _selectedContainer;
        set
        {
            string? previousContainerId = _selectedContainer?.Id;
            if (SetProperty(ref _selectedContainer, value))
            {
                if (!string.Equals(previousContainerId, _selectedContainer?.Id, StringComparison.OrdinalIgnoreCase))
                {
                    _ = DisconnectTerminalAsync();
                }

                NotifySelectedContainerChanged();
                StartCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
                RestartCommand.RaiseCanExecuteChanged();
                DeleteCommand.RaiseCanExecuteChanged();
                ConnectTerminalCommand.RaiseCanExecuteChanged();
                DisconnectTerminalCommand.RaiseCanExecuteChanged();
                ExternalTerminalCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasSelectedContainer => SelectedContainer is not null;

    public bool CanOperateSelectedContainer => SelectedContainer is not null;

    public bool CanStartSelectedContainer => SelectedContainer?.State is ContainerRuntimeState.Created or ContainerRuntimeState.Stopped or ContainerRuntimeState.Exited;

    public bool CanStopSelectedContainer => SelectedContainer?.State == ContainerRuntimeState.Running;

    public bool CanRestartSelectedContainer => SelectedContainer?.State == ContainerRuntimeState.Running;

    public bool CanDeleteSelectedContainer => SelectedContainer is not null;

    public bool CanOpenTerminal => SelectedContainer?.State == ContainerRuntimeState.Running;

    public bool CanConnectTerminal => SelectedContainer?.State == ContainerRuntimeState.Running
        && TerminalState is TerminalSessionState.Disconnected or TerminalSessionState.Exited or TerminalSessionState.Failed;

    public bool CanDisconnectTerminal => TerminalState is TerminalSessionState.Connecting or TerminalSessionState.Connected;

    public bool IsTerminalConnected => TerminalState == TerminalSessionState.Connected;

    public string SelectedContainerName => SelectedContainer?.Name ?? AppServices.Strings.Get("ContainersNoSelection");

    public string SelectedContainerImage => SelectedContainer?.Image ?? AppServices.Strings.Get("ContainersSelectContainerDetail");

    public string SelectedContainerId => SelectedContainer?.Id ?? "-";

    public string SelectedContainerCommand => SelectedContainer?.Command ?? "-";

    public string SelectedContainerPorts => SelectedContainer?.PortSummary ?? "-";

    public IReadOnlyList<ContainerPortLink> SelectedContainerPortLinks => ParsePortLinks(SelectedContainer?.PortSummary);

    public bool HasSelectedContainerPortLinks => SelectedContainerPortLinks.Count > 0;

    public string SelectedContainerState => SelectedContainer?.State.ToString() ?? "-";

    public string SelectedContainerCreated => SelectedContainer?.Created ?? "-";

    public string SelectedContainerUptime => SelectedContainer?.Uptime ?? "-";

    public string SelectedContainerInspectJson => string.IsNullOrWhiteSpace(SelectedContainer?.InspectJson)
        ? AppServices.Strings.Get("InspectUnavailable")
        : SelectedContainer.InspectJson;

    public string SelectedContainerInspectPreview => LimitLines(SelectedContainerInspectJson, 18);

    public IReadOnlyList<ContainerEnvironmentEntry> SelectedContainerEnvironmentEntries => ParseEnvironmentEntries(SelectedContainer?.InspectJson);

    public bool HasSelectedContainerEnvironmentEntries => SelectedContainerEnvironmentEntries.Count > 0;

    public string SelectedContainerCpuText => SelectedContainer is null ? "-" : $"{SelectedContainer.CpuPercent:0.0}%";

    public string SelectedContainerMemoryText => SelectedContainer?.MemoryUsed ?? "-";

    public string SelectedContainerLogsPreview => string.IsNullOrWhiteSpace(SelectedContainerLogs)
        ? AppServices.Strings.Get("LogsUnavailable")
        : LimitLines(SelectedContainerLogs, 12);

    public bool HasVisibleContainers => VisibleContainers.Count > 0;

    public string ContainerSummaryText
    {
        get
        {
            int running = _allContainers.Count(container => container.State == ContainerRuntimeState.Running);
            int stopped = _allContainers.Count(container => container.State is ContainerRuntimeState.Stopped or ContainerRuntimeState.Exited);
            return AppServices.Strings.Format("ContainersSummary", running, stopped);
        }
    }

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

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
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

    public string SelectedStateFilter
    {
        get => _selectedStateFilter;
        set
        {
            string normalized = StateFilters.Contains(value) ? value : FilterAll;
            if (SetProperty(ref _selectedStateFilter, normalized))
            {
                ApplyFilters();
            }
        }
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

    public string SelectedContainerLogs
    {
        get => _selectedContainerLogs;
        private set
        {
            if (SetProperty(ref _selectedContainerLogs, value))
            {
                OnPropertyChanged(nameof(SelectedContainerLogsPreview));
            }
        }
    }

    public string NewContainerName
    {
        get => _newContainerName;
        set => SetProperty(ref _newContainerName, value);
    }

    public string NewContainerImage
    {
        get => _newContainerImage;
        set
        {
            if (SetProperty(ref _newContainerImage, value))
            {
                CreateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewContainerCommand
    {
        get => _newContainerCommand;
        set => SetProperty(ref _newContainerCommand, value);
    }

    public string NewContainerPort
    {
        get => _newContainerPort;
        set => SetProperty(ref _newContainerPort, value);
    }

    public string NewContainerMounts
    {
        get => _newContainerMounts;
        set => SetProperty(ref _newContainerMounts, value);
    }

    public string NewContainerEnvironment
    {
        get => _newContainerEnvironment;
        set => SetProperty(ref _newContainerEnvironment, value);
    }

    public string TerminalInputLine
    {
        get => _terminalInputLine;
        set => SetProperty(ref _terminalInputLine, value);
    }

    public string TerminalTranscript
    {
        get => _terminalTranscript;
        private set => SetProperty(ref _terminalTranscript, value);
    }

    public TerminalSessionState TerminalState
    {
        get => _terminalState;
        private set
        {
            if (SetProperty(ref _terminalState, value))
            {
                OnPropertyChanged(nameof(TerminalStateText));
                OnPropertyChanged(nameof(CanConnectTerminal));
                OnPropertyChanged(nameof(CanDisconnectTerminal));
                OnPropertyChanged(nameof(IsTerminalConnected));
                ConnectTerminalCommand.RaiseCanExecuteChanged();
                DisconnectTerminalCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string TerminalStateText => TerminalState switch
    {
        TerminalSessionState.Connecting => AppServices.Strings.Get("TerminalStateConnecting"),
        TerminalSessionState.Connected => AppServices.Strings.Get("TerminalStateConnected"),
        TerminalSessionState.Exited => AppServices.Strings.Get("TerminalStateExited"),
        TerminalSessionState.Failed => AppServices.Strings.Get("TerminalStateFailed"),
        _ => AppServices.Strings.Get("TerminalStateDisconnected")
    };

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _loadGate.WaitAsync(cancellationToken);
        IsLoading = true;

        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            Containers.Clear();
            _allContainers.Clear();

            foreach (var container in await _containerService.ListContainersAsync(cancellationToken))
            {
                _allContainers.Add(container);
                Containers.Add(container);
            }

            ApplyFilters();
            StatusMessage = AppServices.Strings.Format(
                "ContainersLoaded",
                _allContainers.Count,
                _allContainers.Count == 1 ? string.Empty : "s");
            await LoadSelectedLogsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            _loadGate.Release();
        }
    }

    public async Task LoadSelectedLogsAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedContainer is null)
        {
            SelectedContainerLogs = string.Empty;
            return;
        }

        var lines = new List<string>();

        try
        {
            await foreach (var line in _processService.StreamLogsAsync(SelectedContainer.Id, cancellationToken))
            {
                lines.Add($"[{line.Timestamp:HH:mm:ss}] {line.Stream}: {line.Message}");
            }

            HasError = false;
            ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
        }

        SelectedContainerLogs = string.Join(Environment.NewLine, lines);
    }

    public async Task CreateAsync()
    {
        await PerformAsync("Create container", async () =>
        {
            var container = await _containerService.CreateAsync(new ContainerCreateRequest(
                NewContainerName,
                NewContainerImage,
                ContainerCreateInputParser.ParseCommandLine(NewContainerCommand),
                ContainerCreateInputParser.ParsePortMappings(NewContainerPort),
                ContainerCreateInputParser.ParseMounts(NewContainerMounts),
                ContainerCreateInputParser.ParseEnvironment(NewContainerEnvironment),
                EnableGpu: false,
                AutoRemove: false));
            SelectedContainer = container;
            await _containerService.StartAsync(container.Id);
            await LoadAsync();
        });
    }

    public ContainerCreateDraft CreateDraft()
    {
        return new ContainerCreateDraft(
            NewContainerName,
            NewContainerImage,
            NewContainerCommand,
            NewContainerPort,
            NewContainerMounts,
            NewContainerEnvironment);
    }

    public void ApplyCreateDraft(ContainerCreateDraft draft)
    {
        NewContainerName = draft.Name;
        NewContainerImage = draft.Image;
        NewContainerCommand = draft.Command;
        NewContainerPort = draft.Ports;
        NewContainerMounts = draft.Mounts;
        NewContainerEnvironment = draft.Environment;
    }

    public Task StartSelectedAsync() => CanStartSelectedContainer
        ? PerformSelectedAsync("Start container", id => _containerService.StartAsync(id))
        : Task.CompletedTask;

    public Task StopSelectedAsync() => CanStopSelectedContainer
        ? PerformSelectedAsync("Stop container", id => _containerService.StopAsync(id))
        : Task.CompletedTask;

    public Task RestartSelectedAsync() => CanRestartSelectedContainer
        ? PerformSelectedAsync("Restart container", id => _containerService.RestartAsync(id))
        : Task.CompletedTask;

    public Task DeleteSelectedAsync() => CanDeleteSelectedContainer
        ? PerformSelectedAsync("Delete container", id => _containerService.DeleteAsync(id))
        : Task.CompletedTask;

    public async Task ConnectTerminalAsync()
    {
        var selected = SelectedContainer;
        if (selected is null || !CanConnectTerminal)
        {
            return;
        }

        await CloseTerminalSessionAsync();
        TerminalState = TerminalSessionState.Connecting;
        TerminalTranscript = AppServices.Strings.Format("TerminalConnectingMessage", selected.Name);

        try
        {
            _terminalSession = await _terminalService.ConnectAsync(new TerminalConnectRequest(
                selected.Id,
                selected.Name,
                ["/bin/bash", "/bin/sh"]));
            _terminalSession.OutputReceived += TerminalSession_OutputReceived;
            _terminalSession.StateChanged += TerminalSession_StateChanged;
            TerminalState = _terminalSession.State;
            AppendTerminalText(AppServices.Strings.Format("TerminalConnectedMessage", _terminalSession.Shell));
        }
        catch (Exception ex)
        {
            TerminalState = TerminalSessionState.Failed;
            AppendTerminalText(AppServices.Strings.Format("TerminalConnectFailed", ex.Message));
        }
    }

    public async Task DisconnectTerminalAsync()
    {
        await CloseTerminalSessionAsync();
        TerminalState = TerminalSessionState.Disconnected;
    }

    public async Task OpenExternalTerminalAsync()
    {
        var selected = SelectedContainer;
        if (selected is null || !CanOpenTerminal)
        {
            return;
        }

        await PerformAsync("Open external terminal", async () =>
        {
            await _terminalService.OpenExternalAsync(new TerminalConnectRequest(
                selected.Id,
                selected.Name,
                ["/bin/bash", "/bin/sh"]));
        });
    }

    public async Task SendTerminalInputLineAsync()
    {
        if (_terminalSession is null || TerminalState != TerminalSessionState.Connected || string.IsNullOrEmpty(TerminalInputLine))
        {
            return;
        }

        string input = TerminalInputLine;
        TerminalInputLine = string.Empty;
        AppendTerminalText(input + Environment.NewLine);
        await _terminalSession.SendInputAsync(input + "\r");
    }

    private async Task PerformSelectedAsync(string title, Func<string, Task> operation)
    {
        if (SelectedContainer is null)
        {
            return;
        }

        string id = SelectedContainer.Id;
        await PerformAsync(title, async () =>
        {
            await operation(id);
            SelectedContainer = null;
            await LoadAsync();
        });
    }

    private async Task PerformAsync(string title, Func<Task> operation)
    {
        IsBusy = true;

        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            await operation();
            _operationTracker.Track(new OperationRecord(
                Guid.NewGuid().ToString("N"),
                title,
                OperationState.Succeeded,
                title,
                DateTimeOffset.Now));
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            _operationTracker.Track(new OperationRecord(
                Guid.NewGuid().ToString("N"),
                title,
                OperationState.Failed,
                ex.Message,
                DateTimeOffset.Now));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CloseTerminalSessionAsync()
    {
        if (_terminalSession is null)
        {
            return;
        }

        _terminalSession.OutputReceived -= TerminalSession_OutputReceived;
        _terminalSession.StateChanged -= TerminalSession_StateChanged;
        await _terminalSession.DisconnectAsync();
        await _terminalSession.DisposeAsync();
        _terminalSession = null;
    }

    private void TerminalSession_OutputReceived(object? sender, TerminalOutputEvent e)
    {
        RunOnUiThread(() => AppendTerminalText(e.Text));
    }

    private void TerminalSession_StateChanged(object? sender, TerminalSessionState e)
    {
        RunOnUiThread(() => TerminalState = e);
    }

    private void AppendTerminalText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        TerminalTranscript += text;
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = App.MainWindow?.DispatcherQueue;
        if (dispatcher is not null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(() => action());
            return;
        }

        action();
    }

    private void ApplyFilters()
    {
        string? selectedId = SelectedContainer?.Id;
        var visible = _allContainers
            .Where(MatchesStateFilter)
            .Where(MatchesSearch)
            .OrderBy(container => container.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        VisibleContainers.Clear();
        foreach (var container in visible)
        {
            VisibleContainers.Add(container);
        }

        SelectedContainer = visible.FirstOrDefault(container => container.Id == selectedId) ?? visible.FirstOrDefault();
        OnPropertyChanged(nameof(HasVisibleContainers));
        OnPropertyChanged(nameof(ContainerSummaryText));
    }

    private bool MatchesStateFilter(ContainerSummary container)
    {
        return SelectedStateFilter switch
        {
            FilterRunning => container.State == ContainerRuntimeState.Running,
            FilterStopped => container.State is ContainerRuntimeState.Stopped or ContainerRuntimeState.Exited,
            _ => true
        };
    }

    private bool MatchesSearch(ContainerSummary container)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        string query = SearchText.Trim();
        return Contains(container.Name, query)
            || Contains(container.Image, query)
            || Contains(container.Command, query)
            || Contains(container.PortSummary, query)
            || Contains(container.State.ToString(), query);
    }

    private static bool Contains(string value, string query)
    {
        return value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void NotifySelectedContainerChanged()
    {
        OnPropertyChanged(nameof(HasSelectedContainer));
        OnPropertyChanged(nameof(CanOperateSelectedContainer));
        OnPropertyChanged(nameof(CanStartSelectedContainer));
        OnPropertyChanged(nameof(CanStopSelectedContainer));
        OnPropertyChanged(nameof(CanRestartSelectedContainer));
        OnPropertyChanged(nameof(CanDeleteSelectedContainer));
        OnPropertyChanged(nameof(CanOpenTerminal));
        OnPropertyChanged(nameof(SelectedContainerName));
        OnPropertyChanged(nameof(SelectedContainerImage));
        OnPropertyChanged(nameof(SelectedContainerId));
        OnPropertyChanged(nameof(SelectedContainerCommand));
        OnPropertyChanged(nameof(SelectedContainerPorts));
        OnPropertyChanged(nameof(SelectedContainerPortLinks));
        OnPropertyChanged(nameof(HasSelectedContainerPortLinks));
        OnPropertyChanged(nameof(SelectedContainerState));
        OnPropertyChanged(nameof(SelectedContainerCreated));
        OnPropertyChanged(nameof(SelectedContainerUptime));
        OnPropertyChanged(nameof(SelectedContainerInspectJson));
        OnPropertyChanged(nameof(SelectedContainerInspectPreview));
        OnPropertyChanged(nameof(SelectedContainerEnvironmentEntries));
        OnPropertyChanged(nameof(HasSelectedContainerEnvironmentEntries));
        OnPropertyChanged(nameof(SelectedContainerCpuText));
        OnPropertyChanged(nameof(SelectedContainerMemoryText));
        OnPropertyChanged(nameof(CanConnectTerminal));
        OnPropertyChanged(nameof(CanDisconnectTerminal));
        OnPropertyChanged(nameof(IsTerminalConnected));
    }

    private static IReadOnlyList<ContainerPortLink> ParsePortLinks(string? portSummary)
    {
        if (string.IsNullOrWhiteSpace(portSummary) || portSummary.Trim() == "-")
        {
            return [];
        }

        var links = new List<ContainerPortLink>();
        foreach (string entry in portSummary.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(
                entry,
                @"(?:(?:localhost|0\.0\.0\.0|127\.0\.0\.1):)?(?<host>\d+)\s*(?:->|:)\s*(?<container>\d+)(?:/(?<protocol>\w+))?",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                continue;
            }

            int hostPort = int.Parse(match.Groups["host"].Value, System.Globalization.CultureInfo.InvariantCulture);
            int containerPort = int.Parse(match.Groups["container"].Value, System.Globalization.CultureInfo.InvariantCulture);
            string protocol = match.Groups["protocol"].Success ? match.Groups["protocol"].Value.ToLowerInvariant() : "tcp";
            string url = protocol.Equals("udp", StringComparison.OrdinalIgnoreCase)
                ? $"udp://localhost:{hostPort}"
                : $"http://localhost:{hostPort}";

            links.Add(new ContainerPortLink(
                $":{hostPort}",
                $"{hostPort}->{containerPort}/{protocol}",
                url,
                hostPort,
                containerPort,
                protocol));
        }

        return links;
    }

    private static IReadOnlyList<ContainerEnvironmentEntry> ParseEnvironmentEntries(string? inspectJson)
    {
        if (string.IsNullOrWhiteSpace(inspectJson) || !inspectJson.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(inspectJson);
            JsonElement root = document.RootElement;
            if (TryFindEnvironmentArray(root, out JsonElement envArray))
            {
                return ParseEnvironmentArray(envArray);
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return [];
    }

    private static bool TryFindEnvironmentArray(JsonElement root, out JsonElement envArray)
    {
        envArray = default;
        if (root.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        if (root.TryGetProperty("Config", out JsonElement config)
            && config.ValueKind is JsonValueKind.Object
            && config.TryGetProperty("Env", out envArray)
            && envArray.ValueKind is JsonValueKind.Array)
        {
            return true;
        }

        if (root.TryGetProperty("Env", out envArray) && envArray.ValueKind is JsonValueKind.Array)
        {
            return true;
        }

        return false;
    }

    private static IReadOnlyList<ContainerEnvironmentEntry> ParseEnvironmentArray(JsonElement envArray)
    {
        var entries = new List<ContainerEnvironmentEntry>();
        foreach (JsonElement item in envArray.EnumerateArray())
        {
            if (item.ValueKind is not JsonValueKind.String)
            {
                continue;
            }

            string? value = item.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            int divider = value.IndexOf('=');
            if (divider <= 0)
            {
                entries.Add(new ContainerEnvironmentEntry(value, string.Empty));
                continue;
            }

            entries.Add(new ContainerEnvironmentEntry(value[..divider], value[(divider + 1)..]));
        }

        return entries
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string LimitLines(string text, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        if (lines.Length <= maxLines)
        {
            return text;
        }

        return string.Join(Environment.NewLine, lines.Take(maxLines))
            + Environment.NewLine
            + AppServices.Strings.Format("PreviewTruncated", lines.Length - maxLines);
    }
}
