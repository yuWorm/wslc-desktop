namespace wslc_desktop.Services;

public static class AppLaunchLogger
{
    private static readonly object Sync = new();
    private static bool _globalHandlersInstalled;

    public static string DiagnosticsRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WSLC Desktop",
        "Diagnostics");

    public static string LaunchLogPath { get; } = Path.Combine(
        DiagnosticsRoot,
        $"wslc-desktop-launch-{DateTimeOffset.Now:yyyyMMdd}.log");

    public static string LastCrashPath { get; } = Path.Combine(DiagnosticsRoot, "last-crash.log");

    public static void InstallGlobalHandlers()
    {
        if (_globalHandlersInstalled)
        {
            return;
        }

        _globalHandlersInstalled = true;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                Error("AppDomain unhandled exception.", ex);
            }
            else
            {
                Info($"AppDomain unhandled exception object: {args.ExceptionObject}");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Error("Unobserved task exception.", args.Exception);
        };
    }

    public static void Info(string message)
    {
        Write("INFO", message, null);
    }

    public static void Error(string message, Exception ex)
    {
        Write("ERROR", message, ex);
        WriteLastCrash(message, ex);
    }

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticsRoot);
            string line = $"{DateTimeOffset.Now:O} [{level}] {message}";
            if (ex is not null)
            {
                line += Environment.NewLine + ex;
            }

            lock (Sync)
            {
                File.AppendAllText(LaunchLogPath, line + Environment.NewLine);
            }
        }
        catch
        {
        }
    }

    private static void WriteLastCrash(string message, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticsRoot);
            string text = $"{DateTimeOffset.Now:O}{Environment.NewLine}{message}{Environment.NewLine}{ex}";
            lock (Sync)
            {
                File.WriteAllText(LastCrashPath, text);
            }
        }
        catch
        {
        }
    }
}
