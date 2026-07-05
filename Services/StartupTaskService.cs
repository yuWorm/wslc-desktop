using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using wslc_desktop.Models;

namespace wslc_desktop.Services;

public sealed class StartupTaskService : IStartupTaskService
{
    public const string TaskId = "WslcDesktopStartupTask";

    public async Task<StartupTaskSnapshot> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            StartupTask task = await StartupTask.GetAsync(TaskId);
            cancellationToken.ThrowIfCancellationRequested();
            return Map(task.State, "Startup task status loaded.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or FileNotFoundException or COMException)
        {
            return new StartupTaskSnapshot(false, StartupTaskAvailability.Unavailable, ex.Message);
        }
    }

    public async Task<StartupTaskSnapshot> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        try
        {
            StartupTask task = await StartupTask.GetAsync(TaskId);
            cancellationToken.ThrowIfCancellationRequested();

            if (enabled)
            {
                StartupTaskState state = await task.RequestEnableAsync();
                cancellationToken.ThrowIfCancellationRequested();
                return Map(state, "Startup task enable requested.");
            }

            task.Disable();
            return new StartupTaskSnapshot(false, StartupTaskAvailability.Available, "Startup task disabled.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or FileNotFoundException or COMException)
        {
            return new StartupTaskSnapshot(false, StartupTaskAvailability.Unavailable, ex.Message);
        }
    }

    private static StartupTaskSnapshot Map(StartupTaskState state, string message)
    {
        return state switch
        {
            StartupTaskState.Enabled => new StartupTaskSnapshot(true, StartupTaskAvailability.Available, message),
            StartupTaskState.Disabled => new StartupTaskSnapshot(false, StartupTaskAvailability.Available, message),
            StartupTaskState.DisabledByUser => new StartupTaskSnapshot(false, StartupTaskAvailability.DisabledByUser, "Startup task is disabled by the user in Windows Settings."),
            StartupTaskState.DisabledByPolicy => new StartupTaskSnapshot(false, StartupTaskAvailability.DisabledByPolicy, "Startup task is disabled by policy."),
            _ => new StartupTaskSnapshot(false, StartupTaskAvailability.Unknown, $"Startup task state: {state}.")
        };
    }
}
