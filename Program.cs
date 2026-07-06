using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using wslc_desktop.Services;

namespace wslc_desktop;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppLaunchLogger.InstallGlobalHandlers();
        AppLaunchLogger.Info($"Program.Main started. BaseDirectory={AppContext.BaseDirectory}; ProcessPath={Environment.ProcessPath}; Args={string.Join(" ", args)}");

        try
        {
            global::WinRT.ComWrappersSupport.InitializeComWrappers();
            AppLaunchLogger.Info("WinRT COM wrappers initialized.");

            Application.Start(_ =>
            {
                try
                {
                    var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                    SynchronizationContext.SetSynchronizationContext(context);
                    AppLaunchLogger.Info("Application.Start callback creating App.");
                    new App();
                }
                catch (Exception ex)
                {
                    AppLaunchLogger.Error("Application.Start callback failed.", ex);
                    throw;
                }
            });

            AppLaunchLogger.Info("Program.Main completed.");
        }
        catch (Exception ex)
        {
            AppLaunchLogger.Error("Program.Main failed.", ex);
            throw;
        }
    }
}
