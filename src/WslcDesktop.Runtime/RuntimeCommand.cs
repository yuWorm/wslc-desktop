namespace WslcDesktop.Runtime;

public sealed record RuntimeCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    string Command);

public sealed class RuntimeCommandException : Exception
{
    public RuntimeCommandException(string provider, RuntimeCommandResult result)
        : base(string.IsNullOrWhiteSpace(result.StandardError)
            ? $"{provider} command failed: {result.Command}"
            : result.StandardError)
    {
        Provider = provider;
        Result = result;
    }

    public string Provider { get; }

    public RuntimeCommandResult Result { get; }
}
