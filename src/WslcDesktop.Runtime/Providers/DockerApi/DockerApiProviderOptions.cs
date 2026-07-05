namespace WslcDesktop.Runtime.Providers.DockerApi;

public sealed record DockerApiProviderOptions(
    string Host,
    bool AllowTcp)
{
    public const string DefaultNamedPipeHost = "npipe:////./pipe/docker_engine";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);

    public bool IsNamedPipe => Host.StartsWith("npipe:", StringComparison.OrdinalIgnoreCase);

    public bool IsTcp => Host.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)
        || Host.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
        || Host.StartsWith("https:", StringComparison.OrdinalIgnoreCase);

    public static DockerApiProviderOptions Create(string host, bool allowTcp)
    {
        return new DockerApiProviderOptions(host.Trim(), allowTcp);
    }

    public void Validate()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Docker API provider host is not configured. Use a named pipe such as npipe:////./pipe/docker_engine or an explicitly approved TCP host.");
        }

        if (IsTcp && !AllowTcp)
        {
            throw new InvalidOperationException("TCP Docker API hosts require explicit user confirmation because Docker API access grants runtime control.");
        }

        if (!IsNamedPipe && !IsTcp)
        {
            throw new InvalidOperationException($"Unsupported Docker API host URI: {Host}");
        }
    }
}
