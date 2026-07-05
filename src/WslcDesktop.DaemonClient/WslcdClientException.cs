using System.Net;

namespace WslcDesktop.DaemonClient;

public sealed class WslcdClientException : Exception
{
    public WslcdClientException(HttpStatusCode statusCode, string responseBody)
        : base(CreateMessage(statusCode, responseBody))
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string ResponseBody { get; }

    private static string CreateMessage(HttpStatusCode statusCode, string responseBody)
    {
        string body = string.IsNullOrWhiteSpace(responseBody) ? "No response body." : responseBody.Trim();
        return $"wslcd returned HTTP {(int)statusCode} ({statusCode}): {body}";
    }
}
