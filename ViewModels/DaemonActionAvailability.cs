namespace wslc_desktop.ViewModels;

public sealed record DaemonActionAvailability(
    bool CanStart,
    bool CanRestart,
    bool CanStop)
{
    public static DaemonActionAvailability FromStatus(ShellStatusState state, bool isBusy)
    {
        if (isBusy || state == ShellStatusState.Checking)
        {
            return new DaemonActionAvailability(false, false, false);
        }

        if (state == ShellStatusState.Offline)
        {
            return new DaemonActionAvailability(true, false, false);
        }

        return new DaemonActionAvailability(false, true, true);
    }
}
