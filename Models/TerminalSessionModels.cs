namespace wslc_desktop.Models;

public enum TerminalSessionState
{
    Disconnected,
    Connecting,
    Connected,
    Exited,
    Failed
}

public sealed record TerminalConnectRequest(
    string ContainerId,
    string ContainerName,
    IReadOnlyList<string> ShellCandidates);

public sealed record TerminalOutputEvent(
    DateTimeOffset Timestamp,
    string Stream,
    string Text);
