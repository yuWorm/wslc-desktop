using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.ApplicationModel;
using wslc_desktop.Models;

namespace wslc_desktop.Services;

public sealed class StartupTaskService : IStartupTaskService
{
    public const string TaskId = "WslcDesktopStartupTask";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "WSLC Desktop";

    public async Task<StartupTaskSnapshot> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        StartupTaskSnapshot packagedStatus = await TryGetPackagedStartupStatusAsync(cancellationToken);
        return packagedStatus.Availability == StartupTaskAvailability.Unavailable
            ? GetRunKeyStatus()
            : packagedStatus;
    }

    public async Task<StartupTaskSnapshot> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        StartupTaskSnapshot packagedStatus = await TrySetPackagedStartupAsync(enabled, cancellationToken);
        return packagedStatus.Availability == StartupTaskAvailability.Unavailable
            ? SetRunKeyEnabled(enabled)
            : packagedStatus;
    }

    private static async Task<StartupTaskSnapshot> TryGetPackagedStartupStatusAsync(CancellationToken cancellationToken)
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

    private static async Task<StartupTaskSnapshot> TrySetPackagedStartupAsync(bool enabled, CancellationToken cancellationToken)
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

    private static StartupTaskSnapshot GetRunKeyStatus()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            string? value = key?.GetValue(RunValueName) as string;
            bool enabled = IsCurrentExecutableRunCommand(value);
            string message = enabled
                ? "Startup is registered for this desktop installation."
                : "Startup is not registered for this desktop installation.";
            return new StartupTaskSnapshot(enabled, StartupTaskAvailability.Available, message);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return new StartupTaskSnapshot(false, StartupTaskAvailability.Unavailable, ex.Message);
        }
    }

    private static StartupTaskSnapshot SetRunKeyEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("Could not open the current user startup registry key.");

            if (enabled)
            {
                string? executablePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                {
                    executablePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                }

                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                {
                    throw new FileNotFoundException("Could not resolve the current application executable path.");
                }

                key.SetValue(RunValueName, QuoteRunCommand(executablePath), RegistryValueKind.String);
                return new StartupTaskSnapshot(true, StartupTaskAvailability.Available, "Startup registered for this desktop installation.");
            }

            key.DeleteValue(RunValueName, throwOnMissingValue: false);
            return new StartupTaskSnapshot(false, StartupTaskAvailability.Available, "Startup registration removed for this desktop installation.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException or System.Security.SecurityException)
        {
            return new StartupTaskSnapshot(false, StartupTaskAvailability.Unavailable, ex.Message);
        }
    }

    private static bool IsCurrentExecutableRunCommand(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            executablePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        }

        return !string.IsNullOrWhiteSpace(executablePath)
            && string.Equals(value.Trim().Trim('"'), executablePath, StringComparison.OrdinalIgnoreCase);
    }

    private static string QuoteRunCommand(string executablePath)
    {
        return "\"" + executablePath.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
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
